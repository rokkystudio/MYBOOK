using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Diagnostics;
using Book.Documents;
using Book.Formats.Doc;
using Book.Formats.Pdf;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

/// <summary>
/// Запускает автономные regression-тесты readers без WPF-окон и WebView2.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var repositoryRoot = ResolveRepositoryRoot(args);
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "MYBOOK-Regression-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temporaryDirectory);

        var tests = new (string Name, Action Test)[]
        {
            ("Supported format registry", TestSupportedFormatRegistry),
            ("TXT reader", () => TestTextReader(temporaryDirectory)),
            ("Markdown reader", () => TestMarkdownReader(temporaryDirectory)),
            ("RTF reader", () => TestRtfReader(temporaryDirectory)),
            (".doc RTF dispatch", () => TestDocRtfDispatch(temporaryDirectory)),
            ("DOC field model", TestDocFieldModel),
            ("DOC inline picture", TestDocInlinePicture),
            ("DOC floating shape tables", TestDocFloatingShapeTables),
            ("DOC floating shape image", TestDocFloatingShapeImage),
            ("DOC header story table", TestDocHeaderStoryTable),
            ("DOC section table", TestDocSectionTable),
            ("DOC paragraph properties", TestDocParagraphProperties),
            ("DOC table definition", TestDocTableDefinition),
            ("DOC footnote tables", TestDocFootnoteTables),
            ("DOC endnote tables", TestDocEndnoteTables),
            ("DOC binary reader", () => TestDocBinaryReader(repositoryRoot)),
            ("FB2 reader", () => TestFb2Reader(temporaryDirectory)),
            ("EPUB reader", () => TestEpubReader(temporaryDirectory)),
            ("EPUB NCX reader", () => TestEpubNcxReader(temporaryDirectory)),
            ("EPUB corpus compatibility", () => TestEpubCorpusCompatibility(repositoryRoot)),
            ("DOCX reader", () => TestDocxReader(temporaryDirectory)),
            ("PDF LZW decoder", TestPdfLzwDecoder),
            ("PDF reader", () => TestPdfReader(temporaryDirectory)),
            ("Parser trace", () => TestParserTrace(temporaryDirectory)),
            (".doc corpus dispatch", () => TestDocCorpusDispatch(repositoryRoot))
        };

        var failures = 0;

        try
        {
            foreach (var test in tests)
            {
                try
                {
                    test.Test();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine(
                        $"FAIL {test.Name}: {error.GetType().Name}: {error.Message}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(
                    temporaryDirectory,
                    recursive: true);
            }
            catch
            {
                // Temp cleanup must not hide the actual regression result.
            }
        }

        Console.WriteLine(
            $"RESULT passed={tests.Length - failures} failed={failures}");

        return failures == 0
            ? 0
            : 1;
    }

    private static void TestSupportedFormatRegistry()
    {
        var formats = SupportedFormatRegistry.All;

        AssertEqual(
            10,
            formats.Count,
            "Количество зарегистрированных форматов");

        AssertEqual(
            formats.Count,
            formats
                .Select(format => format.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            "Расширения реестра должны быть уникальными");

        Assert(
            SupportedFormatRegistry.GetByExtension(".html").OpenDirectlyInWebView,
            "HTML должен открываться напрямую в WebView2.");

        Assert(
            SupportedFormatRegistry.GetByExtension(".pdf").Reader != null,
            "PDF должен иметь DocumentModel reader.");

        var filter = SupportedFormatRegistry.BuildOpenFileDialogFilter(
            "Supported documents");

        foreach (var format in formats)
        {
            AssertContains(
                filter,
                "*" + format.Extension,
                "OpenFileDialog filter");
        }
    }

    private static void TestTextReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.txt");

        File.WriteAllText(
            path,
            "TXT marker\nSecond line",
            Encoding.UTF8);

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "TXT marker",
            "TXT model");
    }

    private static void TestMarkdownReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.md");

        File.WriteAllText(
            path,
            "# Regression heading\n\nMarkdown marker **bold**",
            Encoding.UTF8);

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "Markdown marker",
            "Markdown model");
    }

    private static void TestRtfReader(string directory)
    {
        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        var path = Path.Combine(
            directory,
            "regression.rtf");

        var rtf = """
            {\rtf1\ansi\ansicpg1252\uc1
            {\fonttbl{\f0 Arial;}{\f1\fcharset204 Times New Roman;}{\f2\fcharset128 MS Gothic;}}
            {\colortbl;\red255\green0\blue0;\red255\green255\blue0;}
            {\*\bkmkstart bookmark}
            \qc\li720\ri360\fi-180\sb240\sa120\sl360\f1\fs28\cf1\highlight2\b\i\ul Styled Привет\b0\i0\ulnone\highlight0\cf0\par
            \pard\sl360\slmult1\keep\keepn\widctl\rtlpar Paragraph controls\par
            \pard Unicode \u1046?\par
            \f2 \'82\'a0\par
            \f0 \strike strike\strike0 \super sup\nosupersub \sub sub\nosupersub\par
            {\field{\*\fldinst HYPERLINK "https://example.com/rtf"}{\fldrslt External RTF link}}\par
            {\field{\*\fldinst HYPERLINK \l "bookmark"}{\fldrslt Internal RTF link}}\par
            {\field{\*\fldinst HYPERLINK "file:///C:/secret.txt"}{\fldrslt Unsafe RTF link}}\par
            \trowd\intbl Cell A\cell Cell B\cell\row
            \trowd\clmgf\cellx1000\clmrg\cellx2000\intbl Merged RTF cell\cell\cell\row
            \trowd\clvmgf\cellx1000\cellx2000\intbl Vertical RTF cell\cell Row 3 B\cell\row
            \trowd\clvmrg\cellx1000\cellx2000\intbl\cell Row 4 B\cell\row
            \pard
            {\pict\pngblip 89504E470D0A1A0A0000000D4948445200000001000000010804000000B51C0C020000000B4944415478DA63FCFF1F0002EB01F56950DA2A0000000049454E44AE426082}\par
            {\pict\dibitmap0 280000000100000001000000010018000000000004000000000000000000000000000000000000000000FF00}\par
            }
            """;

        File.WriteAllText(
            path,
            rtf,
            Encoding.GetEncoding(1251));

        var model = SupportedFormatRegistry.ReadDocument(path);

        var text = FlattenText(model);

        AssertContains(
            text,
            "Styled Привет",
            "RTF codepage text");

        AssertContains(
            text,
            "Unicode Ж",
            "RTF unicode escape");

        AssertContains(
            text,
            "あ",
            "RTF DBCS font charset switching");

        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        Assert(
            paragraphs.Any(paragraph =>
                string.Equals(
                    paragraph.AnchorId,
                    "bookmark",
                    StringComparison.Ordinal)),
            "RTF bookmark должен сохраняться как внутренний anchor.");

        var formattedParagraph = paragraphs.First(paragraph =>
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains(
                    "Styled Привет",
                    StringComparison.Ordinal)));

        Assert(
            formattedParagraph.Alignment == DocumentParagraphAlignment.Center &&
            formattedParagraph.LeftIndentPoints == 36 &&
            formattedParagraph.RightIndentPoints == 18 &&
            formattedParagraph.FirstLineIndentPoints == -9 &&
            formattedParagraph.SpaceBeforePoints == 12 &&
            formattedParagraph.SpaceAfterPoints == 6 &&
            formattedParagraph.LineSpacingPoints == 18,
            "RTF paragraph alignment, indents и spacing должны сохраняться.");

        var controlledParagraph = paragraphs.First(paragraph =>
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains(
                    "Paragraph controls",
                    StringComparison.Ordinal)));

        Assert(
            controlledParagraph.LineSpacingMultiple == 1.5 &&
            controlledParagraph.KeepTogether &&
            controlledParagraph.KeepWithNext &&
            controlledParagraph.WidowControl &&
            controlledParagraph.RightToLeft,
            "RTF slmult, keep, keepn, widctl и rtlpar должны сохраняться.");

        var inlines = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .ToArray();

        var styled = inlines.FirstOrDefault(inline =>
            inline.Text.Contains(
                "Styled Привет",
                StringComparison.Ordinal));

        Assert(
            styled != null &&
            styled.Bold &&
            styled.Italic &&
            styled.Underline &&
            string.Equals(
                styled.FontFamily,
                "Times New Roman",
                StringComparison.Ordinal) &&
            styled.FontSizePoints == 14 &&
            string.Equals(
                styled.TextColor,
                "#FF0000",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                styled.BackgroundColor,
                "#FFFF00",
                StringComparison.OrdinalIgnoreCase),
            "RTF font table, size, foreground/background и базовые стили должны сохраняться.");

        Assert(
            inlines.Any(inline =>
                inline.Strikethrough &&
                inline.Text.Contains(
                    "strike",
                    StringComparison.Ordinal)),
            "RTF strike должен сохраняться.");

        Assert(
            inlines.Any(inline =>
                inline.Superscript &&
                inline.Text.Contains(
                    "sup",
                    StringComparison.Ordinal)),
            "RTF superscript должен сохраняться.");

        Assert(
            inlines.Any(inline =>
                inline.Subscript &&
                inline.Text.Contains(
                    "sub",
                    StringComparison.Ordinal)),
            "RTF subscript должен сохраняться.");

        Assert(
            inlines.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "https://example.com/rtf",
                    StringComparison.Ordinal)),
            "RTF external HYPERLINK должен сохраняться.");

        Assert(
            inlines.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#bookmark",
                    StringComparison.Ordinal)),
            "RTF internal HYPERLINK должен сохраняться.");

        Assert(
            !inlines.Any(inline =>
                inline.LinkHref?.StartsWith(
                    "file:",
                    StringComparison.OrdinalIgnoreCase) == true),
            "RTF unsafe file: hyperlink не должен попадать в модель.");

        var table = model.Blocks
            .OfType<DocumentTable>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                "RTF table controls должны создавать DocumentTable.");

        Assert(
            table.Rows.Count == 4 &&
            table.Rows[0].Cells.Count == 2 &&
            table.Rows[0].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Cell A",
                    StringComparison.Ordinal)) &&
            table.Rows[0].Cells[1].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Cell B",
                    StringComparison.Ordinal)),
            "RTF table controls должны сохранять строки и ячейки.");

        Assert(
            table.Rows[1].Cells.Count == 1 &&
            table.Rows[1].Cells[0].ColumnSpan == 2 &&
            table.Rows[1].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Merged RTF cell",
                    StringComparison.Ordinal)),
            "RTF clmgf/clmrg должны сохраняться как ColumnSpan.");

        Assert(
            table.Rows[2].Cells.Count == 2 &&
            table.Rows[2].Cells[0].RowSpan == 2 &&
            table.Rows[2].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Vertical RTF cell",
                    StringComparison.Ordinal)) &&
            table.Rows[3].Cells.Count == 1 &&
            table.Rows[3].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Row 4 B",
                    StringComparison.Ordinal)),
            "RTF clvmgf/clvmrg должны сохраняться как RowSpan.");

        Assert(
            model.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    inline.Image != null &&
                    string.Equals(
                        inline.Image.ContentType,
                        "image/png",
                        StringComparison.Ordinal)),
            "RTF pict pngblip должен сохранять встроенное PNG-изображение.");

        var bmpImage = model.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Select(inline => inline.Image)
            .FirstOrDefault(image =>
                image != null &&
                string.Equals(
                    image.ContentType,
                    "image/bmp",
                    StringComparison.Ordinal));

        Assert(
            bmpImage != null &&
            bmpImage.Data.Length > 14 &&
            bmpImage.Data[0] == (byte)'B' &&
            bmpImage.Data[1] == (byte)'M',
            "RTF dibitmap должен преобразовываться в полноценный BMP.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "font-size:14pt",
            "RTF rendered font size");

        AssertContains(
            html,
            "color:#FF0000",
            "RTF rendered text color");

        AssertContains(
            html,
            "background-color:#FFFF00",
            "RTF rendered background color");

        AssertContains(
            html,
            "line-height:1.5",
            "RTF rendered line spacing multiplier");

        AssertContains(
            html,
            "break-inside:avoid",
            "RTF rendered keep together");

        AssertContains(
            html,
            "break-after:avoid",
            "RTF rendered keep with next");

        AssertContains(
            html,
            "orphans:2;widows:2",
            "RTF rendered widow control");

        AssertContains(
            html,
            "direction:rtl",
            "RTF rendered right-to-left paragraph");

        AssertContains(
            html,
            "<table class=\"flow-table\"",
            "RTF rendered table");

        AssertContains(
            html,
            "colspan=\"2\"",
            "RTF rendered horizontal merge");

        AssertContains(
            html,
            "rowspan=\"2\"",
            "RTF rendered vertical merge");

        AssertContains(
            html,
            "class=\"inline-image\"",
            "RTF rendered image");

        AssertContains(
            html,
            "href=\"https://example.com/rtf\"",
            "RTF rendered external hyperlink");

        AssertContains(
            html,
            "href=\"#anchor-bookmark\"",
            "RTF rendered internal hyperlink");

        Assert(
            !html.Contains(
                "href=\"file:",
                StringComparison.OrdinalIgnoreCase),
            "RTF renderer не должен выпускать unsafe file: hyperlink.");
    }

    private static void TestDocRtfDispatch(string directory)
    {
        var path = Path.Combine(
            directory,
            "rtf-disguised-as-doc.doc");

        File.WriteAllText(
            path,
            @"{\rtf1\ansi DOC dispatch marker\par}",
            Encoding.ASCII);

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "DOC dispatch marker",
            ".doc RTF dispatch model");
    }

    private static void TestDocFieldModel()
    {
        var text =
            "Before " +
            '\u0013' +
            " HYPERLINK \"https://example.com/doc\" " +
            '\u0014' +
            "External DOC link" +
            '\u0015' +
            " / " +
            '\u0013' +
            " HYPERLINK \\l \"bookmark\" " +
            '\u0014' +
            "Internal DOC link" +
            '\u0015' +
            " / " +
            '\u0013' +
            " HYPERLINK \"file:///C:/secret.txt\" " +
            '\u0014' +
            "Unsafe DOC link" +
            '\u0015' +
            '\r' +
            "Page one" +
            '\u000C' +
            "Page two";

        var blocks = DocDocumentReader.BuildParagraphs(text);

        var paragraphs = blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        var inlines = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .ToArray();

        Assert(
            inlines.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "https://example.com/doc",
                    StringComparison.Ordinal) &&
                inline.Text.Contains(
                    "External DOC link",
                    StringComparison.Ordinal)),
            "DOC HYPERLINK field должен создавать безопасную внешнюю ссылку.");

        Assert(
            inlines.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#bookmark",
                    StringComparison.Ordinal) &&
                inline.Text.Contains(
                    "Internal DOC link",
                    StringComparison.Ordinal)),
            "DOC HYPERLINK \\l field должен создавать внутреннюю ссылку.");

        Assert(
            inlines.Any(inline =>
                inline.LinkHref == null &&
                inline.Text.Contains(
                    "Unsafe DOC link",
                    StringComparison.Ordinal)),
            "DOC unsafe HYPERLINK должен сохранять result text без href.");

        Assert(
            blocks
                .OfType<DocumentFlowBreak>()
                .Any(flowBreak =>
                    flowBreak.Kind == DocumentFlowBreakKind.Page),
            "DOC form-feed должен сохраняться как page break.");

        var model = new DocumentModel
        {
            Title = "DOC fields",
            Blocks = blocks
        };

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "href=\"https://example.com/doc\"",
            "DOC rendered external hyperlink");

        AssertContains(
            html,
            "href=\"#anchor-bookmark\"",
            "DOC rendered internal hyperlink");

        Assert(
            !html.Contains(
                "href=\"file:",
                StringComparison.OrdinalIgnoreCase),
            "DOC renderer не должен выпускать unsafe file: hyperlink.");
    }

    private static void TestDocInlinePicture()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=");

        var recordLength =
            17 +
            png.Length;
        var totalLength =
            68 +
            8 +
            recordLength;
        var data = new byte[totalLength];

        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(
                0,
                4),
            totalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(
                4,
                2),
            0x44);

        var recordOffset = 68;
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(
                recordOffset,
                2),
            0x6E00);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(
                recordOffset + 2,
                2),
            0xF01E);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(
                recordOffset + 4,
                4),
            (uint)recordLength);

        var payloadOffset =
            recordOffset +
            8;
        data[payloadOffset + 16] =
            0xFF;
        png.CopyTo(
            data,
            payloadOffset + 17);

        var image = DocDocumentReader.ReadInlinePicture(
            data,
            0)
            ?? throw new InvalidOperationException(
                "DOC inline picture parser должен вернуть PNG image.");

        AssertEqual(
            "image/png",
            image.ContentType,
            "DOC inline picture content type");

        Assert(
            image.Data.SequenceEqual(png),
            "DOC inline picture parser должен вернуть исходные PNG bytes.");
    }

    private static void TestDocFloatingShapeTables()
    {
        const int shapeId = 1024;
        const int ccpText = 10;

        var plc = new byte[34];

        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                0,
                4),
            2);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                4,
                4),
            3);

        var spaOffset = 8;

        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                spaOffset,
                4),
            shapeId);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                spaOffset + 4,
                4),
            720);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                spaOffset + 8,
                4),
            1440);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                spaOffset + 12,
                4),
            2160);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                spaOffset + 16,
                4),
            2880);

        const ushort flags =
            (1 << 1) |
            (2 << 3) |
            (2 << 5) |
            (1 << 9) |
            0x4000 |
            0x8000;

        BinaryPrimitives.WriteUInt16LittleEndian(
            plc.AsSpan(
                spaOffset + 20,
                2),
            flags);

        var shapes = DocDocumentReader.ReadMainDocumentShapeAnchors(
            plc,
            0,
            plc.Length,
            ccpText);

        var shape = shapes.Single();

        AssertEqual(
            2,
            shape.Cp,
            "DOC PlcfSpa CP");
        AssertEqual(
            shapeId,
            shape.ShapeId,
            "DOC PlcfSpa shape id");
        AssertEqual(
            720,
            shape.LeftTwips,
            "DOC Spa left");
        AssertEqual(
            2880,
            shape.BottomTwips,
            "DOC Spa bottom");
        AssertEqual(
            1,
            shape.HorizontalOrigin,
            "DOC Spa horizontal origin");
        AssertEqual(
            2,
            shape.VerticalOrigin,
            "DOC Spa vertical origin");
        AssertEqual(
            2,
            shape.WrapStyle,
            "DOC Spa wrap style");
        AssertEqual(
            1,
            shape.WrapSide,
            "DOC Spa wrap side");

        Assert(
            shape.BelowText &&
            shape.AnchorLocked,
            "DOC Spa below-text и anchor-lock flags должны сохраняться.");

        var dgg = new byte[16];

        BinaryPrimitives.WriteUInt16LittleEndian(
            dgg.AsSpan(
                0,
                2),
            0x0012);
        BinaryPrimitives.WriteUInt16LittleEndian(
            dgg.AsSpan(
                2,
                2),
            0xF00A);
        BinaryPrimitives.WriteUInt32LittleEndian(
            dgg.AsSpan(
                4,
                4),
            8);
        BinaryPrimitives.WriteInt32LittleEndian(
            dgg.AsSpan(
                8,
                4),
            shapeId);

        var shapeIds = DocDocumentReader.ReadOfficeArtShapeIds(
            dgg,
            0,
            dgg.Length);

        Assert(
            shapeIds.SetEquals(
                [shapeId]),
            "DOC DggInfo parser должен находить OfficeArtFSP.spid.");
    }

    private static void TestDocFloatingShapeImage()
    {
        const int shapeId = 2048;

        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=");

        var blipPayloadLength =
            17 +
            png.Length;
        var blipRecord = new byte[
            8 +
            blipPayloadLength];

        BinaryPrimitives.WriteUInt16LittleEndian(
            blipRecord.AsSpan(
                0,
                2),
            0x6E00);
        BinaryPrimitives.WriteUInt16LittleEndian(
            blipRecord.AsSpan(
                2,
                2),
            0xF01E);
        BinaryPrimitives.WriteUInt32LittleEndian(
            blipRecord.AsSpan(
                4,
                4),
            (uint)blipPayloadLength);
        blipRecord[8 + 16] =
            0xFF;
        png.CopyTo(
            blipRecord,
            8 + 17);

        var fbsePayload = new byte[
            36 +
            blipRecord.Length];
        blipRecord.CopyTo(
            fbsePayload,
            36);

        var fbseRecord = new byte[
            8 +
            fbsePayload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            fbseRecord.AsSpan(
                0,
                2),
            0x0062);
        BinaryPrimitives.WriteUInt16LittleEndian(
            fbseRecord.AsSpan(
                2,
                2),
            0xF007);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fbseRecord.AsSpan(
                4,
                4),
            (uint)fbsePayload.Length);
        fbsePayload.CopyTo(
            fbseRecord,
            8);

        var bstore = new byte[
            8 +
            fbseRecord.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            bstore.AsSpan(
                0,
                2),
            0x001F);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bstore.AsSpan(
                2,
                2),
            0xF001);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bstore.AsSpan(
                4,
                4),
            (uint)fbseRecord.Length);
        fbseRecord.CopyTo(
            bstore,
            8);

        var fsp = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(
            fsp.AsSpan(
                0,
                2),
            0x0012);
        BinaryPrimitives.WriteUInt16LittleEndian(
            fsp.AsSpan(
                2,
                2),
            0xF00A);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fsp.AsSpan(
                4,
                4),
            8);
        BinaryPrimitives.WriteInt32LittleEndian(
            fsp.AsSpan(
                8,
                4),
            shapeId);

        var fopt = new byte[14];
        BinaryPrimitives.WriteUInt16LittleEndian(
            fopt.AsSpan(
                0,
                2),
            0x0013);
        BinaryPrimitives.WriteUInt16LittleEndian(
            fopt.AsSpan(
                2,
                2),
            0xF00B);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fopt.AsSpan(
                4,
                4),
            6);
        BinaryPrimitives.WriteUInt16LittleEndian(
            fopt.AsSpan(
                8,
                2),
            0x4104);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fopt.AsSpan(
                10,
                4),
            1);

        var spContainer = new byte[
            8 +
            fsp.Length +
            fopt.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            spContainer.AsSpan(
                0,
                2),
            0x000F);
        BinaryPrimitives.WriteUInt16LittleEndian(
            spContainer.AsSpan(
                2,
                2),
            0xF004);
        BinaryPrimitives.WriteUInt32LittleEndian(
            spContainer.AsSpan(
                4,
                4),
            (uint)(fsp.Length + fopt.Length));
        fsp.CopyTo(
            spContainer,
            8);
        fopt.CopyTo(
            spContainer,
            8 + fsp.Length);

        var dgg = new byte[
            bstore.Length +
            spContainer.Length];
        bstore.CopyTo(
            dgg,
            0);
        spContainer.CopyTo(
            dgg,
            bstore.Length);

        var images = DocDocumentReader.ReadOfficeArtShapeImages(
            dgg,
            0,
            dgg.Length);

        Assert(
            images.TryGetValue(
                shapeId,
                out var image),
            "DOC floating shape FOPT.pib должен разрешаться через BStore.");

        AssertEqual(
            "image/png",
            image!.ContentType,
            "DOC floating shape image content type");

        Assert(
            image.Data.SequenceEqual(png),
            "DOC floating shape должен возвращать исходные PNG bytes из FBSE/BLIP.");

        var blocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    shapeId,
                    0,
                    0,
                    1440,
                    1440,
                    0,
                    0,
                    2,
                    0,
                    false,
                    false)
            ],
            images);

        var floatingInline = blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .SingleOrDefault(inline =>
                inline.Image != null);

        Assert(
            floatingInline?.Image != null &&
            floatingInline.Image.Data.SequenceEqual(png),
            "DOC U+0008 anchor должен вставлять разрешённый floating raster image в reflow model.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC floating shape",
                Blocks = blocks
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "class=\"inline-image\"",
            "DOC floating shape rendered image");
    }

    private static void TestDocHeaderStoryTable()
    {
        var cps = new[]
        {
            0, 0, 0, 0, 0, 0, 0,
            4, 4, 8, 8, 12, 16,
            16, 20, 20, 24, 24, 28,
            0
        };
        var plc = new byte[cps.Length * 4];

        for (var index = 0;
             index < cps.Length;
             index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                plc.AsSpan(
                    index * 4,
                    4),
                cps[index]);
        }

        var stories =
            DocDocumentReader.ReadHeaderFooterStoryDescriptors(
                plc,
                ccpHdd: 29);

        AssertEqual(
            7,
            stories.Count,
            "DOC PlcfHdd non-empty header/footer stories");

        Assert(
            stories.Any(story =>
                story.SectionIndex == 0 &&
                story.Kind == DocumentFlowRegionKind.Header &&
                story.Variant == DocumentFlowRegionVariant.Even &&
                story.LocalStart == 0 &&
                story.ContentLength == 3),
            "DOC PlcfHdd even header должен исключать guard paragraph.");

        Assert(
            stories.Any(story =>
                story.SectionIndex == 0 &&
                story.Kind == DocumentFlowRegionKind.Footer &&
                story.Variant == DocumentFlowRegionVariant.First &&
                story.LocalStart == 12 &&
                story.ContentLength == 3),
            "DOC PlcfHdd first footer должен сохранять variant.");

        Assert(
            stories.Any(story =>
                story.SectionIndex == 1 &&
                story.Kind == DocumentFlowRegionKind.Header &&
                story.Variant == DocumentFlowRegionVariant.Odd &&
                story.LocalStart == 16 &&
                story.ContentLength == 3),
            "DOC PlcfHdd должен сохранять section index для odd header.");

        Assert(
            stories.Any(story =>
                story.SectionIndex == 1 &&
                story.Kind == DocumentFlowRegionKind.Footer &&
                story.Variant == DocumentFlowRegionVariant.Odd &&
                story.LocalStart == 20 &&
                story.ContentLength == 3),
            "DOC PlcfHdd должен сохранять odd footer второй секции.");
    }

    private static void TestDocSectionTable()
    {
        var cps = new[]
        {
            0,
            5,
            9
        };
        var plc = new byte[
            cps.Length * 4 +
            2 * 12];

        for (var index = 0;
             index < cps.Length;
             index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                plc.AsSpan(
                    index * 4,
                    4),
                cps[index]);
        }

        const int firstSepxOffset = 64;
        const int secondSepxOffset = 160;
        var sedOffset = cps.Length * 4;

        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                sedOffset + 2,
                4),
            firstSepxOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            plc.AsSpan(
                sedOffset + 12 + 2,
                4),
            secondSepxOffset);

        var sections =
            DocDocumentReader.ReadSectionDescriptors(
                plc,
                ccpText: 9);

        Assert(
            sections.Count == 2 &&
            sections[0].SectionIndex == 0 &&
            sections[0].CpStart == 0 &&
            sections[0].CpEnd == 5 &&
            sections[0].FcSepx == firstSepxOffset &&
            sections[1].SectionIndex == 1 &&
            sections[1].CpStart == 5 &&
            sections[1].CpEnd == 9 &&
            sections[1].FcSepx == secondSepxOffset,
            "DOC PlcfSed должен сохранять CP ranges и Sed.fcSepx.");

        var sectionBreaks =
            DocDocumentReader.ReadSectionBreakCps(
                plc,
                ccpText: 9);

        Assert(
            sectionBreaks.SetEquals(
                new[]
                {
                    4
                }),
            "DOC PlcfSed должен возвращать CP завершающего 0x0C первой секции.");

        var word = new byte[256];
        var grpprl = new List<byte>();

        void AddByteSprm(
            ushort sprm,
            byte value)
        {
            grpprl.Add(
                (byte)(sprm & 0xFF));
            grpprl.Add(
                (byte)(sprm >> 8));
            grpprl.Add(value);
        }

        void AddUInt16Sprm(
            ushort sprm,
            ushort value)
        {
            grpprl.Add(
                (byte)(sprm & 0xFF));
            grpprl.Add(
                (byte)(sprm >> 8));

            Span<byte> operand =
                stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(
                operand,
                value);
            grpprl.AddRange(
                operand.ToArray());
        }

        void AddInt16Sprm(
            ushort sprm,
            short value)
        {
            grpprl.Add(
                (byte)(sprm & 0xFF));
            grpprl.Add(
                (byte)(sprm >> 8));

            Span<byte> operand =
                stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(
                operand,
                value);
            grpprl.AddRange(
                operand.ToArray());
        }

        AddByteSprm(
            0x3009,
            4);
        AddByteSprm(
            0x300A,
            1);
        AddByteSprm(
            0x301D,
            2);
        AddUInt16Sprm(
            0xB01F,
            15840);
        AddUInt16Sprm(
            0xB020,
            12240);
        AddUInt16Sprm(
            0xB021,
            1440);
        AddUInt16Sprm(
            0xB022,
            1080);
        AddInt16Sprm(
            0x9023,
            -720);
        AddInt16Sprm(
            0x9024,
            1440);
        AddUInt16Sprm(
            0xB025,
            360);
        AddUInt16Sprm(
            0xB017,
            720);
        AddUInt16Sprm(
            0xB018,
            720);
        AddByteSprm(
            0x3228,
            1);

        BinaryPrimitives.WriteInt16LittleEndian(
            word.AsSpan(
                firstSepxOffset,
                2),
            checked((short)grpprl.Count));
        grpprl.CopyTo(
            word,
            firstSepxOffset + 2);

        BinaryPrimitives.WriteInt16LittleEndian(
            word.AsSpan(
                secondSepxOffset,
                2),
            0);

        var properties =
            DocDocumentReader.ReadSectionProperties(
                word,
                sections[0]);

        Assert(
            properties.SectionIndex == 0 &&
            properties.BreakKind == DocumentSectionBreakKind.OddPage &&
            properties.Orientation == DocumentPageOrientation.Landscape &&
            properties.PageWidthPoints == 792 &&
            properties.PageHeightPoints == 612 &&
            properties.MarginLeftPoints == 72 &&
            properties.MarginRightPoints == 54 &&
            properties.MarginTopPoints == -36 &&
            properties.MarginBottomPoints == 72 &&
            properties.GutterPoints == 18 &&
            properties.HeaderDistancePoints == 36 &&
            properties.FooterDistancePoints == 36 &&
            properties.DifferentFirstPage == true &&
            properties.RightToLeft == true,
            "DOC SEPX должен сохранять page setup и section flags.");

        var blocks = DocDocumentReader.BuildParagraphs(
            "One\r\fTwo\r",
            sectionBreakCps: sectionBreaks);

        Assert(
            blocks
                .OfType<DocumentFlowBreak>()
                .Count(flowBreak =>
                    flowBreak.Kind ==
                    DocumentFlowBreakKind.Section) == 1,
            "DOC PlcfSed boundary должен превращать 0x0C в section break.");

        Assert(
            !blocks
                .OfType<DocumentFlowBreak>()
                .Any(flowBreak =>
                    flowBreak.Kind ==
                    DocumentFlowBreakKind.Page),
            "DOC section-end 0x0C не должен одновременно считаться page break.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC SEPX",
                Blocks =
                [
                    properties,
                    .. blocks
                ]
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "class=\"section-properties\"",
            "DOC rendered section properties");
        AssertContains(
            html,
            "data-break-kind=\"OddPage\"",
            "DOC rendered section break kind");
        AssertContains(
            html,
            "data-orientation=\"Landscape\"",
            "DOC rendered section orientation");
        AssertContains(
            html,
            "data-page-width-pt=\"792\"",
            "DOC rendered section page width");
        AssertContains(
            html,
            "data-margin-top-pt=\"-36\"",
            "DOC rendered section top margin");
        AssertContains(
            html,
            "data-different-first-page=\"true\"",
            "DOC rendered title page flag");
        AssertContains(
            html,
            "data-right-to-left=\"true\"",
            "DOC rendered bidi section flag");
    }

    private static void TestDocParagraphProperties()
    {
        var grpprl = new List<byte>();

        void AddByteSprm(
            ushort sprm,
            byte value)
        {
            grpprl.Add(
                (byte)(sprm & 0xFF));
            grpprl.Add(
                (byte)(sprm >> 8));
            grpprl.Add(value);
        }

        AddByteSprm(
            0x2405,
            1);
        AddByteSprm(
            0x2406,
            1);
        AddByteSprm(
            0x2431,
            1);
        AddByteSprm(
            0x2441,
            1);
        AddByteSprm(
            0x2640,
            3);
        AddByteSprm(
            0x2407,
            1);

        var formatting =
            new DocDocumentReader.DocParagraphFormatting();

        DocDocumentReader.ReadParagraphGrpprl(
            grpprl.ToArray(),
            formatting);

        Assert(
            formatting.KeepTogether == true &&
            formatting.KeepWithNext == true &&
            formatting.WidowControl == true &&
            formatting.RightToLeft == true &&
            formatting.OutlineLevel == 3 &&
            formatting.PageBreakBefore == true,
            "DOC PAPX keep/keep-next/widow/bidi/outline/page-break SPRM должны сохраняться.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC paragraph properties",
                Blocks =
                [
                    new DocumentParagraph
                    {
                        Inlines =
                        [
                            new DocumentInline
                            {
                                Text = "Paragraph marker"
                            }
                        ],
                        OutlineLevel = formatting.OutlineLevel,
                        KeepTogether =
                            formatting.KeepTogether ?? false,
                        KeepWithNext =
                            formatting.KeepWithNext ?? false,
                        WidowControl =
                            formatting.WidowControl ?? false,
                        RightToLeft =
                            formatting.RightToLeft ?? false
                    }
                ]
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "data-outline-level=\"3\"",
            "DOC rendered outline level");
        AssertContains(
            html,
            "break-inside:avoid",
            "DOC rendered keep together");
        AssertContains(
            html,
            "break-after:avoid",
            "DOC rendered keep with next");
        AssertContains(
            html,
            "orphans:2;widows:2",
            "DOC rendered widow control");
        AssertContains(
            html,
            "direction:rtl",
            "DOC rendered bidi paragraph");
    }

    private static void TestDocTableDefinition()
    {
        const int columnCount = 3;
        const int tc80Size = 20;
        var operand = new byte[
            2 +
            1 +
            (columnCount + 1) * 2 +
            columnCount * tc80Size];

        BinaryPrimitives.WriteUInt16LittleEndian(
            operand.AsSpan(
                0,
                2),
            checked((ushort)(
                operand.Length -
                1)));
        operand[2] =
            columnCount;

        var boundaries = new short[]
        {
            0,
            1000,
            2200,
            3600
        };

        for (var index = 0;
             index < boundaries.Length;
             index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                operand.AsSpan(
                    3 +
                    index * 2,
                    2),
                boundaries[index]);
        }

        var tc80Offset =
            3 +
            boundaries.Length * 2;

        BinaryPrimitives.WriteUInt16LittleEndian(
            operand.AsSpan(
                tc80Offset,
                2),
            0x0002);

        operand[tc80Offset + 4] = 8;
        operand[tc80Offset + 5] = 0x01;
        operand[tc80Offset + 6] = 0x06;
        operand[tc80Offset + 7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(
            operand.AsSpan(
                tc80Offset +
                tc80Size,
                2),
            0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(
            operand.AsSpan(
                tc80Offset +
                2 * tc80Size,
                2),
            0x0660);
        BinaryPrimitives.WriteUInt16LittleEndian(
            operand.AsSpan(
                tc80Offset +
                2 * tc80Size +
                2,
                2),
            1400);

        var definition =
            DocDocumentReader.ReadTableDefinition(
                operand);

        Assert(
            definition.Cells.Count == 3 &&
            definition.Cells[0].LeftTwips == 0 &&
            definition.Cells[0].RightTwips == 1000 &&
            definition.Cells[0].WidthPoints == 50 &&
            definition.Cells[0].HorizontalMerge == 2 &&
            definition.Cells[0].BorderTop is
            {
                WidthPoints: 1,
                Type: 0x01,
                ColorIndex: 0x06
            } &&
            definition.Cells[1].LeftTwips == 1000 &&
            definition.Cells[1].RightTwips == 2200 &&
            definition.Cells[1].WidthPoints == 60 &&
            definition.Cells[1].HorizontalMerge == 1 &&
            definition.Cells[2].LeftTwips == 2200 &&
            definition.Cells[2].RightTwips == 3600 &&
            definition.Cells[2].WidthPoints == 70 &&
            definition.Cells[2].VerticalMerge == 3 &&
            definition.Cells[2].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 70
            },
            "DOC sprmTDefTable должен сохранять cell boundaries, widths и TC80 merge flags.");

        var grpprl = new List<byte>();

        void AddSprm(
            ushort sprm,
            ReadOnlySpan<byte> sprmOperand)
        {
            grpprl.Add(
                (byte)(sprm & 0xFF));
            grpprl.Add(
                (byte)(sprm >> 8));
            grpprl.AddRange(
                sprmOperand.ToArray());
        }

        var legacyTableBorders =
            Enumerable.Repeat(
                    (byte)0xFF,
                    25)
                .ToArray();
        legacyTableBorders[0] = 24;
        legacyTableBorders[5] = 4;
        legacyTableBorders[6] = 0x07;
        legacyTableBorders[7] = 0x06;
        legacyTableBorders[8] = 0;

        var modernTableBorders =
            Enumerable.Repeat(
                    (byte)0xFF,
                    49)
                .ToArray();
        modernTableBorders[0] = 48;

        static void WriteModernBorder(
            byte[] target,
            int offset,
            byte red,
            byte green,
            byte blue,
            byte width,
            byte type)
        {
            target[offset] = red;
            target[offset + 1] = green;
            target[offset + 2] = blue;
            target[offset + 3] = 0;
            target[offset + 4] = width;
            target[offset + 5] = type;
            target[offset + 6] = 0;
            target[offset + 7] = 0;
        }

        WriteModernBorder(
            modernTableBorders,
            9,
            0x00,
            0x80,
            0x00,
            8,
            0x01);
        WriteModernBorder(
            modernTableBorders,
            33,
            0x80,
            0x00,
            0x80,
            4,
            0x07);
        WriteModernBorder(
            modernTableBorders,
            41,
            0x00,
            0x80,
            0x80,
            4,
            0x06);

        AddSprm(
            0xD608,
            operand);
        AddSprm(
            0xD605,
            legacyTableBorders);
        AddSprm(
            0xD613,
            modernTableBorders);
        AddSprm(
            0xF614,
            new byte[]
            {
                0x02,
                0xC4,
                0x09
            });
        AddSprm(
            0x548A,
            new byte[]
            {
                0x01,
                0x00
            });
        AddSprm(
            0x560B,
            new byte[]
            {
                0x01,
                0x00
            });
        AddSprm(
            0x5664,
            new byte[]
            {
                0x00,
                0x00
            });
        AddSprm(
            0x3615,
            new byte[]
            {
                1
            });
        AddSprm(
            0xD635,
            new byte[]
            {
                5,
                0,
                2,
                0x03,
                0xA0,
                0x0F
            });
        AddSprm(
            0x5625,
            new byte[]
            {
                0,
                2
            });
        AddSprm(
            0x5624,
            new byte[]
            {
                1,
                3
            });
        AddSprm(
            0xD620,
            new byte[]
            {
                7,
                1,
                3,
                0x04,
                4,
                0x07,
                0x02,
                0
            });
        AddSprm(
            0xD62B,
            new byte[]
            {
                2,
                0,
                3
            });
        AddSprm(
            0xD62C,
            new byte[]
            {
                3,
                1,
                3,
                2
            });
        AddSprm(
            0x3404,
            new byte[]
            {
                1
            });
        AddSprm(
            0x3466,
            new byte[]
            {
                1
            });
        AddSprm(
            0x9407,
            new byte[]
            {
                0x20,
                0xFE
            });
        AddSprm(
            0xD62F,
            new byte[]
            {
                11,
                0,
                1,
                0x08,
                0x12,
                0x34,
                0x56,
                0x00,
                0x08,
                0x01,
                0x02,
                0x00
            });
        AddSprm(
            0xD62D,
            new byte[]
            {
                12,
                1,
                3,
                0xAA,
                0xBB,
                0xCC,
                0x00,
                0x00,
                0x00,
                0x00,
                0xFF,
                0x01,
                0x00
            });
        AddSprm(
            0x7629,
            new byte[]
            {
                0,
                2,
                0x01,
                0x00
            });
        AddSprm(
            0xF636,
            new byte[]
            {
                0,
                2,
                1
            });
        AddSprm(
            0xD639,
            new byte[]
            {
                3,
                0,
                2,
                1
            });
        AddSprm(
            0xD634,
            new byte[]
            {
                6,
                0,
                1,
                0x05,
                3,
                0x78,
                0x00
            });
        AddSprm(
            0xD632,
            new byte[]
            {
                6,
                0,
                2,
                0x0A,
                3,
                0x68,
                0x01
            });

        var paragraphFormatting =
            new DocDocumentReader.DocParagraphFormatting();

        DocDocumentReader.ReadParagraphGrpprl(
            grpprl.ToArray(),
            paragraphFormatting);

        var overridden =
            paragraphFormatting.TableDefinition
            ?? throw new InvalidOperationException(
                "DOC table SPRM должны сохранять TableDefinition.");

        Assert(
            paragraphFormatting.TableRepeatHeader == true &&
            paragraphFormatting.TablePreventPageSplit == true &&
            paragraphFormatting.TableRowHeightPoints == 24 &&
            paragraphFormatting.TableRowHeightExact == true &&
            paragraphFormatting.TableAutoFit == true &&
            paragraphFormatting.TableAlignment ==
                DocumentTableAlignment.Center &&
            paragraphFormatting.TableBiDi == true &&
            paragraphFormatting.TableBiDi90 == false &&
            paragraphFormatting.TablePreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 50
            } &&
            paragraphFormatting.TableBorders is
            {
                Left: { Color: "#008000" },
                HorizontalInside: { Color: "#800080" },
                VerticalInside: { Color: "#008080" }
            },
            "DOC row/table width, autofit и table-border SPRM должны сохраняться.");

        Assert(
            overridden.Cells[0].HorizontalMerge == 0 &&
            overridden.Cells[0].VerticalMerge == 3 &&
            overridden.Cells[0].BorderTop is
            {
                Type: 0x01,
                ColorIndex: 0x06
            } &&
            overridden.Cells[0].PaddingTopPoints == 6 &&
            overridden.Cells[0].PaddingBottomPoints == 6 &&
            overridden.Cells[0].PaddingLeftPoints == 18 &&
            overridden.Cells[0].PaddingRightPoints == 18 &&
            overridden.Cells[0].NoWrap &&
            overridden.Cells[0].FitText &&
            overridden.Cells[0].TextFlow ==
                DocumentTableCellTextFlow.TopToBottomRightToLeft &&
            overridden.Cells[0].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 200
            } &&
            overridden.Cells[0].BorderRight is
            {
                WidthPoints: 1,
                Type: 0x01,
                Color: "#123456"
            } &&
            overridden.Cells[1].HorizontalMerge == 2 &&
            overridden.Cells[1].VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Bottom &&
            overridden.Cells[1].PaddingLeftPoints == 18 &&
            overridden.Cells[1].PaddingRightPoints == 18 &&
            overridden.Cells[1].NoWrap &&
            overridden.Cells[1].FitText &&
            overridden.Cells[1].TextFlow ==
                DocumentTableCellTextFlow.TopToBottomRightToLeft &&
            overridden.Cells[1].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 200
            } &&
            overridden.Cells[1].BorderBottom is
            {
                WidthPoints: 0.5,
                Type: 0x07,
                ColorIndex: 0x02
            } &&
            overridden.Cells[1].Shading is
            {
                ForegroundColor: "#AABBCC",
                Pattern: 0x0001
            } &&
            overridden.Cells[2].HorizontalMerge == 1 &&
            overridden.Cells[2].VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Bottom &&
            overridden.Cells[2].PaddingTopPoints == 6 &&
            overridden.Cells[2].PaddingBottomPoints == 6 &&
            overridden.Cells[2].PaddingLeftPoints == null &&
            overridden.Cells[2].PaddingRightPoints == null,
            "DOC table merge, vertical-align и cell padding SPRM должны переопределять TC80 properties.");

        var structureGrpprl =
            new List<byte>();

        void AddStructureSprm(
            ushort sprm,
            ReadOnlySpan<byte> sprmOperand)
        {
            structureGrpprl.Add(
                (byte)(sprm & 0xFF));
            structureGrpprl.Add(
                (byte)(sprm >> 8));
            structureGrpprl.AddRange(
                sprmOperand.ToArray());
        }

        AddStructureSprm(
            0xD608,
            operand);
        AddStructureSprm(
            0x7621,
            new byte[]
            {
                1,
                1,
                0x20,
                0x03
            });
        AddStructureSprm(
            0x7623,
            new byte[]
            {
                0,
                2,
                0xB0,
                0x04
            });
        AddStructureSprm(
            0x5622,
            new byte[]
            {
                2,
                3
            });

        var structureFormatting =
            new DocDocumentReader.DocParagraphFormatting();

        DocDocumentReader.ReadParagraphGrpprl(
            structureGrpprl.ToArray(),
            structureFormatting);

        var restructured =
            structureFormatting.TableDefinition
            ?? throw new InvalidOperationException(
                "DOC table structure SPRM должны сохранять TableDefinition.");

        Assert(
            restructured.Cells.Count == 3 &&
            restructured.Cells[0].LeftTwips == 0 &&
            restructured.Cells[0].RightTwips == 1200 &&
            restructured.Cells[0].WidthPoints == 60 &&
            restructured.Cells[1].LeftTwips == 1200 &&
            restructured.Cells[1].RightTwips == 2400 &&
            restructured.Cells[1].WidthPoints == 60 &&
            restructured.Cells[2].LeftTwips == 2400 &&
            restructured.Cells[2].RightTwips == 3800 &&
            restructured.Cells[2].WidthPoints == 70,
            "DOC sprmTInsert/sprmTDxaCol/sprmTDelete должны пересчитывать cells и boundaries по порядку применения.");

        static DocumentTableCell RawCell(
            string text)
        {
            return new DocumentTableCell
            {
                Inlines =
                [
                    new DocumentInline
                    {
                        Text = text
                    }
                ]
            };
        }

        var previousRows =
            new List<DocumentTableRow>();
        var verticalOrigins =
            new Dictionary<int, DocDocumentReader.DocVerticalMergeOrigin>();

        var firstRow =
            DocDocumentReader.ApplyTableDefinitionToRow(
                previousRows,
                new[]
                {
                    RawCell("A"),
                    RawCell("B"),
                    RawCell("ignored")
                },
                overridden,
                verticalOrigins);
        previousRows.Add(
            firstRow);

        Assert(
            firstRow.Cells.Count == 2 &&
            firstRow.Cells[0].RowSpan == 1 &&
            firstRow.Cells[0].WidthPoints == 50 &&
            firstRow.Cells[0].PaddingLeftPoints == 18 &&
            firstRow.Cells[0].PaddingRightPoints == 18 &&
            firstRow.Cells[1].ColumnSpan == 2 &&
            firstRow.Cells[1].WidthPoints == 130 &&
            firstRow.Cells[1].PaddingLeftPoints == 18 &&
            firstRow.Cells[1].PaddingRightPoints == 18 &&
            firstRow.Cells[1].VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Bottom &&
            firstRow.Cells[1].Inlines.Any(inline =>
                inline.Text == "B"),
            "DOC table row conversion должен применять merges, widths, padding и vertical alignment.");

        var secondDefinition =
            new DocDocumentReader.DocTableDefinition(
                new[]
                {
                    overridden.Cells[0] with
                    {
                        VerticalMerge = 1
                    },
                    overridden.Cells[1] with
                    {
                        HorizontalMerge = 0
                    },
                    overridden.Cells[2] with
                    {
                        HorizontalMerge = 0
                    }
                });

        var secondRow =
            DocDocumentReader.ApplyTableDefinitionToRow(
                previousRows,
                new[]
                {
                    RawCell("ignored vertical"),
                    RawCell("D"),
                    RawCell("E")
                },
                secondDefinition,
                verticalOrigins);
        previousRows.Add(
            secondRow);

        Assert(
            previousRows[0].Cells[0].RowSpan == 2 &&
            secondRow.Cells.Count == 2 &&
            secondRow.Cells.All(cell =>
                cell.Inlines.All(inline =>
                    inline.Text != "ignored vertical")),
            "DOC vertical merge continuation должен увеличивать RowSpan и не рендерить собственное содержимое.");

        var html =
            DocumentHtmlRenderer.Render(
                new DocumentModel
                {
                    Title =
                        "DOC table definition",
                    Blocks =
                    [
                        new DocumentTable
                        {
                            PreferredWidth =
                                new DocumentPreferredWidth
                                {
                                    Kind =
                                        DocumentPreferredWidthKind.Percent,
                                    Value = 50
                                },
                            AutoFit = true,
                            Alignment =
                                DocumentTableAlignment.Center,
                            RightToLeft = true,
                            Rows =
                            [
                                new DocumentTableRow
                                {
                                    RepeatAsHeader = true,
                                    PreventPageSplit = true,
                                    HeightPoints = 24,
                                    HeightExact = true,
                                    BorderTop =
                                        new DocumentBorder
                                        {
                                            WidthPoints = 1,
                                            Type = 0x01,
                                            Color = "#00FFFF"
                                        },
                                    BorderLeft =
                                        new DocumentBorder
                                        {
                                            WidthPoints = 1,
                                            Type = 0x01,
                                            Color = "#008000"
                                        },
                                    BorderRight =
                                        new DocumentBorder
                                        {
                                            WidthPoints = 1,
                                            Type = 0x01,
                                            Color = "#800000"
                                        },
                                    BorderBottom =
                                        new DocumentBorder
                                        {
                                            WidthPoints = 1,
                                            Type = 0x01,
                                            Color = "#808000"
                                        },
                                    BorderVerticalInside =
                                        new DocumentBorder
                                        {
                                            WidthPoints = 0.5,
                                            Type = 0x06,
                                            Color = "#008080"
                                        },
                                    Cells =
                                    [
                                        new DocumentTableCell
                                        {
                                            Inlines =
                                            [
                                                new DocumentInline
                                                {
                                                    Text = "Merged cell"
                                                }
                                            ],
                                            ColumnSpan = 2,
                                            RowSpan = 2,
                                            WidthPoints = 110,
                                            PreferredWidth =
                                                new DocumentPreferredWidth
                                                {
                                                    Kind =
                                                        DocumentPreferredWidthKind.Points,
                                                    Value = 200
                                                },
                                            PaddingLeftPoints = 18,
                                            PaddingRightPoints = 18,
                                            VerticalAlignment =
                                                DocumentTableCellVerticalAlignment.Bottom,
                                            BorderTop =
                                                new DocumentBorder
                                                {
                                                    WidthPoints = 1,
                                                    Type = 0x01,
                                                    ColorIndex = 0x06
                                                },
                                            BorderBottom =
                                                new DocumentBorder
                                                {
                                                    WidthPoints = 0.5,
                                                    Type = 0x07,
                                                    ColorIndex = 0x02
                                                },
                                            BorderRight =
                                                new DocumentBorder
                                                {
                                                    WidthPoints = 1,
                                                    Type = 0x01,
                                                    Color = "#123456"
                                                },
                                            Shading =
                                                new DocumentShading
                                                {
                                                    ForegroundColor = "#AABBCC",
                                                    Pattern = 0x0001
                                                },
                                            NoWrap = true,
                                            FitText = true,
                                            TextFlow =
                                                DocumentTableCellTextFlow.TopToBottomRightToLeft
                                        },
                                        new DocumentTableCell
                                        {
                                            Inlines =
                                            [
                                                new DocumentInline
                                                {
                                                    Text = "Third cell"
                                                }
                                            ],
                                            WidthPoints = 70
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                },
                CreateRenderTheme());

        AssertContains(
            html,
            "data-autofit=\"true\"",
            "DOC rendered table autofit");
        AssertContains(
            html,
            "data-table-alignment=\"center\"",
            "DOC rendered table alignment");
        AssertContains(
            html,
            "data-table-rtl=\"true\"",
            "DOC rendered table direction");
        AssertContains(
            html,
            "margin-left:auto;margin-right:auto",
            "DOC rendered centered table margins");
        AssertContains(
            html,
            "data-preferred-width-kind=\"percent\"",
            "DOC rendered table preferred width kind");
        AssertContains(
            html,
            "data-preferred-width-value=\"50\"",
            "DOC rendered table preferred width value");
        AssertContains(
            html,
            "width:50%",
            "DOC rendered preferred table width");
        AssertContains(
            html,
            "data-preferred-width-kind=\"points\"",
            "DOC rendered cell preferred width kind");
        AssertContains(
            html,
            "data-preferred-width-value=\"200\"",
            "DOC rendered cell preferred width value");
        AssertContains(
            html,
            "data-repeat-header=\"true\"",
            "DOC rendered repeating header row");
        AssertContains(
            html,
            "break-inside:avoid",
            "DOC rendered cant-split row");
        AssertContains(
            html,
            "height:24pt",
            "DOC rendered exact row height");
        AssertContains(
            html,
            "colspan=\"2\"",
            "DOC rendered table horizontal merge");
        AssertContains(
            html,
            "rowspan=\"2\"",
            "DOC rendered table vertical merge");
        AssertContains(
            html,
            "width:110pt",
            "DOC rendered merged table width");
        AssertContains(
            html,
            "padding-right:18pt",
            "DOC rendered table cell padding");
        AssertContains(
            html,
            "padding-left:18pt",
            "DOC rendered table cell padding");
        AssertContains(
            html,
            "vertical-align:bottom",
            "DOC rendered table cell vertical alignment");
        AssertContains(
            html,
            "border-top:1pt solid #FF0000",
            "DOC rendered TC80 top border");
        AssertContains(
            html,
            "border-bottom:0.5pt dashed #0000FF",
            "DOC rendered sprmTSetBrc80 border");
        AssertContains(
            html,
            "border-right:1pt solid #123456",
            "DOC rendered sprmTSetBrc border");
        AssertContains(
            html,
            "border-top:1pt solid #00FFFF",
            "DOC rendered row outer top border fallback");
        AssertContains(
            html,
            "border-left:0.5pt dotted #008080",
            "DOC rendered row inside vertical border fallback");
        AssertContains(
            html,
            "border-right:1pt solid #800000",
            "DOC rendered row outer right border fallback");
        AssertContains(
            html,
            "background-color:#AABBCC",
            "DOC rendered solid table shading");
        AssertContains(
            html,
            "white-space:nowrap",
            "DOC rendered cell no-wrap");
        AssertContains(
            html,
            "data-fit-text=\"true\"",
            "DOC rendered fit-text metadata");
        AssertContains(
            html,
            "data-text-flow=\"tbrl\"",
            "DOC rendered text-flow metadata");
        AssertContains(
            html,
            "data-shading-pattern=\"1\"",
            "DOC rendered shading pattern metadata");
        AssertContains(
            html,
            "data-shading-foreground=\"#AABBCC\"",
            "DOC rendered shading foreground metadata");
    }

    private static void TestDocFootnoteTables()
    {
        var referencePlc = new byte[16];
        var referenceCps = new[]
        {
            1,
            3,
            6
        };

        for (var index = 0;
             index < referenceCps.Length;
             index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                referencePlc.AsSpan(
                    index * 4,
                    4),
                referenceCps[index]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            referencePlc.AsSpan(
                12,
                2),
            1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            referencePlc.AsSpan(
                14,
                2),
            0);

        var references =
            DocDocumentReader.ReadFootnoteReferenceDescriptors(
                referencePlc,
                ccpText: 6);

        Assert(
            references.Count == 2 &&
            references[0].Cp == 1 &&
            references[0].NoteIndex == 1 &&
            references[0].Automatic &&
            references[1].Cp == 3 &&
            references[1].NoteIndex == 2 &&
            !references[1].Automatic,
            "DOC PlcffndRef должен сохранять CP, ordinal и auto/custom numbering.");

        var textPlc = new byte[16];
        var textCps = new[]
        {
            0,
            4,
            8,
            0
        };

        for (var index = 0;
             index < textCps.Length;
             index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                textPlc.AsSpan(
                    index * 4,
                    4),
                textCps[index]);
        }

        var notes =
            DocDocumentReader.ReadFootnoteTextDescriptors(
                textPlc,
                ccpFtn: 9);

        Assert(
            notes.Count == 2 &&
            notes[0].NoteIndex == 1 &&
            notes[0].LocalStart == 0 &&
            notes[0].ContentLength == 4 &&
            notes[1].NoteIndex == 2 &&
            notes[1].LocalStart == 4 &&
            notes[1].ContentLength == 4,
            "DOC PlcffndTxt должен сохранять диапазоны footnote stories.");

        var referenceByCp =
            new Dictionary<int, DocDocumentReader.DocNoteReference>
            {
                [1] = references[0]
            };

        var blocks = DocDocumentReader.BuildParagraphs(
            "A\u0002B\r",
            noteReferences: referenceByCp);
        var link = blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .SingleOrDefault(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#doc-footnote-1",
                    StringComparison.Ordinal));

        Assert(
            link != null &&
            link.Superscript &&
            string.Equals(
                link.Text,
                "1",
                StringComparison.Ordinal),
            "DOC U+0002 footnote reference должен стать superscript-ссылкой.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC footnote",
                Blocks =
                [
                    .. blocks,
                    new DocumentParagraph
                    {
                        AnchorId = "doc-footnote-1",
                        IsNote = true,
                        Inlines =
                        [
                            new DocumentInline
                            {
                                Text = "Footnote marker"
                            }
                        ]
                    }
                ]
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "href=\"#anchor-doc-footnote-1\"",
            "DOC rendered footnote reference");

        AssertContains(
            html,
            "id=\"anchor-doc-footnote-1\"",
            "DOC rendered footnote anchor");
    }

    private static void TestDocEndnoteTables()
    {
        var referencePlc = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(
            referencePlc.AsSpan(
                0,
                4),
            2);
        BinaryPrimitives.WriteInt32LittleEndian(
            referencePlc.AsSpan(
                4,
                4),
            6);
        BinaryPrimitives.WriteUInt16LittleEndian(
            referencePlc.AsSpan(
                8,
                2),
            1);

        var references =
            DocDocumentReader.ReadEndnoteReferenceDescriptors(
                referencePlc,
                ccpText: 6);

        Assert(
            references.Count == 1 &&
            references[0].Cp == 2 &&
            references[0].NoteIndex == 1 &&
            references[0].Automatic &&
            references[0].Endnote,
            "DOC PlcfendRef должен сохранять endnote reference descriptor.");

        var textPlc = new byte[12];
        var textCps = new[]
        {
            0,
            4,
            0
        };

        for (var index = 0;
             index < textCps.Length;
             index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                textPlc.AsSpan(
                    index * 4,
                    4),
                textCps[index]);
        }

        var notes =
            DocDocumentReader.ReadEndnoteTextDescriptors(
                textPlc,
                ccpEdn: 5);

        Assert(
            notes.Count == 1 &&
            notes[0].NoteIndex == 1 &&
            notes[0].LocalStart == 0 &&
            notes[0].ContentLength == 4,
            "DOC PlcfendTxt должен сохранять диапазон endnote story.");

        var referenceByCp =
            new Dictionary<int, DocDocumentReader.DocNoteReference>
            {
                [2] = references[0]
            };

        var blocks = DocDocumentReader.BuildParagraphs(
            "AB\u0002C\r",
            noteReferences: referenceByCp);
        var link = blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .SingleOrDefault(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#doc-endnote-1",
                    StringComparison.Ordinal));

        Assert(
            link != null &&
            link.Superscript &&
            string.Equals(
                link.Text,
                "1",
                StringComparison.Ordinal),
            "DOC U+0002 endnote reference должен стать superscript-ссылкой.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC endnote",
                Blocks =
                [
                    .. blocks,
                    new DocumentParagraph
                    {
                        AnchorId = "doc-endnote-1",
                        IsNote = true,
                        Inlines =
                        [
                            new DocumentInline
                            {
                                Text = "Endnote marker"
                            }
                        ]
                    }
                ]
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "href=\"#anchor-doc-endnote-1\"",
            "DOC rendered endnote reference");

        AssertContains(
            html,
            "id=\"anchor-doc-endnote-1\"",
            "DOC rendered endnote anchor");
    }

    private static void TestDocBinaryReader(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "Books",
            "Regression_Word97_Binary.doc");

        Assert(
            File.Exists(path),
            "Books должен содержать Regression_Word97_Binary.doc.");

        var model = DocDocumentReader.Read(path);

        AssertContains(
            FlattenText(model),
            "Binary DOC marker",
            "DOC binary model");

        var paragraph = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph =>
                paragraph.Inlines.Any(inline =>
                    inline.Text.Contains(
                        "Binary DOC marker",
                        StringComparison.Ordinal)));

        AssertEqual(
            1,
            paragraph.HeadingLevel,
            "DOC binary inherited heading level");

        AssertEqual(
            DocumentParagraphAlignment.Justify,
            paragraph.Alignment,
            "DOC binary direct paragraph alignment");

        AssertEqual(
            36.0,
            paragraph.LeftIndentPoints ?? double.NaN,
            "DOC binary inherited left indent");

        AssertEqual(
            18.0,
            paragraph.FirstLineIndentPoints ?? double.NaN,
            "DOC binary direct first-line indent");

        AssertEqual(
            6.0,
            paragraph.SpaceAfterPoints ?? double.NaN,
            "DOC binary inherited paragraph spacing");

        Assert(
            paragraph.PageBreakBefore,
            "DOC binary direct page-break-before должен сохраниться.");

        Assert(
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Any(flowBreak =>
                    flowBreak.Kind == DocumentFlowBreakKind.Page),
            "DOC binary page-break-before должен создавать flow break.");

        var inline = paragraph.Inlines
            .Single(inline =>
                inline.Text.Contains(
                    "Binary DOC marker",
                    StringComparison.Ordinal));

        Assert(
            !inline.Bold,
            "DOC ToggleOperand 0x81 должен инвертировать bold текущего style.");

        Assert(
            inline.Italic,
            "DOC Heading 1 должен наследовать italic из character UPX.");

        Assert(
            inline.Underline,
            "DOC direct CHPX underline должен сохраниться.");

        AssertEqual(
            14.0,
            inline.FontSizePoints ?? double.NaN,
            "DOC direct CHPX font size");

        AssertEqual(
            "#008000",
            inline.TextColor!,
            "DOC direct CHPX RGB color");

        var table = model.Blocks
            .OfType<DocumentTable>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                "DOC binary table markers должны создавать DocumentTable.");

        Assert(
            table.Rows.Count == 1 &&
            table.Rows[0].Cells.Count == 2,
            "DOC binary table markers должны создавать строку с двумя ячейками.");

        Assert(
            table.Rows[0].Cells[0].Inlines.Any(cellInline =>
                cellInline.Text.Contains(
                    "Cell A",
                    StringComparison.Ordinal)),
            "DOC binary первая table cell должна содержать Cell A.");

        Assert(
            table.Rows[0].Cells[1].Inlines.Any(cellInline =>
                cellInline.Text.Contains(
                    "Cell B",
                    StringComparison.Ordinal)),
            "DOC binary вторая table cell должна содержать Cell B.");

        Assert(
            model.Blocks
                .OfType<DocumentParagraph>()
                .Any(candidate =>
                    candidate.Inlines.Any(candidateInline =>
                        candidateInline.Text.Contains(
                            "After table",
                            StringComparison.Ordinal))),
            "DOC binary обычный абзац после таблицы должен остаться потоковым абзацем.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "<h1",
            "DOC binary rendered heading");

        AssertContains(
            html,
            "text-align:justify",
            "DOC binary rendered alignment");

        AssertContains(
            html,
            "margin-left:36pt",
            "DOC binary rendered inherited indent");

        AssertContains(
            html,
            "text-indent:18pt",
            "DOC binary rendered direct first-line indent");

        AssertContains(
            html,
            "font-size:14pt",
            "DOC binary rendered font size");

        AssertContains(
            html,
            "color:#008000",
            "DOC binary rendered color");

        AssertContains(
            html,
            "<table class=\"flow-table\"",
            "DOC binary rendered table");

        AssertContains(
            html,
            "Cell A",
            "DOC binary rendered first cell");

        AssertContains(
            html,
            "Cell B",
            "DOC binary rendered second cell");
    }

    private static void TestFb2Reader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.fb2");

        var pngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=";

        File.WriteAllText(
            path,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0"
                         xmlns:xlink="http://www.w3.org/1999/xlink">
              <description>
                <title-info>
                  <book-title>Regression FB2</book-title>
                  <author>
                    <first-name>Test</first-name>
                    <last-name>Author</last-name>
                  </author>
                  <coverpage>
                    <image xlink:href="#cover-image"/>
                  </coverpage>
                </title-info>
              </description>
              <body id="main-body">
                <section id="chapter-1">
                  <title><p>Regression heading</p></title>
                  <p>
                    FB2 marker
                    <strong>bold</strong>
                    <emphasis>italic</emphasis>
                    <strikethrough>strike</strikethrough>
                    <sub>sub</sub>
                    <sup>sup</sup>
                    <image xlink:href="#inline-image" alt="inline pixel"/>
                    <a xlink:href="#note-1">note link</a>
                    <a xlink:href="javascript:alert(1)">unsafe link</a>
                  </p>
                  <image id="standalone-image" xlink:href="#inline-image" alt="standalone pixel"/>
                  <table id="table-1">
                    <tr>
                      <th colspan="2">Header</th>
                    </tr>
                    <tr>
                      <td rowspan="2"><p>Cell A</p></td>
                      <td><p>Cell B</p></td>
                    </tr>
                    <tr>
                      <td><p>Cell C</p></td>
                    </tr>
                  </table>
                </section>
              </body>
              <body id="appendix-body">
                <section id="appendix">
                  <title><p>Appendix heading</p></title>
                  <p>Second body marker</p>
                </section>
              </body>
              <body name="notes">
                <section id="note-1">
                  <p>Note target</p>
                </section>
              </body>
              <binary id="cover-image" content-type="image/png">{{pngBase64}}</binary>
              <binary id="inline-image" content-type="image/png">{{pngBase64}}</binary>
            </FictionBook>
            """,
            new UTF8Encoding(false));

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertEqual(
            "Regression FB2",
            model.Title,
            "FB2 title");

        Assert(
            model.Cover != null &&
            string.Equals(
                model.Cover.ContentType,
                "image/png",
                StringComparison.Ordinal),
            "FB2 cover binary должен загружаться в DocumentImage.");

        AssertContains(
            FlattenText(model),
            "FB2 marker",
            "FB2 model");

        AssertContains(
            FlattenText(model),
            "Second body marker",
            "FB2 multiple body model");

        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        Assert(
            paragraphs.Any(paragraph =>
                string.Equals(
                    paragraph.AnchorId,
                    "chapter-1",
                    StringComparison.Ordinal)),
            "FB2 section id chapter-1 должен стать внутренним якорем.");

        Assert(
            paragraphs.Any(paragraph =>
                string.Equals(
                    paragraph.AnchorId,
                    "appendix",
                    StringComparison.Ordinal)),
            "FB2 второй body должен сохранять section anchors.");

        Assert(
            paragraphs.Any(paragraph =>
                paragraph.IsNote &&
                string.Equals(
                    paragraph.AnchorId,
                    "note-1",
                    StringComparison.Ordinal)),
            "FB2 body name=notes должен помечать note-блоки в модели.");

        var allInlines = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .ToArray();

        Assert(
            allInlines.Any(inline =>
                inline.Image != null &&
                string.Equals(
                    inline.ImageAlt,
                    "inline pixel",
                    StringComparison.Ordinal)),
            "FB2 inline image должен сохраняться в нейтральной модели.");

        Assert(
            allInlines.Any(inline => inline.Bold && inline.Text.Contains("bold", StringComparison.Ordinal)),
            "FB2 strong должен сохранять bold formatting.");

        Assert(
            allInlines.Any(inline => inline.Italic && inline.Text.Contains("italic", StringComparison.Ordinal)),
            "FB2 emphasis должен сохранять italic formatting.");

        Assert(
            allInlines.Any(inline => inline.Strikethrough && inline.Text.Contains("strike", StringComparison.Ordinal)),
            "FB2 strikethrough должен сохранять formatting.");

        Assert(
            allInlines.Any(inline => inline.Subscript && inline.Text.Contains("sub", StringComparison.Ordinal)),
            "FB2 sub должен сохранять formatting.");

        Assert(
            allInlines.Any(inline => inline.Superscript && inline.Text.Contains("sup", StringComparison.Ordinal)),
            "FB2 sup должен сохранять formatting.");

        Assert(
            allInlines.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#note-1",
                    StringComparison.Ordinal)),
            "FB2 xlink:href должен сохраниться в DocumentInline.");

        var imageBlock = model.Blocks
            .OfType<DocumentImageBlock>()
            .SingleOrDefault();

        Assert(
            imageBlock != null &&
            string.Equals(
                imageBlock.AnchorId,
                "standalone-image",
                StringComparison.Ordinal),
            "FB2 standalone image должен сохраняться отдельным block.");

        var table = model.Blocks
            .OfType<DocumentTable>()
            .SingleOrDefault();

        Assert(
            table != null &&
            table.Rows.Count == 3 &&
            table.Rows[0].Cells[0].Header &&
            table.Rows[0].Cells[0].ColumnSpan == 2 &&
            table.Rows[1].Cells[0].RowSpan == 2,
            "FB2 table должен сохранять строки, header, colspan и rowspan.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "id=\"anchor-note-1\" class=\"note-body\"",
            "FB2 rendered note anchor");

        AssertContains(
            html,
            "href=\"#anchor-note-1\"",
            "FB2 rendered internal link");

        AssertContains(
            html,
            "class=\"inline-image\"",
            "FB2 rendered inline image");

        AssertContains(
            html,
            "class=\"flow-image\"",
            "FB2 rendered standalone image");

        AssertContains(
            html,
            "<table class=\"flow-table\"",
            "FB2 rendered table");

        AssertContains(
            html,
            "colspan=\"2\"",
            "FB2 rendered colspan");

        AssertContains(
            html,
            "rowspan=\"2\"",
            "FB2 rendered rowspan");

        AssertContains(
            html,
            "<s>strike</s>",
            "FB2 rendered strikethrough");

        AssertContains(
            html,
            "<sub>sub</sub>",
            "FB2 rendered subscript");

        Assert(
            !html.Contains(
                "href=\"javascript:",
                StringComparison.OrdinalIgnoreCase),
            "Опасная javascript: ссылка не должна попадать в HTML href.");
    }

    private static void TestEpubReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.epub");

        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=");

        using (var archive = ZipFile.Open(
                   path,
                   ZipArchiveMode.Create))
        {
            WriteZipEntry(
                archive,
                "mimetype",
                "application/epub+zip",
                CompressionLevel.NoCompression);

            WriteZipEntry(
                archive,
                "META-INF/container.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0"
                           xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf"
                              media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/content.opf",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package version="3.0"
                         xmlns="http://www.idpf.org/2007/opf">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Regression EPUB</dc:title>
                    <dc:creator>Test Author</dc:creator>
                  </metadata>
                  <manifest>
                    <item id="chapter1"
                          href="Text/chapter1.xhtml"
                          media-type="application/xhtml+xml" />
                    <item id="chapter2"
                          href="Text/chapter2.xhtml"
                          media-type="application/xhtml+xml" />
                    <item id="nav"
                          href="nav.xhtml"
                          media-type="application/xhtml+xml"
                          properties="nav" />
                    <item id="css"
                          href="Styles/book.css"
                          media-type="text/css" />
                    <item id="png"
                          href="Images/pixel.png"
                          media-type="image/png" />
                    <item id="svg"
                          href="Images/icon.svg"
                          media-type="image/svg+xml" />
                    <item id="font"
                          href="Fonts/embedded.woff2"
                          media-type="font/woff2" />
                    <item id="audio"
                          href="Media/tone.mp3"
                          media-type="audio/mpeg" />
                    <item id="video"
                          href="Media/clip.mp4"
                          media-type="video/mp4" />
                    <item id="captions"
                          href="Media/captions.vtt"
                          media-type="text/vtt" />
                  </manifest>
                  <spine>
                    <itemref idref="chapter1" />
                    <itemref idref="chapter2" />
                  </spine>
                </package>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/nav.xhtml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"
                      xmlns:epub="http://www.idpf.org/2007/ops">
                  <body>
                    <nav epub:type="toc">
                      <ol>
                        <li>
                          <a href="Text/chapter1.xhtml#start">Chapter One</a>
                          <ol>
                            <li><a href="Text/chapter1.xhtml#sub">Subsection</a></li>
                          </ol>
                        </li>
                        <li><a href="Text/chapter2.xhtml#target">Chapter Two</a></li>
                      </ol>
                    </nav>
                    <nav epub:type="landmarks">
                      <h2>Landmarks</h2>
                      <ol>
                        <li><a href="Text/chapter1.xhtml#start">Start</a></li>
                      </ol>
                    </nav>
                    <nav epub:type="page-list">
                      <h2>Pages</h2>
                      <ol>
                        <li><a href="Text/chapter1.xhtml#p1">1</a></li>
                      </ol>
                    </nav>
                  </body>
                </html>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/Styles/book.css",
                """
                @import url("https://example.invalid/remote.css");
                @font-face {
                    font-family: "EmbeddedTest";
                    src: local("DoNotUse"),
                         url("../Fonts/embedded.woff2") format("woff2"),
                         url("https://example.invalid/remote.woff2") format("woff2");
                    font-style: normal;
                    font-weight: 400;
                }
                body { color: rgb(1, 2, 3); }
                .illustrated {
                    background-image: url("../Images/pixel.png");
                    font-family: "EmbeddedTest";
                    margin-top: 4px;
                    position: fixed;
                }
                """);

            WriteZipBytes(
                archive,
                "OEBPS/Images/pixel.png",
                pngBytes);

            WriteZipEntry(
                archive,
                "OEBPS/Images/icon.svg",
                """
                <svg xmlns="http://www.w3.org/2000/svg"
                     width="10"
                     height="10"
                     viewBox="0 0 10 10">
                  <rect x="0" y="0" width="10" height="10" fill="red" />
                </svg>
                """);

            WriteZipBytes(
                archive,
                "OEBPS/Fonts/embedded.woff2",
                [0x77, 0x4F, 0x46, 0x32, 0x00, 0x01, 0x00, 0x00]);

            WriteZipBytes(
                archive,
                "OEBPS/Media/tone.mp3",
                [0x49, 0x44, 0x33, 0x04, 0x00, 0x00]);

            WriteZipBytes(
                archive,
                "OEBPS/Media/clip.mp4",
                [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);

            WriteZipEntry(
                archive,
                "OEBPS/Media/captions.vtt",
                """
                WEBVTT

                00:00.000 --> 00:01.000
                Caption
                """);

            WriteZipEntry(
                archive,
                "OEBPS/Text/chapter1.xhtml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"
                      xmlns:svg="http://www.w3.org/2000/svg"
                      xmlns:epub="http://www.idpf.org/2007/ops">
                  <head>
                    <link rel="stylesheet" href="../Styles/book.css" />
                  </head>
                  <body>
                    <h1 id="start">Regression EPUB</h1>
                    <h2 id="sub">Subsection heading</h2>
                    <p class="illustrated">EPUB marker</p>
                    <span id="p1" epub:type="pagebreak">1</span>
                    <p>
                      Text with note
                      <a epub:type="noteref" href="#note-1">1</a>
                    </p>
                    <aside id="note-1" epub:type="footnote">Footnote text</aside>
                    <img src="../Images/pixel.png"
                         onerror="alert(1)" />
                    <img src="../Images/icon.svg" />
                    <picture>
                      <source srcset="../Images/pixel.png 1x, ../Images/icon.svg 2x"
                              type="image/png" />
                      <img src="../Images/pixel.png"
                           srcset="../Images/pixel.png 1x, ../Images/icon.svg 2x" />
                    </picture>
                    <audio src="../Media/tone.mp3" autoplay="autoplay"></audio>
                    <video src="../Media/clip.mp4"
                           poster="../Images/pixel.png"
                           autoplay="autoplay">
                      <track src="../Media/captions.vtt" kind="captions" />
                    </video>
                    <audio src="https://example.invalid/remote.mp3"></audio>
                    <svg:svg width="12" height="12" viewBox="0 0 12 12">
                      <svg:image href="../Images/pixel.png"
                                 x="0" y="0" width="12" height="12" />
                    </svg:svg>
                    <p><a href="chapter2.xhtml#target">Next chapter</a></p>
                    <p><a href="javascript:alert(1)">Unsafe link</a></p>
                    <script>window.__epubScriptExecuted = true;</script>
                  </body>
                </html>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/Text/chapter2.xhtml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h2 id="target">Target heading</h2>
                    <p><a href="chapter1.xhtml#start">Previous chapter</a></p>
                  </body>
                </html>
                """);
        }

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertEqual(
            "Regression EPUB",
            model.Title,
            "EPUB title");

        AssertContains(
            FlattenText(model),
            "EPUB marker",
            "EPUB model");

        AssertEqual(
            4,
            model.Outlines.Count,
            "EPUB3 navigation root count");

        AssertEqual(
            "Chapter One",
            model.Outlines[0].Title,
            "EPUB3 first outline title");

        AssertEqual(
            "epub-chapter-1-start",
            model.Outlines[0].TargetAnchorId!,
            "EPUB3 first outline target");

        AssertEqual(
            1,
            model.Outlines[0].Children.Count,
            "EPUB3 nested outline count");

        AssertEqual(
            "epub-chapter-1-sub",
            model.Outlines[0].Children[0].TargetAnchorId!,
            "EPUB3 nested outline target");

        AssertEqual(
            "epub-chapter-2-target",
            model.Outlines[1].TargetAnchorId!,
            "EPUB3 second outline target");

        AssertEqual(
            "Landmarks",
            model.Outlines[2].Title,
            "EPUB3 landmarks group");

        AssertEqual(
            "epub-chapter-1-start",
            model.Outlines[2].Children[0].TargetAnchorId!,
            "EPUB3 landmark target");

        AssertEqual(
            "Pages",
            model.Outlines[3].Title,
            "EPUB3 page-list group");

        AssertEqual(
            "epub-chapter-1-p1",
            model.Outlines[3].Children[0].TargetAnchorId!,
            "EPUB3 page-list target");

        var htmlBlocks = model.Blocks
            .OfType<DocumentHtmlBlock>()
            .ToArray();

        AssertEqual(
            2,
            htmlBlocks.Length,
            "EPUB spine chapter count");

        var combinedHtml = string.Join(
            "\n",
            htmlBlocks.Select(block => block.Html));

        AssertContains(
            combinedHtml,
            "id=\"epub-chapter-1\"",
            "EPUB chapter 1 anchor");

        AssertContains(
            combinedHtml,
            "id=\"epub-chapter-2-target\"",
            "EPUB target anchor");

        AssertContains(
            combinedHtml,
            "href=\"#epub-chapter-2-target\"",
            "EPUB relative forward link");

        AssertContains(
            combinedHtml,
            "href=\"#epub-chapter-1-start\"",
            "EPUB relative backward link");

        AssertContains(
            combinedHtml,
            "id=\"epub-chapter-1-p1\"",
            "EPUB pagebreak anchor");

        AssertContains(
            combinedHtml,
            "epub-noteref",
            "EPUB noteref class");

        AssertContains(
            combinedHtml,
            "epub-note",
            "EPUB footnote class");

        AssertContains(
            combinedHtml,
            "href=\"#epub-chapter-1-note-1\"",
            "EPUB footnote target");

        AssertContains(
            combinedHtml,
            "#epub-chapter-1{color:rgb(1, 2, 3)}",
            "EPUB scoped body CSS");

        AssertContains(
            combinedHtml,
            "#epub-chapter-1 .illustrated{background-image:url(data:image/png;base64,",
            "EPUB CSS image data URI");

        AssertContains(
            combinedHtml,
            "src=\"data:image/png;base64,",
            "EPUB PNG resource");

        AssertContains(
            combinedHtml,
            "src=\"data:image/svg+xml;base64,",
            "EPUB SVG resource");

        AssertContains(
            combinedHtml,
            "@font-face{font-family:\"EmbeddedTest\";src:url(data:font/woff2;base64,",
            "EPUB embedded font data URI");

        Assert(
            !combinedHtml.Contains(
                "local(\"DoNotUse\")",
                StringComparison.OrdinalIgnoreCase),
            "EPUB embedded font не должен зависеть от local() font source.");

        AssertContains(
            combinedHtml,
            "srcset=\"data:image/png;base64,",
            "EPUB srcset first embedded candidate");

        AssertContains(
            combinedHtml,
            "data:image/svg+xml;base64,",
            "EPUB srcset SVG candidate");

        AssertContains(
            combinedHtml,
            "src=\"data:audio/mpeg;base64,",
            "EPUB audio resource");

        AssertContains(
            combinedHtml,
            "src=\"data:video/mp4;base64,",
            "EPUB video resource");

        AssertContains(
            combinedHtml,
            "src=\"data:text/vtt;base64,",
            "EPUB captions track resource");

        Assert(
            !combinedHtml.Contains(
                "autoplay=",
                StringComparison.OrdinalIgnoreCase),
            "EPUB autoplay должен удаляться.");

        AssertContains(
            combinedHtml,
            "href=\"data:image/png;base64,",
            "EPUB inline SVG image resource");

        Assert(
            !combinedHtml.Contains(
                "https://example.invalid",
                StringComparison.OrdinalIgnoreCase),
            "EPUB CSS не должен выполнять внешние загрузки.");

        Assert(
            !combinedHtml.Contains(
                "position:fixed",
                StringComparison.OrdinalIgnoreCase),
            "Небезопасное CSS position:fixed не должно применяться.");

        Assert(
            !combinedHtml.Contains(
                "onerror=",
                StringComparison.OrdinalIgnoreCase),
            "EPUB event attributes должны удаляться.");

        Assert(
            !combinedHtml.Contains(
                "<script",
                StringComparison.OrdinalIgnoreCase),
            "EPUB script elements должны удаляться.");

        Assert(
            !combinedHtml.Contains(
                "href=\"javascript:",
                StringComparison.OrdinalIgnoreCase),
            "Опасная javascript: ссылка EPUB не должна попадать в HTML href.");
    }

    private static void TestEpubNcxReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression-epub2.epub");

        using (var archive = ZipFile.Open(
                   path,
                   ZipArchiveMode.Create))
        {
            WriteZipEntry(
                archive,
                "mimetype",
                "application/epub+zip",
                CompressionLevel.NoCompression);

            WriteZipEntry(
                archive,
                "META-INF/container.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <container version="1.0"
                           xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OPS/content.opf"
                              media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);

            WriteZipEntry(
                archive,
                "OPS/content.opf",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package version="2.0"
                         xmlns="http://www.idpf.org/2007/opf">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Regression EPUB2</dc:title>
                  </metadata>
                  <manifest>
                    <item id="c1"
                          href="Text/one.xhtml"
                          media-type="application/xhtml+xml" />
                    <item id="c2"
                          href="Text/two.xhtml"
                          media-type="application/xhtml+xml" />
                    <item id="ncx"
                          href="toc.ncx"
                          media-type="application/x-dtbncx+xml" />
                  </manifest>
                  <spine toc="ncx">
                    <itemref idref="c1" />
                    <itemref idref="c2" />
                  </spine>
                  <guide>
                    <reference type="text"
                               title="Start"
                               href="Text/one.xhtml#one" />
                  </guide>
                </package>
                """);

            WriteZipEntry(
                archive,
                "OPS/toc.ncx",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/">
                  <navMap>
                    <navPoint id="p1" playOrder="1">
                      <navLabel><text>One</text></navLabel>
                      <content src="Text/one.xhtml#one" />
                      <navPoint id="p1a" playOrder="2">
                        <navLabel><text>One A</text></navLabel>
                        <content src="Text/one.xhtml#one-a" />
                      </navPoint>
                    </navPoint>
                    <navPoint id="p2" playOrder="3">
                      <navLabel><text>Two</text></navLabel>
                      <content src="Text/two.xhtml#two" />
                    </navPoint>
                  </navMap>
                  <pageList>
                    <navLabel><text>Pages</text></navLabel>
                    <pageTarget id="pg1" value="1" type="normal" playOrder="4">
                      <navLabel><text>1</text></navLabel>
                      <content src="Text/one.xhtml#page-1" />
                    </pageTarget>
                  </pageList>
                </ncx>
                """);

            WriteZipEntry(
                archive,
                "OPS/Text/one.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1 id="one">One</h1>
                    <span id="page-1">1</span>
                    <h2 id="one-a">One A</h2>
                  </body>
                </html>
                """);

            WriteZipEntry(
                archive,
                "OPS/Text/two.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body><h1 id="two">Two</h1></body>
                </html>
                """);
        }

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertEqual(
            4,
            model.Outlines.Count,
            "EPUB2 NCX root count");

        AssertEqual(
            "epub-chapter-1-one",
            model.Outlines[0].TargetAnchorId!,
            "EPUB2 first NCX target");

        AssertEqual(
            "epub-chapter-1-one-a",
            model.Outlines[0].Children[0].TargetAnchorId!,
            "EPUB2 nested NCX target");

        AssertEqual(
            "epub-chapter-2-two",
            model.Outlines[1].TargetAnchorId!,
            "EPUB2 second NCX target");

        AssertEqual(
            "Pages",
            model.Outlines[2].Title,
            "EPUB2 NCX page-list group");

        AssertEqual(
            "epub-chapter-1-page-1",
            model.Outlines[2].Children[0].TargetAnchorId!,
            "EPUB2 NCX page-list target");

        AssertEqual(
            "Landmarks",
            model.Outlines[3].Title,
            "EPUB2 guide landmarks group");

        AssertEqual(
            "epub-chapter-1-one",
            model.Outlines[3].Children[0].TargetAnchorId!,
            "EPUB2 guide landmark target");
    }

    private static void TestEpubCorpusCompatibility(
        string repositoryRoot)
    {
        var epub2Path = Path.Combine(
            repositoryRoot,
            "Books",
            "Regression_EPUB2.epub");

        var epub3Path = Path.Combine(
            repositoryRoot,
            "Books",
            "Regression_EPUB3.epub");

        Assert(
            File.Exists(epub2Path),
            "EPUB2 compatibility fixture отсутствует в Books.");

        Assert(
            File.Exists(epub3Path),
            "EPUB3 compatibility fixture отсутствует в Books.");

        var epub2 = SupportedFormatRegistry.ReadDocument(
            epub2Path);

        AssertEqual(
            "MYBOOK EPUB2 Corpus",
            epub2.Title,
            "EPUB2 corpus title");

        AssertEqual(
            4,
            epub2.Outlines.Count,
            "EPUB2 corpus navigation groups");

        AssertEqual(
            "epub-chapter-1-start",
            epub2.Outlines[0].TargetAnchorId!,
            "EPUB2 corpus NCX target");

        AssertEqual(
            "Pages",
            epub2.Outlines[2].Title,
            "EPUB2 corpus NCX page-list");

        AssertEqual(
            "Landmarks",
            epub2.Outlines[3].Title,
            "EPUB2 corpus OPF guide");

        var epub2Html = string.Join(
            "\n",
            epub2.Blocks
                .OfType<DocumentHtmlBlock>()
                .Select(block => block.Html));

        AssertContains(
            epub2Html,
            "href=\"#epub-chapter-2-target\"",
            "EPUB2 corpus relative link");

        var epub3 = SupportedFormatRegistry.ReadDocument(
            epub3Path);

        AssertEqual(
            "MYBOOK EPUB3 Corpus",
            epub3.Title,
            "EPUB3 corpus title");

        AssertEqual(
            3,
            epub3.Outlines.Count,
            "EPUB3 corpus navigation groups");

        AssertEqual(
            "Landmarks",
            epub3.Outlines[1].Title,
            "EPUB3 corpus landmarks");

        AssertEqual(
            "Pages",
            epub3.Outlines[2].Title,
            "EPUB3 corpus page-list");

        var epub3Html = string.Join(
            "\n",
            epub3.Blocks
                .OfType<DocumentHtmlBlock>()
                .Select(block => block.Html));

        AssertContains(
            epub3Html,
            "epub-noteref",
            "EPUB3 corpus noteref semantics");

        AssertContains(
            epub3Html,
            "epub-note",
            "EPUB3 corpus footnote semantics");

        AssertContains(
            epub3Html,
            "epub-pagebreak",
            "EPUB3 corpus pagebreak semantics");
    }

    private static void TestDocxReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.docx");

        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=");

        using (var archive = ZipFile.Open(
                   path,
                   ZipArchiveMode.Create))
        {
            WriteZipEntry(
                archive,
                "word/document.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:o="urn:schemas-microsoft-com:office:office">
                  <w:body>
                    <w:p>
                      <w:bookmarkStart w:id="0" w:name="target" />
                      <w:r>
                        <w:t>DOCX marker</w:t>
                      </w:r>
                      <w:bookmarkEnd w:id="0" />
                    </w:p>
                    <w:p>
                      <w:hyperlink r:id="rIdExternal">
                        <w:r><w:t>External link</w:t></w:r>
                      </w:hyperlink>
                      <w:r><w:t> / </w:t></w:r>
                      <w:hyperlink w:anchor="target">
                        <w:r><w:t>Internal link</w:t></w:r>
                      </w:hyperlink>
                      <w:r><w:t> / </w:t></w:r>
                      <w:hyperlink r:id="rIdUnsafe">
                        <w:r><w:t>Unsafe link</w:t></w:r>
                      </w:hyperlink>
                    </w:p>
                    <w:p>
                      <w:pPr><w:pStyle w:val="DerivedStyle"/></w:pPr>
                      <w:r><w:t>Styled text</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:pPr>
                        <w:numPr>
                          <w:ilvl w:val="0"/>
                          <w:numId w:val="7"/>
                        </w:numPr>
                      </w:pPr>
                      <w:r><w:t>List item</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:r>
                        <w:drawing>
                          <a:graphic>
                            <a:graphicData>
                              <a:blip r:embed="rIdImage"/>
                            </a:graphicData>
                          </a:graphic>
                        </w:drawing>
                      </w:r>
                    </w:p>
                    <w:tbl>
                      <w:tr>
                        <w:tc>
                          <w:tcPr><w:gridSpan w:val="2"/></w:tcPr>
                          <w:p><w:r><w:t>Table header</w:t></w:r></w:p>
                        </w:tc>
                      </w:tr>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Cell A</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Cell B</w:t></w:r></w:p></w:tc>
                      </w:tr>
                    </w:tbl>
                    <w:p>
                      <w:r>
                        <w:t>Footnote ref </w:t>
                        <w:footnoteReference w:id="1"/>
                        <w:t> Endnote ref </w:t>
                        <w:endnoteReference w:id="2"/>
                      </w:r>
                    </w:p>
                    <w:p>
                      <w:pPr><w:pageBreakBefore/></w:pPr>
                      <w:r><w:t>Page break before marker</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:r>
                        <w:t>Before explicit break</w:t>
                        <w:br w:type="page"/>
                        <w:t>After explicit break</w:t>
                      </w:r>
                    </w:p>
                    <w:p>
                      <w:r>
                        <w:object>
                          <o:OLEObject ProgID="Package" r:id="rIdOle"/>
                        </w:object>
                      </w:r>
                    </w:p>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdHeader"/>
                      <w:headerReference w:type="even" r:id="rIdHeaderEven"/>
                      <w:footerReference w:type="default" r:id="rIdFooter"/>
                      <w:footerReference w:type="first" r:id="rIdFooterFirst"/>
                      <w:type w:val="oddPage"/>
                      <w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/>
                      <w:pgMar w:top="-720" w:right="1080" w:bottom="1440" w:left="1440"
                               w:header="720" w:footer="720" w:gutter="360"/>
                      <w:titlePg/>
                      <w:bidi/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);

            WriteZipEntry(
                archive,
                "word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExternal"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/docx"
                                TargetMode="External" />
                  <Relationship Id="rIdUnsafe"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="file:///C:/secret.txt"
                                TargetMode="External" />
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="media/image1.png" />
                  <Relationship Id="rIdHeader"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
                                Target="header1.xml" />
                  <Relationship Id="rIdFooter"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer"
                                Target="footer1.xml" />
                  <Relationship Id="rIdHeaderEven"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
                                Target="headerEven.xml" />
                  <Relationship Id="rIdFooterFirst"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer"
                                Target="footerFirst.xml" />
                  <Relationship Id="rIdOle"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject"
                                Target="embeddings/oleObject1.bin" />
                </Relationships>
                """);

            WriteZipEntry(
                archive,
                "word/styles.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="BaseStyle">
                    <w:rPr>
                      <w:b/>
                      <w:i/>
                    </w:rPr>
                  </w:style>
                  <w:style w:type="paragraph" w:styleId="DerivedStyle">
                    <w:basedOn w:val="BaseStyle"/>
                    <w:rPr>
                      <w:u w:val="single"/>
                    </w:rPr>
                  </w:style>
                </w:styles>
                """);

            WriteZipEntry(
                archive,
                "word/numbering.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:abstractNum w:abstractNumId="1">
                    <w:lvl w:ilvl="0">
                      <w:numFmt w:val="bullet"/>
                      <w:lvlText w:val="•"/>
                    </w:lvl>
                  </w:abstractNum>
                  <w:num w:numId="7">
                    <w:abstractNumId w:val="1"/>
                  </w:num>
                </w:numbering>
                """);

            WriteZipBytes(
                archive,
                "word/media/image1.png",
                pngBytes);

            WriteZipEntry(
                archive,
                "word/header1.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Header marker</w:t></w:r></w:p>
                </w:hdr>
                """);

            WriteZipEntry(
                archive,
                "word/footer1.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Footer marker</w:t></w:r></w:p>
                </w:ftr>
                """);

            WriteZipEntry(
                archive,
                "word/headerEven.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Even header marker</w:t></w:r></w:p>
                </w:hdr>
                """);

            WriteZipEntry(
                archive,
                "word/footerFirst.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>First footer marker</w:t></w:r></w:p>
                </w:ftr>
                """);

            WriteZipEntry(
                archive,
                "word/footnotes.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:footnote w:id="1">
                    <w:p><w:r><w:t>Footnote body marker</w:t></w:r></w:p>
                  </w:footnote>
                </w:footnotes>
                """);

            WriteZipEntry(
                archive,
                "word/endnotes.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:endnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:endnote w:id="2">
                    <w:p><w:r><w:t>Endnote body marker</w:t></w:r></w:p>
                  </w:endnote>
                </w:endnotes>
                """);

            WriteZipBytes(
                archive,
                "word/embeddings/oleObject1.bin",
                Encoding.UTF8.GetBytes("embedded-object-marker"));
        }

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "DOCX marker",
            "DOCX model");

        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        AssertEqual(
            "target",
            paragraphs[0].AnchorId!,
            "DOCX bookmark anchor");

        var links = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .Where(inline =>
                !string.IsNullOrWhiteSpace(
                    inline.LinkHref))
            .ToArray();

        Assert(
            links.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "https://example.com/docx",
                    StringComparison.Ordinal)),
            "DOCX external relationship hyperlink должен сохраниться.");

        Assert(
            links.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#target",
                    StringComparison.Ordinal)),
            "DOCX internal bookmark hyperlink должен сохраниться.");

        Assert(
            !links.Any(inline =>
                inline.LinkHref?.StartsWith(
                    "file:",
                    StringComparison.OrdinalIgnoreCase) == true),
            "DOCX file: hyperlink не должен попадать в нейтральную модель.");

        var styledInline = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .FirstOrDefault(inline =>
                inline.Text.Contains(
                    "Styled text",
                    StringComparison.Ordinal));

        Assert(
            styledInline != null &&
            styledInline.Bold &&
            styledInline.Italic &&
            styledInline.Underline,
            "DOCX style inheritance должен объединять basedOn и direct style formatting.");

        Assert(
            paragraphs
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    inline.Text.Contains(
                        "• List item",
                        StringComparison.Ordinal)),
            "DOCX numbering.xml bullet должен попасть в текстовую модель.");

        Assert(
            paragraphs
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    inline.Image != null &&
                    string.Equals(
                        inline.Image.ContentType,
                        "image/png",
                        StringComparison.Ordinal)),
            "DOCX image relationship должен загрузить встроенное изображение.");

        var table = model.Blocks
            .OfType<DocumentTable>()
            .SingleOrDefault();

        Assert(
            table != null &&
            table.Rows.Count == 2 &&
            table.Rows[0].Cells.Count == 1 &&
            table.Rows[0].Cells[0].ColumnSpan == 2 &&
            table.Rows[1].Cells.Count == 2,
            "DOCX table должен сохранять строки, ячейки и gridSpan.");

        Assert(
            links.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#docx-footnote-1",
                    StringComparison.Ordinal)),
            "DOCX footnoteReference должен стать внутренней ссылкой.");

        Assert(
            links.Any(inline =>
                string.Equals(
                    inline.LinkHref,
                    "#docx-endnote-2",
                    StringComparison.Ordinal)),
            "DOCX endnoteReference должен стать внутренней ссылкой.");

        Assert(
            paragraphs.Any(paragraph =>
                paragraph.IsNote &&
                string.Equals(
                    paragraph.AnchorId,
                    "docx-footnote-1",
                    StringComparison.Ordinal)),
            "DOCX footnotes.xml должен создавать note anchor.");

        Assert(
            paragraphs.Any(paragraph =>
                paragraph.IsNote &&
                string.Equals(
                    paragraph.AnchorId,
                    "docx-endnote-2",
                    StringComparison.Ordinal)),
            "DOCX endnotes.xml должен создавать note anchor.");

        var sectionProperties = model.Blocks
            .OfType<DocumentSectionProperties>()
            .Single();

        Assert(
            sectionProperties.SectionIndex == 0 &&
            sectionProperties.BreakKind == DocumentSectionBreakKind.OddPage &&
            sectionProperties.Orientation == DocumentPageOrientation.Landscape &&
            sectionProperties.PageWidthPoints == 792 &&
            sectionProperties.PageHeightPoints == 612 &&
            sectionProperties.MarginLeftPoints == 72 &&
            sectionProperties.MarginRightPoints == 54 &&
            sectionProperties.MarginTopPoints == -36 &&
            sectionProperties.MarginBottomPoints == 72 &&
            sectionProperties.GutterPoints == 18 &&
            sectionProperties.HeaderDistancePoints == 36 &&
            sectionProperties.FooterDistancePoints == 36 &&
            sectionProperties.DifferentFirstPage == true &&
            sectionProperties.RightToLeft == true,
            "DOCX sectPr должен сохранять page setup в нейтральной модели.");

        Assert(
            ReferenceEquals(
                model.Blocks[0],
                sectionProperties),
            "DOCX section properties должны стоять в начале соответствующей секции.");

        var regions = model.Blocks
            .OfType<DocumentFlowRegion>()
            .ToArray();

        Assert(
            regions.Any(region =>
                region.Kind == DocumentFlowRegionKind.Header &&
                region.Blocks
                    .OfType<DocumentParagraph>()
                    .SelectMany(paragraph => paragraph.Inlines)
                    .Any(inline =>
                        inline.Text.Contains(
                            "Header marker",
                            StringComparison.Ordinal))),
            "DOCX header part должен сохраняться как header region.");

        Assert(
            regions.Any(region =>
                region.Kind == DocumentFlowRegionKind.Footer &&
                region.Blocks
                    .OfType<DocumentParagraph>()
                    .SelectMany(paragraph => paragraph.Inlines)
                    .Any(inline =>
                        inline.Text.Contains(
                            "Footer marker",
                            StringComparison.Ordinal))),
            "DOCX footer part должен сохраняться как footer region.");

        Assert(
            regions.Any(region =>
                region.Kind == DocumentFlowRegionKind.Header &&
                region.Variant == DocumentFlowRegionVariant.Default &&
                region.SectionIndex == 0),
            "DOCX default header должен сохранять variant и section index.");

        Assert(
            regions.Any(region =>
                region.Kind == DocumentFlowRegionKind.Header &&
                region.Variant == DocumentFlowRegionVariant.Even &&
                region.SectionIndex == 0 &&
                region.Blocks
                    .OfType<DocumentParagraph>()
                    .SelectMany(paragraph => paragraph.Inlines)
                    .Any(inline =>
                        inline.Text.Contains(
                            "Even header marker",
                            StringComparison.Ordinal))),
            "DOCX even header должен сохраняться как Even region.");

        Assert(
            regions.Any(region =>
                region.Kind == DocumentFlowRegionKind.Footer &&
                region.Variant == DocumentFlowRegionVariant.First &&
                region.SectionIndex == 0 &&
                region.Blocks
                    .OfType<DocumentParagraph>()
                    .SelectMany(paragraph => paragraph.Inlines)
                    .Any(inline =>
                        inline.Text.Contains(
                            "First footer marker",
                            StringComparison.Ordinal))),
            "DOCX first footer должен сохраняться как First region.");

        Assert(
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Count(flowBreak =>
                    flowBreak.Kind == DocumentFlowBreakKind.Page) >= 2,
            "DOCX pageBreakBefore и explicit page break должны сохраняться в модели.");

        Assert(
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Any(flowBreak =>
                    flowBreak.Kind == DocumentFlowBreakKind.Section),
            "DOCX sectPr должен сохранять section break.");

        var embeddedObject = model.Blocks
            .OfType<DocumentEmbeddedObjectBlock>()
            .SingleOrDefault();

        Assert(
            embeddedObject != null &&
            embeddedObject.Data.Length > 0 &&
            string.Equals(
                embeddedObject.FileName,
                "oleObject1.bin",
                StringComparison.Ordinal),
            "DOCX embedded OLE relationship должен сохраняться безопасным бинарным блоком.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "id=\"anchor-target\"",
            "DOCX bookmark rendered anchor");

        AssertContains(
            html,
            "href=\"#anchor-target\"",
            "DOCX internal rendered link");

        AssertContains(
            html,
            "href=\"https://example.com/docx\"",
            "DOCX external rendered link");

        AssertContains(
            html,
            "class=\"inline-image\"",
            "DOCX rendered image");

        AssertContains(
            html,
            "<table class=\"flow-table\"",
            "DOCX rendered table");

        AssertContains(
            html,
            "colspan=\"2\"",
            "DOCX rendered table span");

        AssertContains(
            html,
            "class=\"section-properties\"",
            "DOCX rendered section properties");

        AssertContains(
            html,
            "data-break-kind=\"OddPage\"",
            "DOCX rendered section break kind");

        AssertContains(
            html,
            "data-page-width-pt=\"792\"",
            "DOCX rendered section page width");

        AssertContains(
            html,
            "data-margin-top-pt=\"-36\"",
            "DOCX rendered section top margin");

        AssertContains(
            html,
            "flow-region-header",
            "DOCX rendered header region");

        AssertContains(
            html,
            "flow-region-footer",
            "DOCX rendered footer region");

        AssertContains(
            html,
            "flow-region-even",
            "DOCX rendered even region");

        AssertContains(
            html,
            "flow-region-first",
            "DOCX rendered first region");

        AssertContains(
            html,
            "data-section-index=\"0\"",
            "DOCX rendered region section index");

        AssertContains(
            html,
            "href=\"#anchor-docx-footnote-1\"",
            "DOCX rendered footnote reference");

        AssertContains(
            html,
            "href=\"#anchor-docx-endnote-2\"",
            "DOCX rendered endnote reference");

        AssertContains(
            html,
            "flow-break-page",
            "DOCX rendered page break");

        AssertContains(
            html,
            "flow-break-section",
            "DOCX rendered section break");

        AssertContains(
            html,
            "class=\"embedded-object\"",
            "DOCX rendered embedded object");

        Assert(
            !html.Contains(
                "href=\"file:",
                StringComparison.OrdinalIgnoreCase),
            "DOCX renderer не должен выпускать file: hyperlink.");
    }

    private static void TestPdfLzwDecoder()
    {
        var expected = Enumerable
            .Range(
                0,
                260)
            .Select(index =>
                (byte)(index % 251))
            .ToArray();

        var earlyOne = Convert.FromBase64String(
            "gAAAICAYEAoGA4IBIKBYMBoOB4QCISCYUCoWC4YDIaDYcDoeD4gEIiEYkEomE4oFIqFYsFouF4wGIyGY0Go2G44HI6HY8Ho+H5AIJCIZEIpGI5IJJKJZMJpOJ5QKJSKZUKpWK5YLJaLZcLpeL5gMJiMZkMpmM5oNJqNZsNpuN5wOJyOZ0Op2O54PJ6PZ8Pp+P6AQKCQaEQqGQ6IRKKRaMRqOR6QSKSSaUSqWS6YTKaTacTqeT6gUKiUakUqmU6oVKqVasVquV6wWKyWa0Wq2W64XK6Xa8Xq+X7AYLCYbEYrGY7IZLKZbMZrOZ7QaLSabUarWa7YbLabbcbreb7gcLicbkcrmc7odLqdbsdrud7weLyeb0er2e74fL6AABAQBgIAoDAOBCAg=");

        var earlyZero = Convert.FromBase64String(
            "gAAAICAYEAoGA4IBIKBYMBoOB4QCISCYUCoWC4YDIaDYcDoeD4gEIiEYkEomE4oFIqFYsFouF4wGIyGY0Go2G44HI6HY8Ho+H5AIJCIZEIpGI5IJJKJZMJpOJ5QKJSKZUKpWK5YLJaLZcLpeL5gMJiMZkMpmM5oNJqNZsNpuN5wOJyOZ0Op2O54PJ6PZ8Pp+P6AQKCQaEQqGQ6IRKKRaMRqOR6QSKSSaUSqWS6YTKaTacTqeT6gUKiUakUqmU6oVKqVasVquV6wWKyWa0Wq2W64XK6Xa8Xq+X7AYLCYbEYrGY7IZLKZbMZrOZ7QaLSabUarWa7YbLabbcbreb7gcLicbkcrmc7odLqdbsdrud7weLyeb0er2e74fL6AABAQDAQBQGAcCEBA=");

        Assert(
            PdfStreamFilterDecoder.DecodeLzw(
                earlyOne,
                earlyChange: 1)
                .SequenceEqual(expected),
            "PDF LZW EarlyChange=1 должен корректно переживать переход 9→10 bits.");

        Assert(
            PdfStreamFilterDecoder.DecodeLzw(
                earlyZero,
                earlyChange: 0)
                .SequenceEqual(expected),
            "PDF LZW EarlyChange=0 должен корректно переживать переход 9→10 bits.");
    }

    private static void TestPdfReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.pdf");

        CreateSimplePdf(
            path,
            "PDF marker");

        var model = SupportedFormatRegistry.ReadDocument(path);
        var page = model.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertContains(
            page.Text,
            "PDF marker",
            "PDF text layer");

        AssertEqual(
            1,
            page.PageNumber,
            "PDF page number");

        var lzwPath = Path.Combine(
            directory,
            "regression-lzw.pdf");

        CreateLzwPdf(
            lzwPath,
            "PDF LZW marker");

        var lzwModel =
            SupportedFormatRegistry.ReadDocument(
                lzwPath);
        var lzwPage = lzwModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertContains(
            lzwPage.Text,
            "PDF LZW marker",
            "PDF LZW content stream");
    }

    private static void TestParserTrace(string directory)
    {
        var baseName =
            "parser-trace-" +
            Guid.NewGuid().ToString("N");

        var path = Path.Combine(
            directory,
            baseName + ".pdf");

        CreateSimplePdf(
            path,
            "TRACE marker");

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MYBOOK",
            "Logs");

        ParserTrace.Configure(
            true,
            "MYBOOK");

        try
        {
            var model = SupportedFormatRegistry.ReadDocument(path);

            Assert(
                model.Blocks.OfType<DocumentFixedPage>().Count() == 1,
                "Trace fixture должен прочитаться как одностраничный PDF.");
        }
        finally
        {
            ParserTrace.Configure(
                false,
                "MYBOOK");
        }

        var logPath = Directory
            .EnumerateFiles(
                logDirectory,
                "*-" + baseName + "-pdf.log",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Parser trace не создал log-файл.");

        try
        {
            var trace = File.ReadAllText(
                logPath,
                Encoding.UTF8);

            AssertContains(
                trace,
                "[pdf] version=1.4",
                "Parser trace PDF header");

            AssertContains(
                trace,
                "[pdf-page] page=1",
                "Parser trace page checkpoint");

            AssertContains(
                trace,
                "[registry] success",
                "Parser trace registry result");

            AssertContains(
                trace,
                "[session] END",
                "Parser trace session completion");
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static void TestDocCorpusDispatch(string repositoryRoot)
    {
        var booksDirectory = Path.Combine(
            repositoryRoot,
            "Books");

        var path = Directory
            .EnumerateFiles(
                booksDirectory,
                "*.doc",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Books не содержит regression .doc fixture.");

        var model = SupportedFormatRegistry.ReadDocument(path);

        Assert(
            model.Blocks.Count > 0,
            ".doc corpus dispatch должен вернуть хотя бы один блок.");

        Assert(
            !string.IsNullOrWhiteSpace(FlattenText(model)),
            ".doc corpus dispatch должен вернуть текст.");
    }

    private static string FlattenText(DocumentModel model)
    {
        var result = new StringBuilder();

        foreach (var block in model.Blocks)
        {
            switch (block)
            {
                case DocumentParagraph paragraph:
                    foreach (var inline in paragraph.Inlines)
                    {
                        result.Append(inline.Text);
                    }

                    result.AppendLine();
                    break;

                case DocumentHtmlBlock html:
                    result.AppendLine(html.Html);
                    break;

                case DocumentFixedPage page:
                    result.AppendLine(page.Text);
                    break;
            }
        }

        return result.ToString();
    }

    private static void WriteZipBytes(
        ZipArchive archive,
        string path,
        byte[] data)
    {
        var entry = archive.CreateEntry(
            path,
            CompressionLevel.Optimal);

        using var stream = entry.Open();
        stream.Write(
            data,
            0,
            data.Length);
    }

    private static void WriteZipEntry(
        ZipArchive archive,
        string path,
        string content,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(
            path,
            compressionLevel);

        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            leaveOpen: false);

        writer.Write(content);
    }

    private static void CreateSimplePdf(
        string path,
        string text)
    {
        var encoding = Encoding.ASCII;
        var content =
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "(" + EscapePdfString(text) + ") Tj\n" +
            "ET\n";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {encoding.GetByteCount(content)} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        Write("%PDF-1.4\n");

        var offsets = new List<long>();

        for (var index = 0;
             index < objects.Length;
             index++)
        {
            offsets.Add(memory.Position);

            Write(
                $"{index + 1} 0 obj\n" +
                objects[index] +
                "\nendobj\n");
        }

        var xref = memory.Position;

        Write(
            "xref\n" +
            $"0 {objects.Length + 1}\n" +
            "0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString("0000000000") +
                " 00000 n \n");
        }

        Write(
            "trailer\n" +
            $"<< /Size {objects.Length + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static void CreateLzwPdf(
        string path,
        string text)
    {
        var encoding = Encoding.ASCII;
        var content =
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "(" + EscapePdfString(text) + ") Tj\n" +
            "ET\n";
        var compressed = EncodeLzwLiteralStream(
            encoding.GetBytes(content));

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        Write("%PDF-1.4\n");

        var offsets = new List<long>();

        void WriteObject(
            int objectNumber,
            string body)
        {
            offsets.Add(memory.Position);
            Write(
                $"{objectNumber} 0 obj\n" +
                body +
                "\nendobj\n");
        }

        WriteObject(
            1,
            "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(
            2,
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>");
        WriteObject(
            3,
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");

        offsets.Add(memory.Position);
        Write(
            "4 0 obj\n" +
            $"<< /Length {compressed.Length} /Filter /LZWDecode /DecodeParms << /EarlyChange 1 >> >>\n" +
            "stream\n");
        memory.Write(
            compressed,
            0,
            compressed.Length);
        Write(
            "\nendstream\n" +
            "endobj\n");

        WriteObject(
            5,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var xref = memory.Position;

        Write(
            "xref\n" +
            "0 6\n" +
            "0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString("0000000000") +
                " 00000 n \n");
        }

        Write(
            "trailer\n" +
            "<< /Size 6 /Root 1 0 R >>\n" +
            "startxref\n" +
            xref +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static byte[] EncodeLzwLiteralStream(
        byte[] data)
    {
        if (data.Length > 250)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                "Тестовый literal-only LZW encoder рассчитан на 9-bit stream без смены ширины.");
        }

        var codes = new List<int>(
            data.Length +
            2)
        {
            256
        };

        codes.AddRange(
            data.Select(value =>
                (int)value));
        codes.Add(257);

        var bitCount =
            codes.Count *
            9;
        var output = new byte[
            (bitCount + 7) /
            8];
        var bitPosition = 0;

        foreach (var code in codes)
        {
            for (var shift = 8;
                 shift >= 0;
                 shift--)
            {
                if (((code >> shift) & 1) != 0)
                {
                    var byteIndex =
                        bitPosition /
                        8;
                    var bitIndex =
                        7 -
                        bitPosition %
                        8;

                    output[byteIndex] |=
                        (byte)(1 << bitIndex);
                }

                bitPosition++;
            }
        }

        return output;
    }

    private static string EscapePdfString(string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "(",
                "\\(",
                StringComparison.Ordinal)
            .Replace(
                ")",
                "\\)",
                StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        if (args.Length > 0 &&
            File.Exists(
                Path.Combine(
                    args[0],
                    "MYBOOK.csproj")))
        {
            return Path.GetFullPath(
                args[0]);
        }

        var current = new DirectoryInfo(
            Directory.GetCurrentDirectory());

        while (current != null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "MYBOOK.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Не удалось найти корень репозитория MYBOOK.");
    }

    private static DocumentRenderTheme CreateRenderTheme()
    {
        return new DocumentRenderTheme
        {
            Background = "#FFFFFF",
            Surface = "#F7F7F7",
            Text = "#111111",
            MutedText = "#666666",
            Border = "#DADADA",
            Primary = "#3367D6"
        };
    }

    private static void AssertContains(
        string actual,
        string expected,
        string context)
    {
        Assert(
            actual.Contains(
                expected,
                StringComparison.Ordinal),
            $"{context}: ожидалась строка \"{expected}\".");
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string context)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(
                expected,
                actual))
        {
            throw new InvalidOperationException(
                $"{context}: expected={expected}, actual={actual}.");
        }
    }

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
