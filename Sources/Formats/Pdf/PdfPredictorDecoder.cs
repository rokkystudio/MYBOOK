using System.IO;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Декодирует PDF predictor после FlateDecode для 8-bit raster samples.
/// Поддерживает TIFF predictor 2 и PNG predictors 10..15.
/// </summary>
internal static class PdfPredictorDecoder
{
    /// <summary>
    /// Восстанавливает исходные 8-bit raster samples по параметрам PDF predictor.
    /// </summary>
    public static byte[] Decode(
        byte[] data,
        int predictor,
        int colors,
        int bitsPerComponent,
        int columns)
    {
        if (predictor == 1)
            return data;

        if (colors <= 0 || columns <= 0)
            throw new InvalidDataException("PDF predictor содержит недопустимые Colors/Columns.");

        if (bitsPerComponent != 8)
            throw new InvalidDataException($"PDF predictor для {bitsPerComponent} bits per component пока не поддерживается.");

        var bytesPerPixel = checked(colors);
        var rowBytes = checked(colors * columns);

        return predictor switch
        {
            2 => DecodeTiff(data, rowBytes, bytesPerPixel),
            >= 10 and <= 15 => DecodePng(data, rowBytes, bytesPerPixel),
            _ => throw new InvalidDataException($"PDF predictor {predictor} не поддерживается.")
        };
    }

    private static byte[] DecodeTiff(
        byte[] data,
        int rowBytes,
        int bytesPerPixel)
    {
        if (data.Length % rowBytes != 0)
            throw new InvalidDataException("PDF TIFF predictor имеет неполную raster row.");

        var output = data.ToArray();

        for (var rowStart = 0; rowStart < output.Length; rowStart += rowBytes)
        {
            for (var index = bytesPerPixel; index < rowBytes; index++)
            {
                var position = rowStart + index;
                output[position] = unchecked(
                    (byte)(output[position] + output[position - bytesPerPixel]));
            }
        }

        return output;
    }

    private static byte[] DecodePng(
        byte[] data,
        int rowBytes,
        int bytesPerPixel)
    {
        var encodedRowBytes = checked(rowBytes + 1);

        if (data.Length % encodedRowBytes != 0)
            throw new InvalidDataException("PDF PNG predictor имеет неполную predictor row.");

        var rowCount = data.Length / encodedRowBytes;
        var output = new byte[checked(rowBytes * rowCount)];

        for (var row = 0; row < rowCount; row++)
        {
            var encodedStart = row * encodedRowBytes;
            var filter = data[encodedStart];
            var outputStart = row * rowBytes;

            for (var index = 0; index < rowBytes; index++)
            {
                var raw = data[encodedStart + 1 + index];
                var left = index >= bytesPerPixel
                    ? output[outputStart + index - bytesPerPixel]
                    : (byte)0;
                var up = row > 0
                    ? output[outputStart - rowBytes + index]
                    : (byte)0;
                var upLeft = row > 0 && index >= bytesPerPixel
                    ? output[outputStart - rowBytes + index - bytesPerPixel]
                    : (byte)0;

                output[outputStart + index] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + up)),
                    3 => unchecked((byte)(raw + ((left + up) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, up, upLeft))),
                    _ => throw new InvalidDataException(
                        $"PDF PNG predictor использует неизвестный row filter {filter}.")
                };
            }
        }

        return output;
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var p = left + up - upLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - up);
        var pc = Math.Abs(p - upLeft);

        if (pa <= pb && pa <= pc)
            return left;

        return pb <= pc ? up : upLeft;
    }
}