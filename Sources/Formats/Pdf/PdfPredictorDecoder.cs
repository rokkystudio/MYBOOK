using System.IO;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Декодирует PDF predictor после FlateDecode.
/// Поддерживает TIFF predictor 2 и PNG predictors 10..15 для 8-bit samples.
/// </summary>
internal static class PdfPredictorDecoder
{
    /// <summary>
    /// Восстанавливает raster bytes после применения PDF predictor.
    /// </summary>
    public static byte[] Decode(
        byte[] data,
        int predictor,
        int colors,
        int bitsPerComponent,
        int columns)
    {
        if (predictor is 0 or 1)
        {
            return data;
        }

        if (colors <= 0 || columns <= 0)
        {
            throw new InvalidDataException(
                "PDF predictor требует положительные Colors и Columns.");
        }

        if (bitsPerComponent != 8)
        {
            throw new InvalidDataException(
                $"PDF predictor пока поддерживает только 8 bits per component, получено {bitsPerComponent}.");
        }

        return predictor switch
        {
            2 => DecodeTiff(
                data,
                colors,
                columns),

            >= 10 and <= 15 => DecodePng(
                data,
                colors,
                columns,
                predictor),

            _ => throw new InvalidDataException(
                $"PDF predictor {predictor} не поддерживается.")
        };
    }

    private static byte[] DecodeTiff(
        byte[] data,
        int colors,
        int columns)
    {
        var rowBytes = checked(colors * columns);

        if (data.Length % rowBytes != 0)
        {
            throw new InvalidDataException(
                "TIFF predictor data length не кратна длине строки.");
        }

        var result = data.ToArray();

        for (var rowOffset = 0;
             rowOffset < result.Length;
             rowOffset += rowBytes)
        {
            for (var index = colors;
                 index < rowBytes;
                 index++)
            {
                var position = rowOffset + index;

                result[position] = unchecked(
                    (byte)(
                        result[position] +
                        result[position - colors]));
            }
        }

        return result;
    }

    private static byte[] DecodePng(
        byte[] data,
        int colors,
        int columns,
        int predictor)
    {
        var rowBytes = checked(colors * columns);
        var bytesPerPixel = colors;

        if (predictor == 15)
        {
            var encodedRowBytes = checked(rowBytes + 1);

            if (data.Length % encodedRowBytes != 0)
            {
                throw new InvalidDataException(
                    "PNG predictor 15 data length не кратна строке с filter byte.");
            }

            var rows = data.Length / encodedRowBytes;
            var result = new byte[checked(rows * rowBytes)];
            var previous = new byte[rowBytes];

            for (var row = 0; row < rows; row++)
            {
                var encodedOffset = row * encodedRowBytes;
                var filter = data[encodedOffset];
                var current = result.AsSpan(
                    row * rowBytes,
                    rowBytes);

                DecodePngRow(
                    data.AsSpan(
                        encodedOffset + 1,
                        rowBytes),
                    current,
                    previous,
                    filter,
                    bytesPerPixel);

                current.CopyTo(previous);
            }

            return result;
        }

        var fixedFilter = predictor - 10;

        if (data.Length % rowBytes != 0)
        {
            throw new InvalidDataException(
                "PNG predictor data length не кратна длине строки.");
        }

        var fixedRows = data.Length / rowBytes;
        var fixedResult = new byte[data.Length];
        var previousRow = new byte[rowBytes];

        for (var row = 0; row < fixedRows; row++)
        {
            var current = fixedResult.AsSpan(
                row * rowBytes,
                rowBytes);

            DecodePngRow(
                data.AsSpan(
                    row * rowBytes,
                    rowBytes),
                current,
                previousRow,
                fixedFilter,
                bytesPerPixel);

            current.CopyTo(previousRow);
        }

        return fixedResult;
    }

    private static void DecodePngRow(
        ReadOnlySpan<byte> encoded,
        Span<byte> output,
        ReadOnlySpan<byte> previous,
        int filter,
        int bytesPerPixel)
    {
        if (filter is < 0 or > 4)
        {
            throw new InvalidDataException(
                $"PNG predictor использует неизвестный filter {filter}.");
        }

        for (var index = 0;
             index < encoded.Length;
             index++)
        {
            var left = index >= bytesPerPixel
                ? output[index - bytesPerPixel]
                : 0;

            var up = previous[index];

            var upLeft = index >= bytesPerPixel
                ? previous[index - bytesPerPixel]
                : 0;

            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(
                    left,
                    up,
                    upLeft),
                _ => 0
            };

            output[index] = unchecked(
                (byte)(encoded[index] + predictor));
        }
    }

    private static byte Paeth(
        int left,
        int up,
        int upLeft)
    {
        var p = left + up - upLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - up);
        var pc = Math.Abs(p - upLeft);

        if (pa <= pb && pa <= pc)
        {
            return (byte)left;
        }

        if (pb <= pc)
        {
            return (byte)up;
        }

        return (byte)upLeft;
    }
}
