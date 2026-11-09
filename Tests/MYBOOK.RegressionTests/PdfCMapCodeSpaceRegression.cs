using System.Globalization;
using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Pdf;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет определение длины source codes по PDF CMap codespacerange
    /// отдельно от bfchar/cidchar mappings, включая смешанные длины,
    /// неизвестные коды, bytewise bounds, malformed ranges и PDF reader.
    /// </summary>
    private static void TestPdfCMapCodeSpaces(string directory)
    {
        var codeSpace = new PdfCMapCodeSpace();

        codeSpace.AddRange(
            [0x20],
            [0x7E]);
        codeSpace.AddRange(
            [0x81, 0x40],
            [0x9F, 0xFC]);
        codeSpace.AddRange(
            [0xA1, 0x00, 0x20],
            [0xA1, 0x00, 0x7F]);
        codeSpace.AddRange(
            [0xB1, 0x00, 0x00, 0x00],
            [0xB1, 0xFF, 0xFF, 0xFF]);

        Assert(
            codeSpace.HasRanges &&
            codeSpace.TryReadLength(
                [0x41],
                0,
                out var singleByteLength) &&
            singleByteLength == 1,
            "PDF codespacerange должен узнавать однобайтовые character codes.");

        Assert(
            codeSpace.TryReadLength(
                [0x82, 0x50],
                0,
                out var twoByteLength) &&
            twoByteLength == 2,
            "PDF codespacerange должен сверять каждую байтовую границу двухбайтового кода.");

        Assert(
            codeSpace.TryReadLength(
                [0xA1, 0x00, 0x42],
                0,
                out var threeByteLength) &&
            threeByteLength == 3,
            "PDF codespacerange должен поддерживать трёхбайтовые source codes.");

        Assert(
            codeSpace.TryReadLength(
                [0xB1, 0x12, 0x34, 0x56],
                0,
                out var fourByteLength) &&
            fourByteLength == 4,
            "PDF codespacerange должен поддерживать четырёхбайтовые source codes.");

        foreach (var outsideCodeSpace in new byte[][]
                 {
                     [0x81],
                     [0x81, 0x3F],
                     [0x9F, 0xFD],
                     [0x80, 0x50],
                     [0xA1, 0x01, 0x42],
                     [0xB2, 0x12, 0x34, 0x56]
                 })
        {
            Assert(
                !codeSpace.TryReadLength(
                    outsideCodeSpace,
                    0,
                    out var invalidLength) &&
                invalidLength == 0,
                "PDF codespacerange должен отклонять отсутствующий или не входящий в диапазон code.");
        }

        var mutation = new byte[] { 0x41, 0x81, 0x50 };
        Assert(
            codeSpace.TryReadLength(
                mutation,
                1,
                out var offsetLength) &&
            offsetLength == 2,
            "PDF codespacerange должен определять длину кода с заданного byte offset.");

        bool RejectsRange(byte[] first, byte[] last)
        {
            try
            {
                new PdfCMapCodeSpace().AddRange(
                    first,
                    last);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        Assert(
            RejectsRange([], []) &&
            RejectsRange([0x00, 0x01], [0xFF]) &&
            RejectsRange(
                [0x00, 0x00, 0x00, 0x00, 0x00],
                [0xFF, 0xFF, 0xFF, 0xFF, 0xFF]) &&
            RejectsRange([0x81, 0x50], [0x9F, 0x40]),
            "PDF codespacerange должен проверять длину 1..4 и границы каждого байта.");

        const string toUnicode =
            "/CIDInit /ProcSet findresource begin begincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n" +
            "2 begincodespacerange\n" +
            "<20><7E>\n" +
            "<8140><81FC>\n" +
            "endcodespacerange\n" +
            "3 beginbfchar\n" +
            "<41><0041>\n" +
            "<50><0050>\n" +
            "<8141><0410>\n" +
            "endbfchar\n" +
            "endcmap\n";

        var decoder =
            PdfFontDecoder.FromToUnicode(
                Encoding.ASCII.GetBytes(
                    toUnicode));

        Assert(
            string.Equals(
                decoder.Decode(
                    [0x41, 0x81, 0x41, 0x50]),
                "AАP",
                StringComparison.Ordinal),
            "PDF ToUnicode должен читать mixed one/two-byte codes по codespacerange.");

        Assert(
            string.Equals(
                decoder.Decode(
                    [0x41, 0x81, 0x50, 0x50]),
                "A\uFFFDP",
                StringComparison.Ordinal),
            "PDF ToUnicode должен заменять неизвестный двухбайтовый code одним U+FFFD.");

        Assert(
            string.Equals(
                decoder.Decode([0x81, 0x40]),
                "\uFFFD",
                StringComparison.Ordinal),
            "PDF ToUnicode должен различать valid codespace и отсутствие mapping.");

        foreach (var malformedSource in new byte[][]
                 {
                     [0x81],
                     [0x81, 0x3F],
                     [0x00]
                 })
        {
            var rejected = false;

            try
            {
                decoder.Decode(
                    malformedSource);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    "codespacerange",
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                "PDF ToUnicode должен отклонять code вне объявленного codespacerange.");
        }

        const string encodingCMap =
            "/CIDInit /ProcSet findresource begin begincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) >> def\n" +
            "2 begincodespacerange <20><7E> <8140><81FC> endcodespacerange\n" +
            "3 begincidchar <41> 50 <50> 60 <8141> 500 endcidchar\n" +
            "endcmap\n";

        var cidMap =
            PdfCidMap.FromCMap(
                Encoding.ASCII.GetBytes(
                    encodingCMap));
        var cidData = new byte[]
        {
            0x41,
            0x81, 0x41,
            0x50
        };
        var position = 0;

        Assert(
            cidMap.TryReadCid(
                cidData,
                ref position,
                out var firstCid,
                out var firstLength) &&
            firstCid == 50 &&
            firstLength == 1 &&
            position == 1 &&
            cidMap.TryReadCid(
                cidData,
                ref position,
                out var secondCid,
                out var secondLength) &&
            secondCid == 500 &&
            secondLength == 2 &&
            position == 3 &&
            cidMap.TryReadCid(
                cidData,
                ref position,
                out var thirdCid,
                out var thirdLength) &&
            thirdCid == 60 &&
            thirdLength == 1 &&
            position == 4,
            "PDF Type0 Encoding CMap должен использовать codespacerange для mixed CID codes.");

        position = 0;
        Assert(
            !cidMap.TryReadCid(
                [0x81, 0x50],
                ref position,
                out _,
                out _) &&
            position == 0 &&
            !cidMap.TryReadCid(
                [0x81],
                ref position,
                out _,
                out _) &&
            position == 0,
            "PDF Type0 Encoding CMap не должен сдвигать позицию при отсутствии mapping.");

        var noCodeSpaceUnicode =
            PdfFontDecoder.FromToUnicode(
                Encoding.ASCII.GetBytes(
                    "1 beginbfchar <41><0041> endbfchar"));

        Assert(
            string.Equals(
                noCodeSpaceUnicode.Decode([0x41]),
                "A",
                StringComparison.Ordinal),
            "PDF ToUnicode без явного codespacerange должен читать имеющиеся mappings.");

        var noCodeSpaceCid =
            PdfCidMap.FromCMap(
                Encoding.ASCII.GetBytes(
                    "1 begincidchar <41> 65 endcidchar"));
        position = 0;

        Assert(
            noCodeSpaceCid.TryReadCid(
                [0x41],
                ref position,
                out var fallbackCid,
                out var fallbackLength) &&
            fallbackCid == 65 &&
            fallbackLength == 1 &&
            position == 1,
            "PDF Type0 Encoding CMap без codespacerange должен читать mapped codes.");

        var pdfPath = Path.Combine(
            directory,
            "pdf-cmap-mixed-code-spaces.pdf");
        const string pdfContent =
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "<41814150> Tj\n" +
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
                "AАP",
                StringComparison.Ordinal),
            "PDF reader должен разделять mixed-width source codes по codespacerange.");
    }
}
