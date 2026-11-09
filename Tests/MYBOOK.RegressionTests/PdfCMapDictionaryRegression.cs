using System.Globalization;
using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats;
using Book.Formats.Pdf;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет PostScript dictionaries и literal strings в PDF ToUnicode и
    /// Type0 Encoding CMap, вложенные словари, строгую проверку hex и closing
    /// delimiters, а также полный PDF reader pipeline с CIDSystemInfo.
    /// </summary>
    private static void TestPdfCMapDictionaries(string directory)
    {
        const string toUnicode =
            "/CIDInit /ProcSet findresource begin\n" +
            "12 dict begin\n" +
            "begincmap\n" +
            "/CIDSystemInfo << /Registry(Adobe) /Ordering(UCS) /Supplement 0 >> def\n" +
            "/CMapName /Adobe-Identity-UCS def\n" +
            "1 begincodespacerange\n" +
            "<01><02>\n" +
            "endcodespacerange\n" +
            "2 beginbfchar\n" +
            "<01><0410>\n" +
            "<02><0411>\n" +
            "endbfchar\n" +
            "endcmap\n";

        var decoder =
            PdfFontDecoder.FromToUnicode(
                Encoding.ASCII.GetBytes(
                    toUnicode));

        Assert(
            string.Equals(
                decoder.Decode([0x01, 0x02]),
                "АБ",
                StringComparison.Ordinal),
            "PDF ToUnicode CIDSystemInfo dictionary должен сохранять bfchar mapping.");

        var nestedToUnicode =
            "/CIDInit /ProcSet findresource begin begincmap\n" +
            "/CIDSystemInfo<</Registry (Adobe)" +
            "/Extra<< /Text (nested (literal) \\(quoted\\) <<not-a-dictionary>>) >>" +
            "/Ordering (UCS) /Supplement 0>>def\n" +
            "1 begincodespacerange <01> <02> endcodespacerange\n" +
            "1 beginbfrange <01> <02> [<0041> <0042>] endbfrange\n" +
            "endcmap\n";

        var nestedDecoder =
            PdfFontDecoder.FromToUnicode(
                Encoding.ASCII.GetBytes(
                    nestedToUnicode));

        Assert(
            string.Equals(
                nestedDecoder.Decode([0x01, 0x02]),
                "AB",
                StringComparison.Ordinal),
            "PDF ToUnicode nested dictionaries и literal strings не должны менять bfrange.");

        const string cidEncoding =
            "/CIDInit /ProcSet findresource begin\n" +
            "12 dict begin begincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe)" +
            " /Ordering (Identity) /Supplement 0 >> def\n" +
            "1 begincodespacerange\n" +
            "<01> <02>\n" +
            "endcodespacerange\n" +
            "2 begincidchar\n" +
            "<01> 150\n" +
            "<02> 151\n" +
            "endcidchar\n" +
            "endcmap\n";

        var cidMap =
            PdfCidMap.FromCMap(
                Encoding.ASCII.GetBytes(
                    cidEncoding));
        var position = 0;

        Assert(
            cidMap.TryReadCid(
                [0x01, 0x02],
                ref position,
                out var firstCid,
                out var firstLength) &&
            firstCid == 150 &&
            firstLength == 1 &&
            position == 1 &&
            cidMap.TryReadCid(
                [0x01, 0x02],
                ref position,
                out var secondCid,
                out var secondLength) &&
            secondCid == 151 &&
            secondLength == 1 &&
            position == 2,
            "PDF Type0 Encoding CIDSystemInfo dictionary должен сохранять cidchar mapping.");

        var nestedCidEncoding =
            "/CIDInit /ProcSet findresource begin begincmap\n" +
            "/CIDSystemInfo<</Registry (Adobe)" +
            "/Extra<< /Text (literal (nested) \\(quoted\\) <<ignored>>) >>" +
            "/Ordering(Identity)>> def\n" +
            "1 begincodespacerange <01> <02> endcodespacerange\n" +
            "1 begincidrange <01> <02> 400 endcidrange\n" +
            "endcmap\n";
        var nestedCidMap =
            PdfCidMap.FromCMap(
                Encoding.ASCII.GetBytes(
                    nestedCidEncoding));
        position = 0;

        Assert(
            nestedCidMap.TryReadCid(
                [0x02],
                ref position,
                out var nestedCid,
                out var nestedLength) &&
            nestedCid == 401 &&
            nestedLength == 1,
            "PDF Type0 Encoding nested dictionaries должны сохранять cidrange.");

        bool RejectsInvalid(
            string source,
            bool toUnicodeCMap,
            string expectedMessage)
        {
            try
            {
                if (toUnicodeCMap)
                {
                    PdfFontDecoder.FromToUnicode(
                        Encoding.ASCII.GetBytes(
                            source));
                }
                else
                {
                    PdfCidMap.FromCMap(
                        Encoding.ASCII.GetBytes(
                            source));
                }

                return false;
            }
            catch (InvalidDataException exception)
            {
                return exception.Message.Contains(
                    expectedMessage,
                    StringComparison.Ordinal);
            }
        }

        foreach (var isToUnicode in new[]
                 {
                     true,
                     false
                 })
        {
            var source = isToUnicode
                ? toUnicode
                : cidEncoding;
            var malformedHex = source.Replace(
                "<01>",
                "<0G>",
                StringComparison.Ordinal);

            Assert(
                RejectsInvalid(
                    malformedHex,
                    isToUnicode,
                    "hex-цифру"),
                "PDF CMap parser должен отклонять non-hex цифры в codespacerange.");

            Assert(
                RejectsInvalid(
                    source.Replace(
                        " >> def",
                        " def",
                        StringComparison.Ordinal),
                    isToUnicode,
                    "незакрытый dictionary"),
                "PDF CMap parser должен отклонять незакрытый dictionary.");

            Assert(
                RejectsInvalid(
                    source.Replace(
                        " >> def",
                        " >> >> def",
                        StringComparison.Ordinal),
                    isToUnicode,
                    "dictionary terminator"),
                "PDF CMap parser должен отклонять лишний closing dictionary delimiter.");

            Assert(
                RejectsInvalid(
                    source.Replace(
                        "/Supplement 0",
                        "/Supplement (unclosed",
                        StringComparison.Ordinal),
                    isToUnicode,
                    "незакрытый literal string"),
                "PDF CMap parser должен отклонять незакрытый literal string.");
        }

        var pdfPath =
            Path.Combine(
                directory,
                "pdf-cmap-dictionary.pdf");
        var pdfContent =
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "<0102> Tj\n" +
            "ET\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(pdfContent)} >>\nstream\n{pdfContent}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 6 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(toUnicode)} >>\nstream\n{toUnicode}endstream"
        };

        using (var memory = new MemoryStream())
        {
            void Write(string value)
            {
                var bytes = Encoding.ASCII.GetBytes(value);
                memory.Write(bytes);
            }

            Write("%PDF-1.4\n");
            var offsets = new List<long>();

            for (var index = 0; index < objects.Length; index++)
            {
                offsets.Add(memory.Position);
                Write(
                    $"{index + 1} 0 obj\n" +
                    objects[index] +
                    "\nendobj\n");
            }

            var xrefOffset = memory.Position;

            Write(
                "xref\n" +
                $"0 {objects.Length + 1}\n" +
                "0000000000 65535 f \n");

            foreach (var offset in offsets)
            {
                Write(
                    offset.ToString(
                        "0000000000",
                        CultureInfo.InvariantCulture) +
                    " 00000 n \n");
            }

            Write(
                "trailer\n" +
                $"<< /Size {objects.Length + 1} /Root 1 0 R >>\n" +
                "startxref\n" +
                xrefOffset.ToString(
                    CultureInfo.InvariantCulture) +
                "\n%%EOF\n");

            File.WriteAllBytes(
                pdfPath,
                memory.ToArray());
        }

        var pdf =
            SupportedFormatRegistry.ReadDocument(
                pdfPath);
        var page =
            pdf.Blocks
                .OfType<DocumentFixedPage>()
                .Single();

        Assert(
            page.Text.Contains(
                "АБ",
                StringComparison.Ordinal),
            "PDF reader должен декодировать ToUnicode из CIDSystemInfo dictionary.");
    }
}
