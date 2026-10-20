using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Diagnostics;
using Book.Documents;
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
            ("FB2 reader", () => TestFb2Reader(temporaryDirectory)),
            ("EPUB reader", () => TestEpubReader(temporaryDirectory)),
            ("EPUB NCX reader", () => TestEpubNcxReader(temporaryDirectory)),
            ("EPUB corpus compatibility", () => TestEpubCorpusCompatibility(repositoryRoot)),
            ("DOCX reader", () => TestDocxReader(temporaryDirectory)),
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
        var path = Path.Combine(
            directory,
            "regression.rtf");

        File.WriteAllText(
            path,
            @"{\rtf1\ansi RTF marker \b bold\b0\par}",
            Encoding.ASCII);

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "RTF marker",
            "RTF model");
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
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
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

        Assert(
            !html.Contains(
                "href=\"file:",
                StringComparison.OrdinalIgnoreCase),
            "DOCX renderer не должен выпускать file: hyperlink.");
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
