using System.IO;
using System.IO.Compression;
using System.Text;
using MYBOOK.Documents;
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
            ("DOCX reader", () => TestDocxReader(temporaryDirectory)),
            ("PDF reader", () => TestPdfReader(temporaryDirectory)),
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
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
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
                <section>
                  <title><p>Regression heading</p></title>
                  <p>FB2 marker</p>
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
    }

    private static void TestEpubReader(string directory)
    {
        var path = Path.Combine(
            directory,
            "regression.epub");

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
                    <item id="chapter"
                          href="chapter.xhtml"
                          media-type="application/xhtml+xml" />
                  </manifest>
                  <spine>
                    <itemref idref="chapter" />
                  </spine>
                </package>
                """);

            WriteZipEntry(
                archive,
                "OEBPS/chapter.xhtml",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body>
                    <h1>Regression EPUB</h1>
                    <p>EPUB marker</p>
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
