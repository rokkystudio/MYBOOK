using System.IO;
using System.IO.Compression;
using System.Text;
using MYBOOK.Documents;
using MYBOOK.Rendering;
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

        File.WriteAllText(
            path,
            """
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
                </title-info>
              </description>
              <body>
                <section id="chapter-1">
                  <title><p>Regression heading</p></title>
                  <p>
                    FB2 marker
                    <a xlink:href="#note-1">note link</a>
                    <a xlink:href="javascript:alert(1)">unsafe link</a>
                  </p>
                </section>
              </body>
              <body name="notes">
                <section id="note-1">
                  <p>Note target</p>
                </section>
              </body>
            </FictionBook>
            """,
            new UTF8Encoding(false));

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertEqual(
            "Regression FB2",
            model.Title,
            "FB2 title");

        AssertContains(
            FlattenText(model),
            "FB2 marker",
            "FB2 model");

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
                    "note-1",
                    StringComparison.Ordinal)),
            "FB2 notes section id note-1 должен стать внутренним якорем.");

        Assert(
            paragraphs.Any(paragraph =>
                paragraph.IsNote &&
                string.Equals(
                    paragraph.AnchorId,
                    "note-1",
                    StringComparison.Ordinal)),
            "FB2 body name=notes должен помечать note-блоки в модели.");

        Assert(
            paragraphs
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    string.Equals(
                        inline.LinkHref,
                        "#note-1",
                        StringComparison.Ordinal)),
            "FB2 xlink:href должен сохраниться в DocumentInline.");

        var html = DocumentHtmlRenderer.Render(
            model,
            "Light");

        AssertContains(
            html,
            "id=\"anchor-note-1\" class=\"note-body\"",
            "FB2 rendered note anchor");

        AssertContains(
            html,
            "href=\"#anchor-note-1\"",
            "FB2 rendered internal link");

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
                  </body>
                </html>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/Styles/book.css",
                """
                @import url("https://example.invalid/remote.css");
                body { color: rgb(1, 2, 3); }
                .illustrated {
                    background-image: url("../Images/pixel.png");
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

            WriteZipEntry(
                archive,
                "OEBPS/Text/chapter1.xhtml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"
                      xmlns:svg="http://www.w3.org/2000/svg">
                  <head>
                    <link rel="stylesheet" href="../Styles/book.css" />
                  </head>
                  <body>
                    <h1 id="start">Regression EPUB</h1>
                    <h2 id="sub">Subsection heading</h2>
                    <p class="illustrated">EPUB marker</p>
                    <img src="../Images/pixel.png"
                         onerror="alert(1)" />
                    <img src="../Images/icon.svg" />
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
            2,
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
                </ncx>
                """);

            WriteZipEntry(
                archive,
                "OPS/Text/one.xhtml",
                """
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1 id="one">One</h1>
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
            2,
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
    }

    private static void TestDocxReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.docx");

        using (var archive = ZipFile.Open(
                   path,
                   ZipArchiveMode.Create))
        {
            WriteZipEntry(
                archive,
                "word/document.xml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r>
                        <w:t>DOCX marker</w:t>
                      </w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }

        var model = SupportedFormatRegistry.ReadDocument(path);

        AssertContains(
            FlattenText(model),
            "DOCX marker",
            "DOCX model");
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

        ParserTrace.Configure(true);

        try
        {
            var model = SupportedFormatRegistry.ReadDocument(path);

            Assert(
                model.Blocks.OfType<DocumentFixedPage>().Count() == 1,
                "Trace fixture должен прочитаться как одностраничный PDF.");
        }
        finally
        {
            ParserTrace.Configure(false);
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
