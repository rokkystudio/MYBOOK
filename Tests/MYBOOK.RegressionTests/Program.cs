using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Book.Diagnostics;
using Book.Documents;
using Book.Formats;
using Book.Formats.Doc;
using Book.Formats.Pdf;
using Book.Formats.Rtf;
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
            ("Document input guard", () => TestDocumentInputGuard(temporaryDirectory)),
            ("Binary malformed inputs", () => TestBinaryMalformedInputs(temporaryDirectory)),
            ("TXT reader", () => TestTextReader(temporaryDirectory)),
            ("Markdown reader", () => TestMarkdownReader(temporaryDirectory)),
            ("RTF reader", () => TestRtfReader(temporaryDirectory)),
            (".doc RTF dispatch", () => TestDocRtfDispatch(temporaryDirectory)),
            ("DOC field model", TestDocFieldModel),
            ("DOC inline picture", TestDocInlinePicture),
            ("DOC floating shape tables", TestDocFloatingShapeTables),
            ("DOC floating shape image", TestDocFloatingShapeImage),
            ("DOC OfficeArt vector shapes", TestDocOfficeArtVectorShapes),
            ("DOC header story table", TestDocHeaderStoryTable),
            ("DOC section table", TestDocSectionTable),
            ("DOC paragraph properties", TestDocParagraphProperties),
            ("DOC character style", TestDocCharacterStyle),
            ("DOC table definition", TestDocTableDefinition),
            ("DOC nested tables", TestDocNestedTables),
            ("DOC footnote tables", TestDocFootnoteTables),
            ("DOC endnote tables", TestDocEndnoteTables),
            ("DOC binary reader", () => TestDocBinaryReader(repositoryRoot)),
            ("FB2 reader", () => TestFb2Reader(temporaryDirectory)),
            ("EPUB reader", () => TestEpubReader(temporaryDirectory)),
            ("EPUB NCX reader", () => TestEpubNcxReader(temporaryDirectory)),
            ("EPUB corpus compatibility", () => TestEpubCorpusCompatibility(repositoryRoot)),
            ("DOCX reader", () => TestDocxReader(temporaryDirectory)),
            ("PDF SASLprep", TestPdfSaslPrep),
            ("PDF Indexed matte", TestPdfIndexedMatte),
            ("PDF CIE color spaces", TestPdfCieColorSpaces),
            ("PDF CIE DCT reader", () => TestPdfCieDctReader(temporaryDirectory)),
            ("PDF ICC color space", () => TestPdfIccColorSpace(temporaryDirectory)),
            ("PDF advanced color matte", () => TestPdfAdvancedColorMatte(temporaryDirectory)),
            ("PDF Separation color space", () => TestPdfSeparationColorSpace(temporaryDirectory)),
            ("PDF sampled and stitching functions", TestPdfSampledAndStitchingFunctions),
            ("PDF sampled order 3 reader", () => TestPdfSampledOrder3Reader(temporaryDirectory)),
            ("PDF calculator function", TestPdfCalculatorFunction),
            ("PDF DeviceN color space", () => TestPdfDeviceNColorSpace(temporaryDirectory)),
            ("PDF DCT JPEG decoder", TestPdfJpegDecoder),
            ("PDF CMYK DCT reader", () => TestPdfCmykDctReader(temporaryDirectory)),
            ("PDF ICC CMYK DCT reader", () => TestPdfIccCmykDctReader(temporaryDirectory)),
            ("PDF JPX reader", () => TestPdfJpxReader(temporaryDirectory)),
            ("PDF JBIG2 reader", () => TestPdfJbig2Reader(temporaryDirectory)),
            ("PDF ImageMask stencil", () => TestPdfImageMaskStencil(temporaryDirectory)),
            ("PDF LZW decoder", TestPdfLzwDecoder),
            ("PDF CCITT decoder", () => TestPdfCcittDecoder(temporaryDirectory)),
            ("PDF reader", () => TestPdfReader(temporaryDirectory)),
            ("Parser trace", () => TestParserTrace(temporaryDirectory)),
            (".doc corpus dispatch", () => TestDocCorpusDispatch(repositoryRoot)),
            ("Supported corpus smoke", () => TestSupportedCorpusSmoke(repositoryRoot))
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

    /// <summary>
    /// Проверяет общие resource limits и безопасную XML-конфигурацию readers.
    /// </summary>
    private static void TestDocumentInputGuard(string directory)
    {
        var oversizedPath = Path.Combine(
            directory,
            "oversized.txt");

        using (var stream = new FileStream(
                   oversizedPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.SetLength(
                DocumentInputGuard.MaximumFileBytes +
                1);
        }

        var oversizedRejected = false;

        try
        {
            SupportedFormatRegistry.ReadDocument(
                oversizedPath);
        }
        catch (InvalidDataException)
        {
            oversizedRejected = true;
        }

        Assert(
            oversizedRejected,
            "Общий file-size limit должен срабатывать до чтения oversized документа.");

        var externalEntityPath = Path.Combine(
            directory,
            "xml-external-entity.txt");
        File.WriteAllText(
            externalEntityPath,
            "EXTERNAL ENTITY MUST NOT BE READ",
            Encoding.UTF8);

        var xml = $"""
            <?xml version="1.0"?>
            <!DOCTYPE root SYSTEM "{new Uri(externalEntityPath).AbsoluteUri}">
            <root>safe</root>
            """;

        using var input = new MemoryStream(
            Encoding.UTF8.GetBytes(xml));
        var document = DocumentInputGuard.LoadXml(
            input,
            "Input guard regression");

        AssertEqual(
            "safe",
            document.Root?.Value ?? string.Empty,
            "XML DTD должен игнорироваться без external resolution");

        var deepRtfPath = Path.Combine(
            directory,
            "deep-nesting.rtf");
        File.WriteAllText(
            deepRtfPath,
            "{\\rtf1" +
            new string('{', 1025) +
            "x" +
            new string('}', 1025) +
            "}",
            Encoding.ASCII);

        var deepRtfRejected = false;

        try
        {
            RtfDocumentReader.Read(
                deepRtfPath);
        }
        catch (InvalidDataException)
        {
            deepRtfRejected = true;
        }

        Assert(
            deepRtfRejected,
            "RTF group-depth limit должен отклонять чрезмерно вложенный документ.");
    }

    /// <summary>
    /// Передаёт детерминированные повреждённые бинарные документы в DOC/PDF readers
    /// и требует контролируемого отказа без runtime-specific аварий.
    /// </summary>
    private static void TestBinaryMalformedInputs(string directory)
    {
        static void AssertControlled(
            Action action,
            string context)
        {
            try
            {
                action();
            }
            catch (Exception error)
                when (error is InvalidDataException or
                      EndOfStreamException or
                      IOException or
                      NotSupportedException or
                      ArgumentException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{context}: повреждённый документ был принят без ожидаемого структурного отказа.");
        }

        var random = new Random(0x4D59424F);

        for (var index = 0; index < 32; index++)
        {
            var docBytes = new byte[
                512 +
                index * 17];
            random.NextBytes(docBytes);

            new byte[]
            {
                0xD0, 0xCF, 0x11, 0xE0,
                0xA1, 0xB1, 0x1A, 0xE1
            }.CopyTo(
                docBytes,
                0);

            var docPath = Path.Combine(
                directory,
                $"malformed-{index:00}.doc");
            File.WriteAllBytes(
                docPath,
                docBytes);

            AssertControlled(
                () => SupportedFormatRegistry.ReadDocument(
                    docPath),
                $"DOC malformed fixture {index}");

            var pdfBytes = new byte[
                96 +
                index * 13];
            random.NextBytes(pdfBytes);
            "%PDF-1.7\n"u8.CopyTo(
                pdfBytes);

            var pdfPath = Path.Combine(
                directory,
                $"malformed-{index:00}.pdf");
            File.WriteAllBytes(
                pdfPath,
                pdfBytes);

            AssertControlled(
                () => SupportedFormatRegistry.ReadDocument(
                    pdfPath),
                $"PDF malformed fixture {index}");
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
            {\fonttbl{\f0 Arial;}{\f1\fcharset204 Times New Roman;}{\f2\fcharset128 MS Gothic;}{\f3\fcharset2 Symbol;}{\f4\fcharset255 Terminal;}}
            {\colortbl;\red255\green0\blue0;\red255\green255\blue0;}
            {\*\bkmkstart bookmark}
            \qc\li720\ri360\fi-180\sb240\sa120\sl360\bgdkcross\shading5000\cfpat1\cbpat2\f1\fs28\cf1\highlight2\b\i\ul Styled Привет\b0\i0\ulnone\highlight0\cf0\par
            \pard\sl360\slmult1\keep\keepn\widctl\rtlpar\hyphpar\sbauto1\saauto1\contextualspace\mirrorindents\nosnaplinegrid\suppressline\adjustright\nocwrap\nooverflow\aspalpha\aspnum Paragraph controls\par
            \pard\pagebb\outlinelevel3 Page break outline\par
            \pard\tqr\tldot\tx1440\tqc\tlhyph\tx2880\tqdec\tleq\tx4320 Tabs\tab One\tab Two\tab Three\par
            \pard Unicode \u1046?\par
            \f2 \'82\'a0\par
            {\f3 \'41}\par
            {\pc\f4 \'82}\par
            \f0 \strike strike\strike0 \super sup\nosupersub \sub sub\nosupersub\par
            {\field{\*\fldinst HYPERLINK "https://example.com/rtf"}{\fldrslt External RTF link}}\par
            {\field{\*\fldinst HYPERLINK \l "bookmark"}{\fldrslt Internal RTF link}}\par
            {\field{\*\fldinst HYPERLINK "file:///C:/secret.txt"}{\fldrslt Unsafe RTF link}}\par
            \trowd\irow0\irowband0\tblind720\tblindtype3\tbllkborder\tbllkshading\tbllkfont\tbllkcolor\tbllkbestfit\tbllkhdrrows\tbllklastrow\tbllkhdrcols\tbllklastcol\tbllknorowband\tbllknocolband\trqc\trleft720\trgaph60\rtlrow\trpaddl100\trpaddfl3\trpaddr120\trpaddfr3\trpaddt80\trpaddft3\trpaddb60\trpaddfb3\trspdl40\trspdfl3\trspdr60\trspdfr3\trspdt20\trspdft3\trspdb30\trspdfb3\trbgcross\trshdng5000\trcfpat1\trcbpat2\trftsWidth2\trwWidth5000\trhdr\trkeep\trkeepfollow\trautofit1\trrh-480\trbrdrt\brdrdash\brdrw60\brdrcf2\trbrdrl\brdrs\brdrw20\brdrcf2\trbrdrb\brdrdb\brdrw40\brdrcf2\trbrdrr\brdrdot\brdrw30\brdrcf2\trbrdrh\brdrs\brdrw20\brdrcf2\trbrdrv\brdrdot\brdrw20\brdrcf2\clvertalc\clpadl100\clpadfl3\clpadr120\clpadfr3\clpadt80\clpadft3\clpadb60\clpadfb3\clNoWrap\clhidemark\clbrdrt\brdrs\brdrw20\brdrcf1\clbrdrl\brdrdot\brdrw30\brdrcf1\clbrdrb\brdrdb\brdrw40\brdrcf1\clbrdrr\brdrdash\brdrw20\brdrcf1\clshdng10000\clcfpat2\clcbpat1\cellx1000\clvertalb\clFitText\cellx2400\intbl Cell A\cell Cell B\cell\row
            \trowd\irow1\irowband1\lastrow\trqr\ltrrow\trftsWidth3\trwWidth1440\clmgf\clshdng5000\clcfpat1\clcbpat2\cellx1000\clmrg\cellx2000\intbl Merged RTF cell\cell\cell\row
            \trowd\trql\trftsWidth1\clvmgf\cellx1000\cellx2000\intbl Vertical RTF cell\cell Row 3 B\cell\row
            \trowd\trftsWidth0\trwWidth777\clvmrg\cellx1000\cellx2000\intbl\cell Row 4 B\cell\row
            \trowd\cltxtbrl\clftsWidth0\clwWidth777\cellx600\cltxbtlr\clftsWidth1\clwWidth999\cellx1200\cltxlrtbv\clftsWidth2\clwWidth1250\cellx1800\cltxtbrlv\clftsWidth3\clwWidth600\cellx2400\intbl NullWidth\cell AutoWidth\cell PercentWidth\cell TwipsWidth\cell\row
            \trowd\clbgdkhor\clcfpat1\clcbpat2\cellx400\clbgdkvert\clcfpat1\clcbpat2\cellx800\clbgdkfdiag\clcfpat1\clcbpat2\cellx1200\clbgdkbdiag\clcfpat1\clcbpat2\cellx1600\clbgdkcross\clshdng5000\clcfpat1\clcbpat2\cellx2000\clbgdkdcross\clcfpat1\clcbpat2\cellx2400\intbl DkH\cell DkV\cell DkFD\cell DkBD\cell DkCross\cell DkDCross\cell\row
            \trowd\clbghoriz\clcfpat1\clcbpat2\cellx400\clbgvert\clcfpat1\clcbpat2\cellx800\clbgfdiag\clcfpat1\clcbpat2\cellx1200\clbgbdiag\clcfpat1\clcbpat2\cellx1600\clbgcross\clcfpat1\clcbpat2\cellx2000\clbgdcross\clcfpat1\clcbpat2\cellx2400\clbgcross\clcfpat1\clcbpat2\clshdrawnil\cellx2800\intbl H\cell V\cell FD\cell BD\cell Cross\cell DCross\cell Reset\cell\row
            \trowd\clbrdrt\brdrhair\brdrw10\brdrcf1\cldglu\brdrs\brdrw20\brdrcf2\brsp10\brdrsh\brdrframe\cellx600\clbrdrt\brdrdashd\brdrw20\brdrcf1\cldgll\brdrdb\brdrw40\brdrcf2\cellx1200\clbrdrt\brdrdashdd\brdrw20\brdrcf1\cellx1800\clbrdrt\brdrtriple\brdrw30\brdrcf1\cellx2400\intbl Hair\cell DotDash\cell DotDotDash\cell Triple\cell\row
            \trowd\clbrdrt\brdrth\cellx200\clbrdrt\brdrtnthsg\cellx400\clbrdrt\brdrthtnsg\cellx600\clbrdrt\brdrtnthtnsg\cellx800\clbrdrt\brdrtnthmg\cellx1000\clbrdrt\brdrthtnmg\cellx1200\clbrdrt\brdrtnthtnmg\cellx1400\clbrdrt\brdrtnthlg\cellx1600\clbrdrt\brdrthtnlg\cellx1800\clbrdrt\brdrtnthtnlg\cellx2000\clbrdrt\brdrwavy\cellx2200\clbrdrt\brdrwavydb\cellx2400\clbrdrt\brdrdashsm\cellx2600\clbrdrt\brdrdashdotstr\cellx2800\clbrdrt\brdremboss\cellx3000\clbrdrt\brdrengrave\cellx3200\clbrdrt\brdroutset\cellx3400\clbrdrt\brdrinset\cellx3600\intbl Thick\cell TTSG\cell TTSG2\cell TTTSG\cell TTMG\cell TTMG2\cell TTTMG\cell TTLG\cell TTLG2\cell TTTLG\cell Wavy\cell WavyDb\cell DashSm\cell DashDotStr\cell Emboss\cell Engrave\cell Outset\cell Inset\cell\row
            \pard Between RTF tables\par
            \trowd\cellx4000
            \intbl\itap1 Outer before\par
            \intbl\itap2 Inner before\par
            \intbl\itap3 Deep nested\nestcell
            {\*\nesttableprops\trowd\cellx1000\nestrow}
            \intbl\itap2 Inner after\nestcell
            {\*\nesttableprops\trowd\cellx2000\nestrow}
            \intbl\itap1 Outer after\cell\row
            \pard\itap0
            {\pict\pngblip\bliptag-123{\*\blipuid 00112233445566778899AABBCCDDEEFF}\picw1\pich1\picwgoal1440\pichgoal720\picscalex50\picscaley200\piccropl120\piccropt-40\piccropr60\piccropb20 89504E470D0A1A0A0000000D4948445200000001000000010804000000B51C0C020000000B4944415478DA63FCFF1F0002EB01F56950DA2A0000000049454E44AE426082}\par
            {\pict\dibitmap0\wbmbitspixel24\wbmplanes1\wbmwidthbytes4 280000000100000001000000010018000000000004000000000000000000000000000000000000000000FF00}\par
            {\pict\emfblip 0100000000000000000000000000000000000000000000000000000000000000000000000000000020454D46}\par
            {\pict\wmetafile8 000009000000000000000000000000000000}\par
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

        AssertContains(
            text,
            "",
            "RTF Symbol charset private-use mapping");

        AssertContains(
            text,
            "é",
            "RTF explicitly declared OEM codepage");

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

        Assert(
            formattedParagraph.Shading is
            {
                Pattern: 0x0012,
                FillPercentage: 50,
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFF00"
            },
            "RTF paragraph shading должен сохранять hatch pattern, percentage и оба цвета.");

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
            controlledParagraph.RightToLeft &&
            controlledParagraph.AutomaticHyphenation == true &&
            controlledParagraph.SpaceBeforeAuto == true &&
            controlledParagraph.SpaceAfterAuto == true &&
            controlledParagraph.ContextualSpacing == true &&
            controlledParagraph.MirrorIndents == true &&
            controlledParagraph.SnapToDocumentGrid == false &&
            controlledParagraph.SuppressLineNumber == true &&
            controlledParagraph.AdjustRightIndentToGrid == true &&
            controlledParagraph.DisableCharacterWrap == true &&
            controlledParagraph.DisableOverflowPunctuation == true &&
            controlledParagraph.AutoSpaceLatin == true &&
            controlledParagraph.AutoSpaceNumbers == true,
            "RTF paragraph controls должны сохранять spacing, pagination и редкие layout flags.");

        var outlineParagraph = paragraphs.First(paragraph =>
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains(
                    "Page break outline",
                    StringComparison.Ordinal)));

        Assert(
            outlineParagraph.PageBreakBefore &&
            outlineParagraph.OutlineLevel == 3,
            "RTF pagebb и outlinelevel должны сохраняться в paragraph model.");

        var tabParagraph = paragraphs.First(paragraph =>
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains(
                    "Tabs",
                    StringComparison.Ordinal)));

        Assert(
            tabParagraph.TabStops.Count == 3 &&
            tabParagraph.TabStops[0].PositionPoints == 72 &&
            tabParagraph.TabStops[0].Alignment == DocumentTabAlignment.Right &&
            tabParagraph.TabStops[0].Leader == DocumentTabLeader.Dots &&
            tabParagraph.TabStops[1].PositionPoints == 144 &&
            tabParagraph.TabStops[1].Alignment == DocumentTabAlignment.Center &&
            tabParagraph.TabStops[1].Leader == DocumentTabLeader.Hyphens &&
            tabParagraph.TabStops[2].PositionPoints == 216 &&
            tabParagraph.TabStops[2].Alignment == DocumentTabAlignment.Decimal &&
            tabParagraph.TabStops[2].Leader == DocumentTabLeader.EqualSign &&
            tabParagraph.Inlines.Any(inline =>
                inline.Text.Contains(
                    "\t",
                    StringComparison.Ordinal)),
            "RTF tx, tq* и tab leader controls должны сохранять tab stops и символы табуляции.");

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

        var tables = model.Blocks
            .OfType<DocumentTable>()
            .ToArray();

        Assert(
            tables.Length == 2,
            "RTF fixture должен содержать обычную и nested outer table.");

        var table = tables[0];

        Assert(
            table.Rows.Count == 9 &&
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
            table.Rows[0].RepeatAsHeader &&
            table.Rows[0].PreventPageSplit &&
            table.Rows[0].KeepWithNext &&
            table.Rows[0].AutoFit == true &&
            table.Rows[0].RowIndex == 0 &&
            table.Rows[0].BandIndex == 0 &&
            table.Rows[0].IsLastRow == null &&
            table.Rows[0].TableIndentValue == 720 &&
            table.Rows[0].TableIndentUnit == 3 &&
            table.Rows[0].LookBorder == true &&
            table.Rows[0].LookShading == true &&
            table.Rows[0].LookFont == true &&
            table.Rows[0].LookColor == true &&
            table.Rows[0].LookBestFit == true &&
            table.Rows[0].LookHeaderRows == true &&
            table.Rows[0].LookLastRow == true &&
            table.Rows[0].LookHeaderColumns == true &&
            table.Rows[0].LookLastColumn == true &&
            table.Rows[0].LookNoRowBand == true &&
            table.Rows[0].LookNoColumnBand == true &&
            table.Rows[1].RowIndex == 1 &&
            table.Rows[1].BandIndex == 1 &&
            table.Rows[1].IsLastRow == true &&
            table.Rows[0].HeightPoints == 24 &&
            table.Rows[0].HeightExact,
            "RTF row identity, table-look metadata, trautofit и row sizing должны сохраняться точно.");

        Assert(
            table.Rows[0].Alignment == DocumentTableAlignment.Center &&
            table.Rows[0].LeftOffsetPoints == 36 &&
            table.Rows[0].CellGapPoints == 3 &&
            table.Rows[1].Alignment == DocumentTableAlignment.Right &&
            table.Rows[2].Alignment == DocumentTableAlignment.Left,
            "RTF trql/trqc/trqr, trleft и trgaph должны сохраняться на уровне строки.");

        Assert(
            table.Rows[0].RightToLeft == true &&
            table.Rows[1].RightToLeft == false &&
            table.Rows[0].PaddingLeftPoints == 5 &&
            table.Rows[0].PaddingRightPoints == 6 &&
            table.Rows[0].PaddingTopPoints == 4 &&
            table.Rows[0].PaddingBottomPoints == 3 &&
            table.Rows[0].CellSpacingLeftPoints == 2 &&
            table.Rows[0].CellSpacingRightPoints == 3 &&
            table.Rows[0].CellSpacingTopPoints == 1 &&
            table.Rows[0].CellSpacingBottomPoints == 1.5 &&
            table.Rows[0].Shading is
            {
                Pattern: 0x0018,
                FillPercentage: 50,
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFF00"
            },
            "RTF rtlrow/ltrrow, trpadd*, trspd* и row shading должны сохраняться в row model.");

        Assert(
            table.Rows[0].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 100
            } &&
            table.Rows[1].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 72
            } &&
            table.Rows[2].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Auto,
                Value: null
            } &&
            table.Rows[3].PreferredWidth == null,
            "RTF trftsWidth/trwWidth должны сохранять preferred row width с точными units.");

        var borderedRow = table.Rows[0];

        Assert(
            borderedRow.BorderTop is
            {
                Type: 0x07,
                WidthPoints: 3,
                Color: "#FFFF00"
            } &&
            borderedRow.BorderLeft is
            {
                Type: 0x01,
                WidthPoints: 1,
                Color: "#FFFF00"
            } &&
            borderedRow.BorderBottom is
            {
                Type: 0x03,
                WidthPoints: 2,
                Color: "#FFFF00"
            } &&
            borderedRow.BorderRight is
            {
                Type: 0x06,
                WidthPoints: 1.5,
                Color: "#FFFF00"
            } &&
            borderedRow.BorderHorizontalInside is
            {
                Type: 0x01,
                WidthPoints: 1,
                Color: "#FFFF00"
            } &&
            borderedRow.BorderVerticalInside is
            {
                Type: 0x06,
                WidthPoints: 1,
                Color: "#FFFF00"
            },
            "RTF trbrdr* controls должны сохранять outer и inside borders строки.");

        Assert(
            table.Rows[0].Cells[0].WidthPoints == 50 &&
            table.Rows[0].Cells[0].PaddingLeftPoints == 5 &&
            table.Rows[0].Cells[0].PaddingRightPoints == 6 &&
            table.Rows[0].Cells[0].PaddingTopPoints == 4 &&
            table.Rows[0].Cells[0].PaddingBottomPoints == 3 &&
            table.Rows[0].Cells[0].VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Center &&
            table.Rows[0].Cells[0].NoWrap &&
            table.Rows[0].Cells[0].HiddenEndOfCellMark &&
            table.Rows[0].Cells[1].WidthPoints == 70 &&
            table.Rows[0].Cells[1].VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Bottom &&
            table.Rows[0].Cells[1].FitText,
            "RTF cellx, clpad*, clvertal*, clNoWrap и clFitText должны сохранять свойства ячеек.");

        var borderedCell = table.Rows[0].Cells[0];

        Assert(
            borderedCell.BorderTop is
            {
                Type: 0x01,
                WidthPoints: 1,
                Color: "#FF0000"
            } &&
            borderedCell.BorderLeft is
            {
                Type: 0x06,
                WidthPoints: 1.5,
                Color: "#FF0000"
            } &&
            borderedCell.BorderBottom is
            {
                Type: 0x03,
                WidthPoints: 2,
                Color: "#FF0000"
            } &&
            borderedCell.BorderRight is
            {
                Type: 0x07,
                WidthPoints: 1,
                Color: "#FF0000"
            },
            "RTF clbrdr* с brdrs/brdrdot/brdrdb/brdrdash должны сохранять cell borders.");

        Assert(
            borderedCell.Shading is
            {
                Pattern: 0x0001,
                FillPercentage: 100,
                ForegroundColor: "#FFFF00",
                BackgroundColor: "#FF0000"
            },
            "RTF solid cell shading должна сохранять foreground/background и fill percentage.");

        Assert(
            table.Rows[1].Cells.Count == 1 &&
            table.Rows[1].Cells[0].ColumnSpan == 2 &&
            table.Rows[1].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "Merged RTF cell",
                    StringComparison.Ordinal)),
            "RTF clmgf/clmrg должны сохраняться как ColumnSpan.");

        Assert(
            table.Rows[1].Cells[0].Shading is
            {
                Pattern: 0,
                FillPercentage: 50,
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFF00"
            },
            "RTF patterned cell shading должна сохранять точный процент и цвета без ложного solid mapping.");

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
            table.Rows[4].Cells.Count == 4 &&
            table.Rows[4].Cells[0].PreferredWidth == null &&
            table.Rows[4].Cells[1].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Auto,
                Value: null
            } &&
            table.Rows[4].Cells[2].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 25
            } &&
            table.Rows[4].Cells[3].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 30
            },
            "RTF clftsWidth/clwWidth должны сохранять null/auto/percent/twips preferred cell widths.");

        Assert(
            table.Rows[0].Cells[0].TextFlow ==
                DocumentTableCellTextFlow.LeftToRightTopToBottom &&
            table.Rows[4].Cells[0].TextFlow ==
                DocumentTableCellTextFlow.TopToBottomRightToLeft &&
            table.Rows[4].Cells[1].TextFlow ==
                DocumentTableCellTextFlow.BottomToTopLeftToRight &&
            table.Rows[4].Cells[2].TextFlow ==
                DocumentTableCellTextFlow.LeftToRightTopToBottomVertical &&
            table.Rows[4].Cells[3].TextFlow ==
                DocumentTableCellTextFlow.TopToBottomRightToLeftVertical,
            "RTF cltx* controls должны сохранять все пять документированных cell text-flow режимов.");

        Assert(
            table.Rows[5].Cells.Select(cell => cell.Shading?.Pattern)
                .SequenceEqual(new ushort?[] { 0x000E, 0x000F, 0x0010, 0x0011, 0x0012, 0x0013 }) &&
            table.Rows[6].Cells.Take(6).Select(cell => cell.Shading?.Pattern)
                .SequenceEqual(new ushort?[] { 0x0014, 0x0015, 0x0016, 0x0017, 0x0018, 0x0019 }) &&
            table.Rows[6].Cells[6].Shading == null,
            "RTF cell patterned shading должен сохранять все 12 stripe/cross Ipat patterns и clshdrawnil reset.");

        Assert(
            table.Rows[5].Cells[4].Shading is
            {
                Pattern: 0x0012,
                FillPercentage: 50
            },
            "RTF explicit hatch pattern должен сохранять clshdng metadata без потери pattern.");

        Assert(
            table.Rows[7].Cells[0].BorderDiagonalDown is
            {
                Type: 0x01,
                WidthPoints: 1,
                Color: "#FFFF00",
                SpacingPoints: 0.5,
                Shadow: true,
                Frame: true
            } &&
            table.Rows[7].Cells[1].BorderDiagonalUp is
            {
                Type: 0x03,
                WidthPoints: 2,
                Color: "#FFFF00"
            },
            "RTF cldglu/cldgll должны сохранять диагональные cell borders со всеми border properties.");

        Assert(
            table.Rows[7].Cells.Count == 4 &&
            table.Rows[7].Cells[0].BorderTop?.Type == 0x05 &&
            table.Rows[7].Cells[1].BorderTop?.Type == 0x08 &&
            table.Rows[7].Cells[2].BorderTop?.Type == 0x09 &&
            table.Rows[7].Cells[3].BorderTop?.Type == 0x0A,
            "RTF brdrhair, brdrdashd, brdrdashdd и brdrtriple должны сохранять точные border type codes.");

        Assert(
            table.Rows[8].Cells.Count == 18 &&
            table.Rows[8].Cells
                .Select(cell => cell.BorderTop?.Type)
                .SequenceEqual(new byte?[]
                {
                    0x02,
                    0x0B,
                    0x0C,
                    0x0D,
                    0x0E,
                    0x0F,
                    0x10,
                    0x11,
                    0x12,
                    0x13,
                    0x14,
                    0x15,
                    0x16,
                    0x17,
                    0x18,
                    0x19,
                    0x1A,
                    0x1B
                }),
            "RTF decorative border controls должны сохранять полный набор Word border type codes 0x02/0x0B..0x1B.");

        var pngImage = model.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Select(inline => inline.Image)
            .FirstOrDefault(image =>
                image != null &&
                string.Equals(
                    image.ContentType,
                    "image/png",
                    StringComparison.Ordinal));

        Assert(
            pngImage is
            {
                PixelWidth: 1,
                PixelHeight: 1,
                WidthPoints: 36,
                HeightPoints: 72,
                ScaleXPercent: 50,
                ScaleYPercent: 200,
                CropLeftPoints: 6,
                CropTopPoints: -2,
                CropRightPoints: 3,
                CropBottomPoints: 1,
                SourceTag: -123,
                UniqueId: "00112233445566778899AABBCCDDEEFF"
            },
            "RTF pict должен сохранять geometry, crop offsets, bliptag и blipuid без смешивания UID с image payload.");

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
            bmpImage.Data[1] == (byte)'M' &&
            bmpImage.BitmapBitsPerPixel == 24 &&
            bmpImage.BitmapPlanes == 1 &&
            bmpImage.BitmapBytesPerScanLine == 4,
            "RTF dibitmap должен преобразовываться в полноценный BMP и сохранять bitmap metadata.");

        var emfImage = model.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Select(inline => inline.Image)
            .FirstOrDefault(image =>
                image != null &&
                string.Equals(
                    image.ContentType,
                    "image/emf",
                    StringComparison.Ordinal));

        Assert(
            emfImage != null &&
            emfImage.Data.Length == 44,
            "RTF emfblip должен сохранять валидный EMF payload.");

        var wmfImage = model.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Select(inline => inline.Image)
            .FirstOrDefault(image =>
                image != null &&
                string.Equals(
                    image.ContentType,
                    "image/wmf",
                    StringComparison.Ordinal));

        Assert(
            wmfImage is
            {
                MetafileMappingMode: 8
            } &&
            wmfImage.Data.Length == 18,
            "RTF wmetafile должен сохранять WMF payload и mapping mode.");

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
            "break-before:page;page-break-before:always;",
            "RTF rendered page-break-before");

        AssertContains(
            html,
            "data-automatic-hyphenation=\"true\"",
            "RTF rendered automatic hyphenation metadata");

        AssertContains(
            html,
            "data-snap-to-document-grid=\"false\"",
            "RTF rendered document-grid metadata");

        AssertContains(
            html,
            "data-suppress-line-number=\"true\"",
            "RTF rendered line-number suppression metadata");

        AssertContains(
            html,
            "data-adjust-right-indent-to-grid=\"true\" data-disable-character-wrap=\"true\" data-disable-overflow-punctuation=\"true\" data-auto-space-latin=\"true\" data-auto-space-numbers=\"true\"",
            "RTF rendered rare East-Asian paragraph metadata");

        AssertContains(
            html,
            "data-outline-level=\"3\"",
            "RTF rendered outline level metadata");

        AssertContains(
            html,
            "data-tab-stops=\"72:right:dots;144:center:hyphens;216:decimal:equal-sign\"",
            "RTF rendered tab stop metadata");

        AssertContains(
            html,
            "class=\"flow-tab\"",
            "RTF rendered tab character");

        AssertContains(
            html,
            "<table class=\"flow-table\"",
            "RTF rendered table");

        AssertContains(
            html,
            "data-row-alignment=\"center\"",
            "RTF rendered centered row alignment metadata");

        AssertContains(
            html,
            "data-row-keep-with-next=\"true\"",
            "RTF rendered keep-with-next row metadata");

        AssertContains(
            html,
            "data-row-autofit=\"true\"",
            "RTF rendered row AutoFit metadata");

        AssertContains(
            html,
            "data-row-index=\"0\" data-row-band-index=\"0\" data-table-indent-value=\"720\" data-table-indent-unit=\"3\" data-table-look-border=\"true\" data-table-look-shading=\"true\" data-table-look-font=\"true\" data-table-look-color=\"true\" data-table-look-best-fit=\"true\" data-table-look-header-rows=\"true\" data-table-look-last-row=\"true\" data-table-look-header-columns=\"true\" data-table-look-last-column=\"true\" data-table-look-no-row-band=\"true\" data-table-look-no-column-band=\"true\"",
            "RTF rendered row index, banding and table-look metadata");

        AssertContains(
            html,
            "data-row-index=\"1\" data-row-band-index=\"1\" data-row-last=\"true\"",
            "RTF rendered last-row metadata");

        AssertContains(
            html,
            "data-row-left-offset-pt=\"36\"",
            "RTF rendered row left offset metadata");

        AssertContains(
            html,
            "data-row-cell-gap-pt=\"3\"",
            "RTF rendered row cell gap metadata");

        AssertContains(
            html,
            "data-row-alignment=\"right\"",
            "RTF rendered right row alignment metadata");

        AssertContains(
            html,
            "data-preferred-width-kind=\"percent\" data-preferred-width-value=\"100\"",
            "RTF rendered preferred row width percent metadata");

        AssertContains(
            html,
            "data-preferred-width-kind=\"points\" data-preferred-width-value=\"72\"",
            "RTF rendered preferred row width points metadata");

        AssertContains(
            html,
            "data-preferred-width-kind=\"auto\"",
            "RTF rendered preferred row width auto metadata");

        AssertContains(
            html,
            "data-text-flow=\"lrtb\"",
            "RTF rendered default cell text-flow metadata");

        AssertContains(
            html,
            "data-text-flow=\"tbrl\"",
            "RTF rendered right-to-left top-to-bottom cell text-flow metadata");

        AssertContains(
            html,
            "data-text-flow=\"btlr\"",
            "RTF rendered bottom-to-top cell text-flow metadata");

        AssertContains(
            html,
            "data-text-flow=\"lrtbv\"",
            "RTF rendered vertical left-to-right cell text-flow metadata");

        AssertContains(
            html,
            "data-text-flow=\"tbrlv\"",
            "RTF rendered vertical top-to-bottom right-to-left cell text-flow metadata");

        AssertContains(
            html,
            "data-shading-pattern=\"18\"",
            "RTF rendered dark cross pattern metadata");

        AssertContains(
            html,
            "background-image:repeating-linear-gradient(0deg,#FF0000 0 2px,transparent 2px 4px),repeating-linear-gradient(90deg,#FF0000 0 2px,transparent 2px 4px)",
            "RTF rendered explicit hatch pattern должен иметь приоритет над clshdng percentage.");

        AssertContains(
            html,
            "data-border-diagonal-down-type=\"1\" data-border-diagonal-down-width-pt=\"1\" data-border-diagonal-down-color=\"#FFFF00\" data-border-diagonal-down-spacing-pt=\"0.5\" data-border-diagonal-down-shadow=\"true\" data-border-diagonal-down-frame=\"true\"",
            "RTF rendered cldglu border metadata");

        AssertContains(
            html,
            "data-border-diagonal-up-type=\"3\" data-border-diagonal-up-width-pt=\"2\" data-border-diagonal-up-color=\"#FFFF00\"",
            "RTF rendered cldgll border metadata");

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
            "border-top:1pt solid #FF0000",
            "RTF rendered solid cell border");

        AssertContains(
            html,
            "border-left:1.5pt dotted #FF0000",
            "RTF rendered dotted cell border");

        AssertContains(
            html,
            "border-top:3pt dashed #FFFF00",
            "RTF rendered row outer border fallback");

        AssertContains(
            html,
            "border-left:1pt dotted #FFFF00",
            "RTF rendered row inside border fallback");

        AssertContains(
            html,
            "background-color:#FFFF00",
            "RTF rendered exact solid cell shading");

        AssertContains(
            html,
            "border-top:0.5pt solid #FF0000",
            "RTF rendered hairline border fallback");

        AssertContains(
            html,
            "border-top:1pt dashed #FF0000",
            "RTF rendered dot-dash border fallback");

        AssertContains(
            html,
            "border-top:1.5pt double #FF0000",
            "RTF rendered triple border fallback");

        AssertContains(
            html,
            "data-shading-fill-percentage=\"50\"",
            "RTF rendered patterned shading metadata");

        AssertContains(
            html,
            "data-preferred-width-kind=\"percent\"",
            "RTF rendered preferred cell width percent metadata");

        AssertContains(
            html,
            "data-preferred-width-value=\"25\"",
            "RTF rendered preferred cell width percent value");

        AssertContains(
            html,
            "class=\"inline-image\"",
            "RTF rendered image");

        AssertContains(
            html,
            "data-pixel-width=\"1\" data-pixel-height=\"1\" data-scale-x-percent=\"50\" data-scale-y-percent=\"200\" data-crop-left-pt=\"6\" data-crop-top-pt=\"-2\" data-crop-right-pt=\"3\" data-crop-bottom-pt=\"1\"",
            "RTF rendered picture geometry and positive crop clipping");

        AssertContains(
            html,
            "style=\"width:36pt;height:72pt;clip-path:inset(0pt 3pt 1pt 6pt);\"",
            "RTF rendered positive crop clipping");

        AssertContains(
            html,
            "data-image-source-tag=\"-123\" data-image-uid=\"00112233445566778899AABBCCDDEEFF\"",
            "RTF rendered bliptag/blipuid metadata");

        AssertContains(
            html,
            "data-metafile-mapping-mode=\"8\"",
            "RTF rendered WMF mapping-mode metadata");

        AssertContains(
            html,
            "data-bitmap-bits-per-pixel=\"24\" data-bitmap-planes=\"1\" data-bitmap-bytes-per-scan-line=\"4\"",
            "RTF rendered bitmap metadata");

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

        var dpiPath = Path.Combine(
            directory,
            "regression-rtf-dpi-scale.rtf");
        var dpiPng = EncodePngFixture(
            [0, 0, 0, 255],
            width: 1,
            height: 1,
            PixelFormats.Bgra32,
            stride: 4,
            dpiX: 144,
            dpiY: 72);
        var dpiRtf =
            "{\\rtf1\\ansi " +
            "{\\pict\\pngblip\\picw1\\pich1\\picscalex50\\picscaley200 " +
            Convert.ToHexString(dpiPng) +
            "}\\par}";

        File.WriteAllText(
            dpiPath,
            dpiRtf,
            Encoding.ASCII);

        var dpiModel =
            SupportedFormatRegistry.ReadDocument(
                dpiPath);
        var dpiImage = dpiModel.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Select(inline => inline.Image)
            .Single(image => image != null)!;

        Assert(
            dpiImage.WidthPoints is { } widthPoints &&
            dpiImage.HeightPoints is { } heightPoints &&
            Math.Abs(widthPoints - 0.25) <= 0.01 &&
            Math.Abs(heightPoints - 2.0) <= 0.02,
            "RTF scale без goal-size должен использовать только явно сохранённую raster DPI metadata.");

        var pngDpi =
            RtfRasterMetricsReader.TryReadDpi(
                dpiPng,
                "image/png");

        Assert(
            pngDpi is { } &&
            Math.Abs(pngDpi.Value.DpiX - 144) <= 0.05 &&
            Math.Abs(pngDpi.Value.DpiY - 72) <= 0.05,
            "RTF PNG pHYs density должна читаться без default DPI.");

        var densityJpeg = EncodeJpegFixture(
            [64, 128, 192],
            width: 1,
            height: 1,
            PixelFormats.Rgb24,
            stride: 3,
            dpiX: 150,
            dpiY: 75);
        var jpegDpi =
            RtfRasterMetricsReader.TryReadDpi(
                densityJpeg,
                "image/jpeg");

        Assert(
            jpegDpi is { } &&
            Math.Abs(jpegDpi.Value.DpiX - 150) <= 0.5 &&
            Math.Abs(jpegDpi.Value.DpiY - 75) <= 0.5,
            "RTF JPEG JFIF/EXIF density должна читаться из container metadata.");

        var bmpHeader = new byte[54];
        bmpHeader[0] = (byte)'B';
        bmpHeader[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(
            bmpHeader.AsSpan(
                14,
                4),
            40);
        BinaryPrimitives.WriteInt32LittleEndian(
            bmpHeader.AsSpan(
                38,
                4),
            5906);
        BinaryPrimitives.WriteInt32LittleEndian(
            bmpHeader.AsSpan(
                42,
                4),
            2953);

        var bmpDpi =
            RtfRasterMetricsReader.TryReadDpi(
                bmpHeader,
                "image/bmp");

        Assert(
            bmpDpi is { } &&
            Math.Abs(bmpDpi.Value.DpiX - 150) <= 0.1 &&
            Math.Abs(bmpDpi.Value.DpiY - 75) <= 0.1,
            "RTF BMP pixels-per-meter density должна преобразовываться в DPI.");

        var malformedUidPath = Path.Combine(
            directory,
            "regression-rtf-malformed-blipuid.rtf");
        File.WriteAllText(
            malformedUidPath,
            "{\\rtf1\\ansi {\\pict\\pngblip{\\*\\blipuid 0011}" +
            "89504E470D0A1A0A0000000D4948445200000001000000010804000000B51C0C020000000B4944415478DA63FCFF1F0002EB01F56950DA2A0000000049454E44AE426082}\\par}",
            Encoding.ASCII);

        var malformedUidRejected = false;

        try
        {
            SupportedFormatRegistry.ReadDocument(
                malformedUidPath);
        }
        catch (InvalidDataException error)
            when (error.Message.Contains(
                "blipuid",
                StringComparison.Ordinal))
        {
            malformedUidRejected = true;
        }

        Assert(
            malformedUidRejected,
            "RTF malformed blipuid должен завершаться контролируемой structural error.");

        foreach (var unsupportedPictureType in new[]
        {
            "wbitmap0",
            "macpict",
            "pmmmetafile1"
        })
        {
            var unsupportedPath = Path.Combine(
                directory,
                $"regression-rtf-unsupported-{unsupportedPictureType}.rtf");
            File.WriteAllText(
                unsupportedPath,
                $"{{\\rtf1\\ansi {{\\pict\\{unsupportedPictureType} 00}}\\par}}",
                Encoding.ASCII);

            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    unsupportedPath);
            }
            catch (NotSupportedException error)
                when (error.Message.Contains(
                    "RTF",
                    StringComparison.Ordinal))
            {
                rejected = true;
            }

            Assert(
                rejected,
                $"RTF {unsupportedPictureType} должен завершаться явной unsupported-ошибкой вместо тихого пропуска.");
        }
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
            " / " +
            '\u0013' + " PAGE " + '\u0014' + "12" + '\u0015' +
            " / " +
            '\u0013' + " NUMPAGES " + '\u0014' + "30" + '\u0015' +
            " / " +
            '\u0013' + " REF bookmark \\h " + '\u0014' + "Section title" + '\u0015' +
            " / " +
            '\u0013' + " PAGEREF bookmark " + '\u0014' + "7" + '\u0015' +
            " / " +
            '\u0013' + " NOTEREF noteMark " + '\u0014' + "3" + '\u0015' +
            " / " +
            '\u0013' + " DATE \\@ \"yyyy-MM-dd\" " + '\u0014' + "2026-10-23" + '\u0015' +
            " / " +
            '\u0013' + " TIME \\@ \"HH:mm\" " + '\u0014' + "12:21" + '\u0015' +
            " / " +
            '\u0013' + " MACROBUTTON TestMacro Run " + '\u0014' + "Run" + '\u0015' +
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
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Page &&
                inline.Text == "12" &&
                inline.FieldInstruction?.Contains("PAGE", StringComparison.OrdinalIgnoreCase) == true),
            "DOC PAGE field должен сохранять saved result и field metadata.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.NumPages &&
                inline.Text == "30"),
            "DOC NUMPAGES field должен сохранять saved result.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Ref &&
                inline.FieldTarget == "bookmark" &&
                inline.Text == "Section title"),
            "DOC REF field должен сохранять bookmark target.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.PageRef &&
                inline.FieldTarget == "bookmark" &&
                inline.Text == "7"),
            "DOC PAGEREF field должен сохранять bookmark target.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.NoteRef &&
                inline.FieldTarget == "noteMark" &&
                inline.Text == "3"),
            "DOC NOTEREF field должен сохранять note target.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Date &&
                inline.Text == "2026-10-23") &&
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Time &&
                inline.Text == "12:21"),
            "DOC DATE/TIME fields должны сохранять cached result без пересчёта.");
        Assert(
            inlines.Any(inline =>
                inline.FieldKind == null &&
                inline.Text.Contains("Run", StringComparison.Ordinal)),
            "DOC executable/unknown fields должны оставаться plain saved result без semantic metadata.");

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
        AssertContains(
            html,
            "data-field-kind=\"page\" data-field-instruction=\"PAGE\"",
            "DOC renderer PAGE field metadata");
        AssertContains(
            html,
            "data-field-kind=\"ref\" data-field-instruction=\"REF bookmark \\h\" data-field-target=\"bookmark\"",
            "DOC renderer REF field metadata");
        AssertContains(
            html,
            "data-field-kind=\"pageref\"",
            "DOC renderer PAGEREF field metadata");
        AssertContains(
            html,
            "data-field-kind=\"noteref\"",
            "DOC renderer NOTEREF field metadata");
        AssertContains(
            html,
            "data-field-kind=\"date\"",
            "DOC renderer DATE field metadata");
        AssertContains(
            html,
            "data-field-kind=\"time\"",
            "DOC renderer TIME field metadata");
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

        var invalidSimpleRca = plc.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            invalidSimpleRca.AsSpan(
                spaOffset + 20,
                2),
            checked((ushort)(flags | 0x2000)));
        try
        {
            _ = DocDocumentReader.ReadMainDocumentShapeAnchors(
                invalidSimpleRca,
                0,
                invalidSimpleRca.Length,
                ccpText);
            throw new InvalidOperationException(
                "DOC FSPA fRcaSimple=1 должен быть отклонён.");
        }
        catch (InvalidDataException)
        {
        }

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

        const int headerShapeId = 2049;
        var headerFsp = fsp.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            headerFsp.AsSpan(8, 4),
            headerShapeId);
        var headerSpContainer = new byte[
            8 +
            headerFsp.Length +
            fopt.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            headerSpContainer.AsSpan(0, 2),
            0x000F);
        BinaryPrimitives.WriteUInt16LittleEndian(
            headerSpContainer.AsSpan(2, 2),
            0xF004);
        BinaryPrimitives.WriteUInt32LittleEndian(
            headerSpContainer.AsSpan(4, 4),
            checked((uint)(headerFsp.Length + fopt.Length)));
        headerFsp.CopyTo(headerSpContainer, 8);
        fopt.CopyTo(headerSpContainer, 8 + headerFsp.Length);

        static byte[] WrapOfficeArtContainer(
            ushort recordType,
            byte[] payload)
        {
            var record = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                record.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                record.AsSpan(2, 2),
                recordType);
            BinaryPrimitives.WriteUInt32LittleEndian(
                record.AsSpan(4, 4),
                checked((uint)payload.Length));
            payload.CopyTo(record, 8);
            return record;
        }

        var drawingGroup = WrapOfficeArtContainer(
            0xF000,
            bstore);
        var mainDrawing = WrapOfficeArtContainer(
            0xF002,
            spContainer);
        var headerDrawing = WrapOfficeArtContainer(
            0xF002,
            headerSpContainer);
        var officeArtContent = new byte[
            drawingGroup.Length +
            1 + mainDrawing.Length +
            1 + headerDrawing.Length];
        var officeArtOffset = 0;
        drawingGroup.CopyTo(officeArtContent, officeArtOffset);
        officeArtOffset += drawingGroup.Length;
        officeArtContent[officeArtOffset++] = 0x00;
        mainDrawing.CopyTo(officeArtContent, officeArtOffset);
        officeArtOffset += mainDrawing.Length;
        officeArtContent[officeArtOffset++] = 0x01;
        headerDrawing.CopyTo(officeArtContent, officeArtOffset);

        var mainShapeIds = DocDocumentReader.ReadOfficeArtShapeIds(
            officeArtContent,
            0,
            officeArtContent.Length,
            drawingLabel: 0x00);
        var headerShapeIds = DocDocumentReader.ReadOfficeArtShapeIds(
            officeArtContent,
            0,
            officeArtContent.Length,
            drawingLabel: 0x01);
        Assert(
            mainShapeIds.SetEquals([shapeId]) &&
            headerShapeIds.SetEquals([headerShapeId]),
            "DOC OfficeArtContent должен разделять Main/Header drawings по dgglbl.");

        var mainDrawingImages = DocDocumentReader.ReadOfficeArtShapeImages(
            officeArtContent,
            0,
            officeArtContent.Length,
            drawingLabel: 0x00);
        var headerDrawingImages = DocDocumentReader.ReadOfficeArtShapeImages(
            officeArtContent,
            0,
            officeArtContent.Length,
            drawingLabel: 0x01);
        Assert(
            mainDrawingImages.TryGetValue(shapeId, out var mainDrawingImage) &&
            !mainDrawingImages.ContainsKey(headerShapeId) &&
            headerDrawingImages.TryGetValue(headerShapeId, out var headerDrawingImage) &&
            !headerDrawingImages.ContainsKey(shapeId) &&
            mainDrawingImage.Data.SequenceEqual(png) &&
            headerDrawingImage.Data.SequenceEqual(png),
            "DOC Main/Header drawings должны использовать общий Dgg/BStore, но собственные FSP/FOPT containers.");

        var blocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    shapeId,
                    720,
                    1440,
                    2160,
                    2880,
                    1,
                    2,
                    2,
                    2,
                    false,
                    true)
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
            "class=\"floating-image\"",
            "DOC floating shape rendered image");
        AssertContains(
            html,
            "data-floating-left-pt=\"36\" data-floating-top-pt=\"72\" data-floating-right-pt=\"108\" data-floating-bottom-pt=\"144\"",
            "DOC floating shape должен сохранять FSPA rectangle metadata.");
        AssertContains(
            html,
            "data-floating-horizontal-origin=\"page\" data-floating-vertical-origin=\"paragraph\" data-floating-wrap-style=\"square\" data-floating-wrap-side=\"right\" data-floating-below-text=\"false\" data-floating-anchor-locked=\"true\"",
            "DOC floating shape должен сохранять exact FSPA origin/wrap flags.");
        AssertContains(
            html,
            "style=\"width:72pt;height:72pt;float:left;\"",
            "DOC square wrap/right side должен использовать точный FSPA size и reflow float-left.");

        static byte[] BuildOfficeArtBlipRecord(
            ushort recordType,
            int recordInstance,
            byte[] payload)
        {
            var record = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                record.AsSpan(0, 2),
                checked((ushort)(recordInstance << 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                record.AsSpan(2, 2),
                recordType);
            BinaryPrimitives.WriteUInt32LittleEndian(
                record.AsSpan(4, 4),
                checked((uint)payload.Length));
            payload.CopyTo(record, 8);
            return record;
        }

        static byte[] BuildOfficeArtDgg(
            byte[] blipRecord,
            int blipType,
            int testShapeId)
        {
            var testFbsePayload = new byte[36 + blipRecord.Length];
            blipRecord.CopyTo(testFbsePayload, 36);
            var testFbseRecord = new byte[8 + testFbsePayload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                testFbseRecord.AsSpan(0, 2),
                checked((ushort)((blipType << 4) | 0x0002)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                testFbseRecord.AsSpan(2, 2),
                0xF007);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFbseRecord.AsSpan(4, 4),
                checked((uint)testFbsePayload.Length));
            testFbsePayload.CopyTo(testFbseRecord, 8);

            var testBstore = new byte[8 + testFbseRecord.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testBstore.AsSpan(0, 2), 0x001F);
            BinaryPrimitives.WriteUInt16LittleEndian(testBstore.AsSpan(2, 2), 0xF001);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testBstore.AsSpan(4, 4),
                checked((uint)testFbseRecord.Length));
            testFbseRecord.CopyTo(testBstore, 8);

            var testFsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(0, 2), 0x0012);
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(2, 2), 0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(testFsp.AsSpan(4, 4), 8);
            BinaryPrimitives.WriteInt32LittleEndian(testFsp.AsSpan(8, 4), testShapeId);

            var testFopt = new byte[14];
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(0, 2), 0x0013);
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(2, 2), 0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(testFopt.AsSpan(4, 4), 6);
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(8, 2), 0x4104);
            BinaryPrimitives.WriteUInt32LittleEndian(testFopt.AsSpan(10, 4), 1);

            var testSpContainer = new byte[8 + testFsp.Length + testFopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(0, 2), 0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(2, 2), 0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testSpContainer.AsSpan(4, 4),
                checked((uint)(testFsp.Length + testFopt.Length)));
            testFsp.CopyTo(testSpContainer, 8);
            testFopt.CopyTo(testSpContainer, 8 + testFsp.Length);

            var testDgg = new byte[testBstore.Length + testSpContainer.Length];
            testBstore.CopyTo(testDgg, 0);
            testSpContainer.CopyTo(testDgg, testBstore.Length);
            return testDgg;
        }

        static DocumentImage ReadSingleShapeImage(
            byte[] blipRecord,
            int blipType,
            int testShapeId)
        {
            var testDgg = BuildOfficeArtDgg(
                blipRecord,
                blipType,
                testShapeId);
            var testImages = DocDocumentReader.ReadOfficeArtShapeImages(
                testDgg,
                0,
                testDgg.Length);
            Assert(
                testImages.TryGetValue(testShapeId, out var testImage),
                $"DOC OfficeArt BLIP type {blipType} должен разрешаться через BStore/FOPT.pib.");
            return testImage!;
        }

        var dib = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14, 2), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20, 4), 4);
        dib[40] = 0x33;
        dib[41] = 0x22;
        dib[42] = 0x11;

        var dibPayload = new byte[17 + dib.Length];
        dibPayload[16] = 0xFF;
        dib.CopyTo(dibPayload, 17);
        var dibImage = ReadSingleShapeImage(
            BuildOfficeArtBlipRecord(0xF01F, 0x7A8, dibPayload),
            0x07,
            2049);
        Assert(
            dibImage.ContentType == "image/bmp" &&
            dibImage.Data.Length == 58 &&
            dibImage.Data[0] == (byte)'B' &&
            dibImage.Data[1] == (byte)'M' &&
            BinaryPrimitives.ReadUInt32LittleEndian(dibImage.Data.AsSpan(10, 4)) == 54,
            "DOC OfficeArt DIB должен преобразовываться в полноценный BMP stream.");

        var tiff = new byte[]
        {
            0x49, 0x49, 0x2A, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x00, 0x00
        };
        var tiffPayload = new byte[17 + tiff.Length];
        tiffPayload[16] = 0xFF;
        tiff.CopyTo(tiffPayload, 17);
        var tiffImage = ReadSingleShapeImage(
            BuildOfficeArtBlipRecord(0xF029, 0x6E4, tiffPayload),
            0x11,
            2050);
        Assert(
            tiffImage.ContentType == "image/tiff" &&
            tiffImage.Data.SequenceEqual(tiff),
            "DOC OfficeArt TIFF должен сохранять исходные TIFF bytes.");

        static byte[] BuildMetafilePayload(
            byte[] uncompressed,
            bool compress)
        {
            byte[] saved;
            if (compress)
            {
                using var compressed = new MemoryStream();
                using (var zlib = new ZLibStream(
                           compressed,
                           CompressionLevel.Optimal,
                           leaveOpen: true))
                {
                    zlib.Write(uncompressed);
                }

                saved = compressed.ToArray();
            }
            else
            {
                saved = uncompressed;
            }

            var payload = new byte[16 + 34 + saved.Length];
            var header = payload.AsSpan(16, 34);
            BinaryPrimitives.WriteUInt32LittleEndian(
                header[..4],
                checked((uint)uncompressed.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(
                header.Slice(28, 4),
                checked((uint)saved.Length));
            header[32] = compress ? (byte)0x00 : (byte)0xFE;
            header[33] = 0xFE;
            saved.CopyTo(payload, 50);
            return payload;
        }

        var emf = new byte[]
        {
            0x01, 0x00, 0x00, 0x00,
            0x08, 0x00, 0x00, 0x00
        };
        var emfImage = ReadSingleShapeImage(
            BuildOfficeArtBlipRecord(
                0xF01A,
                0x3D4,
                BuildMetafilePayload(emf, compress: true)),
            0x02,
            2051);
        Assert(
            emfImage.ContentType == "image/emf" &&
            emfImage.Data.SequenceEqual(emf),
            "DOC OfficeArt EMF должен распаковывать RFC1950 BLIPFileData.");

        var wmf = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(wmf.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wmf.AsSpan(2, 2), 9);
        var wmfImage = ReadSingleShapeImage(
            BuildOfficeArtBlipRecord(
                0xF01B,
                0x216,
                BuildMetafilePayload(wmf, compress: false)),
            0x03,
            2052);
        Assert(
            wmfImage.ContentType == "image/wmf" &&
            wmfImage.Data.SequenceEqual(wmf),
            "DOC OfficeArt WMF должен сохранять uncompressed metafile bytes.");

        var fillDgg = BuildOfficeArtDgg(
            blipRecord,
            0x06,
            2053);
        BinaryPrimitives.WriteUInt16LittleEndian(
            fillDgg.AsSpan(
                fillDgg.Length - 6,
                2),
            0x4186);
        var fillImages = DocDocumentReader.ReadOfficeArtShapeImages(
            fillDgg,
            0,
            fillDgg.Length);
        Assert(
            fillImages.TryGetValue(2053, out var fillImage) &&
            fillImage.Data.SequenceEqual(png),
            "DOC simple FOPT.fillBlip должен разрешаться через BStore.");

        static byte[] BuildComplexImageDgg(
            byte[] imageBlipRecord,
            ushort propertyId,
            int testShapeId)
        {
            var testFsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(0, 2), 0x0012);
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(2, 2), 0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(testFsp.AsSpan(4, 4), 8);
            BinaryPrimitives.WriteInt32LittleEndian(testFsp.AsSpan(8, 4), testShapeId);

            var testFopt = new byte[
                14 +
                imageBlipRecord.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(0, 2), 0x0013);
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(2, 2), 0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFopt.AsSpan(4, 4),
                checked((uint)(6 + imageBlipRecord.Length)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                testFopt.AsSpan(8, 2),
                checked((ushort)(0x8000 | propertyId)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFopt.AsSpan(10, 4),
                checked((uint)imageBlipRecord.Length));
            imageBlipRecord.CopyTo(
                testFopt,
                14);

            var testSpContainer = new byte[
                8 +
                testFsp.Length +
                testFopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(0, 2), 0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(2, 2), 0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testSpContainer.AsSpan(4, 4),
                checked((uint)(testFsp.Length + testFopt.Length)));
            testFsp.CopyTo(testSpContainer, 8);
            testFopt.CopyTo(testSpContainer, 8 + testFsp.Length);
            return testSpContainer;
        }

        var complexPibDgg = BuildComplexImageDgg(
            blipRecord,
            0x0104,
            2054);
        var complexPibImages = DocDocumentReader.ReadOfficeArtShapeImages(
            complexPibDgg,
            0,
            complexPibDgg.Length);
        Assert(
            complexPibImages.TryGetValue(2054, out var complexPibImage) &&
            complexPibImage.Data.SequenceEqual(png),
            "DOC complex FOPT.pib должен читать embedded OfficeArtBlip из complex data.");

        var complexFillDgg = BuildComplexImageDgg(
            blipRecord,
            0x0186,
            2055);
        var complexFillImages = DocDocumentReader.ReadOfficeArtShapeImages(
            complexFillDgg,
            0,
            complexFillDgg.Length);
        Assert(
            complexFillImages.TryGetValue(2055, out var complexFillImage) &&
            complexFillImage.Data.SequenceEqual(png),
            "DOC complex FOPT.fillBlip должен читать embedded OfficeArtBlip из complex data.");

        static byte[] BuildDelayedOfficeArtDgg(
            int testShapeId,
            int delayOffset,
            int blipSize)
        {
            var testFbsePayload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFbsePayload.AsSpan(20, 4),
                checked((uint)blipSize));
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFbsePayload.AsSpan(24, 4),
                1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFbsePayload.AsSpan(28, 4),
                checked((uint)delayOffset));

            var testFbseRecord = new byte[
                8 +
                testFbsePayload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testFbseRecord.AsSpan(0, 2), 0x0062);
            BinaryPrimitives.WriteUInt16LittleEndian(testFbseRecord.AsSpan(2, 2), 0xF007);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testFbseRecord.AsSpan(4, 4),
                checked((uint)testFbsePayload.Length));
            testFbsePayload.CopyTo(testFbseRecord, 8);

            var testBstore = new byte[
                8 +
                testFbseRecord.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testBstore.AsSpan(0, 2), 0x001F);
            BinaryPrimitives.WriteUInt16LittleEndian(testBstore.AsSpan(2, 2), 0xF001);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testBstore.AsSpan(4, 4),
                checked((uint)testFbseRecord.Length));
            testFbseRecord.CopyTo(testBstore, 8);

            var testFsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(0, 2), 0x0012);
            BinaryPrimitives.WriteUInt16LittleEndian(testFsp.AsSpan(2, 2), 0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(testFsp.AsSpan(4, 4), 8);
            BinaryPrimitives.WriteInt32LittleEndian(testFsp.AsSpan(8, 4), testShapeId);

            var testFopt = new byte[14];
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(0, 2), 0x0013);
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(2, 2), 0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(testFopt.AsSpan(4, 4), 6);
            BinaryPrimitives.WriteUInt16LittleEndian(testFopt.AsSpan(8, 2), 0x4104);
            BinaryPrimitives.WriteUInt32LittleEndian(testFopt.AsSpan(10, 4), 1);

            var testSpContainer = new byte[
                8 +
                testFsp.Length +
                testFopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(0, 2), 0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(testSpContainer.AsSpan(2, 2), 0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                testSpContainer.AsSpan(4, 4),
                checked((uint)(testFsp.Length + testFopt.Length)));
            testFsp.CopyTo(testSpContainer, 8);
            testFopt.CopyTo(testSpContainer, 8 + testFsp.Length);

            var testDgg = new byte[
                testBstore.Length +
                testSpContainer.Length];
            testBstore.CopyTo(testDgg, 0);
            testSpContainer.CopyTo(testDgg, testBstore.Length);
            return testDgg;
        }

        const int delayedOffset = 128;
        var delayedWordDocument = new byte[
            delayedOffset +
            blipRecord.Length];
        blipRecord.CopyTo(
            delayedWordDocument,
            delayedOffset);
        var delayedDgg = BuildDelayedOfficeArtDgg(
            2056,
            delayedOffset,
            blipRecord.Length);
        var delayedImages = DocDocumentReader.ReadOfficeArtShapeImages(
            delayedDgg,
            0,
            delayedDgg.Length,
            wordDocument: delayedWordDocument);
        Assert(
            delayedImages.TryGetValue(2056, out var delayedImage) &&
            delayedImage.Data.SequenceEqual(png),
            "DOC delayed FBSE должен разрешать foDelay в WordDocument stream.");
    }

    private static void TestDocOfficeArtVectorShapes()
    {
        static byte[] BuildShapeRecord(
            int shapeId,
            int shapeType,
            uint shapeFlags,
            params (ushort PropertyId, uint Value)[] properties)
        {
            var fsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(0, 2),
                checked((ushort)((shapeType << 4) | 0x0002)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(2, 2),
                0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(4, 4),
                8);
            BinaryPrimitives.WriteInt32LittleEndian(
                fsp.AsSpan(8, 4),
                shapeId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(12, 4),
                shapeFlags);

            var fopt = new byte[
                8 +
                properties.Length * 6];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(0, 2),
                checked((ushort)((properties.Length << 4) | 0x0003)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(2, 2),
                0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(4, 4),
                checked((uint)(properties.Length * 6)));

            for (var index = 0;
                 index < properties.Length;
                 index++)
            {
                var propertyOffset =
                    8 +
                    index * 6;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    fopt.AsSpan(propertyOffset, 2),
                    properties[index].PropertyId);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    fopt.AsSpan(propertyOffset + 2, 4),
                    properties[index].Value);
            }

            var container = new byte[
                8 +
                fsp.Length +
                fopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(2, 2),
                0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                container.AsSpan(4, 4),
                checked((uint)(fsp.Length + fopt.Length)));
            fsp.CopyTo(
                container,
                8);
            fopt.CopyTo(
                container,
                8 + fsp.Length);
            return container;
        }

        static byte[] BuildMultiStopGradientShapeRecord(
            int shapeId)
        {
            var fsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(0, 2),
                checked((ushort)((1 << 4) | 0x0002)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(2, 2),
                0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(4, 4),
                8);
            BinaryPrimitives.WriteInt32LittleEndian(
                fsp.AsSpan(8, 4),
                shapeId);

            var shadeColors = new byte[30];
            BinaryPrimitives.WriteUInt16LittleEndian(
                shadeColors.AsSpan(0, 2),
                3);
            BinaryPrimitives.WriteUInt16LittleEndian(
                shadeColors.AsSpan(2, 2),
                3);
            BinaryPrimitives.WriteUInt16LittleEndian(
                shadeColors.AsSpan(4, 2),
                8);

            BinaryPrimitives.WriteUInt32LittleEndian(
                shadeColors.AsSpan(6, 4),
                0x000000FF);
            BinaryPrimitives.WriteInt32LittleEndian(
                shadeColors.AsSpan(10, 4),
                0);

            BinaryPrimitives.WriteUInt32LittleEndian(
                shadeColors.AsSpan(14, 4),
                0x0000FF00);
            BinaryPrimitives.WriteInt32LittleEndian(
                shadeColors.AsSpan(18, 4),
                32768);

            BinaryPrimitives.WriteUInt32LittleEndian(
                shadeColors.AsSpan(22, 4),
                0x00FF0000);
            BinaryPrimitives.WriteInt32LittleEndian(
                shadeColors.AsSpan(26, 4),
                65536);

            var fopt = new byte[
                8 +
                3 * 6 +
                shadeColors.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(0, 2),
                checked((ushort)((3 << 4) | 0x0003)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(2, 2),
                0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(4, 4),
                checked((uint)(3 * 6 + shadeColors.Length)));

            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(8, 2),
                0x0180);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(10, 4),
                4);

            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(14, 2),
                0x018B);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(16, 4),
                90u << 16);

            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(20, 2),
                0x8197);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(22, 4),
                checked((uint)shadeColors.Length));

            shadeColors.CopyTo(
                fopt,
                26);

            var container = new byte[
                8 +
                fsp.Length +
                fopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(2, 2),
                0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                container.AsSpan(4, 4),
                checked((uint)(fsp.Length + fopt.Length)));
            fsp.CopyTo(
                container,
                8);
            fopt.CopyTo(
                container,
                8 + fsp.Length);
            return container;
        }

        static byte[] BuildTextBoxShapeRecord(
            int shapeId,
            uint textIdentifier)
        {
            var fsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(0, 2),
                checked((ushort)((202 << 4) | 0x0002)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(2, 2),
                0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(4, 4),
                8);
            BinaryPrimitives.WriteInt32LittleEndian(
                fsp.AsSpan(8, 4),
                shapeId);

            var clientTextbox = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(
                clientTextbox.AsSpan(0, 2),
                0x0000);
            BinaryPrimitives.WriteUInt16LittleEndian(
                clientTextbox.AsSpan(2, 2),
                0xF00D);
            BinaryPrimitives.WriteUInt32LittleEndian(
                clientTextbox.AsSpan(4, 4),
                4);
            BinaryPrimitives.WriteUInt32LittleEndian(
                clientTextbox.AsSpan(8, 4),
                textIdentifier);

            var container = new byte[
                8 +
                fsp.Length +
                clientTextbox.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(2, 2),
                0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                container.AsSpan(4, 4),
                checked((uint)(fsp.Length + clientTextbox.Length)));
            fsp.CopyTo(
                container,
                8);
            clientTextbox.CopyTo(
                container,
                8 + fsp.Length);
            return container;
        }

        static byte[] AddChildAnchor(
            byte[] shapeRecord,
            int left,
            int top,
            int right,
            int bottom)
        {
            var anchor = new byte[24];
            BinaryPrimitives.WriteUInt16LittleEndian(
                anchor.AsSpan(0, 2),
                0x0000);
            BinaryPrimitives.WriteUInt16LittleEndian(
                anchor.AsSpan(2, 2),
                0xF00F);
            BinaryPrimitives.WriteUInt32LittleEndian(
                anchor.AsSpan(4, 4),
                16);
            BinaryPrimitives.WriteInt32LittleEndian(
                anchor.AsSpan(8, 4),
                left);
            BinaryPrimitives.WriteInt32LittleEndian(
                anchor.AsSpan(12, 4),
                top);
            BinaryPrimitives.WriteInt32LittleEndian(
                anchor.AsSpan(16, 4),
                right);
            BinaryPrimitives.WriteInt32LittleEndian(
                anchor.AsSpan(20, 4),
                bottom);

            var result = new byte[
                shapeRecord.Length +
                anchor.Length];
            shapeRecord.CopyTo(
                result,
                0);
            anchor.CopyTo(
                result,
                shapeRecord.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(4, 4),
                checked((uint)(result.Length - 8)));
            return result;
        }

        static byte[] BuildGroupHeaderRecord(
            int shapeId,
            int left,
            int top,
            int right,
            int bottom,
            bool child = false)
        {
            var fspgr = new byte[24];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fspgr.AsSpan(0, 2),
                0x0001);
            BinaryPrimitives.WriteUInt16LittleEndian(
                fspgr.AsSpan(2, 2),
                0xF009);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fspgr.AsSpan(4, 4),
                16);
            BinaryPrimitives.WriteInt32LittleEndian(
                fspgr.AsSpan(8, 4),
                left);
            BinaryPrimitives.WriteInt32LittleEndian(
                fspgr.AsSpan(12, 4),
                top);
            BinaryPrimitives.WriteInt32LittleEndian(
                fspgr.AsSpan(16, 4),
                right);
            BinaryPrimitives.WriteInt32LittleEndian(
                fspgr.AsSpan(20, 4),
                bottom);

            var fsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(0, 2),
                0x0002);
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(2, 2),
                0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(4, 4),
                8);
            BinaryPrimitives.WriteInt32LittleEndian(
                fsp.AsSpan(8, 4),
                shapeId);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(12, 4),
                child
                    ? 0x00000003u
                    : 0x00000001u);

            var container = new byte[
                8 +
                fspgr.Length +
                fsp.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(2, 2),
                0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                container.AsSpan(4, 4),
                checked((uint)(fspgr.Length + fsp.Length)));
            fspgr.CopyTo(
                container,
                8);
            fsp.CopyTo(
                container,
                8 + fspgr.Length);
            return container;
        }

        static byte[] BuildGroupContainer(
            params byte[][] records)
        {
            var payloadLength =
                records.Sum(record => record.Length);
            var result = new byte[
                8 +
                payloadLength];
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(2, 2),
                0xF003);
            BinaryPrimitives.WriteUInt32LittleEndian(
                result.AsSpan(4, 4),
                checked((uint)payloadLength));

            var offset = 8;
            foreach (var record in records)
            {
                record.CopyTo(
                    result,
                    offset);
                offset += record.Length;
            }

            return result;
        }

        static byte[] BuildCustomPathShapeRecord(
            int shapeId)
        {
            static byte[] BuildPointArray(
                params (int X, int Y)[] points)
            {
                var data = new byte[
                    6 +
                    points.Length * 8];
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(0, 2),
                    checked((ushort)points.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(2, 2),
                    checked((ushort)points.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(4, 2),
                    8);

                for (var index = 0;
                     index < points.Length;
                     index++)
                {
                    var offset =
                        6 +
                        index * 8;
                    BinaryPrimitives.WriteInt32LittleEndian(
                        data.AsSpan(offset, 4),
                        points[index].X);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        data.AsSpan(offset + 4, 4),
                        points[index].Y);
                }

                return data;
            }

            static byte[] BuildGuideArray(
                params (ushort Flags, ushort Param1, ushort Param2, ushort Param3)[] guides)
            {
                var data = new byte[
                    6 +
                    guides.Length * 8];
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(0, 2),
                    checked((ushort)guides.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(2, 2),
                    checked((ushort)guides.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(4, 2),
                    8);

                for (var index = 0;
                     index < guides.Length;
                     index++)
                {
                    var offset =
                        6 +
                        index * 8;
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(offset, 2),
                        guides[index].Flags);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(offset + 2, 2),
                        guides[index].Param1);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(offset + 4, 2),
                        guides[index].Param2);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(offset + 6, 2),
                        guides[index].Param3);
                }

                return data;
            }

            static byte[] BuildSegmentArray(
                params ushort[] segments)
            {
                var data = new byte[
                    6 +
                    segments.Length * 2];
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(0, 2),
                    checked((ushort)segments.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(2, 2),
                    checked((ushort)segments.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    data.AsSpan(4, 2),
                    2);

                for (var index = 0;
                     index < segments.Length;
                     index++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(
                            6 + index * 2,
                            2),
                        segments[index]);
                }

                return data;
            }

            var guides = BuildGuideArray(
                (0x2000, 0x0147, 250, 0),
                (0x2001, 0x0400, 1, 2));
            var vertices = BuildPointArray(
                (0, 0),
                (1000, 0),
                (1000, unchecked((int)0x80000001u)),
                (unchecked((int)0x80000000u), 1000),
                (0, 1000));
            var segments = BuildSegmentArray(
                0x0002,
                0x0008,
                0x0009,
                0x000B,
                0x0004);

            var properties = new (ushort PropertyId, uint Value)[]
            {
                (0x0140, 0),
                (0x0141, 0),
                (0x0142, 1000),
                (0x0143, 1000),
                (0x0144, 4),
                (0x0147, 250),
                (0x8156, checked((uint)guides.Length)),
                (0x8145, checked((uint)vertices.Length)),
                (0x8146, checked((uint)segments.Length)),
                (0x0181, 0x00EECC22),
                (0x01C0, 0x00224466)
            };

            var fsp = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(0, 2),
                0x0002);
            BinaryPrimitives.WriteUInt16LittleEndian(
                fsp.AsSpan(2, 2),
                0xF00A);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fsp.AsSpan(4, 4),
                8);
            BinaryPrimitives.WriteInt32LittleEndian(
                fsp.AsSpan(8, 4),
                shapeId);

            var complexLength =
                guides.Length +
                vertices.Length +
                segments.Length;
            var fopt = new byte[
                8 +
                properties.Length * 6 +
                complexLength];
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(0, 2),
                checked((ushort)((properties.Length << 4) | 0x0003)));
            BinaryPrimitives.WriteUInt16LittleEndian(
                fopt.AsSpan(2, 2),
                0xF00B);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fopt.AsSpan(4, 4),
                checked((uint)(properties.Length * 6 + complexLength)));

            for (var index = 0;
                 index < properties.Length;
                 index++)
            {
                var propertyOffset =
                    8 +
                    index * 6;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    fopt.AsSpan(propertyOffset, 2),
                    properties[index].PropertyId);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    fopt.AsSpan(propertyOffset + 2, 4),
                    properties[index].Value);
            }

            var complexOffset =
                8 +
                properties.Length * 6;
            guides.CopyTo(
                fopt,
                complexOffset);
            vertices.CopyTo(
                fopt,
                complexOffset + guides.Length);
            segments.CopyTo(
                fopt,
                complexOffset + guides.Length + vertices.Length);

            var container = new byte[
                8 +
                fsp.Length +
                fopt.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(0, 2),
                0x000F);
            BinaryPrimitives.WriteUInt16LittleEndian(
                container.AsSpan(2, 2),
                0xF004);
            BinaryPrimitives.WriteUInt32LittleEndian(
                container.AsSpan(4, 4),
                checked((uint)(fsp.Length + fopt.Length)));
            fsp.CopyTo(
                container,
                8);
            fopt.CopyTo(
                container,
                8 + fsp.Length);
            return container;
        }

        const int rectangleShapeId = 3001;
        const int lineShapeId = 3002;
        const int diamondShapeId = 3003;
        const int adjustedDiamondShapeId = 3004;
        const int connectorShapeId = 3005;
        const int customShapeId = 3006;
        const int textBoxShapeId = 3007;
        const int linkedTextBoxShapeId = 3008;
        const int groupShapeId = 3009;
        const int groupRectangleShapeId = 3010;
        const int groupEllipseShapeId = 3011;
        const int gradientShapeId = 3012;
        const int multiStopGradientShapeId = 3013;

        var rectangle = BuildShapeRecord(
            rectangleShapeId,
            1,
            0,
            (0x0181, 0x00332211),
            (0x01C0, 0x00665544),
            (0x01CB, 25400));

        var line = BuildShapeRecord(
            lineShapeId,
            20,
            0x000000C0,
            (0x0004, 45u << 16),
            (0x01C0, 0x0000AAFF),
            (0x01CB, 12700),
            (0x01D0, 1),
            (0x01D1, 5),
            (0x01D6, 0),
            (0x01D7, 1));

        var diamond = BuildShapeRecord(
            diamondShapeId,
            4,
            0,
            (0x0181, 0x0000CC44),
            (0x01C0, 0x00000000));

        var adjustedDiamond = BuildShapeRecord(
            adjustedDiamondShapeId,
            4,
            0,
            (0x0147, 5400),
            (0x0181, 0x0000CC44));

        var connector = BuildShapeRecord(
            connectorShapeId,
            32,
            0,
            (0x01C0, 0x00CC0000),
            (0x01CB, 19050),
            (0x01D1, 3));

        var custom = BuildCustomPathShapeRecord(
            customShapeId);
        var textBox = BuildTextBoxShapeRecord(
            textBoxShapeId,
            0x00010000);
        var linkedTextBox = BuildTextBoxShapeRecord(
            linkedTextBoxShapeId,
            0x00010001);
        var groupRectangle = AddChildAnchor(
            BuildShapeRecord(
                groupRectangleShapeId,
                1,
                0x00000002,
                (0x0181, 0x000000FF)),
            0,
            0,
            1000,
            500);
        var groupEllipse = AddChildAnchor(
            BuildShapeRecord(
                groupEllipseShapeId,
                3,
                0x00000002,
                (0x0181, 0x0000FF00)),
            1000,
            500,
            2000,
            1000);
        var group = BuildGroupContainer(
            BuildGroupHeaderRecord(
                groupShapeId,
                0,
                0,
                2000,
                1000),
            groupRectangle,
            groupEllipse);
        var gradient = BuildShapeRecord(
            gradientShapeId,
            1,
            0,
            (0x0180, 4),
            (0x0181, 0x000000FF),
            (0x0183, 0x00FF0000),
            (0x018B, 45u << 16));
        var multiStopGradient =
            BuildMultiStopGradientShapeRecord(
                multiStopGradientShapeId);

        var dgg = new byte[
            rectangle.Length +
            line.Length +
            diamond.Length +
            adjustedDiamond.Length +
            connector.Length +
            custom.Length +
            textBox.Length +
            linkedTextBox.Length +
            group.Length +
            gradient.Length +
            multiStopGradient.Length];
        var dggOffset = 0;
        rectangle.CopyTo(
            dgg,
            dggOffset);
        dggOffset += rectangle.Length;
        line.CopyTo(
            dgg,
            dggOffset);
        dggOffset += line.Length;
        diamond.CopyTo(
            dgg,
            dggOffset);
        dggOffset += diamond.Length;
        adjustedDiamond.CopyTo(
            dgg,
            dggOffset);
        dggOffset += adjustedDiamond.Length;
        connector.CopyTo(
            dgg,
            dggOffset);
        dggOffset += connector.Length;
        custom.CopyTo(
            dgg,
            dggOffset);
        dggOffset += custom.Length;
        textBox.CopyTo(
            dgg,
            dggOffset);
        dggOffset += textBox.Length;
        linkedTextBox.CopyTo(
            dgg,
            dggOffset);
        dggOffset += linkedTextBox.Length;
        group.CopyTo(
            dgg,
            dggOffset);
        dggOffset += group.Length;
        gradient.CopyTo(
            dgg,
            dggOffset);
        dggOffset += gradient.Length;
        multiStopGradient.CopyTo(
            dgg,
            dggOffset);

        var shapes = DocDocumentReader.ReadOfficeArtShapes(
            dgg,
            0,
            dgg.Length);

        Assert(
            shapes.TryGetValue(
                rectangleShapeId,
                out var rectangleShape),
            "DOC OfficeArt rectangle должен разрешаться из FSP/FOPT.");
        AssertEqual(
            DocumentShapeKind.Rectangle,
            rectangleShape!.Kind,
            "DOC OfficeArt rectangle kind");
        Assert(
            rectangleShape.FillColor == "#112233",
            "DOC OfficeArt fillColor RGB.");
        Assert(
            rectangleShape.StrokeColor == "#445566",
            "DOC OfficeArt lineColor RGB.");
        AssertEqual(
            2.0,
            rectangleShape.StrokeWidthPoints!.Value,
            "DOC OfficeArt lineWidth points");

        Assert(
            shapes.TryGetValue(
                lineShapeId,
                out var lineShape) &&
            lineShape.Kind == DocumentShapeKind.Line &&
            lineShape.FlipHorizontal &&
            lineShape.FlipVertical &&
            lineShape.StrokeColor == "#FFAA00" &&
            lineShape.StrokeWidthPoints == 1.0 &&
            lineShape.RotationDegrees == 45.0 &&
            lineShape.StartArrowhead == DocumentShapeArrowhead.Triangle &&
            lineShape.EndArrowhead == DocumentShapeArrowhead.Open &&
            lineShape.LineJoin == DocumentShapeLineJoin.Bevel &&
            lineShape.LineCap == DocumentShapeLineCap.Square,
            "DOC OfficeArt line должен сохранять FSP flips, rotation, arrowheads и line properties.");

        Assert(
            shapes.TryGetValue(
                diamondShapeId,
                out var diamondShape) &&
            diamondShape.Kind == DocumentShapeKind.Diamond &&
            diamondShape.FillColor == "#44CC00",
            "DOC OfficeArt diamond preset должен сохраняться как vector shape.");

        Assert(
            !shapes.ContainsKey(adjustedDiamondShapeId),
            "DOC OfficeArt preset shape с adjustValue не должен рендериться как default geometry.");

        Assert(
            shapes.TryGetValue(
                connectorShapeId,
                out var connectorShape) &&
            connectorShape.Kind == DocumentShapeKind.StraightConnector &&
            connectorShape.StrokeColor == "#0000CC" &&
            connectorShape.StrokeWidthPoints == 1.5 &&
            connectorShape.EndArrowhead == DocumentShapeArrowhead.Diamond,
            "DOC OfficeArt StraightConnector1 должен сохранять line и arrowhead properties.");

        Assert(
            shapes.TryGetValue(
                customShapeId,
                out var customShape) &&
            customShape.Kind == DocumentShapeKind.CustomPath &&
            customShape.PathData == "M0 0 L100 0 C100 25 50 100 0 100 Z" &&
            customShape.FillColor == "#22CCEE" &&
            customShape.StrokeColor == "#664422",
            "DOC OfficeArt pVertices/pSegmentInfo должен строить mixed custom SVG path.");

        var blocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    rectangleShapeId,
                    720,
                    1440,
                    2160,
                    2880,
                    1,
                    2,
                    2,
                    2,
                    false,
                    true)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);

        var shapeInline = blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .SingleOrDefault(inline =>
                inline.Shape != null);

        Assert(
            shapeInline?.Shape?.Kind == DocumentShapeKind.Rectangle &&
            shapeInline.FloatingPlacement != null,
            "DOC floating vector shape должен попадать в нейтральный inline с FSPA placement.");

        var html = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt vector shape",
                Blocks = blocks
            },
            CreateRenderTheme());

        AssertContains(
            html,
            "class=\"floating-shape\"",
            "DOC OfficeArt vector shape SVG class");
        AssertContains(
            html,
            "data-shape-kind=\"rectangle\"",
            "DOC OfficeArt vector shape kind metadata");
        AssertContains(
            html,
            "fill=\"#112233\" stroke=\"#445566\" stroke-width=\"2pt\" vector-effect=\"non-scaling-stroke\"",
            "DOC OfficeArt vector shape paint");
        AssertContains(
            html,
            "style=\"width:72pt;height:72pt;float:left;\"",
            "DOC OfficeArt vector shape FSPA reflow sizing");

        var lineBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    lineShapeId,
                    0,
                    0,
                    1440,
                    1440,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);

        var lineHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt line",
                Blocks = lineBlocks
            },
            CreateRenderTheme());

        AssertContains(
            lineHtml,
            "data-shape-rotation-deg=\"45\"",
            "DOC OfficeArt rotation metadata");
        AssertContains(
            lineHtml,
            "data-shape-start-arrowhead=\"triangle\" data-shape-end-arrowhead=\"open\"",
            "DOC OfficeArt arrowhead metadata");
        AssertContains(
            lineHtml,
            "data-shape-line-join=\"bevel\" data-shape-line-cap=\"square\" stroke-linejoin=\"bevel\" stroke-linecap=\"square\"",
            "DOC OfficeArt line join/cap metadata and SVG rendering");
        AssertContains(
            lineHtml,
            "transform:rotate(45deg) scale(-1,-1);transform-origin:center;",
            "DOC OfficeArt rotation and FSP flips rendering");
        AssertContains(
            lineHtml,
            "marker-start=\"url(#shape-marker-",
            "DOC OfficeArt start arrowhead SVG marker");
        AssertContains(
            lineHtml,
            "marker-end=\"url(#shape-marker-",
            "DOC OfficeArt end arrowhead SVG marker");

        var connectorBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    connectorShapeId,
                    0,
                    0,
                    1440,
                    720,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);

        var connectorHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt connector",
                Blocks = connectorBlocks
            },
            CreateRenderTheme());

        AssertContains(
            connectorHtml,
            "data-shape-kind=\"straight-connector\"",
            "DOC OfficeArt StraightConnector1 renderer");
        AssertContains(
            connectorHtml,
            "data-shape-end-arrowhead=\"diamond\"",
            "DOC OfficeArt connector arrowhead renderer");

        var customBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    customShapeId,
                    0,
                    0,
                    1440,
                    1440,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);

        var customHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt custom path",
                Blocks = customBlocks
            },
            CreateRenderTheme());

        AssertContains(
            customHtml,
            "data-shape-kind=\"custom-path\"",
            "DOC OfficeArt custom path renderer metadata");
        AssertContains(
            customHtml,
            "<path d=\"M0 0 L100 0 C100 25 50 100 0 100 Z\"",
            "DOC OfficeArt custom mixed path SVG");

        Assert(
            shapes.TryGetValue(
                gradientShapeId,
                out var gradientShape) &&
            gradientShape.Kind == DocumentShapeKind.Rectangle &&
            gradientShape.FillColor == null &&
            gradientShape.FillGradient != null &&
            gradientShape.FillGradient.Stops.Count == 2 &&
            gradientShape.FillGradient.Stops[0].Color == "#FF0000" &&
            gradientShape.FillGradient.Stops[0].Position == 0 &&
            gradientShape.FillGradient.Stops[1].Color == "#0000FF" &&
            gradientShape.FillGradient.Stops[1].Position == 1 &&
            gradientShape.FillGradient.AngleDegrees == 45.0,
            "DOC OfficeArt msofillShade должен сохранять двухцветный linear gradient.");

        var gradientBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    gradientShapeId,
                    0,
                    0,
                    1440,
                    720,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);
        var gradientHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt gradient",
                Blocks = gradientBlocks
            },
            CreateRenderTheme());

        AssertContains(
            gradientHtml,
            "<linearGradient",
            "DOC OfficeArt gradient SVG definition");
        AssertContains(
            gradientHtml,
            "gradientTransform=\"rotate(-45 50 50)\"",
            "DOC OfficeArt fillAngle renderer");
        AssertContains(
            gradientHtml,
            "stop-color=\"#FF0000\"",
            "DOC OfficeArt gradient start color");
        AssertContains(
            gradientHtml,
            "stop-color=\"#0000FF\"",
            "DOC OfficeArt gradient end color");

        Assert(
            shapes.TryGetValue(
                multiStopGradientShapeId,
                out var multiStopGradientShape) &&
            multiStopGradientShape.FillGradient != null &&
            multiStopGradientShape.FillGradient.Stops.Count == 3 &&
            multiStopGradientShape.FillGradient.Stops[0].Color == "#FF0000" &&
            multiStopGradientShape.FillGradient.Stops[0].Position == 0 &&
            multiStopGradientShape.FillGradient.Stops[1].Color == "#00FF00" &&
            multiStopGradientShape.FillGradient.Stops[1].Position == 0.5 &&
            multiStopGradientShape.FillGradient.Stops[2].Color == "#0000FF" &&
            multiStopGradientShape.FillGradient.Stops[2].Position == 1 &&
            multiStopGradientShape.FillGradient.AngleDegrees == 90.0,
            "DOC OfficeArt fillShadeColors должен сохранять упорядоченные MSOSHADECOLOR stops.");

        var multiStopGradientBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    multiStopGradientShapeId,
                    0,
                    0,
                    1440,
                    720,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);
        var multiStopGradientHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt multi-stop gradient",
                Blocks = multiStopGradientBlocks
            },
            CreateRenderTheme());

        AssertContains(
            multiStopGradientHtml,
            "offset=\"50%\" stop-color=\"#00FF00\"",
            "DOC OfficeArt multi-stop gradient midpoint");
        AssertContains(
            multiStopGradientHtml,
            "gradientTransform=\"rotate(-90 50 50)\"",
            "DOC OfficeArt multi-stop fillAngle renderer");

        Assert(
            shapes.TryGetValue(
                groupShapeId,
                out var groupShape) &&
            groupShape.Kind == DocumentShapeKind.Group &&
            groupShape.Group != null &&
            groupShape.Group.Left == 0 &&
            groupShape.Group.Top == 0 &&
            groupShape.Group.Right == 2000 &&
            groupShape.Group.Bottom == 1000 &&
            groupShape.Group.Children.Count == 2 &&
            groupShape.Group.Children[0].Shape.Kind == DocumentShapeKind.Rectangle &&
            groupShape.Group.Children[0].Left == 0 &&
            groupShape.Group.Children[0].Top == 0 &&
            groupShape.Group.Children[0].Right == 1000 &&
            groupShape.Group.Children[0].Bottom == 500 &&
            groupShape.Group.Children[1].Shape.Kind == DocumentShapeKind.Ellipse &&
            groupShape.Group.Children[1].Left == 1000 &&
            groupShape.Group.Children[1].Top == 500 &&
            groupShape.Group.Children[1].Right == 2000 &&
            groupShape.Group.Children[1].Bottom == 1000 &&
            shapes.ContainsKey(groupRectangleShapeId) &&
            shapes.ContainsKey(groupEllipseShapeId),
            "DOC OfficeArt group должен сохранять FSPGR coordinate system, child anchors и child spid lookup.");

        var groupBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    groupShapeId,
                    0,
                    0,
                    2880,
                    1440,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);
        var groupHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt group",
                Blocks = groupBlocks
            },
            CreateRenderTheme());

        AssertContains(
            groupHtml,
            "data-shape-kind=\"group\"",
            "DOC OfficeArt group renderer metadata");
        AssertContains(
            groupHtml,
            "viewBox=\"0 0 2000 1000\"",
            "DOC OfficeArt group coordinate system renderer");
        AssertContains(
            groupHtml,
            "class=\"group-child-shape\" x=\"0\" y=\"0\" width=\"1000\" height=\"500\"",
            "DOC OfficeArt first group child anchor renderer");
        AssertContains(
            groupHtml,
            "class=\"group-child-shape\" x=\"1000\" y=\"500\" width=\"1000\" height=\"500\"",
            "DOC OfficeArt second group child anchor renderer");

        Assert(
            shapes.TryGetValue(
                textBoxShapeId,
                out var textBoxShape) &&
            textBoxShape.Kind == DocumentShapeKind.TextBox &&
            textBoxShape.TextIdentifier == 0x00010000,
            "DOC OfficeArt TextBox должен сохранять OfficeArtClientTextbox identifier.");

        var textboxPlc = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxPlc.AsSpan(0, 4),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxPlc.AsSpan(4, 4),
            6);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxPlc.AsSpan(8, 4),
            7);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxPlc.AsSpan(12, 4),
            2);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxPlc.AsSpan(26, 4),
            textBoxShapeId);
        BinaryPrimitives.WriteUInt16LittleEndian(
            textboxPlc.AsSpan(42, 2),
            1);

        var textboxStories = DocDocumentReader.ReadTextboxStoryDescriptors(
            textboxPlc,
            0,
            textboxPlc.Length,
            7);

        Assert(
            textboxStories.Count == 1 &&
            textboxStories[0].Index == 0 &&
            textboxStories[0].LocalStart == 0 &&
            textboxStories[0].ContentLength == 5 &&
            textboxStories[0].ShapeId == textBoxShapeId &&
            textboxStories[0].ChainLength == 2,
            "DOC PlcftxbxTxt должен связывать textbox story с FTXBXS.lid.");

        Assert(
            shapes.TryGetValue(
                linkedTextBoxShapeId,
                out var linkedTextBoxShape) &&
            linkedTextBoxShape.Kind == DocumentShapeKind.TextBox &&
            linkedTextBoxShape.TextIdentifier == 0x00010001,
            "DOC linked TextBox должен сохранять zero-based chain index в OfficeArtClientTextbox.");

        var textboxBreakPlc = new byte[34];
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxBreakPlc.AsSpan(0, 4),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxBreakPlc.AsSpan(4, 4),
            2);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxBreakPlc.AsSpan(8, 4),
            5);
        BinaryPrimitives.WriteInt32LittleEndian(
            textboxBreakPlc.AsSpan(12, 4),
            7);
        BinaryPrimitives.WriteInt16LittleEndian(
            textboxBreakPlc.AsSpan(16, 2),
            0);
        BinaryPrimitives.WriteInt16LittleEndian(
            textboxBreakPlc.AsSpan(22, 2),
            0);

        var textboxBreaks = DocDocumentReader.ReadTextboxBreakDescriptors(
            textboxBreakPlc,
            0,
            textboxBreakPlc.Length,
            7);

        Assert(
            textboxBreaks.Count == 2 &&
            textboxBreaks[0].TextboxIndex == 0 &&
            textboxBreaks[0].LocalStart == 0 &&
            textboxBreaks[0].ContentLength == 2 &&
            textboxBreaks[1].TextboxIndex == 0 &&
            textboxBreaks[1].LocalStart == 2 &&
            textboxBreaks[1].ContentLength == 3,
            "DOC PlcfTxbxBkd должен разделять linked textbox chain на дискретные Tbkd ranges.");

        textBoxShape!.TextBlocks =
        [
            new DocumentParagraph
            {
                Alignment = DocumentParagraphAlignment.Center,
                Inlines =
                [
                    new DocumentInline
                    {
                        Text = "Hello",
                        FontFamily = "Arial",
                        FontSizePoints = 14,
                        TextColor = "#112233",
                        Bold = true
                    }
                ]
            }
        ];
        linkedTextBoxShape!.TextBlocks =
        [
            new DocumentParagraph
            {
                Inlines =
                [
                    new DocumentInline
                    {
                        Text = "World",
                        Italic = true
                    }
                ]
            }
        ];
        var textBoxBlocks = DocDocumentReader.BuildParagraphs(
            "\u0008\r",
            [
                new DocDocumentReader.DocFloatingShapeAnchor(
                    0,
                    textBoxShapeId,
                    0,
                    0,
                    1440,
                    720,
                    1,
                    2,
                    2,
                    2,
                    false,
                    false)
            ],
            new Dictionary<int, DocumentImage>(),
            shapes);
        var textBoxHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC OfficeArt textbox",
                Blocks = textBoxBlocks
            },
            CreateRenderTheme());

        AssertContains(
            textBoxHtml,
            "data-shape-kind=\"text-box\"",
            "DOC OfficeArt textbox renderer metadata");
        AssertContains(
            textBoxHtml,
            "class=\"shape-text\"",
            "DOC OfficeArt textbox text renderer");
        AssertContains(
            textBoxHtml,
            "Hello",
            "DOC OfficeArt textbox story text");
        AssertContains(
            textBoxHtml,
            "text-align:center;",
            "DOC OfficeArt textbox paragraph formatting");
        AssertContains(
            textBoxHtml,
            "font-family:&#39;Arial&#39;;font-size:14pt;color:#112233;",
            "DOC OfficeArt textbox character formatting");
        AssertContains(
            textBoxHtml,
            "<strong>Hello</strong>",
            "DOC OfficeArt textbox bold formatting");
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

        var plcSpaHdr = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(plcSpaHdr.AsSpan(0, 4), 17);
        BinaryPrimitives.WriteInt32LittleEndian(plcSpaHdr.AsSpan(4, 4), 21);
        BinaryPrimitives.WriteInt32LittleEndian(plcSpaHdr.AsSpan(8, 4), 29);

        static void WriteSpa(
            Span<byte> target,
            int shapeId,
            int left,
            int top,
            int right,
            int bottom)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target[..4], shapeId);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(4, 4), left);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(8, 4), top);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(12, 4), right);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(16, 4), bottom);
            BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(20, 2), 0);
        }

        WriteSpa(
            plcSpaHdr.AsSpan(12, 26),
            3001,
            10,
            20,
            1010,
            1020);
        WriteSpa(
            plcSpaHdr.AsSpan(38, 26),
            3002,
            30,
            40,
            1030,
            1040);

        var headerAnchors =
            DocDocumentReader.ReadHeaderDocumentShapeAnchors(
                plcSpaHdr,
                0,
                plcSpaHdr.Length,
                ccpHdd: 29);

        Assert(
            headerAnchors.Count == 2 &&
            headerAnchors[0].Cp == 17 &&
            headerAnchors[0].ShapeId == 3001 &&
            headerAnchors[1].Cp == 21 &&
            headerAnchors[1].ShapeId == 3002,
            "DOC PlcSpaHdr должен читать shape anchors в координатах Header Document.");

        var oddHeaderAnchors =
            DocDocumentReader.RebaseHeaderShapeAnchors(
                headerAnchors,
                storyLocalStart: 16,
                storyContentLength: 3);
        var oddFooterAnchors =
            DocDocumentReader.RebaseHeaderShapeAnchors(
                headerAnchors,
                storyLocalStart: 20,
                storyContentLength: 3);

        Assert(
            oddHeaderAnchors.Count == 1 &&
            oddHeaderAnchors[0].Cp == 1 &&
            oddHeaderAnchors[0].ShapeId == 3001,
            "DOC PlcSpaHdr odd-header anchor должен rebased к локальному CP story.");
        Assert(
            oddFooterAnchors.Count == 1 &&
            oddFooterAnchors[0].Cp == 1 &&
            oddFooterAnchors[0].ShapeId == 3002,
            "DOC PlcSpaHdr odd-footer anchor должен попадать только в свою story.");
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
        const int secondSepxOffset = 320;
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

        var word = new byte[512];
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

        void AddUInt32Sprm(
            ushort sprm,
            uint value)
        {
            grpprl.Add((byte)(sprm & 0xFF));
            grpprl.Add((byte)(sprm >> 8));
            Span<byte> operand = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(operand, value);
            grpprl.AddRange(operand.ToArray());
        }

        void AddColumnSprm(
            ushort sprm,
            byte columnIndex,
            ushort twips)
        {
            grpprl.Add((byte)(sprm & 0xFF));
            grpprl.Add((byte)(sprm >> 8));
            grpprl.Add(columnIndex);
            grpprl.Add((byte)(twips & 0xFF));
            grpprl.Add((byte)(twips >> 8));
        }

        void AddModernSectionBorder(
            ushort sprm,
            byte red,
            byte green,
            byte blue,
            byte widthEighthPoints,
            byte type)
        {
            grpprl.Add((byte)(sprm & 0xFF));
            grpprl.Add((byte)(sprm >> 8));
            grpprl.Add(8);
            grpprl.Add(red);
            grpprl.Add(green);
            grpprl.Add(blue);
            grpprl.Add(0);
            grpprl.Add(widthEighthPoints);
            grpprl.Add(type);
            grpprl.Add(0);
            grpprl.Add(0);
        }

        AddByteSprm(0x3000, 3);
        AddByteSprm(0x3001, 2);
        AddByteSprm(0x3013, 1);
        AddUInt16Sprm(0x5015, 5);
        AddUInt16Sprm(0x9016, 360);
        AddUInt16Sprm(0x501B, 9);
        AddModernSectionBorder(0xD234, 0xFF, 0x00, 0x00, 8, 0x01);
        AddModernSectionBorder(0xD235, 0x00, 0x80, 0x00, 16, 0x06);
        AddModernSectionBorder(0xD236, 0x00, 0x00, 0xFF, 24, 0x03);
        AddModernSectionBorder(0xD237, 0x80, 0x00, 0x80, 12, 0x07);
        AddUInt16Sprm(0x522F, 0x002A);

        AddColumnSprm(0xF203, 0, 2880);
        AddColumnSprm(0xF203, 1, 2160);
        AddColumnSprm(0xF203, 2, 1440);
        AddColumnSprm(0xF204, 0, 360);
        AddColumnSprm(0xF204, 1, 720);
        AddByteSprm(0x3005, 0);
        AddUInt16Sprm(0x500B, 2);
        AddUInt16Sprm(0x900C, 540);
        AddByteSprm(0x300E, 1);
        AddByteSprm(0x3011, 1);
        AddByteSprm(0x3019, 1);
        AddByteSprm(0x301A, 1);
        AddUInt16Sprm(0x501C, 3);
        AddUInt16Sprm(0x9031, 360);
        AddUInt16Sprm(0x5032, 2);
        AddUInt32Sprm(0x7044, 70000);
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
            properties.RightToLeft == true &&
            properties.ColumnCount == 3 &&
            properties.ColumnsEvenlySpaced == false &&
            properties.ColumnSpacingPoints == 27 &&
            properties.LineBetweenColumns == true &&
            properties.Columns.Count == 3 &&
            properties.Columns[0].WidthPoints == 144 &&
            properties.Columns[0].SpaceAfterPoints == 18 &&
            properties.Columns[1].WidthPoints == 108 &&
            properties.Columns[1].SpaceAfterPoints == 36 &&
            properties.Columns[2].WidthPoints == 72 &&
            properties.Columns[2].SpaceAfterPoints == null &&
            properties.VerticalAlignment == DocumentSectionVerticalAlignment.Center &&
            properties.PageNumberFormatCode == 1 &&
            properties.RestartPageNumbering == true &&
            properties.PageNumberStart == 70000 &&
            properties.GridMode == DocumentGridMode.LinesOnly &&
            properties.GridLinePitchPoints == 18 &&
            properties.ChapterSeparatorCode == 3 &&
            properties.ChapterHeadingLevel == 2 &&
            properties.LineNumberingMode == DocumentLineNumberingMode.RestartEachSection &&
            properties.LineNumberingInterval == 5 &&
            properties.LineNumberingStart == 10 &&
            properties.LineNumberDistancePoints == 18 &&
            properties.PageBorderTop is { WidthPoints: 1, Type: 0x01, Color: "#FF0000" } &&
            properties.PageBorderLeft is { WidthPoints: 2, Type: 0x06, Color: "#008000" } &&
            properties.PageBorderBottom is { WidthPoints: 3, Type: 0x03, Color: "#0000FF" } &&
            properties.PageBorderRight is { WidthPoints: 1.5, Type: 0x07, Color: "#800080" } &&
            properties.PageBorderApplyTo == DocumentPageBorderApplyTo.AllButFirst &&
            properties.PageBorderDepth == DocumentPageBorderDepth.Back &&
            properties.PageBorderOffsetFrom == DocumentPageBorderOffsetFrom.PageEdge,
            "DOC SEPX должен сохранять page setup, columns, page/chapter/line numbering, page borders и document grid.");

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
        AssertContains(
            html,
            "data-column-count=\"3\"",
            "DOC rendered section column count");
        AssertContains(
            html,
            "data-columns-evenly-spaced=\"false\"",
            "DOC rendered uneven-columns flag");
        AssertContains(
            html,
            "data-column-spacing-pt=\"27\"",
            "DOC rendered default column spacing");
        AssertContains(
            html,
            "data-line-between-columns=\"true\"",
            "DOC rendered line-between-columns flag");
        AssertContains(
            html,
            "data-columns=\"0:144:18;1:108:36;2:72:\"",
            "DOC rendered explicit uneven column layout");
        AssertContains(
            html,
            "data-vertical-alignment=\"Center\"",
            "DOC rendered section vertical alignment");
        AssertContains(
            html,
            "data-page-number-format=\"1\" data-page-number-restart=\"true\" data-page-number-start=\"70000\"",
            "DOC rendered page-numbering metadata");
        AssertContains(
            html,
            "data-grid-mode=\"LinesOnly\" data-grid-line-pitch-pt=\"18\"",
            "DOC rendered document-grid metadata");
        AssertContains(
            html,
            "data-chapter-separator=\"3\"",
            "DOC rendered chapter separator metadata");
        AssertContains(
            html,
            "data-chapter-heading-level=\"2\"",
            "DOC rendered chapter heading level metadata");
        AssertContains(
            html,
            "data-line-numbering-mode=\"RestartEachSection\"",
            "DOC rendered line-numbering mode");
        AssertContains(
            html,
            "data-line-numbering-interval=\"5\" data-line-numbering-start=\"10\" data-line-number-distance-pt=\"18\"",
            "DOC rendered line-numbering interval/start/distance");
        AssertContains(
            html,
            "data-page-border-apply-to=\"AllButFirst\" data-page-border-depth=\"Back\" data-page-border-offset-from=\"PageEdge\"",
            "DOC rendered page-border properties");
        AssertContains(
            html,
            "data-page-border-top-width-pt=\"1\" data-page-border-top-type=\"1\" data-page-border-top-color-index=\"0\" data-page-border-top-color=\"#FF0000\"",
            "DOC rendered top page-border metadata");
        AssertContains(
            html,
            "data-page-border-bottom-width-pt=\"3\" data-page-border-bottom-type=\"3\" data-page-border-bottom-color-index=\"0\" data-page-border-bottom-color=\"#0000FF\"",
            "DOC rendered bottom page-border metadata");
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
        AddByteSprm(
            0x260A,
            2);
        grpprl.Add(0x0B);
        grpprl.Add(0x46);
        grpprl.AddRange(BitConverter.GetBytes((short)-3));

        void AddModernParagraphBorder(
            ushort sprm,
            byte red,
            byte green,
            byte blue,
            byte widthEighthPoints,
            byte type)
        {
            grpprl.Add((byte)(sprm & 0xFF));
            grpprl.Add((byte)(sprm >> 8));
            grpprl.Add(8);
            grpprl.Add(red);
            grpprl.Add(green);
            grpprl.Add(blue);
            grpprl.Add(0);
            grpprl.Add(widthEighthPoints);
            grpprl.Add(type);
            grpprl.Add(0);
            grpprl.Add(0);
        }

        AddModernParagraphBorder(0xC64E, 0xFF, 0x00, 0x00, 8, 0x01);
        AddModernParagraphBorder(0xC64F, 0x00, 0x80, 0x00, 16, 0x06);
        AddModernParagraphBorder(0xC650, 0x00, 0x00, 0xFF, 24, 0x03);
        AddModernParagraphBorder(0xC651, 0x80, 0x00, 0x80, 12, 0x07);
        AddModernParagraphBorder(0xC652, 0x00, 0x80, 0x80, 8, 0x01);
        AddModernParagraphBorder(0xC653, 0x80, 0x80, 0x00, 8, 0x01);
        grpprl.Add(0x65);
        grpprl.Add(0x64);
        grpprl.AddRange(BitConverter.GetBytes((uint)1234));

        grpprl.Add(0x4D);
        grpprl.Add(0xC6);
        grpprl.Add(10);
        grpprl.AddRange(new byte[] { 0xFF, 0x00, 0x00, 0x00 });
        grpprl.AddRange(new byte[] { 0xFF, 0xFF, 0x00, 0x00 });
        grpprl.AddRange(BitConverter.GetBytes((ushort)1));

        grpprl.Add(0x0D);
        grpprl.Add(0xC6);
        grpprl.Add(13);
        grpprl.Add(1);
        grpprl.AddRange(BitConverter.GetBytes((short)720));
        grpprl.Add(3);
        grpprl.AddRange(BitConverter.GetBytes((short)1440));
        grpprl.AddRange(BitConverter.GetBytes((short)2880));
        grpprl.AddRange(BitConverter.GetBytes((short)4320));
        grpprl.Add(0x0A);
        grpprl.Add(0x11);
        grpprl.Add(0x1B);

        var formatting =
            new DocDocumentReader.DocParagraphFormatting
            {
                TabStops =
                [
                    new DocumentTabStop
                    {
                        PositionPoints = 36,
                        Alignment = DocumentTabAlignment.Left,
                        Leader = DocumentTabLeader.None
                    }
                ]
            };

        DocDocumentReader.ReadParagraphGrpprl(
            grpprl.ToArray(),
            formatting);

        var legacyShadingFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[] { 0x2D, 0x44, 0xE6, 0x04 },
            legacyShadingFormatting);

        Assert(
            legacyShadingFormatting.Shading is
            {
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFF00",
                Pattern: 1
            },
            "DOC sprmPShd80 должен декодировать legacy Ico colors и pattern.");

        var nilLegacyShadingFormatting =
            new DocDocumentReader.DocParagraphFormatting
            {
                Shading = legacyShadingFormatting.Shading
            };
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[] { 0x2D, 0x44, 0xFF, 0xFF },
            nilLegacyShadingFormatting);

        Assert(
            nilLegacyShadingFormatting.Shading == null,
            "DOC Shd80Nil должен сбрасывать paragraph shading.");

        var legacyBorderFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[]
            {
                0x24, 0x64, 0x08, 0x01, 0x06, 0x00,
                0x25, 0x64, 0x10, 0x06, 0x0B, 0x00,
                0x26, 0x64, 0x18, 0x03, 0x02, 0x00,
                0x27, 0x64, 0x0C, 0x07, 0x0C, 0x00,
                0x28, 0x64, 0x08, 0x01, 0x0A, 0x00,
                0x29, 0x66, 0x08, 0x01, 0x06, 0x00
            },
            legacyBorderFormatting);

        var word95BorderFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[]
            {
                0x24, 0x44, 0xA8, 0x11,
                0x25, 0x44, 0xCE, 0x02,
                0x26, 0x44, 0x9A, 0x00,
                0x27, 0x44, 0x0F, 0x03,
                0x28, 0x44, 0x93, 0x02,
                0x29, 0x46, 0xA8, 0x11
            },
            word95BorderFormatting);

        Assert(
            word95BorderFormatting.BorderTop is
            {
                Type: 0x01,
                WidthPoints: 0.75,
                ColorIndex: 0x06,
                SpacingPoints: 2,
                Shadow: true
            } &&
            word95BorderFormatting.BorderLeft is { Type: 0x06, WidthPoints: 0.75, ColorIndex: 0x0B } &&
            word95BorderFormatting.BorderBottom is { Type: 0x03, WidthPoints: 2.25, ColorIndex: 0x02 } &&
            word95BorderFormatting.BorderRight is { Type: 0x07, WidthPoints: 0.75, ColorIndex: 0x0C } &&
            word95BorderFormatting.BorderBetween is { Type: 0x02, WidthPoints: 3, ColorIndex: 0x0A } &&
            word95BorderFormatting.BorderBar is { Type: 0x01, WidthPoints: 0.75, ColorIndex: 0x06 },
            "DOC sprmPBrc*70 должны декодировать Word 95 BRC70 bit fields, включая bar.");

        Assert(
            legacyBorderFormatting.BorderTop is { Type: 0x01, WidthPoints: 1, ColorIndex: 0x06 } &&
            legacyBorderFormatting.BorderLeft is { Type: 0x06, WidthPoints: 2, ColorIndex: 0x0B } &&
            legacyBorderFormatting.BorderBottom is { Type: 0x03, WidthPoints: 3, ColorIndex: 0x02 } &&
            legacyBorderFormatting.BorderRight is { Type: 0x07, WidthPoints: 1.5, ColorIndex: 0x0C } &&
            legacyBorderFormatting.BorderBetween is { Type: 0x01, WidthPoints: 1, ColorIndex: 0x0A } &&
            legacyBorderFormatting.BorderBar is { Type: 0x01, WidthPoints: 1, ColorIndex: 0x06 },
            "DOC sprmPBrc*80 должны декодировать legacy Brc80 paragraph borders, включая bar metadata.");

        var skippedListFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[] { 0x0A, 0x26, 0x0C, 0x0B, 0x46, 0x01, 0x00 },
            skippedListFormatting);
        Assert(
            skippedListFormatting.ListFormatOverrideRaw == 1 &&
            skippedListFormatting.ListLevelRaw == 0x0C,
            "DOC sprmPIlvl 0x0C должен сохранять skip-numbering list semantics.");

        var clearedListFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[] { 0x0B, 0x46, 0x00, 0x00 },
            clearedListFormatting);
        Assert(
            clearedListFormatting.ListFormatOverrideRaw == 0,
            "DOC sprmPIlfo 0 должен явно сбрасывать list membership.");

        var noListFormatting =
            new DocDocumentReader.DocParagraphFormatting();
        DocDocumentReader.ReadParagraphGrpprl(
            new byte[] { 0x0B, 0x46, 0x01, 0xF8 },
            noListFormatting);
        Assert(
            noListFormatting.ListFormatOverrideRaw == -2047,
            "DOC sprmPIlfo 0xF801 должен сохранять explicit not-in-list semantics.");

        var plfLst = new List<byte>();
        plfLst.AddRange(BitConverter.GetBytes((short)1));
        plfLst.AddRange(BitConverter.GetBytes(1234));
        plfLst.AddRange(new byte[4]);
        for (var index = 0; index < 9; index++)
        {
            plfLst.AddRange(BitConverter.GetBytes((ushort)0x0FFF));
        }
        plfLst.Add(0x01);
        plfLst.Add(0x00);

        var lvlTail = new List<byte>();
        lvlTail.AddRange(BitConverter.GetBytes(1));
        lvlTail.Add(0x17);
        lvlTail.Add(0x00);
        lvlTail.AddRange(new byte[9]);
        lvlTail.Add(0x01);
        lvlTail.AddRange(new byte[4]);
        lvlTail.AddRange(new byte[4]);
        lvlTail.Add(0x00);
        lvlTail.Add(0x00);
        lvlTail.Add(0x00);
        lvlTail.Add(0x00);
        lvlTail.AddRange(BitConverter.GetBytes((ushort)1));
        lvlTail.AddRange(BitConverter.GetBytes((ushort)'·'));

        var plfLfo = new List<byte>();
        plfLfo.AddRange(BitConverter.GetBytes((uint)1));
        plfLfo.AddRange(BitConverter.GetBytes(1234));
        plfLfo.AddRange(new byte[12]);
        plfLfo.AddRange(BitConverter.GetBytes(0));

        var listTables = DocDocumentReader.ReadListTables(
            plfLst.ToArray(),
            lvlTail.ToArray(),
            plfLfo.ToArray());

        Assert(
            listTables.ListsById.Count == 1 &&
            listTables.OverridesByIndex.Select(item => item.ListId).SequenceEqual([1234]) &&
            listTables.ListsById[1234].Simple &&
            listTables.ListsById[1234].Levels.Count == 1 &&
            listTables.ListsById[1234].Levels[0] is
            {
                StartAt: 1,
                NumberFormatCode: 0x17,
                FollowCharacter: 1,
                NumberText: "·"
            },
            "DOC PlfLst/LVL/PlfLfo должны сохранять simple bullet list definition.");

        var plfLfoStartOverride = new List<byte>();
        plfLfoStartOverride.AddRange(BitConverter.GetBytes((uint)1));
        plfLfoStartOverride.AddRange(BitConverter.GetBytes(1234));
        plfLfoStartOverride.AddRange(new byte[8]);
        plfLfoStartOverride.Add(1);
        plfLfoStartOverride.AddRange(new byte[3]);
        plfLfoStartOverride.AddRange(BitConverter.GetBytes(0));
        plfLfoStartOverride.AddRange(BitConverter.GetBytes(7));
        plfLfoStartOverride.AddRange(BitConverter.GetBytes((uint)0x10));

        var overrideTables = DocDocumentReader.ReadListTables(
            plfLst.ToArray(),
            lvlTail.ToArray(),
            plfLfoStartOverride.ToArray());

        Assert(
            overrideTables.OverridesByIndex[0].LevelOverrides[0].StartAtOverride == 7 &&
            overrideTables.OverridesByIndex[0].LevelOverrides[0].ReplacementLevel == null,
            "DOC LFOLVL fStartAt должен сохранять one-shot startAt override.");

        var listBlocks = DocDocumentReader.BuildParagraphs(
            "Bullet\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(
                    0,
                    7,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        ListFormatOverrideRaw = 1,
                        ListLevelRaw = 0
                    })
            ],
            listTables);
        var listParagraph = (DocumentParagraph)listBlocks.Single();

        Assert(
            listParagraph.ListFormatOverrideIndex == 1 &&
            listParagraph.ListLevel == 0 &&
            listParagraph.ListStartAt == 1 &&
            listParagraph.ListNumberFormatCode == 0x17 &&
            listParagraph.ListFollowCharacter == 1 &&
            listParagraph.ListBulletText == "·" &&
            listParagraph.ListMarkerText == "· ",
            "DOC paragraph должен разрешать iLfo/iLvl в LVL properties.");

        var listHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC list tables",
                Blocks = listBlocks
            },
            CreateRenderTheme());
        AssertContains(
            listHtml,
            "data-list-format-override=\"1\" data-list-level=\"0\" data-list-start-at=\"1\" data-list-number-format=\"23\" data-list-follow=\"1\" data-list-bullet=\"&#183;\"",
            "DOC resolved list definition metadata");
        AssertContains(
            listHtml,
            "<span class=\"flow-list-marker\" style=\"white-space:pre\">&#183; </span>Bullet",
            "DOC rendered bullet marker");

        static byte[] PlaceholderOffsets(params byte[] values)
        {
            var result = new byte[9];
            Array.Copy(values, result, values.Length);
            return result;
        }

        var numberedList = new DocDocumentReader.DocListDefinition(
            4321,
            false,
            [
                new DocDocumentReader.DocListLevelDefinition(
                    1,
                    0x00,
                    1,
                    "\0.",
                    PlaceholderOffsets(1),
                    false,
                    false,
                    0),
                new DocDocumentReader.DocListLevelDefinition(
                    1,
                    0x04,
                    1,
                    "\u0000.\u0001)",
                    PlaceholderOffsets(1, 3),
                    false,
                    false,
                    0)
            ]);

        var startOverrideTables = new DocDocumentReader.DocListTables(
            new Dictionary<int, DocDocumentReader.DocListDefinition>
            {
                [4321] = numberedList
            },
            [new DocDocumentReader.DocListOverrideDefinition(
                4321,
                new Dictionary<int, DocDocumentReader.DocListLevelOverride>
                {
                    [0] = new DocDocumentReader.DocListLevelOverride(5, null)
                })]);
        var startOverrideBlocks = DocDocumentReader.BuildParagraphs(
            "Five\rSix\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(0, 5, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(5, 9, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 })
            ],
            startOverrideTables);
        Assert(
            startOverrideBlocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .SequenceEqual(["5. ", "6. "]),
            "DOC LFOLVL startAt override должен применяться один раз для iLfo.");

        var replacementLevel = numberedList.Levels[0] with
        {
            NumberFormatCode = 0x01,
            NumberText = "\0)"
        };
        var replacementTables = new DocDocumentReader.DocListTables(
            new Dictionary<int, DocDocumentReader.DocListDefinition>
            {
                [4321] = numberedList
            },
            [new DocDocumentReader.DocListOverrideDefinition(
                4321,
                new Dictionary<int, DocDocumentReader.DocListLevelOverride>
                {
                    [0] = new DocDocumentReader.DocListLevelOverride(null, replacementLevel)
                })]);
        var replacementBlocks = DocDocumentReader.BuildParagraphs(
            "First\rSecond\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(0, 6, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(6, 13, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 })
            ],
            replacementTables);
        Assert(
            replacementBlocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .SequenceEqual(["I) ", "II) "]),
            "DOC LFOLVL replacement LVL должен заменять list level formatting.");

        var numberedTables = new DocDocumentReader.DocListTables(
            new Dictionary<int, DocDocumentReader.DocListDefinition>
            {
                [4321] = numberedList
            },
            [new DocDocumentReader.DocListOverrideDefinition(
                4321,
                new Dictionary<int, DocDocumentReader.DocListLevelOverride>())]);
        var numberedBlocks = DocDocumentReader.BuildParagraphs(
            "One\rSubA\rSubB\rTwo\rSubC\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(0, 4, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(4, 9, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 1 }),
                new DocDocumentReader.DocParagraphFormattingSpan(9, 14, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 1 }),
                new DocDocumentReader.DocParagraphFormattingSpan(14, 18, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(18, 23, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 1 })
            ],
            numberedTables);
        var numberedParagraphs = numberedBlocks.Cast<DocumentParagraph>().ToArray();

        Assert(
            numberedParagraphs.Select(paragraph => paragraph.ListMarkerText)
                .SequenceEqual(["1. ", "1.a) ", "1.b) ", "2. ", "2.a) "]),
            "DOC numbering должен increment/restart nested list levels.");

        var numberedHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = "DOC numbering",
                Blocks = numberedBlocks
            },
            CreateRenderTheme());
        AssertContains(
            numberedHtml,
            "<span class=\"flow-list-marker\" style=\"white-space:pre\">2.a) </span>SubC",
            "DOC rendered nested numbered marker");

        var noRestartList = numberedList with
        {
            ListId = 4322,
            Levels =
            [
                numberedList.Levels[0],
                numberedList.Levels[1] with
                {
                    NoRestart = true,
                    RestartLimit = 0
                }
            ]
        };
        var noRestartTables = new DocDocumentReader.DocListTables(
            new Dictionary<int, DocDocumentReader.DocListDefinition>
            {
                [4322] = noRestartList
            },
            [new DocDocumentReader.DocListOverrideDefinition(
                4322,
                new Dictionary<int, DocDocumentReader.DocListLevelOverride>())]);
        var noRestartBlocks = DocDocumentReader.BuildParagraphs(
            "One\rSubA\rTwo\rSubB\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(0, 4, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(4, 9, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 1 }),
                new DocDocumentReader.DocParagraphFormattingSpan(9, 13, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(13, 18, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 1 })
            ],
            noRestartTables);
        Assert(
            noRestartBlocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .SequenceEqual(["1. ", "1.a) ", "2. ", "2.b) "]),
            "DOC fNoRestart/ilvlRestartLim должны сохранять child sequence.");

        var romanList = new DocDocumentReader.DocListDefinition(
            4323,
            true,
            [
                new DocDocumentReader.DocListLevelDefinition(
                    1,
                    0x01,
                    1,
                    "\0.",
                    PlaceholderOffsets(1),
                    false,
                    false,
                    0)
            ]);
        var romanBlocks = DocDocumentReader.BuildParagraphs(
            "First\rSecond\r",
            [
                new DocDocumentReader.DocParagraphFormattingSpan(0, 6, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                new DocDocumentReader.DocParagraphFormattingSpan(6, 13, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 })
            ],
            new DocDocumentReader.DocListTables(
                new Dictionary<int, DocDocumentReader.DocListDefinition>
                {
                    [4323] = romanList
                },
                [new DocDocumentReader.DocListOverrideDefinition(
                    4323,
                    new Dictionary<int, DocDocumentReader.DocListLevelOverride>())]));
        Assert(
            romanBlocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .SequenceEqual(["I. ", "II. "]),
            "DOC upper-Roman list numbering должен форматироваться точно.");

        static string?[] BuildSimpleListMarkers(
            byte formatCode,
            string numberText,
            byte followCharacter,
            int startAt = 1)
        {
            const int listId = 5000;
            var definition = new DocDocumentReader.DocListDefinition(
                listId,
                true,
                [
                    new DocDocumentReader.DocListLevelDefinition(
                        startAt,
                        formatCode,
                        followCharacter,
                        numberText,
                        PlaceholderOffsets(1),
                        false,
                        false,
                        0)
                ]);
            var tables = new DocDocumentReader.DocListTables(
                new Dictionary<int, DocDocumentReader.DocListDefinition>
                {
                    [listId] = definition
                },
                [new DocDocumentReader.DocListOverrideDefinition(
                    listId,
                    new Dictionary<int, DocDocumentReader.DocListLevelOverride>())]);
            var blocks = DocDocumentReader.BuildParagraphs(
                "A\rB\r",
                [
                    new DocDocumentReader.DocParagraphFormattingSpan(0, 2, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 }),
                    new DocDocumentReader.DocParagraphFormattingSpan(2, 4, new DocDocumentReader.DocParagraphFormatting { ListFormatOverrideRaw = 1, ListLevelRaw = 0 })
                ],
                tables);

            return blocks.Cast<DocumentParagraph>()
                .Select(paragraph => paragraph.ListMarkerText)
                .ToArray();
        }

        Assert(
            BuildSimpleListMarkers(0x0F, "\0.", 1)
                .SequenceEqual(["1. ", "2. "]),
            "DOC decimalHalfWidth numbering должен использовать single-byte decimal digits.");
        Assert(
            BuildSimpleListMarkers(0x28, "\0.", 1)
                .SequenceEqual(["1. ", "2. "]),
            "DOC MSONFC 0x28 должен использовать decimal numbering.");
        Assert(
            BuildSimpleListMarkers(0x39, "\0", 2)
                .SequenceEqual(["- 1 -", "- 2 -"]),
            "DOC numberInDash должен выводить число с пробелами между двумя тире.");
        Assert(
            BuildSimpleListMarkers(0xFF, "\0", 1)
                .All(marker => marker == null),
            "DOC MSONFC none не должен создавать видимый list marker.");
        Assert(
            BuildSimpleListMarkers(0x00, "\0", 2, 0)
                .SequenceEqual(["0", "1"]),
            "DOC decimal numbering должен поддерживать iStartAt=0.");
        Assert(
            BuildSimpleListMarkers(0x08, "\0", 2, 10)
                .SequenceEqual(["A", "B"]),
            "DOC hex numbering должен использовать uppercase hexadecimal digits.");
        Assert(
            BuildSimpleListMarkers(0x0E, "\0", 2, 12)
                .SequenceEqual(["１２", "１３"]),
            "DOC decimalFullWidth должен переводить каждую decimal digit в full-width Unicode block.");
        Assert(
            BuildSimpleListMarkers(0x16, "\0", 2)
                .SequenceEqual(["01", "02"]),
            "DOC decimalZero должен добавлять leading zero для однозначных чисел.");
        Assert(
            BuildSimpleListMarkers(0x16, "\0", 2, 10)
                .SequenceEqual(["10", "11"]),
            "DOC decimalZero не должен добавлять лишний zero к двузначным числам.");
        Assert(
            BuildSimpleListMarkers(0x12, "\0", 2)
                .SequenceEqual(["①", "②"]),
            "DOC decimalEnclosedCircle должен использовать Unicode circled digits.");
        Assert(
            BuildSimpleListMarkers(0x1A, "\0", 2)
                .SequenceEqual(["⒈", "⒉"]),
            "DOC decimalEnclosedFullstop должен использовать Unicode digit-full-stop sequence.");
        Assert(
            BuildSimpleListMarkers(0x1B, "\0", 2)
                .SequenceEqual(["⑴", "⑵"]),
            "DOC decimalEnclosedParen должен использовать Unicode parenthesized digits.");
        Assert(
            BuildSimpleListMarkers(0x33, "\0", 2, 12)
                .SequenceEqual(["१२", "१३"]),
            "DOC hindiNumbers должен использовать Devanagari decimal digits.");
        Assert(
            BuildSimpleListMarkers(0x36, "\0", 2, 12)
                .SequenceEqual(["๑๒", "๑๓"]),
            "DOC thaiNumbers должен использовать Thai decimal digits.");
        Assert(
            BuildSimpleListMarkers(0x0C, "\0", 2)
                .SequenceEqual(["ｱ", "ｲ"]),
            "DOC aiueo должен использовать half-width Katakana sequence.");
        Assert(
            BuildSimpleListMarkers(0x0C, "\0", 2, 46)
                .SequenceEqual(["ﾝ", "ｱ"]),
            "DOC aiueo должен restart sequence после 46 символов.");
        Assert(
            BuildSimpleListMarkers(0x14, "\0", 2)
                .SequenceEqual(["ア", "イ"]),
            "DOC aiueoFullWidth должен использовать full-width Katakana sequence.");
        Assert(
            BuildSimpleListMarkers(0x14, "\0", 2, 46)
                .SequenceEqual(["ン", "ア"]),
            "DOC aiueoFullWidth должен restart sequence после 46 символов.");
        Assert(
            BuildSimpleListMarkers(0x18, "\0", 2)
                .SequenceEqual(["가", "나"]),
            "DOC ganada должен использовать Korean Ganada sequence.");
        Assert(
            BuildSimpleListMarkers(0x18, "\0", 2, 14)
                .SequenceEqual(["하", "가"]),
            "DOC ganada должен restart sequence после 14 символов.");
        Assert(
            BuildSimpleListMarkers(0x19, "\0", 2)
                .SequenceEqual(["ㄱ", "ㄴ"]),
            "DOC chosung должен использовать Korean Chosung sequence.");
        Assert(
            BuildSimpleListMarkers(0x19, "\0", 2, 14)
                .SequenceEqual(["ㅎ", "ㄱ"]),
            "DOC chosung должен restart sequence после 14 символов.");
        Assert(
            BuildSimpleListMarkers(0x08, "\0", 2, 65535)
                .SequenceEqual(["FFFF", null]),
            "DOC hex должен прекращать вывод после Word-limit 65535.");
        Assert(
            BuildSimpleListMarkers(0x1C, "\0", 2, 10)
                .SequenceEqual(["⑩", "11"]),
            "DOC decimalEnclosedCircleChinese должен после 10 переходить на decimal.");
        Assert(
            BuildSimpleListMarkers(0x3A, "\0", 2)
                .SequenceEqual(["а", "б"]),
            "DOC russianLower должен использовать последовательный Cyrillic Unicode range без ё.");
        Assert(
            BuildSimpleListMarkers(0x3A, "\0", 2, 32)
                .SequenceEqual(["я", null]),
            "DOC russianLower не должен синтезировать неподтверждённый rollover после 32 символов.");
        Assert(
            BuildSimpleListMarkers(0x3B, "\0", 2)
                .SequenceEqual(["А", "Б"]),
            "DOC russianUpper должен использовать последовательный Cyrillic Unicode range без Ё.");
        Assert(
            BuildSimpleListMarkers(0x3B, "\0", 2, 32)
                .SequenceEqual(["Я", null]),
            "DOC russianUpper не должен синтезировать неподтверждённый rollover после 32 символов.");
        Assert(
            BuildSimpleListMarkers(0x0D, "\0", 2)
                .SequenceEqual(["ｲ", "ﾛ"]),
            "DOC iroha должен использовать документированный half-width Iroha sequence.");
        Assert(
            BuildSimpleListMarkers(0x0D, "\0", 2, 24)
                .SequenceEqual(["ｳ", "ヰ"]),
            "DOC iroha должен сохранять full-width archaic Wi внутри half-width sequence.");
        Assert(
            BuildSimpleListMarkers(0x0D, "\0", 2, 48)
                .SequenceEqual(["ﾝ", "ｲ"]),
            "DOC iroha должен повторять 48-символьный sequence после ン.");
        Assert(
            BuildSimpleListMarkers(0x15, "\0", 2)
                .SequenceEqual(["イ", "ロ"]),
            "DOC irohaFullWidth должен начинаться с И-Ро-Ха sequence.");
        Assert(
            BuildSimpleListMarkers(0x15, "\0", 2, 48)
                .SequenceEqual(["ン", "イ"]),
            "DOC irohaFullWidth должен повторять документированный 48-символьный sequence.");
        Assert(
            BuildSimpleListMarkers(0x2D, "\0", 2)
                .SequenceEqual(["א", "ב"]),
            "DOC hebrew1 должен начинаться с Hebrew numerals 1, 2.");
        Assert(
            BuildSimpleListMarkers(0x2D, "\0", 2, 10)
                .SequenceEqual(["י", "יא"]),
            "DOC hebrew1 должен использовать additive tens.");
        Assert(
            BuildSimpleListMarkers(0x2D, "\0", 2, 15)
                .SequenceEqual(["טו", "טז"]),
            "DOC hebrew1 должен использовать традиционные формы 15 и 16.");
        Assert(
            BuildSimpleListMarkers(0x2D, "\0", 2, 123)
                .SequenceEqual(["קכג", "קכד"]),
            "DOC hebrew1 должен форматировать 123/124 как קכג/קכד.");
        Assert(
            BuildSimpleListMarkers(0x2D, "\0", 2, 399)
                .SequenceEqual(["שצט", null]),
            "DOC hebrew1 должен ограничиваться стандартным диапазоном до 399.");
        Assert(
            BuildSimpleListMarkers(0x2F, "\0", 2)
                .SequenceEqual(["\u200Fא", "\u200Fב"]),
            "DOC hebrew2 должен добавлять Word RTL marker перед Hebrew alphabet symbol.");
        Assert(
            BuildSimpleListMarkers(0x2F, "\0", 2, 22)
                .SequenceEqual(["\u200Fת", null]),
            "DOC hebrew2 должен ограничиваться документированным набором из 22 букв.");
        Assert(
            BuildSimpleListMarkers(0x2E, "\0", 2)
                .SequenceEqual(["أ\u200C", "ب\u200C"]),
            "DOC arabicAlpha должен добавлять Word ZWNJ после sequence number.");
        Assert(
            BuildSimpleListMarkers(0x2E, "\0", 2, 29)
                .SequenceEqual(["أأ\u200C", "بب\u200C"]),
            "DOC arabicAlpha должен повторять выбранный символ после 28 значений.");
        Assert(
            BuildSimpleListMarkers(0x30, "\0", 2)
                .SequenceEqual(["\u200Cأ", "\u200Cب"]),
            "DOC arabicAbjad должен добавлять Word ZWNJ перед sequence number.");
        Assert(
            BuildSimpleListMarkers(0x30, "\0", 2, 29)
                .SequenceEqual(["\u200Cأأ", "\u200Cبب"]),
            "DOC arabicAbjad должен повторять выбранный символ после 28 значений.");
        Assert(
            BuildSimpleListMarkers(0x31, "\0", 2)
                .SequenceEqual(["क", "ख"]),
            "DOC hindiVowels должен следовать Word-compatible consonant Unicode sequence.");
        Assert(
            BuildSimpleListMarkers(0x31, "\0", 2, 37)
                .SequenceEqual(["ह", null]),
            "DOC hindiVowels не должен синтезировать rollover после U+0939.");
        Assert(
            BuildSimpleListMarkers(0x32, "\0", 2)
                .SequenceEqual(["अ", "आ"]),
            "DOC hindiConsonants должен следовать Word-compatible vowel Unicode sequence.");
        Assert(
            BuildSimpleListMarkers(0x32, "\0", 2, 16)
                .SequenceEqual(["औ", null]),
            "DOC hindiConsonants не должен синтезировать rollover после U+0914.");
        Assert(
            BuildSimpleListMarkers(0x35, "\0", 2)
                .SequenceEqual(["ก", "ข"]),
            "DOC thaiLetters должен использовать Thai consonant sequence без устаревших letters.");
        Assert(
            BuildSimpleListMarkers(0x35, "\0", 2, 42)
                .SequenceEqual(["ฮ", null]),
            "DOC thaiLetters не должен синтезировать неподтверждённый rollover после 42 letters.");
        Assert(
            BuildSimpleListMarkers(0x1E, "\0", 2)
                .SequenceEqual(["甲", "乙"]),
            "DOC ideographTraditional должен использовать heavenly-stem sequence.");
        Assert(
            BuildSimpleListMarkers(0x1E, "\0", 2, 10)
                .SequenceEqual(["癸", null]),
            "DOC ideographTraditional не должен синтезировать rollover после 10 stems.");
        Assert(
            BuildSimpleListMarkers(0x1F, "\0", 2)
                .SequenceEqual(["子", "丑"]),
            "DOC ideographZodiac должен использовать earthly-branch sequence.");
        Assert(
            BuildSimpleListMarkers(0x1F, "\0", 2, 12)
                .SequenceEqual(["亥", null]),
            "DOC ideographZodiac не должен синтезировать rollover после 12 branches.");
        Assert(
            BuildSimpleListMarkers(0x20, "\0", 2)
                .SequenceEqual(["甲子", "乙丑"]),
            "DOC ideographZodiacTraditional должен сочетать heavenly stems и earthly branches.");
        Assert(
            BuildSimpleListMarkers(0x20, "\0", 2, 60)
                .SequenceEqual(["癸亥", null]),
            "DOC ideographZodiacTraditional не должен синтезировать rollover после 60 combinations.");
        Assert(
            BuildSimpleListMarkers(0x1D, "\0", 2)
                .SequenceEqual(["㈠", "㈡"]),
            "DOC ideographEnclosedCircle должен использовать Word-compatible parenthesized ideographs.");
        Assert(
            BuildSimpleListMarkers(0x1D, "\0", 2, 10)
                .SequenceEqual(["㈩", null]),
            "DOC ideographEnclosedCircle не должен синтезировать значения после Unicode parenthesized one-ten block.");
        Assert(
            BuildSimpleListMarkers(0x0A, "\0", 2, 10)
                .SequenceEqual(["一〇", "一一"]),
            "DOC ideographDigital должен переводить каждую decimal digit в ideograph digit set с U+3007 для zero.");
        Assert(
            BuildSimpleListMarkers(0x0A, "\0", 2, 101)
                .SequenceEqual(["一〇一", "一〇二"]),
            "DOC ideographDigital должен сохранять positional zeros без counting semantics.");
        Assert(
            BuildSimpleListMarkers(0x24, "\0", 2, 10)
                .SequenceEqual(["一○", "一一"]),
            "DOC taiwaneseDigital должен использовать U+25CB как positional zero.");
        Assert(
            BuildSimpleListMarkers(0x24, "\0", 2, 101)
                .SequenceEqual(["一○一", "一○二"]),
            "DOC taiwaneseDigital должен сохранять positional zeros без counting semantics.");
        Assert(
            BuildSimpleListMarkers(0x29, "\0", 2, 10)
                .SequenceEqual(["일영", "일일"]),
            "DOC koreanDigital должен переводить decimal digits в Hangul digit set.");
        Assert(
            BuildSimpleListMarkers(0x29, "\0", 2, 101)
                .SequenceEqual(["일영일", "일영이"]),
            "DOC koreanDigital должен сохранять positional zeros без counting semantics.");
        Assert(
            BuildSimpleListMarkers(0x2C, "\0", 2, 101)
                .SequenceEqual(["一零一", "一零二"]),
            "DOC koreanDigital2 должен использовать ideograph digits с U+96F6 как zero.");
        Assert(
            BuildSimpleListMarkers(0x2C, "\0", 2, 999999)
                .SequenceEqual(["九九九九九九", null]),
            "DOC koreanDigital2 должен прекращать вывод после Word-limit 999999.");
        Assert(
            BuildSimpleListMarkers(0x09, "\0", 2)
                .SequenceEqual(["*", "†"]),
            "DOC chicago должен начинаться с *, dagger.");
        Assert(
            BuildSimpleListMarkers(0x09, "\0", 2, 5)
                .SequenceEqual(["**", "††"]),
            "DOC chicago должен повторять выбранный symbol после полного цикла.");
        Assert(
            BuildSimpleListMarkers(0x09, "\0", 2, 116)
                .SequenceEqual([new string('§', 29), null]),
            "DOC chicago должен прекращать вывод после Word-limit 116.");
        Assert(
            BuildSimpleListMarkers(0x0B, "\0", 2, 10)
                .SequenceEqual(["十", "十一"]),
            "DOC japaneseCounting должен использовать Japanese ten-unit semantics.");
        Assert(
            BuildSimpleListMarkers(0x0B, "\0", 2, 105)
                .SequenceEqual(["百五", "百六"]),
            "DOC japaneseCounting должен опускать zero groups.");
        Assert(
            BuildSimpleListMarkers(0x0B, "\0", 2, 10005)
                .SequenceEqual(["一万五", "一万六"]),
            "DOC japaneseCounting должен сохранять 一 перед 万.");
        Assert(
            BuildSimpleListMarkers(0x0B, "\0", 2, 999999)
                .SequenceEqual(["九十九万九千九百九十九", null]),
            "DOC japaneseCounting должен прекращать вывод после Word-limit 999999.");
        Assert(
            BuildSimpleListMarkers(0x11, "\0", 2, 101)
                .SequenceEqual(["一〇一", "一〇二"]),
            "DOC japaneseDigitalTenThousand должен быть positional digital format.");
        Assert(
            BuildSimpleListMarkers(0x11, "\0", 2, 9999)
                .SequenceEqual(["九九九九", null]),
            "DOC japaneseDigitalTenThousand должен прекращать вывод после Word-limit 9999.");
        Assert(
            BuildSimpleListMarkers(0x13, "\0", 2, 12)
                .SequenceEqual(["１２", "１３"]),
            "DOC decimalFullWidth2 должен использовать full-width decimal digits.");

        Assert(
            formatting.KeepTogether == true &&
            formatting.KeepWithNext == true &&
            formatting.WidowControl == true &&
            formatting.RightToLeft == true &&
            formatting.OutlineLevel == 3 &&
            formatting.ListFormatOverrideRaw == -3 &&
            formatting.ListLevelRaw == 2 &&
            formatting.PageBreakBefore == true &&
            formatting.Shading is
            {
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFF00",
                Pattern: 1
            } &&
            formatting.BorderTop is { Type: 0x01, WidthPoints: 1, Color: "#FF0000" } &&
            formatting.BorderLeft is { Type: 0x06, WidthPoints: 2, Color: "#008000" } &&
            formatting.BorderBottom is { Type: 0x03, WidthPoints: 3, Color: "#0000FF" } &&
            formatting.BorderRight is { Type: 0x07, WidthPoints: 1.5, Color: "#800080" } &&
            formatting.BorderBetween is { Type: 0x01, WidthPoints: 1, Color: "#008080" } &&
            formatting.BorderBar is { Type: 0x01, WidthPoints: 1, Color: "#808000" } &&
            formatting.ParagraphGroupId == 1234 &&
            formatting.TabStops.Count == 3 &&
            formatting.TabStops[0].PositionPoints == 72 &&
            formatting.TabStops[0].Alignment == DocumentTabAlignment.Right &&
            formatting.TabStops[0].Leader == DocumentTabLeader.Dots &&
            formatting.TabStops[1].PositionPoints == 144 &&
            formatting.TabStops[1].Alignment == DocumentTabAlignment.Center &&
            formatting.TabStops[1].Leader == DocumentTabLeader.Hyphens &&
            formatting.TabStops[2].PositionPoints == 216 &&
            formatting.TabStops[2].Alignment == DocumentTabAlignment.Decimal &&
            formatting.TabStops[2].Leader == DocumentTabLeader.Underline &&
            formatting.DeletedTabStopPositionsPoints.SequenceEqual([36.0]),
            "DOC PAPX keep/keep-next/widow/bidi/outline/page-break и custom tabs должны сохраняться.");

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
                        ListFormatOverrideIndex = 3,
                        ListLevel = 2,
                        ListPreserveIndents = true,
                        KeepTogether =
                            formatting.KeepTogether ?? false,
                        KeepWithNext =
                            formatting.KeepWithNext ?? false,
                        WidowControl =
                            formatting.WidowControl ?? false,
                        RightToLeft =
                            formatting.RightToLeft ?? false,
                        Shading = formatting.Shading,
                        BorderTop = formatting.BorderTop,
                        BorderLeft = formatting.BorderLeft,
                        BorderBottom = formatting.BorderBottom,
                        BorderRight = formatting.BorderRight,
                        BorderBetween = formatting.BorderBetween,
                        BorderBar = formatting.BorderBar,
                        TableDepth = 2,
                        ParagraphGroupId = formatting.ParagraphGroupId,
                        TabStops = formatting.TabStops
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
            "data-list-format-override=\"3\" data-list-level=\"2\" data-list-preserve-indents=\"true\"",
            "DOC rendered list membership metadata");
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
        AssertContains(
            html,
            "data-shading-pattern=\"1\" data-shading-foreground=\"#FF0000\" data-shading-background=\"#FFFF00\"",
            "DOC rendered paragraph shading metadata");
        AssertContains(
            html,
            "background-color:#FF0000",
            "DOC rendered solid paragraph shading");
        AssertContains(
            html,
            "border-top:1pt solid #FF0000",
            "DOC rendered paragraph top border");
        AssertContains(
            html,
            "border-left:2pt dotted #008000",
            "DOC rendered paragraph left border");
        AssertContains(
            html,
            "border-bottom:3pt double #0000FF",
            "DOC rendered paragraph bottom border");
        AssertContains(
            html,
            "border-right:1.5pt dashed #800080",
            "DOC rendered paragraph right border");
        AssertContains(
            html,
            "data-border-between-type=\"1\" data-border-between-width-pt=\"1\" data-border-between-color=\"#008080\"",
            "DOC paragraph between-border metadata");
        AssertContains(
            html,
            "data-border-bar-type=\"1\" data-border-bar-width-pt=\"1\" data-border-bar-color=\"#808000\"",
            "DOC paragraph bar-border metadata");
        AssertContains(
            html,
            "data-table-depth=\"2\" data-paragraph-group-id=\"1234\"",
            "DOC paragraph adjacency metadata");
        AssertContains(
            html,
            "data-tab-stops=\"72:right:dots;144:center:hyphens;216:decimal:underline\"",
            "DOC rendered custom tab metadata");

        var patternedShadingHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = string.Empty,
                Blocks =
                [
                    new DocumentParagraph
                    {
                        Inlines =
                        [
                            new DocumentInline
                            {
                                Text = "Fifty percent"
                            }
                        ],
                        Shading =
                            new DocumentShading
                            {
                                ForegroundColor = "#FF0000",
                                BackgroundColor = "#0000FF",
                                Pattern = 0x0008
                            }
                    },
                    new DocumentParagraph
                    {
                        Inlines =
                        [
                            new DocumentInline
                            {
                                Text = "Diagonal cross"
                            }
                        ],
                        Shading =
                            new DocumentShading
                            {
                                ForegroundColor = "#00FF00",
                                BackgroundColor = "#FFFFFF",
                                Pattern = 0x0013
                            }
                    },
                    new DocumentTable
                    {
                        Rows =
                        [
                            new DocumentTableRow
                            {
                                Cells =
                                [
                                    new DocumentTableCell
                                    {
                                        Inlines =
                                        [
                                            new DocumentInline
                                            {
                                                Text = "Thin horizontal"
                                            }
                                        ],
                                        Shading =
                                            new DocumentShading
                                            {
                                                ForegroundColor = "#123456",
                                                BackgroundColor = "#ABCDEF",
                                                Pattern = 0x0014
                                            }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            },
            CreateRenderTheme());

        AssertContains(
            patternedShadingHtml,
            "background-color:#0000FF;background-image:linear-gradient(rgba(255,0,0,0.5),rgba(255,0,0,0.5))",
            "DOC renderer должен визуализировать percentage Ipat shading.");
        AssertContains(
            patternedShadingHtml,
            "background-color:#FFFFFF;background-image:repeating-linear-gradient(45deg,#00FF00 0 2px,transparent 2px 4px),repeating-linear-gradient(-45deg,#00FF00 0 2px,transparent 2px 4px)",
            "DOC renderer должен визуализировать diagonal-cross Ipat shading.");
        AssertContains(
            patternedShadingHtml,
            "background-color:#ABCDEF;background-image:repeating-linear-gradient(0deg,#123456 0 1px,transparent 1px 5px)",
            "DOC table cell renderer должен визуализировать thin horizontal Ipat shading.");

        DocumentParagraph CreateBorderParagraph(
            string text,
            uint paragraphGroupId)
        {
            return new DocumentParagraph
            {
                Inlines =
                [
                    new DocumentInline
                    {
                        Text = text
                    }
                ],
                LeftIndentPoints = 12,
                RightIndentPoints = 18,
                BorderTop = formatting.BorderTop,
                BorderLeft = formatting.BorderLeft,
                BorderBottom = formatting.BorderBottom,
                BorderRight = formatting.BorderRight,
                BorderBetween = formatting.BorderBetween,
                TableDepth = 1,
                ParagraphGroupId = paragraphGroupId
            };
        }

        var adjacencyHtml = DocumentHtmlRenderer.Render(
            new DocumentModel
            {
                Title = string.Empty,
                Blocks =
                [
                    CreateBorderParagraph("Adjacent first", 42),
                    CreateBorderParagraph("Adjacent second", 42),
                    CreateBorderParagraph("Different group", 43)
                ]
            },
            CreateRenderTheme());

        static string ExtractParagraphHtml(
            string source,
            string text)
        {
            var textIndex = source.IndexOf(text, StringComparison.Ordinal);
            Assert(textIndex >= 0, $"Paragraph text not found: {text}");
            var start = source.LastIndexOf("<p", textIndex, StringComparison.Ordinal);
            var end = source.IndexOf("</p>", textIndex, StringComparison.Ordinal);
            Assert(start >= 0 && end >= start, $"Paragraph HTML not found: {text}");
            return source[start..(end + 4)];
        }

        var firstAdjacentHtml = ExtractParagraphHtml(adjacencyHtml, "Adjacent first");
        var secondAdjacentHtml = ExtractParagraphHtml(adjacencyHtml, "Adjacent second");
        var differentGroupHtml = ExtractParagraphHtml(adjacencyHtml, "Different group");

        AssertContains(
            firstAdjacentHtml,
            "border-top:1pt solid #FF0000",
            "DOC first paragraph keeps outer top border");
        AssertContains(
            firstAdjacentHtml,
            "border-bottom:1pt solid #008080",
            "DOC adjacent paragraphs render border-between instead of bottom border");
        Assert(
            !firstAdjacentHtml.Contains("border-bottom:3pt double #0000FF", StringComparison.Ordinal),
            "DOC adjacent first paragraph должен скрывать обычный bottom border.");
        Assert(
            !secondAdjacentHtml.Contains("border-top:1pt solid #FF0000", StringComparison.Ordinal),
            "DOC adjacent second paragraph должен скрывать внутренний top border.");
        AssertContains(
            secondAdjacentHtml,
            "border-bottom:3pt double #0000FF",
            "DOC differing next sprmPIpgp restores ordinary bottom border");
        AssertContains(
            differentGroupHtml,
            "border-top:1pt solid #FF0000",
            "DOC differing sprmPIpgp restores next paragraph top border");
    }

    private static void TestDocCharacterStyle()
    {
        var direct =
            DocDocumentReader.ReadCharacterGrpprl(
                new byte[]
                {
                    0x30, 0x4A,
                    0x05, 0x00,
                    0x36, 0x08,
                    0x01
                });

        Assert(
            direct.CharacterStyleIndex == 5 &&
            direct.Italic == true,
            "DOC sprmCIstd должен сохранять character style index рядом с direct CHPX.");

        var paragraphStyleFormatting =
            new DocDocumentReader.DocCharacterFormatting
            {
                Bold = false,
                Italic = false,
                Underline = false,
                Superscript = false,
                Subscript = false,
                FontSizePoints = 10,
                TextColor = "#000000"
            };
        var characterStyleFormatting =
            new DocDocumentReader.DocCharacterFormatting
            {
                Bold = true,
                Italic = false,
                FontSizePoints = 14,
                TextColor = "#336699"
            };

        var characterStyle =
            new DocDocumentReader.DocStyle(
                StyleIndex: 5,
                Sti: 0,
                Kind: 2,
                BaseStyleIndex: 0x0FFF,
                Name: "Emphasis",
                ParagraphFormatting:
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        StyleIndex = 5
                    },
                CharacterFormatting:
                    characterStyleFormatting,
                HeadingLevel: 0);

        var styles =
            new Dictionary<ushort, DocDocumentReader.DocStyle>
            {
                [5] = characterStyle
            };

        var resolved =
            DocDocumentReader.ResolveCharacterFormatting(
                paragraphStyleFormatting,
                direct,
                styles);

        Assert(
            resolved.Bold == true &&
            resolved.Italic == true &&
            resolved.FontSizePoints == 14 &&
            resolved.TextColor == "#336699",
            "DOC character style должен применяться между paragraph-style formatting и direct CHPX.");

        var plainReset =
            DocDocumentReader.ReadCharacterGrpprl(
                new byte[]
                {
                    0x30, 0x4A, 0x05, 0x00,
                    0x35, 0x08, 0x01,
                    0x03, 0x6A, 0x78, 0x56, 0x34, 0x12,
                    0x33, 0x2A, 0x00,
                    0x36, 0x08, 0x01
                });
        var plainResolved =
            DocDocumentReader.ResolveCharacterFormatting(
                paragraphStyleFormatting,
                plainReset,
                styles);

        Assert(
            plainReset.CharacterStyleIndex == null &&
            plainReset.PictureLocation == 0x12345678 &&
            plainResolved.Bold == false &&
            plainResolved.Italic == true &&
            plainResolved.FontSizePoints == 10 &&
            plainResolved.TextColor == "#000000",
            "DOC sprmCPlain должен сбрасывать character style/direct properties к paragraph style, сохраняя picture location и последующие direct SPRM.");

        static byte[] BuildMajorityGrpprl(
            ushort? characterStyleIndex)
        {
            var bytes = new List<byte>();
            if (characterStyleIndex is { } styleIndex)
            {
                bytes.Add(0x30);
                bytes.Add(0x4A);
                bytes.AddRange(BitConverter.GetBytes(styleIndex));
            }

            bytes.AddRange(new byte[] { 0x35, 0x08, 0x01 });
            bytes.AddRange(new byte[] { 0x3E, 0x2A, 0x01 });
            bytes.AddRange(new byte[] { 0x48, 0x2A, 0x01 });
            bytes.AddRange(new byte[] { 0x70, 0x68, 0xFF, 0x00, 0x00, 0x00 });

            var criteria = new byte[]
            {
                0x3E, 0x2A, 0x01,
                0x48, 0x2A, 0x01,
                0x70, 0x68, 0xFF, 0x00, 0x00, 0x00
            };
            bytes.Add(0x47);
            bytes.Add(0xCA);
            bytes.Add(checked((byte)criteria.Length));
            bytes.AddRange(criteria);
            return bytes.ToArray();
        }

        var majorityDirect =
            DocDocumentReader.ReadCharacterGrpprl(
                BuildMajorityGrpprl(null));
        var majorityResolved =
            DocDocumentReader.ResolveCharacterFormatting(
                paragraphStyleFormatting,
                majorityDirect,
                styles);

        Assert(
            majorityResolved.Bold == true &&
            majorityResolved.Underline == false &&
            majorityResolved.Superscript == false &&
            majorityResolved.Subscript == false &&
            majorityResolved.TextColor == "#000000",
            "DOC sprmCMajority должен reset-ить совпавшие underline/script/color к paragraph style, не затрагивая bold.");

        var majorityStyledDirect =
            DocDocumentReader.ReadCharacterGrpprl(
                BuildMajorityGrpprl(5));
        var majorityStyledResolved =
            DocDocumentReader.ResolveCharacterFormatting(
                paragraphStyleFormatting,
                majorityStyledDirect,
                styles);

        Assert(
            majorityStyledResolved.Underline == true &&
            majorityStyledResolved.Superscript == true &&
            majorityStyledResolved.Subscript == false &&
            majorityStyledResolved.TextColor == "#FF0000",
            "DOC sprmCMajority должен игнорироваться при non-default character style.");

        static byte[] BuildFfn(
            string name,
            string? alternateName,
            byte charset,
            byte family,
            short weight)
        {
            var nameData = Encoding.Unicode.GetBytes(
                alternateName == null
                    ? name + "\0"
                    : name + "\0" + alternateName + "\0");
            var ffn = new byte[39 + nameData.Length];
            ffn[0] = family;
            BinaryPrimitives.WriteInt16LittleEndian(ffn.AsSpan(1, 2), weight);
            ffn[3] = charset;
            ffn[4] = alternateName == null
                ? (byte)0
                : checked((byte)(name.Length + 1));
            nameData.CopyTo(ffn, 39);
            return ffn;
        }

        var ffnEntries = new[]
        {
            BuildFfn("Arial", "Arial Alt", 0, 2, 400),
            BuildFfn("MS Mincho", null, 128, 3, 400),
            BuildFfn("Times New Roman", null, 0, 1, 400),
            BuildFfn("Traditional Arabic", null, 178, 2, 400)
        };
        var fontTableLength = 4 + ffnEntries.Sum(entry => 1 + entry.Length);
        var fontTableBytes = new byte[fontTableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(fontTableBytes.AsSpan(0, 2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(fontTableBytes.AsSpan(2, 2), 0);
        var fontTableOffset = 4;
        foreach (var entry in ffnEntries)
        {
            fontTableBytes[fontTableOffset++] = checked((byte)entry.Length);
            entry.CopyTo(fontTableBytes, fontTableOffset);
            fontTableOffset += entry.Length;
        }

        var fonts = DocDocumentReader.ReadFontTable(fontTableBytes);
        Assert(
            fonts.Count == 4 &&
            fonts[0].Name == "Arial" &&
            fonts[0].AlternateName == "Arial Alt" &&
            fonts[0].Charset == 0 &&
            fonts[0].Family == 2 &&
            fonts[0].Weight == 400 &&
            fonts[1].Name == "MS Mincho" &&
            fonts[1].Charset == 128 &&
            fonts[3].Charset == 178,
            "DOC SttbfFfn должен сохранять FFN font names, alternate names, charset, family и weight.");

        var fontFormatting = DocDocumentReader.ReadCharacterGrpprl(
            new byte[]
            {
                0x4F, 0x4A, 0x00, 0x00,
                0x50, 0x4A, 0x01, 0x00,
                0x51, 0x4A, 0x02, 0x00,
                0x5E, 0x4A, 0x03, 0x00,
                0x6F, 0x28, 0x01
            });

        Assert(
            fontFormatting.FontAsciiIndex == 0 &&
            fontFormatting.FontEastAsiaIndex == 1 &&
            fontFormatting.FontOtherIndex == 2 &&
            fontFormatting.FontBiIndex == 3 &&
            fontFormatting.FontHint == 1,
            "DOC CHPX должен сохранять ftc0/ftc1/ftc2/ftcBi и sprmCIdctHint.");
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                fontFormatting,
                fonts,
                DocDocumentReader.DocFontSlot.Ascii) == "Arial",
            "DOC ASCII font mapping должен разрешать ftc0 через SttbfFfn.");
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                fontFormatting,
                fonts,
                DocDocumentReader.DocFontSlot.EastAsia) == "MS Mincho",
            "DOC East Asia font mapping должен разрешать ftc1 через SttbfFfn.");
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                fontFormatting,
                fonts,
                DocDocumentReader.DocFontSlot.Other) == "Times New Roman",
            "DOC Other Unicode font mapping должен разрешать ftc2 через SttbfFfn.");
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                fontFormatting,
                fonts,
                DocDocumentReader.DocFontSlot.Bi) == "Traditional Arabic",
            "DOC complex-script font mapping должен разрешать ftcBi через SttbfFfn.");

        var stsh = new byte[22];
        BinaryPrimitives.WriteUInt16LittleEndian(stsh.AsSpan(0, 2), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(stsh.AsSpan(2, 2), 15);
        BinaryPrimitives.WriteUInt16LittleEndian(stsh.AsSpan(4, 2), 10);
        BinaryPrimitives.WriteInt16LittleEndian(stsh.AsSpan(14, 2), 0);
        BinaryPrimitives.WriteInt16LittleEndian(stsh.AsSpan(16, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(stsh.AsSpan(18, 2), 2);
        BinaryPrimitives.WriteInt16LittleEndian(stsh.AsSpan(20, 2), 3);
        var defaults = DocDocumentReader.ReadDefaultFontIndices(stsh);
        Assert(
            defaults.Ascii == 0 &&
            defaults.EastAsia == 1 &&
            defaults.Other == 2 &&
            defaults.Bi == 3,
            "DOC STSHI.Stshif должен читать ftcAsci/ftcFE/ftcOther/ftcBi defaults.");

        var noDirectFonts = new DocDocumentReader.DocCharacterFormatting();
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                noDirectFonts,
                fonts,
                DocDocumentReader.DocFontSlot.Ascii,
                defaults) == "Arial" &&
            DocDocumentReader.ResolveDocFontFamily(
                noDirectFonts,
                fonts,
                DocDocumentReader.DocFontSlot.EastAsia,
                defaults) == "MS Mincho" &&
            DocDocumentReader.ResolveDocFontFamily(
                noDirectFonts,
                fonts,
                DocDocumentReader.DocFontSlot.Other,
                defaults) == "Times New Roman" &&
            DocDocumentReader.ResolveDocFontFamily(
                noDirectFonts,
                fonts,
                DocDocumentReader.DocFontSlot.Bi,
                defaults) == "Traditional Arabic",
            "DOC font resolution должен использовать STSHI defaults при отсутствии style/direct ftc.");

        var directOverride = new DocDocumentReader.DocCharacterFormatting
        {
            FontAsciiIndex = 2
        };
        Assert(
            DocDocumentReader.ResolveDocFontFamily(
                directOverride,
                fonts,
                DocDocumentReader.DocFontSlot.Ascii,
                defaults) == "Times New Roman",
            "DOC explicit CHPX font должен переопределять STSHI default.");

        var symbolFormatting = DocDocumentReader.ReadCharacterGrpprl(
            new byte[]
            {
                0x09, 0x6A,
                0x01, 0x00,
                0x05, 0x26
            });
        Assert(
            symbolFormatting.SymbolFontIndex == 1 &&
            symbolFormatting.SymbolCharacterCode == 0x2605,
            "DOC sprmCSymbol должен сохранять font-table index и Unicode xchar.");

        var symbolPlainReset = DocDocumentReader.ReadCharacterGrpprl(
            new byte[]
            {
                0x09, 0x6A, 0x01, 0x00, 0x05, 0x26,
                0x33, 0x2A, 0x00
            });
        Assert(
            symbolPlainReset.SymbolFontIndex == null &&
            symbolPlainReset.SymbolCharacterCode == null,
            "DOC sprmCPlain должен сбрасывать sprmCSymbol.");

        var fontPlainReset = DocDocumentReader.ReadCharacterGrpprl(
            new byte[]
            {
                0x4F, 0x4A, 0x02, 0x00,
                0x6F, 0x28, 0x01,
                0x33, 0x2A, 0x00
            });
        Assert(
            fontPlainReset.FontAsciiIndex == null &&
            fontPlainReset.FontHint == null,
            "DOC sprmCPlain должен сбрасывать direct font slots и hint.");

        var wrongKind =
            characterStyle with
            {
                Kind = 1
            };

        try
        {
            _ =
                DocDocumentReader.ResolveCharacterFormatting(
                    paragraphStyleFormatting,
                    direct,
                    new Dictionary<ushort, DocDocumentReader.DocStyle>
                    {
                        [5] = wrongKind
                    });

            throw new InvalidOperationException(
                "DOC sprmCIstd на non-character style должен быть отклонён.");
        }
        catch (InvalidDataException)
        {
        }
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

    private static void TestDocNestedTables()
    {
        var paragraphGrpprl =
            new List<byte>();

        void AddParagraphSprm(
            ushort sprm,
            ReadOnlySpan<byte> operand)
        {
            paragraphGrpprl.Add(
                (byte)(sprm & 0xFF));
            paragraphGrpprl.Add(
                (byte)(sprm >> 8));
            paragraphGrpprl.AddRange(
                operand.ToArray());
        }

        AddParagraphSprm(
            0x6649,
            new byte[]
            {
                1, 0, 0, 0
            });
        AddParagraphSprm(
            0x664A,
            new byte[]
            {
                1, 0, 0, 0
            });
        AddParagraphSprm(
            0x244B,
            new byte[]
            {
                1
            });
        AddParagraphSprm(
            0x244C,
            new byte[]
            {
                1
            });

        var nestedFormatting =
            new DocDocumentReader.DocParagraphFormatting();

        DocDocumentReader.ReadParagraphGrpprl(
            paragraphGrpprl.ToArray(),
            nestedFormatting);

        Assert(
            nestedFormatting.InTable == true &&
            nestedFormatting.TableDepth == 2 &&
            nestedFormatting.InnerTableCell == true &&
            nestedFormatting.InnerTableRowEnd == true,
            "DOC nested-table PAPX должен применять PItap/PDtap и inner cell/row flags.");

        var outerDefinition =
            new DocDocumentReader.DocTableDefinition(
                new[]
                {
                    new DocDocumentReader.DocTableCellDefinition(
                        LeftTwips: 0,
                        RightTwips: 3000,
                        WidthPoints: 150,
                        HorizontalMerge: 0,
                        VerticalMerge: 0)
                });
        var innerDefinition =
            new DocDocumentReader.DocTableDefinition(
                new[]
                {
                    new DocDocumentReader.DocTableCellDefinition(
                        LeftTwips: 0,
                        RightTwips: 1800,
                        WidthPoints: 90,
                        HorizontalMerge: 0,
                        VerticalMerge: 0)
                });

        var text =
            "before\r" +
            "inner\r" +
            "\r" +
            "after\u0007" +
            "\u0007";

        var spans =
            new[]
            {
                new DocDocumentReader.DocParagraphFormattingSpan(
                    0,
                    7,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        InTable = true,
                        TableDepth = 1
                    }),
                new DocDocumentReader.DocParagraphFormattingSpan(
                    7,
                    13,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        InTable = true,
                        TableDepth = 2,
                        InnerTableCell = true
                    }),
                new DocDocumentReader.DocParagraphFormattingSpan(
                    13,
                    14,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        InTable = true,
                        TableDepth = 2,
                        InnerTableRowEnd = true,
                        TableDefinition = innerDefinition
                    }),
                new DocDocumentReader.DocParagraphFormattingSpan(
                    14,
                    20,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        InTable = true,
                        TableDepth = 1
                    }),
                new DocDocumentReader.DocParagraphFormattingSpan(
                    20,
                    21,
                    new DocDocumentReader.DocParagraphFormatting
                    {
                        InTable = true,
                        TableDepth = 1,
                        TableRowEnd = true,
                        TableDefinition = outerDefinition
                    })
            };

        var blocks =
            DocDocumentReader.BuildParagraphs(
                text,
                spans);

        var outerTable =
            blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            outerTable.Rows.Count == 1 &&
            outerTable.Rows[0].Cells.Count == 1,
            "DOC nested-table fixture должен создавать одну outer cell.");

        var outerCell =
            outerTable.Rows[0].Cells[0];

        Assert(
            outerCell.Inlines.Count == 0 &&
            outerCell.Blocks.Count == 3,
            "DOC outer cell с nested table должен сохранять ordered block content.");

        Assert(
            outerCell.Blocks[0] is DocumentParagraph before &&
            before.Inlines.Any(inline =>
                inline.Text.Contains(
                    "before",
                    StringComparison.Ordinal)),
            "DOC nested table должен сохранять outer text до таблицы.");

        var innerTable =
            outerCell.Blocks[1] as DocumentTable
            ?? throw new InvalidOperationException(
                "DOC nested table должен быть отдельным block внутри outer cell.");

        Assert(
            innerTable.Rows.Count == 1 &&
            innerTable.Rows[0].Cells.Count == 1 &&
            innerTable.Rows[0].Cells[0].Inlines.Any(inline =>
                inline.Text.Contains(
                    "inner",
                    StringComparison.Ordinal)),
            "DOC nested table должен сохранять содержимое inner cell.");

        Assert(
            outerCell.Blocks[2] is DocumentParagraph after &&
            after.Inlines.Any(inline =>
                inline.Text.Contains(
                    "after",
                    StringComparison.Ordinal)),
            "DOC nested table должен сохранять outer text после таблицы.");

        var html =
            DocumentHtmlRenderer.Render(
                new DocumentModel
                {
                    Title = "DOC nested table",
                    Blocks =
                    [
                        outerTable
                    ]
                },
                CreateRenderTheme());

        Assert(
            html.IndexOf(
                "before",
                StringComparison.Ordinal) <
            html.IndexOf(
                "inner",
                StringComparison.Ordinal) &&
            html.IndexOf(
                "inner",
                StringComparison.Ordinal) <
            html.IndexOf(
                "after",
                StringComparison.Ordinal),
            "DOC nested table renderer должен сохранять порядок outer/inner/outer content.");

        Assert(
            html.Split(
                    "<table",
                    StringSplitOptions.None)
                .Length -
            1 ==
            2,
            "DOC nested table renderer должен создавать вложенный HTML table.");
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

    private static void TestPdfSaslPrep()
    {
        Assert(
            string.Equals(
                PdfSaslPrep.Prepare("I\u00ADX"),
                "IX",
                StringComparison.Ordinal) &&
            string.Equals(
                PdfSaslPrep.Prepare("\u00AA"),
                "a",
                StringComparison.Ordinal) &&
            string.Equals(
                PdfSaslPrep.Prepare("\u2168"),
                "IX",
                StringComparison.Ordinal),
            "PDF SASLprep должен проходить RFC 4013 mapping/NFKC examples.");

        Assert(
            string.Equals(
                PdfSaslPrep.Prepare("cafe\u0301"),
                "caf\u00E9",
                StringComparison.Ordinal) &&
            string.Equals(
                PdfSaslPrep.Prepare("\uFF21\uFF22"),
                "AB",
                StringComparison.Ordinal) &&
            string.Equals(
                PdfSaslPrep.Prepare("a\u00A0b"),
                "a b",
                StringComparison.Ordinal),
            "PDF SASLprep должен использовать Unicode 3.2 NFKC и non-ASCII space mapping.");

        var prohibitedRejected = false;
        try
        {
            PdfSaslPrep.Prepare("\u0007");
        }
        catch (ArgumentException)
        {
            prohibitedRejected = true;
        }

        var bidiRejected = false;
        try
        {
            PdfSaslPrep.Prepare("\u06271");
        }
        catch (ArgumentException)
        {
            bidiRejected = true;
        }

        Assert(
            prohibitedRejected &&
            bidiRejected &&
            string.Equals(
                PdfSaslPrep.Prepare("\u06271\u0628"),
                "\u06271\u0628",
                StringComparison.Ordinal),
            "PDF SASLprep должен применять prohibited и BiDi rules RFC 4013.");

        var truncated =
            PdfSaslPrep.PasswordBytes(
                new string('a', 126) +
                "\u00E9");

        Assert(
            truncated.Length == 127 &&
            truncated[126] == 0xC3,
            "PDF AES-256 password должен обрезаться после UTF-8 encoding ровно до 127 bytes.");

        var legacyPassword =
            PdfPasswordEncoding.EncodeLegacy(
                "A\u2022\u20AC\u00E9");

        Assert(
            legacyPassword.SequenceEqual(
                new byte[]
                {
                    0x41,
                    0x80,
                    0xA0,
                    0xE9
                }),
            "PDF legacy password должен кодироваться точным PDFDocEncoding.");

        var unsupportedLegacyCharacterRejected = false;
        try
        {
            PdfPasswordEncoding.EncodeLegacy(
                "\u0416");
        }
        catch (InvalidDataException)
        {
            unsupportedLegacyCharacterRejected = true;
        }

        Assert(
            unsupportedLegacyCharacterRejected,
            "PDF legacy password не должен эвристически заменять символы вне PDFDocEncoding.");
    }

    private static void TestPdfCieColorSpaces()
    {
        double[] d65 =
        [
            0.95047,
            1.0,
            1.08883
        ];

        var calGray =
            PdfImageColorConverter.ConvertCieToRgb(
                [255],
                [0.0, 1.0],
                new PdfCieColorSpace(
                    PdfCieColorSpaceKind.CalGray,
                    d65,
                    [1.0],
                    [
                        1.0, 0.0, 0.0,
                        0.0, 1.0, 0.0,
                        0.0, 0.0, 1.0
                    ],
                    [0.0, 1.0]));

        Assert(
            calGray.Length == 3 &&
            calGray.All(value => value >= 254),
            "PDF CalGray D65 white должен преобразовываться в sRGB white.");

        var calRgb =
            PdfImageColorConverter.ConvertCieToRgb(
                [255, 0, 0],
                [
                    0.0, 1.0,
                    0.0, 1.0,
                    0.0, 1.0
                ],
                new PdfCieColorSpace(
                    PdfCieColorSpaceKind.CalRgb,
                    d65,
                    [1.0, 1.0, 1.0],
                    [
                        0.4124564, 0.2126729, 0.0193339,
                        0.3575761, 0.7151522, 0.1191920,
                        0.1804375, 0.0721750, 0.9503041
                    ],
                    [0.0, 1.0]));

        Assert(
            calRgb.Length == 3 &&
            calRgb[0] >= 254 &&
            calRgb[1] <= 1 &&
            calRgb[2] <= 1,
            "PDF CalRGB sRGB-primary red должен сохранять красный primary.");

        var lab =
            PdfImageColorConverter.ConvertCieToRgb(
                [255, 128, 128],
                [
                    0.0, 100.0,
                    -128.0, 127.0,
                    -128.0, 127.0
                ],
                new PdfCieColorSpace(
                    PdfCieColorSpaceKind.Lab,
                    d65,
                    [1.0, 1.0, 1.0],
                    [
                        1.0, 0.0, 0.0,
                        0.0, 1.0, 0.0,
                        0.0, 0.0, 1.0
                    ],
                    [-128.0, 127.0, -128.0, 127.0]));

        Assert(
            lab.Length == 3 &&
            lab.All(value => value >= 252),
            "PDF Lab L*=100 a*=0 b*=0 должен преобразовываться в near-white sRGB.");
    }

    private static void TestPdfCieDctReader(
        string directory)
    {
        var calGrayPath =
            Path.Combine(
                directory,
                "regression-calgray-dct.pdf");

        CreateCieDctImagePdf(
            calGrayPath,
            colorSpaceSyntax:
                "[/CalGray << /WhitePoint [0.95047 1 1.08883] /Gamma 1 >>]",
            jpegPixels: [255],
            PixelFormats.Gray8,
            stride: 1,
            colorTransform: null);

        var calGrayPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(calGrayPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            calGrayPixel.R >= 252 &&
            calGrayPixel.G >= 252 &&
            calGrayPixel.B >= 252,
            "PDF CalGray+DCTDecode должен декодироваться как grayscale samples до CIE conversion.");

        var calRgbPath =
            Path.Combine(
                directory,
                "regression-calrgb-dct.pdf");

        CreateCieDctImagePdf(
            calRgbPath,
            colorSpaceSyntax:
                "[/CalRGB << " +
                "/WhitePoint [0.95047 1 1.08883] " +
                "/Gamma [1 1 1] " +
                "/Matrix [" +
                "0.4124564 0.2126729 0.0193339 " +
                "0.3575761 0.7151522 0.1191920 " +
                "0.1804375 0.0721750 0.9503041" +
                "] >>]",
            jpegPixels: [255, 0, 0],
            PixelFormats.Rgb24,
            stride: 3,
            colorTransform: 1);

        var calRgbPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(calRgbPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            calRgbPixel.R >= 240 &&
            calRgbPixel.G <= 20 &&
            calRgbPixel.B <= 20,
            "PDF CalRGB+DCTDecode ColorTransform=1 должен пройти JPEG YUV→RGB sample decode до CalRGB conversion.");

        var redJpeg = EncodeJpegFixture(
            [255, 0, 0],
            width: 1,
            height: 1,
            PixelFormats.Rgb24,
            stride: 3);
        var raw = PdfJpegSampleDecoder.DecodeRawThreeComponents(
            redJpeg,
            1,
            1);
        var converted = PdfJpegSampleDecoder.DecodeRgb(
            redJpeg,
            1,
            1);

        Assert(
            raw.Length == 3 &&
            raw[0] >= 65 && raw[0] <= 90 &&
            raw[1] >= 70 && raw[1] <= 100 &&
            raw[2] >= 240 &&
            converted[0] >= 240 && converted[1] <= 20,
            "PDF raw JPEG samples должны сохранять JPEG Y/Cb/Cr components без RGB color conversion.");

        var rawCalRgbPath = Path.Combine(
            directory,
            "regression-calrgb-dct-transform0.pdf");
        CreateCieDctImagePdf(
            rawCalRgbPath,
            colorSpaceSyntax:
                "[/CalRGB << /WhitePoint [0.95047 1 1.08883] >>]",
            jpegPixels: [255, 0, 0],
            PixelFormats.Rgb24,
            stride: 3,
            colorTransform: 0);

        var rawCalRgbPixel = DecodeRgb24Pixel(
            SupportedFormatRegistry.ReadDocument(rawCalRgbPath)
                .Blocks.OfType<DocumentFixedPage>().Single()
                .ImageRuns.Single().Data);

        Assert(
            rawCalRgbPixel.R < 240,
            "PDF CalRGB+DCTDecode ColorTransform=0 должен использовать исходные JPEG components, а не decoded RGB.");

        var rawLabPath = Path.Combine(
            directory,
            "regression-lab-dct-transform0.pdf");
        CreateCieDctImagePdf(
            rawLabPath,
            colorSpaceSyntax:
                "[/Lab << /WhitePoint [0.95047 1 1.08883] /Range [-128 127 -128 127] >>]",
            jpegPixels: [255, 0, 0],
            PixelFormats.Rgb24,
            stride: 3,
            colorTransform: 0);

        var rawLabPixel = DecodeRgb24Pixel(
            SupportedFormatRegistry.ReadDocument(rawLabPath)
                .Blocks.OfType<DocumentFixedPage>().Single()
                .ImageRuns.Single().Data);

        Assert(
            rawLabPixel.R != rawCalRgbPixel.R ||
            rawLabPixel.G != rawCalRgbPixel.G ||
            rawLabPixel.B != rawCalRgbPixel.B,
            "PDF Lab+DCTDecode ColorTransform=0 должен применять Lab conversion к raw JPEG components.");
    }

    private static void TestPdfIccColorSpace(
        string directory)
    {
        var context =
            new ColorContext(
                PixelFormats.Rgb24);

        byte[] profileData;

        using (var profileStream =
               context.OpenProfileStream())
        using (var memory =
               new MemoryStream())
        {
            profileStream.CopyTo(memory);
            profileData = memory.ToArray();
        }

        Assert(
            profileData.Length > 0,
            "WPF sRGB ColorContext должен предоставлять ICC profile bytes.");

        var path = Path.Combine(
            directory,
            "regression-icc-image.pdf");

        CreateIccImagePdf(
            path,
            profileData,
            [255, 0, 0]);

        var model =
            SupportedFormatRegistry.ReadDocument(
                path);
        var image =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R >= 250 &&
            pixel.G <= 5 &&
            pixel.B <= 5,
            "PDF ICCBased sRGB red должен пройти embedded profile -> ICC engine -> PNG без смены primary.");
    }

    private static void TestPdfAdvancedColorMatte(
        string directory)
    {
        var context =
            new ColorContext(
                PixelFormats.Rgb24);
        byte[] profileData;

        using (var profileStream = context.OpenProfileStream())
        using (var memory = new MemoryStream())
        {
            profileStream.CopyTo(memory);
            profileData = memory.ToArray();
        }

        var iccPath =
            Path.Combine(
                directory,
                "regression-icc-matte.pdf");

        CreateRawImagePdf(
            iccPath,
            colorSpaceSyntax: "[/ICCBased 6 0 R]",
            imageSamples: [255, 127, 127],
            profileData,
            softMaskSample: 128,
            softMaskMatte: [1.0, 1.0, 1.0]);

        var iccPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(iccPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            iccPixel.R >= 248 &&
            iccPixel.G <= 8 &&
            iccPixel.B <= 8,
            "PDF ICCBased /SMask /Matte должен удаляться в ICC component coordinates до profile conversion.");

        var ciePath =
            Path.Combine(
                directory,
                "regression-calrgb-matte.pdf");

        CreateRawImagePdf(
            ciePath,
            colorSpaceSyntax:
                "[/CalRGB << " +
                "/WhitePoint [0.95047 1 1.08883] " +
                "/Gamma [1 1 1] " +
                "/Matrix [" +
                "0.4124564 0.2126729 0.0193339 " +
                "0.3575761 0.7151522 0.1191920 " +
                "0.1804375 0.0721750 0.9503041" +
                "] >>]",
            imageSamples: [255, 127, 127],
            profileData: null,
            softMaskSample: 128,
            softMaskMatte: [1.0, 1.0, 1.0]);

        var ciePixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(ciePath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            ciePixel.R >= 248 &&
            ciePixel.G <= 8 &&
            ciePixel.B <= 8,
            "PDF CalRGB /SMask /Matte должен удаляться в CIE component coordinates до gamma/matrix conversion.");

        var separationPath =
            Path.Combine(
                directory,
                "regression-separation-matte.pdf");

        CreateRawImagePdf(
            separationPath,
            colorSpaceSyntax:
                "[/Separation /SpotMatte /DeviceRGB 6 0 R]",
            imageSamples: [128],
            profileData: null,
            calculatorProgramData:
                Encoding.ASCII.GetBytes(
                    "{ 0.9 gt { 1 0 0 } { 0 1 0 } ifelse }"),
            softMaskSample: 128,
            softMaskMatte: [0.0]);

        var separationPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(separationPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            separationPixel.R >= 250 &&
            separationPixel.G <= 5 &&
            separationPixel.B <= 5,
            "PDF Separation /SMask /Matte должен удаляться в tint coordinates до tint transform.");

        var deviceNPath =
            Path.Combine(
                directory,
                "regression-devicen-matte.pdf");

        CreateRawImagePdf(
            deviceNPath,
            colorSpaceSyntax:
                "[/DeviceN [/SpotA /SpotB] /DeviceRGB 6 0 R]",
            imageSamples: [128, 127],
            profileData: null,
            functionData:
            [
                255, 255, 255,
                255, 0, 0,
                0, 255, 0,
                0, 0, 0
            ],
            softMaskSample: 128,
            softMaskMatte: [0.0, 1.0]);

        var deviceNPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(deviceNPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            deviceNPixel.R >= 245 &&
            deviceNPixel.G <= 10 &&
            deviceNPixel.B <= 10,
            "PDF DeviceN /SMask /Matte должен удаляться отдельно по tint components до tint transform.");
    }

    private static void TestPdfSeparationColorSpace(
        string directory)
    {
        var path = Path.Combine(
            directory,
            "regression-separation-image.pdf");

        CreateSeparationImagePdf(
            path,
            tintSample: 255);

        var model =
            SupportedFormatRegistry.ReadDocument(
                path);
        var image =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R >= 250 &&
            pixel.G <= 5 &&
            pixel.B <= 5,
            "PDF Separation FunctionType 2 tint=1 должен преобразовываться через alternate DeviceRGB в red.");

        var calculatorPath = Path.Combine(
            directory,
            "regression-separation-function4.pdf");

        CreateRawImagePdf(
            calculatorPath,
            colorSpaceSyntax:
                "[/Separation /SpotCalculator /DeviceRGB 6 0 R]",
            imageSamples: [255],
            profileData: null,
            calculatorProgramData:
                Encoding.ASCII.GetBytes(
                    "{ 0.5 gt { 1 0 0 } { 0 1 0 } ifelse }"));

        var calculatorModel =
            SupportedFormatRegistry.ReadDocument(
                calculatorPath);
        var calculatorImage =
            calculatorModel.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var calculatorPixel =
            DecodeRgb24Pixel(
                calculatorImage.Data);

        Assert(
            calculatorPixel.R >= 250 &&
            calculatorPixel.G <= 5 &&
            calculatorPixel.B <= 5,
            "PDF Separation FunctionType 4 должен выполнять calculator program end-to-end.");
    }

    private static void TestPdfCalculatorFunction()
    {
        var calculator =
            new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range:
                [
                    0.0, 1.0,
                    0.0, 1.0,
                    0.0, 1.0
                ],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ dup 0.5 gt { pop 1 0 0 } { pop 0 1 0 } ifelse }"));

        var red =
            calculator.Evaluate([0.75]);
        var green =
            calculator.Evaluate([0.25]);

        Assert(
            red[0] > 0.999 &&
            red[1] < 0.001 &&
            red[2] < 0.001 &&
            green[0] < 0.001 &&
            green[1] > 0.999 &&
            green[2] < 0.001,
            "PDF FunctionType 4 должен выполнять comparison и ifelse.");

        var arithmetic =
            new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range:
                [
                    0.0, 1.0,
                    0.0, 1.0
                ],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ dup mul 7 3 and 1 bitshift 2 div }"));

        var arithmeticResult =
            arithmetic.Evaluate([0.5]);

        Assert(
            Math.Abs(arithmeticResult[0] - 0.25) < 0.001 &&
            Math.Abs(arithmeticResult[1] - 1.0) < 0.001,
            "PDF FunctionType 4 должен выполнять arithmetic, stack и bitwise operators.");

        var extraStack =
            new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range: [0.0, 1.0],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ 0.25 exch }"));

        var extraStackResult =
            extraStack.Evaluate([0.75]);

        Assert(
            Math.Abs(extraStackResult[0] - 0.75) < 0.00001,
            "PDF FunctionType 4 должен брать верхние N outputs и игнорировать более глубокие stack values.");

        var rounding =
            new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range: [-10.0, 10.0],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ pop -6.5 round }"));

        Assert(
            Math.Abs(
                rounding.Evaluate([0.0])[0] +
                6.0) < 0.00001,
            "PDF FunctionType 4 round при отрицательном midpoint должен выбирать большее целое: -6.5 -> -6.");

        var logicalShift =
            new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range: [0.0, 2147483647.0],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ pop -1 -1 bitshift }"));

        Assert(
            Math.Abs(
                logicalShift.Evaluate([0.0])[0] -
                2147483647.0) < 0.5,
            "PDF FunctionType 4 отрицательный bitshift должен сдвигать вправо с нулевым заполнением.");

        var overflowPromoted = false;

        try
        {
            var overflow =
                new PdfCalculatorFunction(
                    domain: [0.0, 1.0],
                    range: [0.0, 1.0],
                    programData:
                        Encoding.ASCII.GetBytes(
                            "{ pop 2147483647 1 add 1 and }"));
            overflow.Evaluate([0.0]);
        }
        catch (InvalidDataException)
        {
            overflowPromoted = true;
        }

        Assert(
            overflowPromoted,
            "PDF FunctionType 4 integer add overflow должен promoted-to-real, после чего bitwise operator обязан дать type error.");

        var cviRangeRejected = false;

        try
        {
            var cviOverflow =
                new PdfCalculatorFunction(
                    domain: [0.0, 1.0],
                    range: [0.0, 1.0],
                    programData:
                        Encoding.ASCII.GetBytes(
                            "{ pop 2147483648.0 cvi }"));
            cviOverflow.Evaluate([0.0]);
        }
        catch (InvalidDataException)
        {
            cviRangeRejected = true;
        }

        Assert(
            cviRangeRejected,
            "PDF FunctionType 4 cvi должен отклонять real вне 32-bit PDF integer range.");

        var exponentRejected = false;

        try
        {
            var exponent =
                new PdfCalculatorFunction(
                    domain: [0.0, 1.0],
                    range: [0.0, 1.0],
                    programData:
                        Encoding.ASCII.GetBytes(
                            "{ pop 1e3 }"));
            exponent.Evaluate([0.0]);
        }
        catch (InvalidDataException error)
            when (error.Message.Contains(
                "1e3",
                StringComparison.Ordinal))
        {
            exponentRejected = true;
        }

        Assert(
            exponentRejected,
            "PDF FunctionType 4 operands используют PDF numeric syntax и не должны принимать exponent notation.");

        var freeProcedureRejected = false;

        try
        {
            _ = new PdfCalculatorFunction(
                domain: [0.0, 1.0],
                range: [0.0, 1.0],
                programData:
                    Encoding.ASCII.GetBytes(
                        "{ pop { 1 } { 1 } eq }"));
        }
        catch (InvalidDataException)
        {
            freeProcedureRejected = true;
        }

        Assert(
            freeProcedureRejected,
            "PDF FunctionType 4 braces допустимы только как conditional expressions для if/ifelse.");

        var rejected = false;

        try
        {
            var unsupported =
                new PdfCalculatorFunction(
                    domain: [0.0, 1.0],
                    range: [0.0, 1.0],
                    programData:
                        Encoding.ASCII.GetBytes(
                            "{ mysteryop }"));

            unsupported.Evaluate([0.5]);
        }
        catch (InvalidDataException error)
            when (error.Message.Contains(
                "mysteryop",
                StringComparison.Ordinal))
        {
            rejected = true;
        }

        Assert(
            rejected,
            "PDF FunctionType 4 неизвестный operator должен завершаться явной structural error.");
    }

    private static void TestPdfSampledAndStitchingFunctions()
    {
        var sampled =
            new PdfSampledFunction(
                domain:
                [
                    0.0, 1.0,
                    0.0, 1.0
                ],
                size: [2, 2],
                bitsPerSample: 8,
                order: 1,
                encode:
                [
                    0.0, 1.0,
                    0.0, 1.0
                ],
                decode:
                [
                    0.0, 1.0,
                    0.0, 1.0,
                    0.0, 1.0
                ],
                range:
                [
                    0.0, 1.0,
                    0.0, 1.0,
                    0.0, 1.0
                ],
                sampleData:
                [
                    255, 255, 255,
                    255, 0, 0,
                    0, 255, 0,
                    0, 0, 0
                ]);

        var exact =
            sampled.Evaluate([1.0, 0.0]);

        Assert(
            exact[0] > 0.999 &&
            exact[1] < 0.001 &&
            exact[2] < 0.001,
            "PDF FunctionType 0 должен точно выбирать sample table corner.");

        var midpoint =
            sampled.Evaluate([0.5, 0.0]);

        Assert(
            Math.Abs(midpoint[0] - 1.0) < 0.002 &&
            Math.Abs(midpoint[1] - 0.5) < 0.01 &&
            Math.Abs(midpoint[2] - 0.5) < 0.01,
            "PDF FunctionType 0 должен выполнять multilinear interpolation.");

        var cubic =
            new PdfSampledFunction(
                domain: [0.0, 1.0],
                size: [4],
                bitsPerSample: 8,
                order: 3,
                encode: [0.0, 3.0],
                decode: [0.0, 1.0],
                range: [0.0, 1.0],
                sampleData: [0, 0, 255, 0]);
        var cubicMidpoint =
            cubic.Evaluate([0.5]);

        Assert(
            Math.Abs(
                cubicMidpoint[0] -
                143.4375 / 255.0) < 0.00001,
            "PDF FunctionType 0 /Order=3 должен использовать Acrobat-compatible cubic interpolation, а не linear.");

        var first =
            new PdfExponentialFunction(
                0,
                1,
                [0.0],
                [1.0],
                1,
                null);
        var second =
            new PdfExponentialFunction(
                0,
                1,
                [1.0],
                [0.0],
                1,
                null);
        var stitched =
            new PdfStitchingFunction(
                [0.0, 1.0],
                [first, second],
                [0.5],
                [
                    0.0, 1.0,
                    0.0, 1.0
                ],
                [0.0, 1.0]);

        var left =
            stitched.Evaluate([0.25]);
        var right =
            stitched.Evaluate([0.75]);

        Assert(
            Math.Abs(left[0] - 0.5) < 0.001 &&
            Math.Abs(right[0] - 0.5) < 0.001,
            "PDF FunctionType 3 должен выбирать subfunction и применять /Encode.");
    }

    private static void TestPdfSampledOrder3Reader(
        string directory)
    {
        var path = Path.Combine(
            directory,
            "regression-sampled-order3.pdf");

        CreateRawImagePdf(
            path,
            colorSpaceSyntax:
                "[/Separation /CubicSpot /DeviceRGB 6 0 R]",
            imageSamples: [128],
            profileData: null,
            sampledOrder3Data:
            [
                0, 0, 0,
                0, 0, 0,
                255, 0, 0,
                0, 0, 0
            ]);

        var model =
            SupportedFormatRegistry.ReadDocument(path);
        var image =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R is >= 140 and <= 147 &&
            pixel.G <= 2 &&
            pixel.B <= 2,
            "PDF FunctionType 0 /Order=3 должен пройти parser и cubic tint transform end-to-end.");
    }

    private static void TestPdfDeviceNColorSpace(
        string directory)
    {
        var path = Path.Combine(
            directory,
            "regression-devicen-image.pdf");

        CreateDeviceNImagePdf(path);

        var model =
            SupportedFormatRegistry.ReadDocument(
                path);
        var image =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R >= 250 &&
            pixel.G <= 5 &&
            pixel.B <= 5,
            "PDF DeviceN two-ink sample должен пройти FunctionType 0 tint transform в DeviceRGB red.");
    }

    private static (
        int Width,
        int Height,
        byte[] Pixels) DecodeGray8Pixels(
        byte[] png)
    {
        using var input =
            new MemoryStream(
                png,
                writable: false);
        var decoder =
            new PngBitmapDecoder(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        BitmapSource source =
            decoder.Frames.Single();

        if (source.Format != PixelFormats.Gray8)
        {
            source =
                new FormatConvertedBitmap(
                    source,
                    PixelFormats.Gray8,
                    null,
                    0);
        }

        var stride =
            source.PixelWidth;
        var pixels =
            new byte[
                checked(
                    stride *
                    source.PixelHeight)];

        source.CopyPixels(
            pixels,
            stride,
            0);

        return (
            source.PixelWidth,
            source.PixelHeight,
            pixels);
    }

    private static (byte R, byte G, byte B) DecodeRgb24Pixel(
        byte[] png)
    {
        using var input =
            new MemoryStream(
                png,
                writable: false);
        var decoder =
            new PngBitmapDecoder(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        BitmapSource source =
            decoder.Frames.Single();

        if (source.Format != PixelFormats.Rgb24)
        {
            source =
                new FormatConvertedBitmap(
                    source,
                    PixelFormats.Rgb24,
                    null,
                    0);
        }

        var stride =
            checked(
                source.PixelWidth * 3);
        var pixels =
            new byte[
                checked(
                    stride *
                    source.PixelHeight)];

        source.CopyPixels(
            pixels,
            stride,
            0);

        return (
            pixels[0],
            pixels[1],
            pixels[2]);
    }

    private static void TestPdfIndexedMatte()
    {
        var rgb = PdfImageColorConverter.ConvertIndexedToRgb(
            [0x80],
            width: 1,
            height: 1,
            bitsPerComponent: 2,
            highValue: 3,
            rgbPalette:
            [
                0, 0, 0,
                255, 0, 0,
                0, 255, 0,
                0, 0, 255
            ],
            decodeMinimum: 0,
            decodeMaximum: 3,
            alphaSamples: [128],
            matteIndex: 1);

        Assert(
            rgb.SequenceEqual(
                new byte[]
                {
                    0,
                    0,
                    255
                }),
            "PDF Indexed /SMask /Matte должен распремультиплицировать decoded index до palette lookup.");
    }

    private static void TestPdfJpegDecoder()
    {
        static bool Near(
            byte actual,
            int expected,
            int tolerance)
        {
            return Math.Abs(
                       actual -
                       expected) <=
                   tolerance;
        }

        var grayJpeg = EncodeJpegFixture(
            [32, 224],
            width: 2,
            height: 1,
            PixelFormats.Gray8,
            stride: 2);
        var gray = PdfJpegSampleDecoder.DecodeGray(
            grayJpeg,
            expectedWidth: 2,
            expectedHeight: 1);

        Assert(
            gray.Length == 2 &&
            Near(gray[0], 32, 4) &&
            Near(gray[1], 224, 4),
            "PDF DCTDecode grayscale JPEG должен декодироваться в 8-bit samples.");

        var inverted = PdfImageColorConverter.ApplyComponentDecode(
            gray,
            components: 1,
            new[] { 1.0, 0.0 });

        Assert(
            Near(inverted[0], 223, 4) &&
            Near(inverted[1], 31, 4),
            "PDF нестандартный /Decode должен применяться к decoded DCT samples.");

        var rgbJpeg = EncodeJpegFixture(
            [200, 50, 20],
            width: 1,
            height: 1,
            PixelFormats.Rgb24,
            stride: 3);
        var rgb = PdfJpegSampleDecoder.DecodeRgb(
            rgbJpeg,
            expectedWidth: 1,
            expectedHeight: 1);

        Assert(
            rgb.Length == 3 &&
            Near(rgb[0], 200, 8) &&
            Near(rgb[1], 50, 8) &&
            Near(rgb[2], 20, 8),
            "PDF DCTDecode RGB JPEG должен декодироваться в RGB samples.");

        var adobeOverride =
            InjectAdobeApp14Transform(
                rgbJpeg,
                transform: 1);

        Assert(
            PdfJpegSampleDecoder.ResolveEffectiveColorTransform(
                adobeOverride,
                requestedColorTransform: 0,
                components: 3) == 1,
            "PDF DCTDecode Adobe APP14 marker должен иметь приоритет над /DecodeParms /ColorTransform.");

        var cmykJpeg = EncodeJpegFixture(
            [32, 96, 160, 224],
            width: 1,
            height: 1,
            PixelFormats.Cmyk32,
            stride: 4);
        var cmyk = PdfJpegSampleDecoder.DecodeCmyk(
            cmykJpeg,
            expectedWidth: 1,
            expectedHeight: 1);

        Assert(
            cmyk.Length == 4 &&
            Near(cmyk[0], 32, 8) &&
            Near(cmyk[1], 96, 8) &&
            Near(cmyk[2], 160, 8) &&
            Near(cmyk[3], 224, 8),
            "PDF DCTDecode CMYK JPEG должен декодироваться в CMYK samples без RGB промежуточного преобразования.");

        var matteRemoved = PdfImageColorConverter.RemoveMatte(
            rgb,
            components: 3,
            alphaSamples: [255],
            matte: [1.0, 1.0, 1.0]);

        Assert(
            matteRemoved.SequenceEqual(rgb),
            "PDF /SMask /Matte должен работать с decoded DCT RGB samples.");

        var geometryRejected = false;

        try
        {
            PdfJpegSampleDecoder.DecodeGray(
                grayJpeg,
                expectedWidth: 3,
                expectedHeight: 1);
        }
        catch (InvalidDataException)
        {
            geometryRejected = true;
        }

        Assert(
            geometryRejected,
            "PDF DCTDecode должен отклонять JPEG geometry, несовместимую с Image XObject.");
    }

    private static void TestPdfCmykDctReader(
        string directory)
    {
        var path =
            Path.Combine(
                directory,
                "regression-cmyk-dct.pdf");

        CreateCmykDctImagePdf(path);

        var model =
            SupportedFormatRegistry.ReadDocument(path);
        var image =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R <= 8 &&
            pixel.G >= 245 &&
            pixel.B >= 245,
            "PDF DeviceCMYK DCT с нестандартным /Decode должен проходить через decoded CMYK samples и давать cyan.");


    }

    private static void TestPdfIccCmykDctReader(
        string directory)
    {
        var path =
            Path.Combine(
                directory,
                "regression-icc-cmyk-dct.pdf");

        CreateCmykDctImagePdf(
            path,
            CreateSyntheticCmykInputProfile());

        var image =
            SupportedFormatRegistry
                .ReadDocument(path)
                .Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var pixel =
            DecodeRgb24Pixel(
                image.Data);

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            pixel.R >= 170 &&
            pixel.R <= 205 &&
            Math.Abs(pixel.R - pixel.G) <= 3 &&
            Math.Abs(pixel.R - pixel.B) <= 3,
            "PDF ICCBased /N=4 DCT должен проходить через CMYK JPEG samples и embedded CMYK ICC profile с ожидаемой нейтральной Lab→sRGB яркостью.");
    }

    private static void TestPdfJpxReader(
        string directory)
    {
        var jpx =
            Convert.FromBase64String(
                "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAQAAAAIAAwcHAAAAAAAPY29scgEAAAAAABAAAACYanAyY/9P/1EALwAAAAAAAgAAAAEAAAAAAAAAAAAAAAIAAAABAAAAAAAAAAAAAwcBAQcBAQcBAf9SAAwAAAABAAAEBAAB/1wABEBA/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAAIAAB/5PfgCALsop/34AYBaLd34AQCT//2Q==");

        var direct =
            PdfJpxSampleDecoder.Decode(
                jpx,
                expectedWidth: 2,
                expectedHeight: 1,
                useEmbeddedColorSpace: false);

        Assert(
            direct.Components == 3 &&
            direct.BitDepths.SequenceEqual(
                new[] { 8, 8, 8 }) &&
            direct.Samples.SequenceEqual(
                new byte[]
                {
                    255, 0, 0,
                    0, 255, 0
                }),
            "PDF JPX decoder должен вернуть точные interleaved RGB samples и исходную component depth.");

        var sixteenBitJpx =
            Convert.FromBase64String(
                "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAQAAAAIAAQ8HAAAAAAAPY29scgEAAAAAABEAAACGanAyY/9P/1EAKQAAAAAAAgAAAAEAAAAAAAAAAAAAAAIAAAABAAAAAAAAAAAAAQ8BAf9SAAwAAAABAAAEBAAB/1wABECA/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAAFAAB/5PP/DAIBCv/2Q==");
        var sixteenBit =
            PdfJpxSampleDecoder.Decode(
                sixteenBitJpx,
                expectedWidth: 2,
                expectedHeight: 1,
                useEmbeddedColorSpace: false);

        Assert(
            sixteenBit.Components == 1 &&
            sixteenBit.BitDepths.SequenceEqual(
                new[] { 16 }) &&
            sixteenBit.Samples.SequenceEqual(
                new byte[] { 255, 128 }),
            "PDF JPX decoder должен сохранять исходную 16-bit component depth и нормализовать samples в 8-bit render coordinates.");

        var explicitPath =
            Path.Combine(
                directory,
                "regression-jpx-explicit-rgb.pdf");
        CreateJpxImagePdf(
            explicitPath,
            jpx,
            includeColorSpace: true,
            includeReverseDecode: true);

        var explicitImage =
            SupportedFormatRegistry
                .ReadDocument(explicitPath)
                .Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .Single();
        var explicitPixel =
            DecodeRgb24Pixel(
                explicitImage.Data);

        Assert(
            string.Equals(
                explicitImage.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            explicitPixel.R >= 250 &&
            explicitPixel.G <= 5 &&
            explicitPixel.B <= 5,
            "PDF JPX с явным /DeviceRGB должен игнорировать /Decode и /BitsPerComponent и сохранить красный первый pixel.");

        var implicitPath =
            Path.Combine(
                directory,
                "regression-jpx-implicit-rgb.pdf");
        CreateJpxImagePdf(
            implicitPath,
            jpx,
            includeColorSpace: false,
            includeReverseDecode: false);

        var implicitPixel =
            DecodeRgb24Pixel(
                SupportedFormatRegistry
                    .ReadDocument(implicitPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            implicitPixel.R >= 250 &&
            implicitPixel.G <= 5 &&
            implicitPixel.B <= 5,
            "PDF JPX без /ColorSpace должен использовать embedded JPX sRGB mapping.");

        var geometryRejected = false;

        try
        {
            _ = PdfJpxSampleDecoder.Decode(
                jpx,
                expectedWidth: 1,
                expectedHeight: 1,
                useEmbeddedColorSpace: false);
        }
        catch (InvalidDataException)
        {
            geometryRejected = true;
        }

        Assert(
            geometryRejected,
            "PDF JPX decoder должен строго отклонять geometry mismatch.");

        var mismatchPath =
            Path.Combine(
                directory,
                "regression-jpx-component-mismatch.pdf");
        CreateJpxImagePdf(
            mismatchPath,
            jpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "/DeviceGray");

        var componentMismatchRejected = false;

        try
        {
            SupportedFormatRegistry.ReadDocument(
                mismatchPath);
        }
        catch (InvalidDataException)
        {
            componentMismatchRejected = true;
        }

        Assert(
            componentMismatchRejected,
            "PDF JPX explicit ColorSpace должен строго совпадать с числом decoded components.");

        var rgbaJpx = Convert.FromBase64String(
            "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAABPanAyaAAAABZpaGRyAAAAAQAAAAIABAcHAAAAAAAPY29scgEAAAAAABAAAAAiY2RlZgAEAAAAAAABAAEAAAACAAIAAAADAAMAAQAAAAAAoGpwMmP/T/9RADIAAAAAAAIAAAABAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAQHAQEHAQEHAQEHAQH/UgAMAAAAAQAABAQAAf9cAARAQP9kACUAAUNyZWF0ZWQgYnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAACUAAf+T34AgC7KKf9+AGAWi3d+AEAk/z7QIBB//2Q==");

        var alphaPath = Path.Combine(
            directory,
            "regression-jpx-smaskindata.pdf");
        CreateJpxImagePdf(
            alphaPath,
            rgbaJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            softMaskInData: 1);

        var alphaImage = SupportedFormatRegistry
            .ReadDocument(alphaPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var input = new MemoryStream(alphaImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[0] == 0 &&
                pixels[1] == 0 &&
                pixels[2] == 255 &&
                pixels[3] == 255 &&
                pixels[4] == 0 &&
                pixels[5] == 255 &&
                pixels[6] == 0 &&
                pixels[7] == 128,
                "PDF JPX /SMaskInData=1 должен сохранять RGBA samples и unassociated opacity.");
        }

        var implicitAlphaPath = Path.Combine(
            directory,
            "regression-jpx-implicit-alpha.pdf");
        CreateJpxImagePdf(
            implicitAlphaPath,
            rgbaJpx,
            includeColorSpace: false,
            includeReverseDecode: false,
            softMaskInData: 1);

        var implicitAlphaImage = SupportedFormatRegistry
            .ReadDocument(implicitAlphaPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        Assert(
            implicitAlphaImage.Data.SequenceEqual(alphaImage.Data),
            "PDF JPX embedded sRGB с /SMaskInData=1 должен давать те же RGBA samples.");

        var preblendedPath = Path.Combine(
            directory,
            "regression-jpx-preblended-alpha.pdf");
        CreateJpxImagePdf(
            preblendedPath,
            rgbaJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            softMaskInData: 2);

        var preblendedJpx = Convert.FromBase64String(
            "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAABPanAyaAAAABZpaGRyAAAAAQAAAAIABAcHAAAAAAAPY29scgEAAAAAABAAAAAiY2RlZgAEAAAAAAABAAEAAAACAAIAAAADAAMAAQAAAAAAn2pwMmP/T/9RADIAAAAAAAIAAAABAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAQHAQEHAQEHAQEHAQH/UgAMAAAAAQAABAQAAf9cAARAQP9kACUAAUNyZWF0ZWQgYnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAACQAAf+T34AgC7KKf9+AEAef34AQCT/PtAgEH//Z");
        var rgbAlphaFirst = (byte[])preblendedJpx.Clone();
        var rgbCdefMarker = Encoding.ASCII.GetBytes("cdef");
        var rgbCdef = rgbAlphaFirst.AsSpan().IndexOf(rgbCdefMarker);
        Assert(rgbCdef >= 0, "JP2 RGB+alpha fixture требует cdef.");
        var rgbCount = (rgbAlphaFirst[rgbCdef + 4] << 8) | rgbAlphaFirst[rgbCdef + 5];
        Assert(rgbCount == 4, "JP2 RGB+alpha fixture требует четыре записи cdef.");
        for (var index = 0; index < rgbCount; index++)
        {
            var offset = rgbCdef + 6 + index * 6;
            var previous = (rgbAlphaFirst[offset] << 8) | rgbAlphaFirst[offset + 1];
            var shifted = (previous + 1) % 4;
            rgbAlphaFirst[offset] = 0;
            rgbAlphaFirst[offset + 1] = (byte)shifted;
        }

        var originalRgbAlpha = PdfJpxSampleDecoder.Decode(preblendedJpx, 2, 1, true);
        var shiftedRgbAlpha = PdfJpxSampleDecoder.Decode(rgbAlphaFirst, 2, 1, true);
        Assert(
            shiftedRgbAlpha.Components == 4 &&
            Enumerable.Range(0, 2).All(pixel =>
                Enumerable.Range(0, 4).All(channel =>
                    shiftedRgbAlpha.Samples[pixel * 4 + channel] ==
                    originalRgbAlpha.Samples[pixel * 4 + (channel + 1) % 4])),
            "PDF JP2 RGB+alpha alpha-first cdef должен сохранять правильную перестановку RGBA samples.");

        var rgbAlphaFirstPdfPath = Path.Combine(
            directory, "regression-jpx-rgb-alpha-first.pdf");
        CreateJpxImagePdf(
            rgbAlphaFirstPdfPath,
            rgbAlphaFirst,
            includeColorSpace: true,
            includeReverseDecode: false,
            softMaskInData: 1);

        var rgbAlphaFirstImage = SupportedFormatRegistry
            .ReadDocument(rgbAlphaFirstPdfPath).Blocks
            .OfType<DocumentFixedPage>().Single().ImageRuns.Single();

        using (var rgbAlphaStream = new MemoryStream(rgbAlphaFirstImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                rgbAlphaStream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                Enumerable.Range(0, 2).All(pixel =>
                    pixels[pixel * 4] == shiftedRgbAlpha.Samples[pixel * 4 + 2] &&
                    pixels[pixel * 4 + 1] == shiftedRgbAlpha.Samples[pixel * 4 + 1] &&
                    pixels[pixel * 4 + 2] == shiftedRgbAlpha.Samples[pixel * 4] &&
                    pixels[pixel * 4 + 3] == shiftedRgbAlpha.Samples[pixel * 4 + 3]),
                "PDF JPX RGB alpha-first cdef должен сохранять RGBA в конечном PNG.");
        }

        foreach (var (shift, alphaIndex) in new[] { (2, 1), (3, 2) })
        {
            var middleAlphaJpx = (byte[])preblendedJpx.Clone();
            for (var entry = 0; entry < rgbCount; entry++)
            {
                var offset = rgbCdef + 6 + entry * 6;
                var previous = (middleAlphaJpx[offset] << 8) |
                    middleAlphaJpx[offset + 1];
                middleAlphaJpx[offset] = 0;
                middleAlphaJpx[offset + 1] = (byte)((previous + shift) % 4);
            }

            var decodedMiddleAlpha = PdfJpxSampleDecoder.Decode(middleAlphaJpx, 2, 1, true);
            Assert(
                decodedMiddleAlpha.Components == 4 &&
                Enumerable.Range(0, 2).All(pixel =>
                    Enumerable.Range(0, 4).All(channel =>
                    {
                        var raw = channel == 3 ? alphaIndex :
                            channel < alphaIndex ? channel : channel + 1;
                        return decodedMiddleAlpha.Samples[pixel * 4 + channel] ==
                            originalRgbAlpha.Samples[pixel * 4 + raw];
                    })),
                $"PDF JP2 cdef alpha index {alphaIndex} должен правильно переставлять samples.");

            var middleAlphaPdf = Path.Combine(
                directory, $"regression-jpx-middle-alpha-{alphaIndex}.pdf");
            CreateJpxImagePdf(
                middleAlphaPdf,
                middleAlphaJpx,
                includeColorSpace: true,
                includeReverseDecode: false,
                softMaskInData: 1);
            var middleAlphaImage = SupportedFormatRegistry.ReadDocument(middleAlphaPdf)
                .Blocks.OfType<DocumentFixedPage>().Single().ImageRuns.Single();

            using var stream = new MemoryStream(middleAlphaImage.Data);
            var bitmap = new PngBitmapDecoder(
                stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);
            Assert(
                Enumerable.Range(0, 2).All(pixel =>
                    pixels[pixel * 4] == decodedMiddleAlpha.Samples[pixel * 4 + 2] &&
                    pixels[pixel * 4 + 1] == decodedMiddleAlpha.Samples[pixel * 4 + 1] &&
                    pixels[pixel * 4 + 2] == decodedMiddleAlpha.Samples[pixel * 4] &&
                    pixels[pixel * 4 + 3] == decodedMiddleAlpha.Samples[pixel * 4 + 3]),
                $"PDF JP2 cdef alpha index {alphaIndex} должен сохранять BGRA в итоговом PNG.");

            var preblendedMiddlePath = Path.Combine(
                directory, $"regression-jpx-middle-alpha-matte-{alphaIndex}.pdf");
            CreateJpxImagePdf(
                preblendedMiddlePath,
                middleAlphaJpx,
                includeColorSpace: true,
                includeReverseDecode: false,
                softMaskInData: 2,
                matteSyntax: "[0 0 0]");
            var preblendedMiddleImage = SupportedFormatRegistry
                .ReadDocument(preblendedMiddlePath).Blocks
                .OfType<DocumentFixedPage>().Single().ImageRuns.Single();

            using var preblendedStream = new MemoryStream(preblendedMiddleImage.Data);
            var preblendedBitmap = new PngBitmapDecoder(
                preblendedStream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource preblendedBgra = preblendedBitmap.Format == PixelFormats.Bgra32
                ? preblendedBitmap
                : new FormatConvertedBitmap(preblendedBitmap, PixelFormats.Bgra32, null, 0);
            var preblendedPixels = new byte[8];
            preblendedBgra.CopyPixels(preblendedPixels, 8, 0);

            Assert(
                Enumerable.Range(0, 2).All(pixel =>
                {
                    var offset = pixel * 4;
                    var alpha = pixels[offset + 3];
                    return preblendedPixels[offset + 3] == alpha &&
                        Enumerable.Range(0, 3).All(channel =>
                        {
                            var expected = alpha == 0 ? 0 :
                                Math.Min(255, (int)Math.Round(
                                    pixels[offset + channel] * 255.0 / alpha));
                            return Math.Abs(preblendedPixels[offset + channel] - expected) <= 1;
                        });
                }),
                $"PDF JP2 cdef alpha index {alphaIndex} должен применять Matte к фактическим RGB samples.");
        }

        var compensatedPath = Path.Combine(
            directory,
            "regression-jpx-preblended-compensated.pdf");
        CreateJpxImagePdf(
            compensatedPath,
            preblendedJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            softMaskInData: 2,
            matteSyntax: "[0 0 0]");

        var compensatedImage = SupportedFormatRegistry
            .ReadDocument(compensatedPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var compensatedInput = new MemoryStream(compensatedImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                compensatedInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[2] == 255 &&
                pixels[3] == 255 &&
                pixels[4] == 0 &&
                pixels[5] == 255 &&
                pixels[6] == 0 &&
                pixels[7] == 128,
                "PDF JPX /SMaskInData=2 должен восстанавливать green из preblended samples по /Matte.");
        }

        var ciePath = Path.Combine(
            directory,
            "regression-jpx-calrgb-preblended.pdf");
        CreateJpxImagePdf(
            ciePath,
            preblendedJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "[/CalRGB << /WhitePoint [0.95047 1 1.08883] >>]",
            softMaskInData: 2,
            matteSyntax: "[0 0 0]");

        var cieImage = SupportedFormatRegistry
            .ReadDocument(ciePath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var cieInput = new MemoryStream(cieImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                cieInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[4] <= 8 &&
                pixels[5] >= 245 &&
                pixels[6] <= 8 &&
                pixels[7] == 128,
                "PDF JPX CalRGB /SMaskInData=2 должен компенсировать Matte до CIE conversion.");
        }

        var context = new ColorContext(PixelFormats.Rgb24);
        byte[] iccProfile;

        using (var profileStream = context.OpenProfileStream())
        using (var profileMemory = new MemoryStream())
        {
            profileStream.CopyTo(profileMemory);
            iccProfile = profileMemory.ToArray();
        }

        var iccPath = Path.Combine(
            directory,
            "regression-jpx-icc-preblended.pdf");
        CreateJpxImagePdf(
            iccPath,
            preblendedJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "[/ICCBased 6 0 R]",
            softMaskInData: 2,
            matteSyntax: "[0 0 0]",
            iccProfile: iccProfile);

        var iccImage = SupportedFormatRegistry
            .ReadDocument(iccPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();
        using (var iccInput = new MemoryStream(iccImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                iccInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[4] <= 8 &&
                pixels[5] >= 245 &&
                pixels[6] <= 8 &&
                pixels[7] == 128,
                "PDF JPX ICCBased /SMaskInData=2 должен компенсировать Matte перед ICC conversion.");
        }

        // Lossless two-channel JPEG 2000 (Gray + alpha): white opaque,
        // half-gray with half opacity; ColorSpace is intentionally implicit.
        var implicitGrayAlphaJpx = Convert.FromBase64String(
            "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAABDanAyaAAAABZpaGRyAAAAAQAAAAIAAgcHAAAAAAAPY29scgEAAAAAABEAAAAWY2RlZgACAAAAAAABAAEAAQAAAAAAjWpwMmP/T/9RACwAAAAAAAIAAAABAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAIHAQEHAQH/UgAMAAAAAQAABAQAAf9cAARAQP9kACUAAUNyZWF0ZWQgYnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAABgAAf+Tz7QIBB/PtAgEH//Z");

        var implicitGrayAlphaPath = Path.Combine(
            directory, "regression-jpx-implicit-gray-alpha.pdf");
        CreateJpxImagePdf(
            implicitGrayAlphaPath,
            implicitGrayAlphaJpx,
            includeColorSpace: false,
            includeReverseDecode: false,
            softMaskInData: 1);

        var implicitGrayImage = SupportedFormatRegistry
            .ReadDocument(implicitGrayAlphaPath).Blocks
            .OfType<DocumentFixedPage>().Single().ImageRuns.Single();

        using (var grayInput = new MemoryStream(implicitGrayImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                grayInput, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var samples = new byte[8];
            bgra.CopyPixels(samples, 8, 0);

            Assert(
                samples[0] == 255 && samples[1] == 255 &&
                samples[2] == 255 && samples[3] == 255 &&
                samples[4] >= 125 && samples[4] <= 131 &&
                samples[5] == samples[4] &&
                samples[6] == samples[4] && samples[7] == 128,
                "PDF implicit JPX Gray+alpha должен сохранять Gray и embedded opacity.");
        }

        var implicitGrayMattePath = Path.Combine(
            directory, "regression-jpx-implicit-gray-matte.pdf");
        CreateJpxImagePdf(
            implicitGrayMattePath,
            implicitGrayAlphaJpx,
            includeColorSpace: false,
            includeReverseDecode: false,
            softMaskInData: 2,
            matteSyntax: "[0]");

        var implicitMatteImage = SupportedFormatRegistry
            .ReadDocument(implicitGrayMattePath).Blocks
            .OfType<DocumentFixedPage>().Single().ImageRuns.Single();

        using (var matteInput = new MemoryStream(implicitMatteImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                matteInput, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var samples = new byte[8];
            bgra.CopyPixels(samples, 8, 0);

            Assert(
                samples[4] >= 248 &&
                samples[5] == samples[4] &&
                samples[6] == samples[4] &&
                samples[7] == 128,
                "PDF implicit JPX Gray /SMaskInData=2 должен компенсировать Matte до RGBA.");
        }

        var separationJpx = Convert.FromBase64String(
            "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAABDanAyaAAAABZpaGRyAAAAAQAAAAIAAgcHAAAAAAAPY29scgEAAAAAABEAAAAWY2RlZgACAAAAAAABAAEAAQAAAAAAjWpwMmP/T/9RACwAAAAAAAIAAAABAAAAAAAAAAAAAAACAAAAAQAAAAAAAAAAAAIHAQEHAQH/UgAMAAAAAQAABAQAAf9cAARAQP9kACUAAUNyZWF0ZWQgYnkgT3BlbkpQRUcgdmVyc2lvbiAyLjUuNP+QAAoAAAAAABgAAf+Tz7QIBB/PtAgEH//Z");
        var nonLastAlpha = (byte[])separationJpx.Clone();
        var definitionMarker = Encoding.ASCII.GetBytes("cdef");
        var definitionOffset = nonLastAlpha.AsSpan().IndexOf(definitionMarker);
        Assert(definitionOffset >= 0,
            "JPX fixture должен содержать JP2 cdef metadata.");

        var alphaEntryFound = false;
        var definitionCount = (nonLastAlpha[definitionOffset + 4] << 8) |
            nonLastAlpha[definitionOffset + 5];
        for (var entry = 0; entry < definitionCount; entry++)
        {
            var offset = definitionOffset + 6 + entry * 6;
            var channelType = (nonLastAlpha[offset + 2] << 8) |
                nonLastAlpha[offset + 3];
            if (channelType is 1 or 2)
            {
                nonLastAlpha[offset] = 0;
                nonLastAlpha[offset + 1] = 0;
                alphaEntryFound = true;
            }
            else
            {
                nonLastAlpha[offset] = 0;
                nonLastAlpha[offset + 1] = 1;
            }
        }
        Assert(alphaEntryFound, "JPX fixture должен маркировать alpha cdef entry.");

        var sourceChannels = PdfJpxSampleDecoder.Decode(separationJpx, 2, 1, true);
        var alphaFirst = PdfJpxSampleDecoder.Decode(nonLastAlpha, 2, 1, true);

        Assert(
            alphaFirst.Samples.Length == sourceChannels.Samples.Length &&
            Enumerable.Range(0, 2).All(pixel =>
                alphaFirst.Samples[pixel * 2] == sourceChannels.Samples[pixel * 2 + 1] &&
                alphaFirst.Samples[pixel * 2 + 1] == sourceChannels.Samples[pixel * 2]),
            "PDF JPX Gray+alpha с первым alpha в JP2 cdef должен декодироваться с перестановкой компонентов.");

        var separationJpxPath = Path.Combine(
            directory,
            "regression-jpx-separation-preblended.pdf");
        CreateJpxImagePdf(
            separationJpxPath,
            separationJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "[/Separation /SpotRed /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0 0] /N 1 >>]",
            softMaskInData: 2,
            matteSyntax: "[0]");

        var separationJpxImage = SupportedFormatRegistry
            .ReadDocument(separationJpxPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var separationInput = new MemoryStream(separationJpxImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                separationInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[2] == 255 &&
                pixels[3] == 255 &&
                pixels[4] == 0 &&
                pixels[5] == 0 &&
                pixels[6] == 255 &&
                pixels[7] == 128,
                "PDF JPX Separation preblended opacity должен компенсировать tint перед FunctionType 2.");
        }

        var deviceNJpxPath = Path.Combine(
            directory,
            "regression-jpx-devicen-preblended.pdf");
        CreateJpxImagePdf(
            deviceNJpxPath,
            preblendedJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "[/DeviceN [/SpotR /SpotG /SpotB] /DeviceRGB 6 0 R]",
            softMaskInData: 2,
            matteSyntax: "[0 0 0]",
            calculatorFunction: "{ 0 add }");

        var deviceNJpxImage = SupportedFormatRegistry
            .ReadDocument(deviceNJpxPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var deviceNInput = new MemoryStream(deviceNJpxImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                deviceNInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[2] == 255 &&
                pixels[3] == 255 &&
                pixels[4] == 0 &&
                pixels[5] == 255 &&
                pixels[6] == 0 &&
                pixels[7] == 128,
                "PDF JPX DeviceN должен компенсировать три tint components до FunctionType 4.");
        }

        var indexedJpxPath = Path.Combine(
            directory,
            "regression-jpx-indexed-preblended.pdf");
        CreateJpxImagePdf(
            indexedJpxPath,
            separationJpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            colorSpaceSyntax: "[/Indexed /DeviceRGB 1 <000000FF0000>]",
            softMaskInData: 2,
            matteSyntax: "[0]");

        var indexedJpxImage = SupportedFormatRegistry
            .ReadDocument(indexedJpxPath)
            .Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        using (var indexedInput = new MemoryStream(indexedJpxImage.Data))
        {
            var bitmap = new PngBitmapDecoder(
                indexedInput,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames.Single();
            BitmapSource bgra = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[8];
            bgra.CopyPixels(pixels, 8, 0);

            Assert(
                pixels[2] == 255 &&
                pixels[3] == 255 &&
                pixels[4] == 0 &&
                pixels[5] == 0 &&
                pixels[6] == 255 &&
                pixels[7] == 128,
                "PDF JPX Indexed /SMaskInData=2 должен компенсировать palette index до lookup.");
        }

        var preblendedRejected = false;

        try
        {
            SupportedFormatRegistry.ReadDocument(preblendedPath);
        }
        catch (InvalidDataException error)
            when (error.Message.Contains("SMaskInData=2", StringComparison.Ordinal))
        {
            preblendedRejected = true;
        }

        Assert(
            preblendedRejected,
            "PDF JPX preblended opacity без compensation должен оставаться unsupported.");

        var invalidPath = Path.Combine(
            directory,
            "regression-jpx-smaskindata-mismatch.pdf");
        CreateJpxImagePdf(
            invalidPath,
            jpx,
            includeColorSpace: true,
            includeReverseDecode: false,
            softMaskInData: 1);

        var rejected = false;

        try
        {
            SupportedFormatRegistry.ReadDocument(invalidPath);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(
            rejected,
            "PDF JPX /SMaskInData=1 без embedded opacity channel должен завершаться ошибкой.");
    }

    private static void TestPdfJbig2Reader(
        string directory)
    {
        var embedded =
            Convert.FromBase64String(
                "AAAAADAAAQAAABMAAAAgAAAAEAAAAAAAAAAAAQAAAAAAASYAAQAAADMAAAAgAAAAEAAAAAAAAAAAAAAD//3/Av7+/v9SWaqXAS01C97qHUhYDdtf/3/9lm1P/6w=");

        var direct =
            PdfJbig2SampleDecoder.Decode(
                embedded,
                Array.Empty<byte>(),
                width: 32,
                height: 16);

        var expectedRows =
            new[]
            {
                "################################",
                "###...###...###...###...###...##",
                "#..###...###...###...###...###.#",
                "#..###...###...###...###...###.#",
                "###...###...###...###...###...##",
                "###...###...###...###...###...##",
                "#..###...###...###...###...###.#",
                "#..###...###...###...###...###.#",
                "###...###...###...###...###...##",
                "###...###...###...###...###...##",
                "#..###...###...###...###...###.#",
                "#..###...###...###...###...###.#",
                "###...###...###...###...###...##",
                "###...###...###...###...###...##",
                "#..###...###...###...###...###.#",
                "################################"
            };

        Assert(
            direct.Length == 32 * 16 &&
            Enumerable.Range(0, 16)
                .All(row =>
                    Enumerable.Range(0, 32)
                        .All(column =>
                            direct[row * 32 + column] ==
                            (expectedRows[row][column] == '#'
                                ? (byte)0
                                : (byte)255))),
            "PDF JBIG2 decoder должен точно воспроизводить upstream jbig2enc -p fixture в Gray8 polarity.");

        // T.88 symbol dictionary segment, SDHUFF=1, with zero exported/new symbols.
        // No valid raster is implied: unsupported Huffman coding must never
        // be silently interpreted as an arithmetic dictionary.
        var huffmanDictionary = new byte[]
        {
            0, 0, 0, 1, // segment number
            0,          // segment type: symbol dictionary
            0,          // no referred segments
            1,          // page association
            0, 0, 0, 10, // data length
            0, 1,       // SDHUFF bit
            0, 0, 0, 0, // SDNUMEXSYMS
            0, 0, 0, 0  // SDNUMNEWSYMS
        };

        var huffmanRejected = false;

        try
        {
            PdfJbig2SampleDecoder.Decode(
                huffmanDictionary,
                Array.Empty<byte>(),
                32,
                16);
        }
        catch (NotSupportedException error)
            when (error.Message.Contains("Huffman", StringComparison.OrdinalIgnoreCase) ||
                  error.Message.Contains("SDHUFF", StringComparison.OrdinalIgnoreCase))
        {
            huffmanRejected = true;
        }

        Assert(
            huffmanRejected,
            "PDF JBIG2 SDHUFF должен завершаться явным unsupported, а не возвращать правдоподобный raster.");

        var plainPath =
            Path.Combine(
                directory,
                "regression-jbig2.pdf");
        CreateJbig2ImagePdf(
            plainPath,
            embedded,
            globals: null,
            reverseDecode: false);

        var plain =
            DecodeGray8Pixels(
                SupportedFormatRegistry
                    .ReadDocument(plainPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            plain.Width == 32 &&
            plain.Height == 16 &&
            plain.Pixels[0] == 0 &&
            plain.Pixels[35] == 255,
            "PDF JBIG2 Image XObject должен рендерить black/white samples через DeviceGray.");

        var inversePath =
            Path.Combine(
                directory,
                "regression-jbig2-inverse.pdf");
        CreateJbig2ImagePdf(
            inversePath,
            embedded,
            globals: null,
            reverseDecode: true);

        var inverse =
            DecodeGray8Pixels(
                SupportedFormatRegistry
                    .ReadDocument(inversePath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            inverse.Pixels[0] == 255 &&
            inverse.Pixels[35] == 0,
            "PDF JBIG2 должен применять PDF /Decode после JBIG2 reconstruction.");

        var globals =
            embedded[..30]
                .ToArray();
        globals[27] |= 0x04;
        var pageOnly =
            embedded[30..];

        var globalsPath =
            Path.Combine(
                directory,
                "regression-jbig2-globals.pdf");
        CreateJbig2ImagePdf(
            globalsPath,
            pageOnly,
            globals,
            reverseDecode: false);

        var globalPixels =
            DecodeGray8Pixels(
                SupportedFormatRegistry
                    .ReadDocument(globalsPath)
                    .Blocks
                    .OfType<DocumentFixedPage>()
                    .Single()
                    .ImageRuns
                    .Single()
                    .Data);

        Assert(
            globalPixels.Pixels.All(
                value => value == 0),
            "PDF /JBIG2Globals должен декодироваться перед embedded stream и участвовать в page state.");

        var customHuffmanTable =
            new byte[]
            {
                0, 0, 0, 1,
                53,
                0,
                1,
                0, 0, 0, 4,
                0, 0, 0, 0
            };
        var unsupported = false;

        try
        {
            _ = PdfJbig2SampleDecoder.Decode(
                customHuffmanTable,
                Array.Empty<byte>(),
                width: 8,
                height: 8);
        }
        catch (NotSupportedException)
        {
            unsupported = true;
        }

        Assert(
            unsupported,
            "PDF JBIG2 custom Huffman table должен оставаться явным unsupported.");
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

    private static void TestPdfCcittDecoder(string directory)
    {
        const int width = 16;
        const int height = 4;
        const int stride = 2;

        byte[] packed =
        [
            0b10101010, 0b01010101,
            0b11110000, 0b00001111,
            0b11001100, 0b00110011,
            0b00000000, 0b11111111
        ];

        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.BlackWhite,
            null,
            packed,
            stride);

        var encoder = new TiffBitmapEncoder
        {
            Compression = TiffCompressOption.Ccitt4
        };
        encoder.Frames.Add(
            BitmapFrame.Create(source));

        byte[] tiff;

        using (var memory = new MemoryStream())
        {
            encoder.Save(memory);
            tiff = memory.ToArray();
        }

        var ifdOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            tiff.AsSpan(
                4,
                4)));
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(
            tiff.AsSpan(
                ifdOffset,
                2));
        uint stripOffset = 0;
        uint stripByteCount = 0;
        ushort compression = 0;
        ushort photometric = 0;

        for (var index = 0;
             index < entryCount;
             index++)
        {
            var entryOffset =
                ifdOffset +
                2 +
                index * 12;
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(
                tiff.AsSpan(
                    entryOffset,
                    2));
            var type = BinaryPrimitives.ReadUInt16LittleEndian(
                tiff.AsSpan(
                    entryOffset + 2,
                    2));
            var count = BinaryPrimitives.ReadUInt32LittleEndian(
                tiff.AsSpan(
                    entryOffset + 4,
                    4));

            if (count != 1)
            {
                continue;
            }

            var value = type == 3
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    tiff.AsSpan(
                        entryOffset + 8,
                        2))
                : BinaryPrimitives.ReadUInt32LittleEndian(
                    tiff.AsSpan(
                        entryOffset + 8,
                        4));

            switch (tag)
            {
                case 259:
                    compression = checked((ushort)value);
                    break;

                case 262:
                    photometric = checked((ushort)value);
                    break;

                case 273:
                    stripOffset = value;
                    break;

                case 279:
                    stripByteCount = value;
                    break;
            }
        }

        AssertEqual(
            (ushort)4,
            compression,
            "TIFF CCITT regression compression");

        Assert(
            stripOffset > 0 &&
            stripByteCount > 0 &&
            stripOffset + stripByteCount <= tiff.Length,
            "TIFF CCITT regression strip должен быть валидным.");

        var strip = tiff
            .AsSpan(
                checked((int)stripOffset),
                checked((int)stripByteCount))
            .ToArray();

        var decoded = PdfCcittFaxDecoder.Decode(
            strip,
            width,
            height,
            k: -1,
            endOfLine: false,
            encodedByteAlign: false,
            blackIs1: photometric == 0);

        BitmapSource reference = source;
        if (reference.Format != PixelFormats.Gray8)
        {
            reference = new FormatConvertedBitmap(
                reference,
                PixelFormats.Gray8,
                null,
                0);
        }

        var expected = new byte[
            width *
            height];
        reference.CopyPixels(
            expected,
            width,
            0);

        Assert(
            decoded.SequenceEqual(expected),
            "PDF CCITTFaxDecode Group 4 должен совпадать с эталонным TIFF T.6 decode.");

        var pdfPath = Path.Combine(
            directory,
            "regression-ccitt.pdf");
        CreateCcittImagePdf(
            pdfPath,
            strip,
            width,
            height,
            blackIs1: photometric == 0);

        var model = SupportedFormatRegistry.ReadDocument(
            pdfPath);
        var image = model.Blocks
            .OfType<DocumentFixedPage>()
            .Single()
            .ImageRuns
            .Single();

        Assert(
            string.Equals(
                image.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            image.Data.Length > 8 &&
            image.Data.AsSpan(
                    0,
                    8)
                .SequenceEqual(
                    new byte[]
                    {
                        137, 80, 78, 71,
                        13, 10, 26, 10
                    }),
            "PDF CCITTFaxDecode Image XObject должен пройти полный PDF reader pipeline в PNG.");
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

        var namedCMapPath = Path.Combine(
            directory,
            "regression-named-cmap.pdf");
        CreateNamedCMapPdf(
            namedCMapPath);

        var namedCMapModel =
            SupportedFormatRegistry.ReadDocument(
                namedCMapPath);
        var namedCMapPage =
            namedCMapModel.Blocks
                .OfType<DocumentFixedPage>()
                .Single();

        AssertContains(
            namedCMapPage.Text,
            "A",
            "PDF predefined UniJIS-UCS2-H должен декодироваться через code-to-CID и Adobe-Japan1-UCS2 без /ToUnicode.");

        var horizontalCMap =
            PdfPredefinedCMapRegistry.Get(
                "90ms-RKSJ-H");
        var horizontalPosition = 0;

        Assert(
            horizontalCMap.TryReadCid(
                [0x81, 0x41],
                ref horizontalPosition,
                out var horizontalCid,
                out var horizontalLength) &&
            horizontalCid == 634 &&
            horizontalLength == 2 &&
            horizontalPosition == 2,
            "PDF predefined horizontal CMap должен читать canonical Adobe code-to-CID mapping.");

        var verticalCMap =
            PdfPredefinedCMapRegistry.Get(
                "90ms-RKSJ-V");
        var verticalPosition = 0;

        Assert(
            verticalCMap.TryReadCid(
                [0x81, 0x41],
                ref verticalPosition,
                out var verticalCid,
                out var verticalLength) &&
            verticalCid == 7887 &&
            verticalLength == 2 &&
            verticalPosition == 2,
            "PDF predefined vertical CMap должен применять usecmap inheritance и vertical override.");

        var cidUnicodeDecoder =
            PdfCidUnicodeRegistry.TryGet(
                "Adobe",
                "Japan1");

        Assert(
            cidUnicodeDecoder != null &&
            string.Equals(
                cidUnicodeDecoder.Decode(
                    [0x00, 0x22]),
                "A",
                StringComparison.Ordinal),
            "PDF Adobe-Japan1 CID-to-Unicode resource должен декодировать CID 34 как A.");

        var encryptedPath = Path.Combine(
            directory,
            "regression-encrypted.pdf");
        CreateEncryptedPdf(
            encryptedPath);

        var encryptedModel =
            SupportedFormatRegistry.ReadDocument(
                encryptedPath);
        var encryptedPage = encryptedModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertContains(
            encryptedPage.Text,
            "Encrypted marker",
            "PDF Standard Security Handler R2 empty-password decrypt");

        var encryptedObjectStreamPath = Path.Combine(
            directory,
            "regression-encrypted-object-stream.pdf");
        CreateEncryptedObjectStreamPdf(
            encryptedObjectStreamPath);

        var encryptedObjectStreamModel =
            SupportedFormatRegistry.ReadDocument(
                encryptedObjectStreamPath);
        var encryptedObjectStreamPage =
            encryptedObjectStreamModel.Blocks
                .OfType<DocumentFixedPage>()
                .Single();

        AssertContains(
            encryptedObjectStreamPage.Text,
            "Encrypted object stream marker",
            "PDF encrypted object stream decrypt");

        var aesEncryptedPath = Path.Combine(
            directory,
            "regression-aesv2.pdf");
        CreateAesV2EncryptedPdf(
            aesEncryptedPath);

        var aesEncryptedModel =
            SupportedFormatRegistry.ReadDocument(
                aesEncryptedPath);
        var aesEncryptedPage = aesEncryptedModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertContains(
            aesEncryptedPage.Text,
            "AES default marker",
            "PDF AESV2 default StmF decrypt");
        AssertContains(
            aesEncryptedPage.Text,
            "AES explicit marker",
            "PDF AESV2 explicit Crypt filter decrypt");

        var runLengthPath = Path.Combine(
            directory,
            "regression-runlength.pdf");
        CreateRunLengthPdf(
            runLengthPath,
            "PDF RunLength marker");

        var runLengthModel =
            SupportedFormatRegistry.ReadDocument(
                runLengthPath);
        var runLengthPage = runLengthModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertContains(
            runLengthPage.Text,
            "PDF RunLength marker",
            "PDF RunLengthDecode content stream");

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

        var dctImagePath = Path.Combine(
            directory,
            "regression-dct-image.pdf");
        CreateDctImagePdf(
            dctImagePath);

        var dctImageModel =
            SupportedFormatRegistry.ReadDocument(
                dctImagePath);
        var dctImagePage = dctImageModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();
        var dctImage = dctImagePage.ImageRuns.Single();

        Assert(
            string.Equals(
                dctImage.ContentType,
                "image/png",
                StringComparison.Ordinal) &&
            dctImage.Data.Length > 8 &&
            dctImage.Data.AsSpan(0, 8).SequenceEqual(
                new byte[]
                {
                    137, 80, 78, 71,
                    13, 10, 26, 10
                }) &&
            string.Equals(
                dctImage.SoftMaskContentType,
                "image/png",
                StringComparison.Ordinal) &&
            dctImage.SoftMaskData is { Length: > 8 } &&
            dctImage.SoftMaskData.AsSpan(0, 8).SequenceEqual(
                new byte[]
                {
                    137, 80, 78, 71,
                    13, 10, 26, 10
                }),
            "PDF DCT Image XObject должен декодировать нестандартный /Decode и DCT /SMask /Matte в PNG resources.");

        var extGStatePath = Path.Combine(
            directory,
            "regression-extgstate.pdf");
        CreateExtGStatePdf(extGStatePath);

        foreach (var (suffix, mask) in new[]
        {
            ("missing-group", "<< /S /Alpha >>"),
            ("invalid-subtype", "<< /S /Bogus >>"),
            ("invalid-group-type", "<< /S /Alpha /G /NotAStream >>")
        })
        {
            var invalidPath = Path.Combine(
                directory,
                $"regression-extgstate-smask-{suffix}.pdf");
            CreateExtGStatePdf(invalidPath, mask);
            var rejected = false;

            try
            {
                _ = SupportedFormatRegistry.ReadDocument(invalidPath);
            }
            catch (InvalidDataException error)
                when (error.Message.Contains("/SMask", StringComparison.Ordinal))
            {
                rejected = true;
            }

            Assert(
                rejected,
                $"PDF ExtGState /SMask must reject {suffix} with explicit diagnostics.");
        }

        foreach (var (suffix, form) in new[]
        {
            ("missing-bbox", "<< /Type /XObject /Subtype /Form /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream"),
            ("wrong-group", "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Other >> /Length 0 >>\nstream\n\nendstream"),
            ("zero-area", "<< /Type /XObject /Subtype /Form /BBox [0 0 0 10] /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream"),
            ("reverse-bbox", "<< /Type /XObject /Subtype /Form /BBox [10 0 0 10] /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream"),
            ("invalid-isolated", "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /I 1 >> /Length 0 >>\nstream\n\nendstream"),
            ("invalid-knockout", "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /K /True >> /Length 0 >>\nstream\n\nendstream"),
            ("invalid-matrix-length", "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [1 0 0 1 0] /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream"),
            ("singular-matrix", "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [1 0 0 0 0 0] /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream")
        })
        {
            var invalidPath = Path.Combine(directory, $"regression-smask-form-{suffix}.pdf");
            CreateExtGStatePdf(invalidPath, "<< /S /Alpha /G 7 0 R >>", form);
            var rejected = false;
            try
            {
                _ = SupportedFormatRegistry.ReadDocument(invalidPath);
            }
            catch (InvalidDataException error)
                when (error.Message.Contains("/SMask /G", StringComparison.Ordinal))
            {
                rejected = true;
            }

            Assert(rejected, $"PDF ExtGState /SMask /G must reject {suffix}.");
        }

        foreach (var (suffix, mask) in new[]
        {
            ("nonidentity-transfer", "<< /S /Alpha /TR << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> /G 7 0 R >>"),
            ("backdrop-color", "<< /S /Luminosity /BC [1 1 1] /G 7 0 R >>")
        })
        {
            var unsupportedPath = Path.Combine(
                directory, $"regression-smask-{suffix}.pdf");
            CreateExtGStatePdf(
                unsupportedPath,
                mask,
                "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency >> /Length 0 >>\nstream\n\nendstream");

            var rejected = false;
            try
            {
                _ = SupportedFormatRegistry.ReadDocument(unsupportedPath);
            }
            catch (NotSupportedException error)
                when (error.Message.Contains("/SMask", StringComparison.Ordinal))
            {
                rejected = true;
            }

            Assert(rejected,
                $"PDF ExtGState /SMask {suffix} должен явно отклоняться до compositing.");
        }

        var validMaskGroupPath = Path.Combine(
            directory,
            "regression-smask-form-valid-unsupported.pdf");
        CreateExtGStatePdf(
            validMaskGroupPath,
            "<< /S /Alpha /G 7 0 R >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /I true /K false >> /Length 0 >>\nstream\n\nendstream");

        var realMaskRejected = false;
        try
        {
            _ = SupportedFormatRegistry.ReadDocument(validMaskGroupPath);
        }
        catch (NotSupportedException error)
            when (error.Message.Contains("compositing", StringComparison.Ordinal))
        {
            realMaskRejected = true;
        }

        Assert(
            realMaskRejected,
            "PDF ExtGState /SMask /G must explicitly reject real transparency groups until compositing is implemented.");

        var extGStateModel = SupportedFormatRegistry.ReadDocument(extGStatePath);
        var extGStatePage = extGStateModel.Blocks
            .OfType<DocumentFixedPage>()
            .Single();

        AssertEqual(
            4,
            extGStatePage.PathRuns.Count,
            "PDF ExtGState fixture path count");

        var firstPath = extGStatePage.PathRuns[0];
        var nestedPath = extGStatePage.PathRuns[1];
        var restoredPath = extGStatePage.PathRuns[2];
        var defaultPath = extGStatePage.PathRuns[3];

        Assert(
            firstPath.BlendMode == "Multiply" &&
            firstPath.FillOverprint &&
            firstPath.StrokeOverprint &&
            firstPath.OverprintMode == 1 &&
            !firstPath.SoftMaskActive &&
            Math.Abs(firstPath.FillOpacity - 0.5) < 0.0001 &&
            Math.Abs(firstPath.StrokeOpacity - 0.75) < 0.0001,
            "PDF ExtGState должен применять BM/OP/op/OPM/SMask/CA/ca к path state.");

        Assert(
            nestedPath.BlendMode == "Screen" &&
            !nestedPath.FillOverprint &&
            !nestedPath.StrokeOverprint &&
            nestedPath.OverprintMode == 0 &&
            !nestedPath.SoftMaskActive,
            "PDF nested ExtGState должен заменять blend/overprint/soft-mask state.");

        Assert(
            restoredPath.BlendMode == "Multiply" &&
            restoredPath.FillOverprint &&
            restoredPath.StrokeOverprint &&
            restoredPath.OverprintMode == 1 &&
            !restoredPath.SoftMaskActive,
            "PDF Q должен восстанавливать ExtGState composite parameters.");

        Assert(
            defaultPath.BlendMode == "Normal" &&
            !defaultPath.FillOverprint &&
            !defaultPath.StrokeOverprint &&
            defaultPath.OverprintMode == 0 &&
            !defaultPath.SoftMaskActive,
            "PDF outer Q должен восстанавливать default composite state.");

        var extGStateHtml = DocumentHtmlRenderer.Render(
            extGStateModel,
            CreateRenderTheme());
        AssertContains(
            extGStateHtml,
            "data-pdf-blend-mode=\"Multiply\" style=\"mix-blend-mode:multiply;\"",
            "PDF renderer blend mode");
        AssertContains(
            extGStateHtml,
            "data-pdf-overprint-stroke=\"true\" data-pdf-overprint-fill=\"true\" data-pdf-overprint-mode=\"1\"",
            "PDF renderer overprint/soft-mask metadata");
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

    /// <summary>
    /// Открывает весь локальный Books corpus через тот же registry, что использует UI,
    /// и проверяет полный reader → neutral model → HTML renderer pipeline.
    /// Direct HTML проверяется отдельно как существующий непустой пользовательский файл.
    /// </summary>
    private static void TestSupportedCorpusSmoke(string repositoryRoot)
    {
        var booksDirectory = Path.Combine(
            repositoryRoot,
            "Books");
        var files = Directory
            .EnumerateFiles(
                booksDirectory,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var supportedFiles = files
            .Where(path =>
            {
                try
                {
                    SupportedFormatRegistry.GetByPath(path);
                    return true;
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            })
            .ToArray();

        Assert(
            supportedFiles.Length > 0,
            "Books corpus не содержит поддерживаемых документов.");

        var renderedCount = 0;
        var directHtmlCount = 0;
        var theme = CreateRenderTheme();

        foreach (var path in supportedFiles)
        {
            var format = SupportedFormatRegistry.GetByPath(path);

            if (format.OpenDirectlyInWebView)
            {
                var info = new FileInfo(path);

                Assert(
                    info.Exists &&
                    info.Length > 0,
                    $"Direct HTML corpus file пуст или отсутствует: {path}.");

                directHtmlCount++;
                continue;
            }

            var model = SupportedFormatRegistry.ReadDocument(path);

            Assert(
                !string.IsNullOrWhiteSpace(model.Title),
                $"Corpus reader не вернул title: {path}.");

            Assert(
                model.Blocks.Count > 0,
                $"Corpus reader не вернул blocks: {path}.");

            var html = DocumentHtmlRenderer.Render(
                model,
                theme);

            Assert(
                html.Contains(
                    "<!DOCTYPE html>",
                    StringComparison.OrdinalIgnoreCase),
                $"Corpus renderer не вернул HTML document: {path}.");

            renderedCount++;
        }

        Assert(
            renderedCount > 0,
            "Corpus smoke не проверил ни одного reader-backed документа.");

        Console.WriteLine(
            $"CORPUS supported={supportedFiles.Length} rendered={renderedCount} direct-html={directHtmlCount}");
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

    private static void CreateExtGStatePdf(string path, string? softMaskSyntax = null, string? groupForm = null)
    {
        var encoding = Encoding.ASCII;
        var content =
            "q\n" +
            "/GS1 gs\n" +
            "10 10 40 40 re B\n" +
            "q\n" +
            "/GS2 gs\n" +
            "60 10 40 40 re B\n" +
            "Q\n" +
            "110 10 40 40 re B\n" +
            "Q\n" +
            "160 10 40 40 re B\n";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 240 120] >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /ExtGState << /GS1 5 0 R /GS2 6 0 R >> >> >>",
            $"<< /Length {encoding.GetByteCount(content)} >>\nstream\n{content}endstream",
            $"<< /Type /ExtGState /BM [/UnknownVendorMode /Multiply] /OP true /op true /OPM 1 /SMask {softMaskSyntax ?? "/None"} /CA 0.75 /ca 0.5 >>",
            "<< /Type /ExtGState /BM /Screen /OP false /op false /OPM 0 /SMask /None >>"
        };

        if (groupForm != null)
        {
            objects = [.. objects, groupForm];
        }

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.7\n");
        var offsets = new List<long>();

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(memory.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = memory.Position;
        Write("xref\n" + $"0 {objects.Length + 1}\n" + "0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write(
            "trailer\n" +
            $"<< /Size {objects.Length + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(path, memory.ToArray());
    }

    private static void CreateEncryptedPdf(
        string path)
    {
        var encoding = Encoding.ASCII;
        var content = encoding.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "(Encrypted marker) Tj\n" +
            "ET\n");
        byte[] passwordPadding =
        [
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
            0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
            0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        ];
        var ownerKey = MD5.HashData(passwordPadding)[..5];
        var owner = Rc4(passwordPadding, ownerKey);
        var fileId = MD5.HashData(encoding.GetBytes("MYBOOK encrypted fixture"));
        const int permissions = -4;
        var keyMaterial = new byte[
            passwordPadding.Length +
            owner.Length +
            4 +
            fileId.Length];
        passwordPadding.CopyTo(
            keyMaterial,
            0);
        owner.CopyTo(
            keyMaterial,
            passwordPadding.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            keyMaterial.AsSpan(
                passwordPadding.Length + owner.Length,
                4),
            permissions);
        fileId.CopyTo(
            keyMaterial,
            passwordPadding.Length + owner.Length + 4);
        var fileKey = MD5.HashData(keyMaterial)[..5];
        var user = Rc4(passwordPadding, fileKey);

        var objectKeyMaterial = new byte[fileKey.Length + 5];
        fileKey.CopyTo(
            objectKeyMaterial,
            0);
        objectKeyMaterial[fileKey.Length] = 4;
        var objectKey = MD5.HashData(objectKeyMaterial)[..10];
        var encryptedContent = Rc4(content, objectKey);

        static byte[] Rc4(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> key)
        {
            var state = Enumerable
                .Range(0, 256)
                .Select(value => checked((byte)value))
                .ToArray();
            var j = 0;

            for (var i = 0; i < 256; i++)
            {
                j =
                    (j + state[i] + key[i % key.Length]) &
                    0xFF;
                (state[i], state[j]) =
                    (state[j], state[i]);
            }

            var output = new byte[data.Length];
            var x = 0;
            j = 0;

            for (var index = 0; index < data.Length; index++)
            {
                x =
                    (x + 1) &
                    0xFF;
                j =
                    (j + state[x]) &
                    0xFF;
                (state[x], state[j]) =
                    (state[j], state[x]);
                output[index] =
                    checked((byte)(
                        data[index] ^
                        state[(state[x] + state[j]) & 0xFF]));
            }

            return output;
        }

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
            string value)
        {
            offsets.Add(memory.Position);
            Write($"{objectNumber} 0 obj\n{value}\nendobj\n");
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
            $"4 0 obj\n<< /Length {encryptedContent.Length} >>\nstream\n");
        memory.Write(
            encryptedContent,
            0,
            encryptedContent.Length);
        Write("\nendstream\nendobj\n");

        WriteObject(
            5,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        WriteObject(
            6,
            $"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {permissions} >>");

        var xref = memory.Position;
        Write("xref\n0 7\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        var idHex = Convert.ToHexString(fileId);
        Write(
            "trailer\n" +
            $"<< /Size 7 /Root 1 0 R /Encrypt 6 0 R /ID [<{idHex}> <{idHex}>] >>\n" +
            "startxref\n" +
            xref.ToString(CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }
    private static void CreateEncryptedObjectStreamPdf(
        string path)
    {
        var encoding = Encoding.ASCII;
        byte[] passwordPadding =
        [
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
            0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
            0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        ];

        static byte[] Rc4(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> key)
        {
            var state = Enumerable
                .Range(0, 256)
                .Select(value => checked((byte)value))
                .ToArray();
            var j = 0;

            for (var i = 0; i < 256; i++)
            {
                j =
                    (j + state[i] + key[i % key.Length]) &
                    0xFF;
                (state[i], state[j]) =
                    (state[j], state[i]);
            }

            var output = new byte[data.Length];
            var x = 0;
            j = 0;

            for (var index = 0; index < data.Length; index++)
            {
                x =
                    (x + 1) &
                    0xFF;
                j =
                    (j + state[x]) &
                    0xFF;
                (state[x], state[j]) =
                    (state[j], state[x]);
                output[index] =
                    checked((byte)(
                        data[index] ^
                        state[(state[x] + state[j]) & 0xFF]));
            }

            return output;
        }

        static byte[] CreateObjectKey(
            byte[] fileKey,
            int objectNumber)
        {
            var material = new byte[fileKey.Length + 5];
            fileKey.CopyTo(
                material,
                0);
            material[fileKey.Length] =
                checked((byte)(objectNumber & 0xFF));
            material[fileKey.Length + 1] =
                checked((byte)((objectNumber >> 8) & 0xFF));
            material[fileKey.Length + 2] =
                checked((byte)((objectNumber >> 16) & 0xFF));
            return MD5.HashData(material)[
                ..Math.Min(
                    fileKey.Length + 5,
                    16)];
        }

        var ownerKey = MD5.HashData(passwordPadding)[..5];
        var owner = Rc4(passwordPadding, ownerKey);
        var fileId = MD5.HashData(
            encoding.GetBytes(
                "MYBOOK encrypted object stream fixture"));
        const int permissions = -4;
        var keyMaterial = new byte[
            passwordPadding.Length +
            owner.Length +
            4 +
            fileId.Length];
        passwordPadding.CopyTo(
            keyMaterial,
            0);
        owner.CopyTo(
            keyMaterial,
            passwordPadding.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            keyMaterial.AsSpan(
                passwordPadding.Length + owner.Length,
                4),
            permissions);
        fileId.CopyTo(
            keyMaterial,
            passwordPadding.Length + owner.Length + 4);
        var fileKey = MD5.HashData(keyMaterial)[..5];
        var user = Rc4(passwordPadding, fileKey);

        var content = encoding.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "(Encrypted object stream marker) Tj\n" +
            "ET\n");
        var encryptedContent = Rc4(
            content,
            CreateObjectKey(
                fileKey,
                4));

        var objectStreamPlain = encoding.GetBytes(
            "5 0 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var encryptedObjectStream = Rc4(
            objectStreamPlain,
            CreateObjectKey(
                fileKey,
                6));

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        var offsets = new Dictionary<int, long>();

        void BeginObject(int objectNumber)
        {
            offsets[objectNumber] =
                memory.Position;
            Write(
                $"{objectNumber} 0 obj\n");
        }

        Write("%PDF-1.5\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {encryptedContent.Length} >>\nstream\n");
        memory.Write(
            encryptedContent,
            0,
            encryptedContent.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(6);
        Write(
            $"<< /Type /ObjStm /N 1 /First 4 /Length {encryptedObjectStream.Length} >>\nstream\n");
        memory.Write(
            encryptedObjectStream,
            0,
            encryptedObjectStream.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(7);
        Write(
            $"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {permissions} >>\nendobj\n");

        var xrefOffset = memory.Position;
        offsets[8] = xrefOffset;
        var xrefData = new byte[9 * 7];

        void WriteXrefEntry(
            int objectNumber,
            byte type,
            uint field2,
            ushort field3)
        {
            var offset =
                objectNumber * 7;
            xrefData[offset] =
                type;
            BinaryPrimitives.WriteUInt32BigEndian(
                xrefData.AsSpan(
                    offset + 1,
                    4),
                field2);
            BinaryPrimitives.WriteUInt16BigEndian(
                xrefData.AsSpan(
                    offset + 5,
                    2),
                field3);
        }

        WriteXrefEntry(
            0,
            0,
            0,
            ushort.MaxValue);

        foreach (var objectNumber in new[] { 1, 2, 3, 4, 6, 7, 8 })
        {
            WriteXrefEntry(
                objectNumber,
                1,
                checked((uint)offsets[objectNumber]),
                0);
        }

        WriteXrefEntry(
            5,
            2,
            6,
            0);

        var idHex = Convert.ToHexString(fileId);

        BeginObject(8);
        Write(
            $"<< /Type /XRef /Size 9 /W [1 4 2] /Root 1 0 R /Encrypt 7 0 R /ID [<{idHex}> <{idHex}>] /Length {xrefData.Length} >>\nstream\n");
        memory.Write(
            xrefData,
            0,
            xrefData.Length);
        Write("\nendstream\nendobj\n");
        Write(
            "startxref\n" +
            xrefOffset.ToString(CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static void CreateAesV2EncryptedPdf(
        string path)
    {
        var encoding = Encoding.ASCII;
        byte[] passwordPadding =
        [
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
            0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
            0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        ];

        static byte[] Rc4(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> key)
        {
            var state = Enumerable
                .Range(0, 256)
                .Select(value => checked((byte)value))
                .ToArray();
            var j = 0;

            for (var i = 0; i < 256; i++)
            {
                j =
                    (j + state[i] + key[i % key.Length]) &
                    0xFF;
                (state[i], state[j]) =
                    (state[j], state[i]);
            }

            var output = new byte[data.Length];
            var x = 0;
            j = 0;

            for (var index = 0; index < data.Length; index++)
            {
                x =
                    (x + 1) &
                    0xFF;
                j =
                    (j + state[x]) &
                    0xFF;
                (state[x], state[j]) =
                    (state[j], state[x]);
                output[index] =
                    checked((byte)(
                        data[index] ^
                        state[(state[x] + state[j]) & 0xFF]));
            }

            return output;
        }

        static byte[] CreateObjectKey(
            byte[] fileKey,
            int objectNumber)
        {
            var material = new byte[
                fileKey.Length +
                9];
            fileKey.CopyTo(
                material,
                0);
            material[fileKey.Length] =
                checked((byte)(objectNumber & 0xFF));
            material[fileKey.Length + 1] =
                checked((byte)((objectNumber >> 8) & 0xFF));
            material[fileKey.Length + 2] =
                checked((byte)((objectNumber >> 16) & 0xFF));
            material[^4] = 0x73;
            material[^3] = 0x41;
            material[^2] = 0x6C;
            material[^1] = 0x54;
            return MD5.HashData(material)[..16];
        }

        static byte[] AesEncrypt(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> key,
            byte ivSeed)
        {
            var iv = Enumerable
                .Range(0, 16)
                .Select(value => checked((byte)(ivSeed + value)))
                .ToArray();

            using var aes = Aes.Create();
            aes.Key = key.ToArray();
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor =
                aes.CreateEncryptor();
            var encrypted =
                encryptor.TransformFinalBlock(
                    data.ToArray(),
                    0,
                    data.Length);
            var result = new byte[
                iv.Length +
                encrypted.Length];
            iv.CopyTo(
                result,
                0);
            encrypted.CopyTo(
                result,
                iv.Length);
            return result;
        }

        static byte[] CreateOwnerEntry(
            byte[] padding)
        {
            var hash = MD5.HashData(padding);
            for (var index = 0; index < 50; index++)
            {
                hash = MD5.HashData(hash);
            }

            var key = hash[..16];
            var value = Rc4(
                padding,
                key);

            for (var iteration = 1; iteration <= 19; iteration++)
            {
                var iterationKey =
                    key.Select(
                            item =>
                                checked((byte)(item ^ iteration)))
                        .ToArray();
                value = Rc4(
                    value,
                    iterationKey);
            }

            return value;
        }

        var owner =
            CreateOwnerEntry(
                passwordPadding);
        var fileId =
            MD5.HashData(
                encoding.GetBytes(
                    "MYBOOK AESV2 fixture"));
        const int permissions = -4;
        var keyMaterial = new byte[
            passwordPadding.Length +
            owner.Length +
            4 +
            fileId.Length +
            4];
        passwordPadding.CopyTo(
            keyMaterial,
            0);
        owner.CopyTo(
            keyMaterial,
            passwordPadding.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            keyMaterial.AsSpan(
                passwordPadding.Length + owner.Length,
                4),
            permissions);
        fileId.CopyTo(
            keyMaterial,
            passwordPadding.Length + owner.Length + 4);
        keyMaterial[^4] = 0xFF;
        keyMaterial[^3] = 0xFF;
        keyMaterial[^2] = 0xFF;
        keyMaterial[^1] = 0xFF;

        var hash = MD5.HashData(
            keyMaterial);
        for (var index = 0; index < 50; index++)
        {
            hash = MD5.HashData(hash);
        }
        var fileKey = hash[..16];

        var userSeed = new byte[
            passwordPadding.Length +
            fileId.Length];
        passwordPadding.CopyTo(
            userSeed,
            0);
        fileId.CopyTo(
            userSeed,
            passwordPadding.Length);
        var userValue = Rc4(
            MD5.HashData(
                userSeed),
            fileKey);
        for (var iteration = 1; iteration <= 19; iteration++)
        {
            var iterationKey =
                fileKey.Select(
                        item =>
                            checked((byte)(item ^ iteration)))
                    .ToArray();
            userValue = Rc4(
                userValue,
                iterationKey);
        }

        var user = new byte[32];
        userValue.CopyTo(
            user,
            0);

        var defaultContent = encoding.GetBytes(
            "BT\n/F1 12 Tf\n1 0 0 1 20 120 Tm\n(AES default marker) Tj\nET\n");
        var explicitContent = encoding.GetBytes(
            "BT\n/F1 12 Tf\n1 0 0 1 20 90 Tm\n(AES explicit marker) Tj\nET\n");
        var encryptedDefault =
            AesEncrypt(
                defaultContent,
                CreateObjectKey(
                    fileKey,
                    4),
                0x10);
        var encryptedExplicit =
            AesEncrypt(
                explicitContent,
                fileKey,
                0x30);

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        var offsets = new List<long>();
        void BeginObject(int number)
        {
            offsets.Add(memory.Position);
            Write(
                $"{number} 0 obj\n");
        }

        Write("%PDF-1.7\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents [4 0 R 5 0 R] /Resources << /Font << /F1 6 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {encryptedDefault.Length} >>\nstream\n");
        memory.Write(
            encryptedDefault,
            0,
            encryptedDefault.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(5);
        Write(
            $"<< /Length {encryptedExplicit.Length} /Filter /Crypt /DecodeParms << /Name /StdCF >> >>\nstream\n");
        memory.Write(
            encryptedExplicit,
            0,
            encryptedExplicit.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(6);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        BeginObject(7);
        Write(
            $"<< /Filter /Standard /V 4 /R 4 /Length 128 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {permissions} /EncryptMetadata false /CF << /StdCF << /Type /CryptFilter /CFM /AESV2 /Length 16 >> >> /StmF /StdCF /StrF /StdCF >>\nendobj\n");

        var xref = memory.Position;
        Write("xref\n0 8\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write(
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        var idHex =
            Convert.ToHexString(
                fileId);
        Write(
            "trailer\n" +
            $"<< /Size 8 /Root 1 0 R /Encrypt 7 0 R /ID [<{idHex}> <{idHex}>] >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    /// <summary>
    /// Кодирует raster fixture в JPEG с максимальным quality для PDF DCT regressions.
    /// </summary>
    private static byte[] InjectAdobeApp14Transform(
        byte[] jpeg,
        byte transform)
    {
        if (jpeg.Length < 2 ||
            jpeg[0] != 0xFF ||
            jpeg[1] != 0xD8)
        {
            throw new ArgumentException(
                "JPEG fixture не содержит SOI marker.",
                nameof(jpeg));
        }

        byte[] marker =
        [
            0xFF, 0xEE,
            0x00, 0x0E,
            (byte)'A', (byte)'d', (byte)'o',
            (byte)'b', (byte)'e',
            0x00, 0x64,
            0x00, 0x00,
            0x00, 0x00,
            transform
        ];

        var result =
            new byte[checked(jpeg.Length + marker.Length)];
        Buffer.BlockCopy(
            jpeg,
            0,
            result,
            0,
            2);
        Buffer.BlockCopy(
            marker,
            0,
            result,
            2,
            marker.Length);
        Buffer.BlockCopy(
            jpeg,
            2,
            result,
            2 + marker.Length,
            jpeg.Length - 2);
        return result;
    }

    private static byte[] EncodeJpegFixture(
        byte[] pixels,
        int width,
        int height,
        PixelFormat format,
        int stride,
        double dpiX = 96,
        double dpiY = 96)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            dpiX,
            dpiY,
            format,
            null,
            pixels,
            stride);
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = 100
        };
        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>
    /// Кодирует PNG fixture с явно сохранённой физической DPI metadata.
    /// </summary>
    private static byte[] EncodePngFixture(
        byte[] pixels,
        int width,
        int height,
        PixelFormat format,
        int stride,
        double dpiX,
        double dpiY)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            dpiX,
            dpiY,
            format,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void CreateIccImagePdf(
        string path,
        byte[] profileData,
        byte[] imageSamples)
    {
        if (imageSamples.Length != 3)
        {
            throw new ArgumentException(
                "ICC RGB fixture должен содержать ровно один RGB pixel.",
                nameof(imageSamples));
        }

        CreateRawImagePdf(
            path,
            colorSpaceSyntax: "[/ICCBased 6 0 R]",
            imageSamples,
            profileData);
    }

    private static void CreateDeviceNImagePdf(
        string path)
    {
        CreateRawImagePdf(
            path,
            colorSpaceSyntax:
                "[/DeviceN [/SpotA /SpotB] /DeviceRGB 6 0 R]",
            imageSamples: [255, 0],
            profileData: null,
            functionData:
            [
                255, 255, 255,
                255, 0, 0,
                0, 255, 0,
                0, 0, 0
            ]);
    }

    private static void CreateSeparationImagePdf(
        string path,
        byte tintSample)
    {
        CreateRawImagePdf(
            path,
            colorSpaceSyntax:
                "[/Separation /SpotRed /DeviceRGB " +
                "<< /FunctionType 2 /Domain [0 1] " +
                "/C0 [1 1 1] /C1 [1 0 0] /N 1 >>]",
            [tintSample],
            profileData: null);
    }

    private static void CreateRawImagePdf(
        string path,
        string colorSpaceSyntax,
        byte[] imageSamples,
        byte[]? profileData,
        byte[]? functionData = null,
        byte[]? calculatorProgramData = null,
        byte[]? sampledOrder3Data = null,
        byte? softMaskSample = null,
        double[]? softMaskMatte = null)
    {
        var encoding = Encoding.ASCII;
        var content = encoding.GetBytes(
            "q\n" +
            "20 0 0 20 20 20 cm\n" +
            "/Im1 Do\n" +
            "Q\n");
        using var memory = new MemoryStream();
        var offsets = new List<long>();
        var hasAuxiliaryObject =
            profileData != null ||
            functionData != null ||
            calculatorProgramData != null ||
            sampledOrder3Data != null;
        var softMaskObjectNumber =
            softMaskSample != null
                ? (hasAuxiliaryObject ? 7 : 6)
                : (int?)null;

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void StartObject(int number)
        {
            if (number != offsets.Count + 1)
            {
                throw new InvalidOperationException(
                    "PDF fixture objects должны записываться последовательно.");
            }

            offsets.Add(memory.Position);
            Write($"{number} 0 obj\n");
        }

        void EndObject()
        {
            Write("\nendobj\n");
        }

        Write("%PDF-1.4\n");

        StartObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>");
        EndObject();

        StartObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
        EndObject();

        StartObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R " +
              "/Resources << /XObject << /Im1 5 0 R >> >> >>");
        EndObject();

        StartObject(4);
        Write($"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("endstream");
        EndObject();

        StartObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image " +
            "/Width 1 /Height 1 " +
            $"/ColorSpace {colorSpaceSyntax} " +
            "/BitsPerComponent 8 " +
            (softMaskObjectNumber != null
                ? $"/SMask {softMaskObjectNumber.Value} 0 R "
                : string.Empty) +
            $"/Length {imageSamples.Length} >>\nstream\n");
        memory.Write(
            imageSamples,
            0,
            imageSamples.Length);
        Write("\nendstream");
        EndObject();

        if (profileData != null)
        {
            StartObject(6);
            Write(
                $"<< /N 3 /Length {profileData.Length} >>\nstream\n");
            memory.Write(
                profileData,
                0,
                profileData.Length);
            Write("\nendstream");
            EndObject();
        }
        else if (functionData != null)
        {
            StartObject(6);
            Write(
                "<< /FunctionType 0 " +
                "/Domain [0 1 0 1] " +
                "/Range [0 1 0 1 0 1] " +
                "/Size [2 2] " +
                "/BitsPerSample 8 " +
                "/Encode [0 1 0 1] " +
                "/Decode [0 1 0 1 0 1] " +
                $"/Length {functionData.Length} >>\nstream\n");
            memory.Write(
                functionData,
                0,
                functionData.Length);
            Write("\nendstream");
            EndObject();
        }
        else if (calculatorProgramData != null)
        {
            StartObject(6);
            Write(
                "<< /FunctionType 4 " +
                "/Domain [0 1] " +
                "/Range [0 1 0 1 0 1] " +
                $"/Length {calculatorProgramData.Length} >>\nstream\n");
            memory.Write(
                calculatorProgramData,
                0,
                calculatorProgramData.Length);
            Write("\nendstream");
            EndObject();
        }
        else if (sampledOrder3Data != null)
        {
            StartObject(6);
            Write(
                "<< /FunctionType 0 " +
                "/Domain [0 1] " +
                "/Range [0 1 0 1 0 1] " +
                "/Size [4] " +
                "/BitsPerSample 8 " +
                "/Order 3 " +
                "/Encode [0 3] " +
                "/Decode [0 1 0 1 0 1] " +
                $"/Length {sampledOrder3Data.Length} >>\nstream\n");
            memory.Write(
                sampledOrder3Data,
                0,
                sampledOrder3Data.Length);
            Write("\nendstream");
            EndObject();
        }

        if (softMaskSample != null)
        {
            if (softMaskMatte == null ||
                softMaskMatte.Length == 0)
            {
                throw new ArgumentException(
                    "Soft-mask fixture требует непустой /Matte.",
                    nameof(softMaskMatte));
            }

            StartObject(
                softMaskObjectNumber
                ?? throw new InvalidOperationException(
                    "Soft-mask fixture не определил object number."));
            Write(
                "<< /Type /XObject /Subtype /Image " +
                "/Width 1 /Height 1 " +
                "/ColorSpace /DeviceGray /BitsPerComponent 8 " +
                "/Matte [" +
                string.Join(
                    " ",
                    softMaskMatte.Select(value =>
                        value.ToString(
                            CultureInfo.InvariantCulture))) +
                "] /Length 1 >>\nstream\n");
            memory.WriteByte(
                softMaskSample.Value);
            Write("\nendstream");
            EndObject();
        }

        var xref = memory.Position;
        Write(
            "xref\n" +
            $"0 {offsets.Count + 1}\n" +
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
            $"<< /Size {offsets.Count + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    /// <summary>
    /// Создаёт одностраничный PDF с Group 4 CCITT Image XObject.
    /// </summary>
    private static void CreateCcittImagePdf(
        string path,
        byte[] ccitt,
        int width,
        int height,
        bool blackIs1)
    {
        var encoding = Encoding.ASCII;
        var content = encoding.GetBytes(
            "q\n" +
            "40 0 0 20 20 20 cm\n" +
            "/Im1 Do\n" +
            "Q\n");

        using var memory = new MemoryStream();
        var offsets = new List<long>();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void BeginObject(int number)
        {
            offsets.Add(memory.Position);
            Write(
                number.ToString(
                    CultureInfo.InvariantCulture) +
                " 0 obj\n");
        }

        Write("%PDF-1.4\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /XObject << /Im1 5 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("endstream\nendobj\n");

        BeginObject(5);
        Write(
            $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
            "/ColorSpace /DeviceGray /BitsPerComponent 1 " +
            "/Filter /CCITTFaxDecode " +
            $"/DecodeParms << /K -1 /Columns {width} /Rows {height} /BlackIs1 {(blackIs1 ? "true" : "false")} >> " +
            $"/Length {ccitt.Length} >>\nstream\n");
        memory.Write(
            ccitt,
            0,
            ccitt.Length);
        Write("\nendstream\nendobj\n");

        var xref = memory.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");

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
            "<< /Size 6 /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static void CreateCieDctImagePdf(
        string path,
        string colorSpaceSyntax,
        byte[] jpegPixels,
        PixelFormat format,
        int stride,
        int? colorTransform)
    {
        var encoding = Encoding.ASCII;
        var jpeg = EncodeJpegFixture(
            jpegPixels,
            width: 1,
            height: 1,
            format,
            stride);
        var content =
            encoding.GetBytes(
                "q\n20 0 0 20 20 20 cm\n/Im1 Do\nQ\n");
        using var memory = new MemoryStream();
        var offsets = new List<long>();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(bytes, 0, bytes.Length);
        }

        void StartObject(int number)
        {
            if (number != offsets.Count + 1)
            {
                throw new InvalidOperationException(
                    "PDF fixture objects должны записываться последовательно.");
            }

            offsets.Add(memory.Position);
            Write($"{number} 0 obj\n");
        }

        void EndObject() =>
            Write("\nendobj\n");

        Write("%PDF-1.4\n");

        StartObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>");
        EndObject();

        StartObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
        EndObject();

        StartObject(3);
        Write(
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R " +
            "/Resources << /XObject << /Im1 5 0 R >> >> >>");
        EndObject();

        StartObject(4);
        Write($"<< /Length {content.Length} >>\nstream\n");
        memory.Write(content, 0, content.Length);
        Write("endstream");
        EndObject();

        StartObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image " +
            "/Width 1 /Height 1 " +
            $"/ColorSpace {colorSpaceSyntax} " +
            "/BitsPerComponent 8 /Filter /DCTDecode " +
            (colorTransform != null
                ? $"/DecodeParms << /ColorTransform {colorTransform.Value} >> "
                : string.Empty) +
            $"/Length {jpeg.Length} >>\nstream\n");
        memory.Write(jpeg, 0, jpeg.Length);
        Write("\nendstream");
        EndObject();

        var xref = memory.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    /// <summary>
    /// Проверяет ImageMask с независимыми nonstroking colors,
    /// построчным bit packing и обратным PDF /Decode.
    /// </summary>
    private static void TestPdfImageMaskStencil(string directory)
    {
        foreach (var inverted in new[] { false, true })
        {
            var path = Path.Combine(
                directory,
                inverted
                    ? "regression-stencil-inverse.pdf"
                    : "regression-stencil.pdf");

            var encoding = Encoding.ASCII;
            var content = encoding.GetBytes(
                "q 2 0 0 2 10 10 cm 1 0 0 rg /Im1 Do Q\n" +
                "q 2 0 0 2 20 10 cm 0 0 1 rg /Im1 Do Q\n");
            var samples = new byte[] { 0x40, 0x80 };
            using var output = new MemoryStream();
            var offsets = new List<long>();

            void Write(string value)
            {
                var bytes = encoding.GetBytes(value);
                output.Write(bytes, 0, bytes.Length);
            }

            void BeginObject(int number)
            {
                Assert(
                    number == offsets.Count + 1,
                    "PDF ImageMask fixture object order.");
                offsets.Add(output.Position);
                Write($"{number} 0 obj\n");
            }

            void EndObject() => Write("\nendobj\n");

            Write("%PDF-1.4\n");
            BeginObject(1);
            Write("<< /Type /Catalog /Pages 2 0 R >>");
            EndObject();
            BeginObject(2);
            Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
            EndObject();
            BeginObject(3);
            Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /XObject << /Im1 5 0 R >> >> >>");
            EndObject();
            BeginObject(4);
            Write($"<< /Length {content.Length} >>\nstream\n");
            output.Write(content, 0, content.Length);
            Write("\nendstream");
            EndObject();
            BeginObject(5);
            Write(
                "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                "/ImageMask true /BitsPerComponent 1 " +
                (inverted ? "/Decode [1 0] " : string.Empty) +
                $"/Length {samples.Length} >>\nstream\n");
            output.Write(samples, 0, samples.Length);
            Write("\nendstream");
            EndObject();

            var xref = output.Position;
            Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");

            foreach (var offset in offsets)
            {
                Write(
                    offset.ToString("0000000000", CultureInfo.InvariantCulture) +
                    " 00000 n \n");
            }

            Write(
                $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\n" +
                "startxref\n" +
                xref.ToString(CultureInfo.InvariantCulture) +
                "\n%%EOF\n");
            File.WriteAllBytes(path, output.ToArray());

            var images = SupportedFormatRegistry
                .ReadDocument(path)
                .Blocks
                .OfType<DocumentFixedPage>()
                .Single()
                .ImageRuns
                .ToArray();

            Assert(
                images.Length == 2,
                "PDF ImageMask должен допускать повторное использование с разными цветами.");

            for (var index = 0; index < images.Length; index++)
            {
                using var input = new MemoryStream(
                    images[index].Data,
                    writable: false);
                var decoder = new PngBitmapDecoder(
                    input,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapSource bitmap = decoder.Frames.Single();

                if (bitmap.Format != PixelFormats.Bgra32)
                {
                    bitmap = new FormatConvertedBitmap(
                        bitmap,
                        PixelFormats.Bgra32,
                        null,
                        0);
                }

                var pixels = new byte[16];
                bitmap.CopyPixels(pixels, 8, 0);

                var expectedAlpha = inverted ? (byte)0 : (byte)255;
                Assert(
                    bitmap.PixelWidth == 2 &&
                    bitmap.PixelHeight == 2 &&
                    pixels[3] == expectedAlpha &&
                    pixels[7] == (byte)(255 - expectedAlpha) &&
                    pixels[11] == (byte)(255 - expectedAlpha) &&
                    pixels[15] == expectedAlpha,
                    "PDF ImageMask должен сохранять orientation и polarity.");

                var red = index == 0;
                Assert(
                    pixels[0] == (red ? (byte)0 : (byte)255) &&
                    pixels[1] == 0 &&
                    pixels[2] == (red ? (byte)255 : (byte)0),
                    "PDF ImageMask должен использовать текущий nonstroking RGB color.");
            }
        }
    }

    private static void CreateJbig2ImagePdf(
        string path,
        byte[] embedded,
        byte[]? globals,
        bool reverseDecode)
    {
        var encoding =
            Encoding.ASCII;
        var content =
            encoding.GetBytes(
                "q\n32 0 0 16 20 20 cm\n/Im1 Do\nQ\n");
        using var memory =
            new MemoryStream();
        var offsets =
            new List<long>();

        void Write(string value)
        {
            var bytes =
                encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void StartObject(int number)
        {
            if (number != offsets.Count + 1)
            {
                throw new InvalidOperationException(
                    "PDF fixture objects должны записываться последовательно.");
            }

            offsets.Add(
                memory.Position);
            Write(
                $"{number} 0 obj\n");
        }

        void EndObject() =>
            Write("\nendobj\n");

        Write("%PDF-1.4\n");

        StartObject(1);
        Write(
            "<< /Type /Catalog /Pages 2 0 R >>");
        EndObject();

        StartObject(2);
        Write(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
        EndObject();

        StartObject(3);
        Write(
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R " +
            "/Resources << /XObject << /Im1 5 0 R >> >> >>");
        EndObject();

        StartObject(4);
        Write(
            $"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("endstream");
        EndObject();

        StartObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image " +
            "/Width 32 /Height 16 " +
            "/ColorSpace /DeviceGray " +
            "/BitsPerComponent 1 " +
            (reverseDecode
                ? "/Decode [1 0] "
                : string.Empty) +
            (globals != null
                ? "/DecodeParms << /JBIG2Globals 6 0 R >> "
                : string.Empty) +
            "/Filter /JBIG2Decode " +
            $"/Length {embedded.Length} >>\nstream\n");
        memory.Write(
            embedded,
            0,
            embedded.Length);
        Write("\nendstream");
        EndObject();

        if (globals != null)
        {
            StartObject(6);
            Write(
                $"<< /Length {globals.Length} >>\nstream\n");
            memory.Write(
                globals,
                0,
                globals.Length);
            Write("\nendstream");
            EndObject();
        }

        var objectCount =
            offsets.Count;
        var xref =
            memory.Position;
        Write(
            $"xref\n0 {objectCount + 1}\n" +
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
            $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static void CreateJpxImagePdf(
        string path,
        byte[] jpx,
        bool includeColorSpace,
        bool includeReverseDecode,
        string colorSpaceSyntax = "/DeviceRGB",
        int? softMaskInData = null,
        string? matteSyntax = null,
        byte[]? iccProfile = null,
        string? calculatorFunction = null)
    {
        var encoding = Encoding.ASCII;
        var content =
            encoding.GetBytes(
                "q\n20 0 0 10 20 20 cm\n/Im1 Do\nQ\n");
        using var memory =
            new MemoryStream();
        var offsets =
            new List<long>();

        void Write(string value)
        {
            var bytes =
                encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void StartObject(int number)
        {
            if (number != offsets.Count + 1)
            {
                throw new InvalidOperationException(
                    "PDF fixture objects должны записываться последовательно.");
            }

            offsets.Add(
                memory.Position);
            Write(
                $"{number} 0 obj\n");
        }

        void EndObject() =>
            Write("\nendobj\n");

        Write("%PDF-1.5\n");

        StartObject(1);
        Write(
            "<< /Type /Catalog /Pages 2 0 R >>");
        EndObject();

        StartObject(2);
        Write(
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
        EndObject();

        StartObject(3);
        Write(
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R " +
            "/Resources << /XObject << /Im1 5 0 R >> >> >>");
        EndObject();

        StartObject(4);
        Write(
            $"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("endstream");
        EndObject();

        StartObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image " +
            "/Width 2 /Height 1 " +
            (includeColorSpace
                ? $"/ColorSpace {colorSpaceSyntax} "
                : string.Empty) +
            (includeReverseDecode
                ? "/Decode [1 0 1 0 1 0] "
                : string.Empty) +
            (softMaskInData != null
                ? $"/SMaskInData {softMaskInData.Value} "
                : string.Empty) +
            (matteSyntax != null
                ? $"/Matte {matteSyntax} "
                : string.Empty) +
            "/Filter /JPXDecode " +
            $"/Length {jpx.Length} >>\nstream\n");
        memory.Write(
            jpx,
            0,
            jpx.Length);
        Write("\nendstream");
        EndObject();

        if (iccProfile != null)
        {
            StartObject(6);
            Write($"<< /N 3 /Length {iccProfile.Length} >>\nstream\n");
            memory.Write(iccProfile, 0, iccProfile.Length);
            Write("\nendstream");
            EndObject();
        }
        else if (calculatorFunction != null)
        {
            StartObject(6);
            var program = encoding.GetBytes(calculatorFunction);
            Write(
                "<< /FunctionType 4 /Domain [0 1 0 1 0 1] " +
                $"/Range [0 1 0 1 0 1] /Length {program.Length} >>\nstream\n");
            memory.Write(program, 0, program.Length);
            Write("\nendstream");
            EndObject();
        }

        var xref =
            memory.Position;
        Write(
            $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    private static byte[] CreateSyntheticCmykInputProfile()
    {
        static void WriteSignature(
            Span<byte> destination,
            string value)
        {
            if (destination.Length < 4 ||
                value.Length != 4)
            {
                throw new ArgumentException(
                    "ICC signature должна содержать четыре ASCII символа.");
            }

            for (var index = 0;
                 index < 4;
                 index++)
            {
                destination[index] =
                    checked((byte)value[index]);
            }
        }

        static void WriteS15Fixed16(
            Span<byte> destination,
            double value)
        {
            var encoded =
                checked(
                    (int)Math.Round(
                        value * 65536.0,
                        MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt32BigEndian(
                destination,
                encoded);
        }

        static byte[] CreateDescriptionTag()
        {
            var description =
                Encoding.ASCII.GetBytes(
                    "MYBOOK synthetic CMYK input\0");
            var data =
                new byte[
                    12 +
                    description.Length +
                    4 +
                    4 +
                    2 +
                    1 +
                    67];

            WriteSignature(
                data.AsSpan(0, 4),
                "desc");
            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(8, 4),
                checked((uint)description.Length));
            description.CopyTo(
                data,
                12);
            return data;
        }

        static byte[] CreateTextTag()
        {
            var text =
                Encoding.ASCII.GetBytes(
                    "Generated by MYBOOK regression\0");
            var data =
                new byte[8 + text.Length];
            WriteSignature(
                data.AsSpan(0, 4),
                "text");
            text.CopyTo(
                data,
                8);
            return data;
        }

        static byte[] CreateWhitePointTag()
        {
            var data =
                new byte[20];
            WriteSignature(
                data.AsSpan(0, 4),
                "XYZ ");
            WriteS15Fixed16(
                data.AsSpan(8, 4),
                0.9642);
            WriteS15Fixed16(
                data.AsSpan(12, 4),
                1.0);
            WriteS15Fixed16(
                data.AsSpan(16, 4),
                0.8249);
            return data;
        }

        static byte[] CreateA2B0Tag()
        {
            const int inputChannels = 4;
            const int outputChannels = 3;
            const int gridPoints = 2;
            const int headerBytes = 48;
            const int inputTableBytes =
                inputChannels * 256;
            const int clutBytes =
                16 * outputChannels;
            const int outputTableBytes =
                outputChannels * 256;

            var data =
                new byte[
                    headerBytes +
                    inputTableBytes +
                    clutBytes +
                    outputTableBytes];

            WriteSignature(
                data.AsSpan(0, 4),
                "mft1");
            data[8] = inputChannels;
            data[9] = outputChannels;
            data[10] = gridPoints;

            for (var row = 0;
                 row < 3;
                 row++)
            {
                for (var column = 0;
                     column < 3;
                     column++)
                {
                    WriteS15Fixed16(
                        data.AsSpan(
                            12 +
                            (row * 3 + column) * 4,
                            4),
                        row == column
                            ? 1.0
                            : 0.0);
                }
            }

            var position =
                headerBytes;

            for (var channel = 0;
                 channel < inputChannels;
                 channel++)
            {
                for (var value = 0;
                     value < 256;
                     value++)
                {
                    data[position++] =
                        checked((byte)value);
                }
            }

            for (var c = 0;
                 c <= 1;
                 c++)
            {
                for (var m = 0;
                     m <= 1;
                     m++)
                {
                    for (var y = 0;
                         y <= 1;
                         y++)
                    {
                        for (var k = 0;
                             k <= 1;
                             k++)
                        {
                            var ink =
                                (c + m + y + k) /
                                4.0;
                            data[position++] =
                                checked(
                                    (byte)Math.Round(
                                        255.0 *
                                        (1.0 - ink),
                                        MidpointRounding.AwayFromZero));
                            data[position++] = 128;
                            data[position++] = 128;
                        }
                    }
                }
            }

            for (var channel = 0;
                 channel < outputChannels;
                 channel++)
            {
                for (var value = 0;
                     value < 256;
                     value++)
                {
                    data[position++] =
                        checked((byte)value);
                }
            }

            if (position != data.Length)
            {
                throw new InvalidOperationException(
                    "Synthetic ICC A2B0 size mismatch.");
            }

            return data;
        }

        var tags =
            new (string Signature, byte[] Data)[]
            {
                ("desc", CreateDescriptionTag()),
                ("A2B0", CreateA2B0Tag()),
                ("wtpt", CreateWhitePointTag()),
                ("cprt", CreateTextTag())
            };

        const int headerSize = 128;
        var tagTableSize =
            4 +
            tags.Length * 12;
        var offset =
            headerSize +
            tagTableSize;
        var entries =
            new (int Offset, int Size)[tags.Length];

        for (var index = 0;
             index < tags.Length;
             index++)
        {
            offset =
                (offset + 3) &
                ~3;
            entries[index] =
                (offset, tags[index].Data.Length);
            offset +=
                tags[index].Data.Length;
        }

        var profile =
            new byte[
                (offset + 3) &
                ~3];

        BinaryPrimitives.WriteUInt32BigEndian(
            profile.AsSpan(0, 4),
            checked((uint)profile.Length));
        BinaryPrimitives.WriteUInt32BigEndian(
            profile.AsSpan(8, 4),
            0x02000000);
        WriteSignature(
            profile.AsSpan(12, 4),
            "scnr");
        WriteSignature(
            profile.AsSpan(16, 4),
            "CMYK");
        WriteSignature(
            profile.AsSpan(20, 4),
            "Lab ");

        ushort[] created =
        [
            2026, 10, 27,
            0, 0, 0
        ];

        for (var index = 0;
             index < created.Length;
             index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                profile.AsSpan(
                    24 + index * 2,
                    2),
                created[index]);
        }

        WriteSignature(
            profile.AsSpan(36, 4),
            "acsp");
        WriteS15Fixed16(
            profile.AsSpan(68, 4),
            0.9642);
        WriteS15Fixed16(
            profile.AsSpan(72, 4),
            1.0);
        WriteS15Fixed16(
            profile.AsSpan(76, 4),
            0.8249);

        BinaryPrimitives.WriteUInt32BigEndian(
            profile.AsSpan(
                headerSize,
                4),
            checked((uint)tags.Length));

        for (var index = 0;
             index < tags.Length;
             index++)
        {
            var entryOffset =
                headerSize +
                4 +
                index * 12;
            WriteSignature(
                profile.AsSpan(
                    entryOffset,
                    4),
                tags[index].Signature);
            BinaryPrimitives.WriteUInt32BigEndian(
                profile.AsSpan(
                    entryOffset + 4,
                    4),
                checked(
                    (uint)entries[index].Offset));
            BinaryPrimitives.WriteUInt32BigEndian(
                profile.AsSpan(
                    entryOffset + 8,
                    4),
                checked(
                    (uint)entries[index].Size));
            tags[index].Data.CopyTo(
                profile,
                entries[index].Offset);
        }

        return profile;
    }

    private static void CreateCmykDctImagePdf(
        string path,
        byte[]? iccProfile = null)
    {
        var encoding = Encoding.ASCII;
        var jpeg = EncodeJpegFixture(
            [0, 0, 0, 0],
            width: 1,
            height: 1,
            PixelFormats.Cmyk32,
            stride: 4);
        var content = encoding.GetBytes(
            "q\n20 0 0 20 20 20 cm\n/Im1 Do\nQ\n");
        using var memory = new MemoryStream();
        var offsets = new List<long>();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(bytes, 0, bytes.Length);
        }

        void StartObject(int number)
        {
            if (number != offsets.Count + 1)
            {
                throw new InvalidOperationException(
                    "PDF fixture objects должны записываться последовательно.");
            }

            offsets.Add(memory.Position);
            Write($"{number} 0 obj\n");
        }

        void EndObject() =>
            Write("\nendobj\n");

        Write("%PDF-1.4\n");

        StartObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>");
        EndObject();

        StartObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>");
        EndObject();

        StartObject(3);
        Write(
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R " +
            "/Resources << /XObject << /Im1 5 0 R >> >> >>");
        EndObject();

        StartObject(4);
        Write($"<< /Length {content.Length} >>\nstream\n");
        memory.Write(content, 0, content.Length);
        Write("endstream");
        EndObject();

        StartObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image " +
            "/Width 1 /Height 1 /ColorSpace " +
            (iccProfile == null
                ? "/DeviceCMYK "
                : "[/ICCBased 6 0 R] ") +
            "/BitsPerComponent 8 /Filter /DCTDecode " +
            "/Decode [1 0 0 1 0 1 0 1] " +
            $"/Length {jpeg.Length} >>\nstream\n");
        memory.Write(jpeg, 0, jpeg.Length);
        Write("\nendstream");
        EndObject();

        if (iccProfile != null)
        {
            StartObject(6);
            Write(
                "<< /N 4 /Range [0 1 0 1 0 1 0 1] " +
                $"/Length {iccProfile.Length} >>\nstream\n");
            memory.Write(
                iccProfile,
                0,
                iccProfile.Length);
            Write("\nendstream");
            EndObject();
        }

        var xref = memory.Position;
        Write(
            "xref\n0 " +
            (offsets.Count + 1).ToString(
                CultureInfo.InvariantCulture) +
            "\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            "trailer\n<< /Size " +
            (offsets.Count + 1).ToString(
                CultureInfo.InvariantCulture) +
            " /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    /// <summary>
    /// Создаёт одностраничный PDF с RGB DCT Image XObject, нестандартным /Decode
    /// и grayscale DCT soft mask с /Matte.
    /// </summary>
    private static void CreateDctImagePdf(string path)
    {
        var encoding = Encoding.ASCII;
        var imageJpeg = EncodeJpegFixture(
            [64, 128, 192],
            width: 1,
            height: 1,
            PixelFormats.Rgb24,
            stride: 3);
        var maskJpeg = EncodeJpegFixture(
            [128],
            width: 1,
            height: 1,
            PixelFormats.Gray8,
            stride: 1);
        var content = encoding.GetBytes(
            "q\n" +
            "20 0 0 20 20 20 cm\n" +
            "/Im1 Do\n" +
            "Q\n");

        using var memory = new MemoryStream();
        var offsets = new List<long>();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void BeginObject(int number)
        {
            offsets.Add(memory.Position);
            Write(
                number.ToString(
                    CultureInfo.InvariantCulture) +
                " 0 obj\n");
        }

        Write("%PDF-1.4\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /XObject << /Im1 5 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("endstream\nendobj\n");

        BeginObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
            "/ColorSpace /DeviceRGB /BitsPerComponent 8 " +
            "/Filter /DCTDecode /Decode [1 0 1 0 1 0] /SMask 6 0 R " +
            $"/Length {imageJpeg.Length} >>\nstream\n");
        memory.Write(
            imageJpeg,
            0,
            imageJpeg.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(6);
        Write(
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 " +
            "/ColorSpace /DeviceGray /BitsPerComponent 8 " +
            "/Filter /DCTDecode /Matte [1 1 1] " +
            $"/Length {maskJpeg.Length} >>\nstream\n");
        memory.Write(
            maskJpeg,
            0,
            maskJpeg.Length);
        Write("\nendstream\nendobj\n");

        var xref = memory.Position;
        Write("xref\n0 7\n0000000000 65535 f \n");

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
            "<< /Size 7 /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }

    /// <summary>
    /// Создаёт PDF с Type0 font, использующим predefined named CMap,
    /// чтобы reader не мог молча интерпретировать его как Identity.
    /// </summary>
    private static void CreateNamedCMapPdf(string path)
    {
        var encoding = Encoding.ASCII;
        var content =
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "<0041> Tj\n" +
            "ET\n";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {encoding.GetByteCount(content)} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type0 /BaseFont /TestCID /Encoding /UniJIS-UCS2-H /DescendantFonts [6 0 R] >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TestCID /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 0 >> /DW 1000 >>"
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
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            "trailer\n" +
            $"<< /Size {objects.Length + 1} /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
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

    private static void CreateRunLengthPdf(
        string path,
        string text)
    {
        var encoding = Encoding.ASCII;
        var content = encoding.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +
            "1 0 0 1 20 100 Tm\n" +
            "(" + EscapePdfString(text) + ") Tj\n" +
            "ET\n");

        if (content.Length > 128)
        {
            throw new InvalidOperationException(
                "RunLength PDF fixture content превышает один literal run.");
        }

        var compressed = new byte[
            content.Length +
            2];
        compressed[0] =
            checked((byte)(content.Length - 1));
        content.CopyTo(
            compressed,
            1);
        compressed[^1] = 128;

        using var memory = new MemoryStream();

        void Write(string value)
        {
            var bytes = encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        var offsets = new List<long>();

        void BeginObject(int objectNumber)
        {
            offsets.Add(memory.Position);
            Write(
                $"{objectNumber} 0 obj\n");
        }

        Write("%PDF-1.4\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {compressed.Length} /Filter /RunLengthDecode >>\nstream\n");
        memory.Write(
            compressed,
            0,
            compressed.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(5);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var xref = memory.Position;
        Write("xref\n0 6\n0000000000 65535 f \n");

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
            "<< /Size 6 /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(
                CultureInfo.InvariantCulture) +
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
