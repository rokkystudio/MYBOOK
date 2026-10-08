using System.IO;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Представляет подготовленный PDF font resource для декодирования текста
/// и вычисления горизонтального advance по исходным character codes.
/// </summary>
internal sealed class PdfFontResource
{
    private readonly PdfFontDecoder? decoder_;
    private readonly IReadOnlyDictionary<int, double> widths_;
    private readonly double defaultWidth_;
    private readonly int codeUnitLength_;
    private readonly bool applyWordSpacing_;
    private readonly PdfCidMap? cidMap_;

    public PdfFontResource(
        PdfFontDecoder? decoder,
        IReadOnlyDictionary<int, double> widths,
        double defaultWidth,
        int codeUnitLength,
        bool applyWordSpacing,
        PdfCidMap? cidMap = null)
    {
        if (codeUnitLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(codeUnitLength));
        }

        decoder_ = decoder;
        widths_ = widths;
        defaultWidth_ = defaultWidth;
        codeUnitLength_ = codeUnitLength;
        applyWordSpacing_ = applyWordSpacing;
        cidMap_ = cidMap;
    }

    /// <summary>
    /// Возвращает true, если для шрифта доступны точные PDF-widths.
    /// </summary>
    public bool HasWidthMetrics =>
        widths_.Count > 0 || defaultWidth_ > 0;

    /// <summary>
    /// Декодирует исходные character codes через ToUnicode,
    /// а при его отсутствии использует безопасный fallback PDF-string.
    /// </summary>
    public string Decode(byte[] data)
    {
        return decoder_?.Decode(data)
               ?? PdfFontDecoder.DecodeFallback(data);
    }

    /// <summary>
    /// Вычисляет горизонтальное смещение текста в user-space units
    /// по PDF glyph widths, размеру шрифта и text-state spacing.
    /// Для custom Type0 CMap исходный code сначала преобразуется в CID.
    /// </summary>
    public double MeasureAdvance(
        byte[] data,
        double fontSize,
        double characterSpacing,
        double wordSpacing,
        double horizontalScale)
    {
        if (!HasWidthMetrics || data.Length == 0)
        {
            throw new InvalidOperationException(
                "Для PDF-шрифта отсутствуют width metrics.");
        }

        var total = 0.0;
        var position = 0;

        while (position < data.Length)
        {
            int cid;
            int sourceCode;
            int length;

            if (cidMap_ != null)
            {
                var sourcePosition = position;

                if (!cidMap_.TryReadCid(
                        data,
                        ref position,
                        out cid,
                        out length))
                {
                    throw new InvalidDataException(
                        $"PDF Type0 Encoding CMap не содержит character code в позиции {sourcePosition}.");
                }

                sourceCode = ReadCode(
                    data,
                    sourcePosition,
                    length);
            }
            else
            {
                var remaining = data.Length - position;
                length = Math.Min(
                    codeUnitLength_,
                    remaining);

                sourceCode = ReadCode(
                    data,
                    position,
                    length);
                cid = sourceCode;
                position += length;
            }

            var width = widths_.TryGetValue(
                cid,
                out var explicitWidth)
                ? explicitWidth
                : defaultWidth_;

            total += width / 1000.0 * fontSize;
            total += characterSpacing;

            if (applyWordSpacing_ &&
                length == 1 &&
                sourceCode == 0x20)
            {
                total += wordSpacing;
            }
        }

        return total * horizontalScale;
    }

    private static int ReadCode(
        byte[] data,
        int position,
        int length)
    {
        if (length <= 0 ||
            length > sizeof(int) ||
            position < 0 ||
            position + length > data.Length)
        {
            throw new InvalidDataException(
                "PDF character code имеет неподдерживаемую длину.");
        }

        var code = 0;

        for (var index = 0; index < length; index++)
        {
            code = checked(
                (code << 8) |
                data[position + index]);
        }

        return code;
    }
}
