using System.IO;
using System.IO.Compression;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет EPUB OPF manifest fallback chains для foreign spine items,
    /// выбор XHTML, порядок глав, ссылки на исходный spine resource,
    /// обычные XHTML itemrefs и явные ошибки отсутствующих IDREF и циклов.
    /// Создаёт ZIP fixtures только во временном каталоге regression suite.
    /// </summary>
    private static void TestEpubSpineFallback(string directory)
    {
        const string manifest =
            """
            <item id="foreign" href="Text/source.bin" media-type="application/x-epub-test" fallback="intermediate"/>
            <item id="intermediate" href="Text/alternate.bin" media-type="application/x-epub-alternate" fallback="chapter"/>
            <item id="chapter" href="Text/chapter.xhtml" media-type="application/xhtml+xml"/>
            <item id="next" href="Text/next.xhtml" media-type="application/xhtml+xml" fallback="missing-but-unused"/>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            """;

        /// <summary>
        /// Создаёт EPUB 3 с заданными OPF manifest и spine, XHTML-главами и nav.
        /// Все части архива формируются из констант без чтения внешних книг.
        /// </summary>
        string CreateEpub(
            string fileName,
            string manifestItems,
            string spineItems)
        {
            var path = Path.Combine(
                directory,
                fileName);

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
                    <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                      <rootfiles>
                        <rootfile full-path="OPS/package.opf"
                                  media-type="application/oebps-package+xml"/>
                      </rootfiles>
                    </container>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/package.opf",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <package version="3.0" xmlns="http://www.idpf.org/2007/opf">
                      <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                        <dc:title>EPUB Fallback Regression</dc:title>
                      </metadata>
                      <manifest>
                    """ +
                    manifestItems +
                    """
                      </manifest>
                      <spine>
                    """ +
                    spineItems +
                    """
                      </spine>
                    </package>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/Text/source.bin",
                    "Foreign primary content");

                WriteZipEntry(
                    archive,
                    "OPS/Text/alternate.bin",
                    "Foreign intermediate content");

                WriteZipEntry(
                    archive,
                    "OPS/Text/chapter.xhtml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head><title>Fallback</title></head>
                      <body>
                        <h1 id="fallback-heading">Fallback XHTML chapter</h1>
                        <p>Foreign resource fallback text.</p>
                        <a href="next.xhtml#next-heading">Continue</a>
                      </body>
                    </html>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/Text/next.xhtml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head><title>Next</title></head>
                      <body>
                        <h1 id="next-heading">Next XHTML chapter</h1>
                        <a href="source.bin">Back to foreign spine item</a>
                      </body>
                    </html>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/nav.xhtml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml"
                          xmlns:epub="http://www.idpf.org/2007/ops">
                      <body>
                        <nav epub:type="toc">
                          <ol>
                            <li><a href="Text/source.bin">Foreign section</a></li>
                            <li><a href="Text/next.xhtml#next-heading">Next section</a></li>
                          </ol>
                        </nav>
                      </body>
                    </html>
                    """);
            }

            return path;
        }

        var validPath = CreateEpub(
            "epub-manifest-fallback-valid.epub",
            manifest,
            """
            <itemref idref="foreign"/>
            <itemref idref="next"/>
            """);

        var document = SupportedFormatRegistry.ReadDocument(
            validPath);
        var chapters = document.Blocks
            .OfType<DocumentHtmlBlock>()
            .ToArray();

        AssertEqual(
            2,
            chapters.Length,
            "EPUB fallback должен сохранять spine chapter count.");

        AssertContains(
            chapters[0].Html,
            "Foreign resource fallback text.",
            "EPUB foreign item должен отображать XHTML fallback.");

        AssertContains(
            chapters[1].Html,
            "Next XHTML chapter",
            "EPUB следующий XHTML spine item должен сохранять порядок глав.");

        AssertContains(
            chapters[0].Html,
            "href=\"#epub-chapter-2-next-heading\"",
            "EPUB fallback должен сохранять относительные ссылки в XHTML.");

        AssertContains(
            chapters[1].Html,
            "href=\"#epub-chapter-1\"",
            "EPUB должен перенаправлять ссылки на foreign spine resource к XHTML fallback.");

        Assert(
            document.Outlines.Any(item =>
                item.Title == "Foreign section" &&
                item.TargetAnchorId == "epub-chapter-1") &&
            document.Outlines.Any(item =>
                item.Title == "Next section" &&
                item.TargetAnchorId == "epub-chapter-2-next-heading"),
            "EPUB navigation должен разрешать foreign spine path и XHTML anchor.");

        var nativePath = CreateEpub(
            "epub-manifest-fallback-native.epub",
            manifest,
            """<itemref idref="next"/>""");

        var native = SupportedFormatRegistry.ReadDocument(
            nativePath);
        AssertEqual(
            1,
            native.Blocks
                .OfType<DocumentHtmlBlock>()
                .Count(),
            "EPUB XHTML spine item должен читаться без обхода fallback цепочки.");

        /// <summary>
        /// Проверяет явное отклонение повреждённого OPF spine/fallback
        /// по ожидаемой диагностике InvalidDataException.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string manifestItems,
            string spineItems,
            string message)
        {
            var path = CreateEpub(
                fileName,
                manifestItems,
                spineItems);
            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    path);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    message,
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"EPUB должен отклонять {fileName} с диагностикой {message}.");
        }

        AssertInvalid(
            "epub-fallback-cycle.epub",
            manifest.Replace(
                "fallback=\"chapter\"",
                "fallback=\"foreign\"",
                StringComparison.Ordinal),
            """<itemref idref="foreign"/>""",
            "цикл");

        AssertInvalid(
            "epub-fallback-self.epub",
            manifest.Replace(
                "fallback=\"intermediate\"",
                "fallback=\"foreign\"",
                StringComparison.Ordinal),
            """<itemref idref="foreign"/>""",
            "цикл");

        AssertInvalid(
            "epub-fallback-missing-id.epub",
            manifest.Replace(
                "fallback=\"intermediate\"",
                "fallback=\"nonexistent\"",
                StringComparison.Ordinal),
            """<itemref idref="foreign"/>""",
            "manifest не содержит item");

        AssertInvalid(
            "epub-fallback-no-xhtml.epub",
            manifest.Replace(
                " fallback=\"chapter\"",
                string.Empty,
                StringComparison.Ordinal),
            """<itemref idref="foreign"/>""",
            "не содержит XHTML fallback");

        AssertInvalid(
            "epub-fallback-no-declaration.epub",
            manifest.Replace(
                " fallback=\"intermediate\"",
                string.Empty,
                StringComparison.Ordinal),
            """<itemref idref="foreign"/>""",
            "не содержит XHTML fallback");

        AssertInvalid(
            "epub-fallback-missing-idref.epub",
            manifest,
            """<itemref/>""",
            "не содержит idref");

        AssertInvalid(
            "epub-fallback-broken-spine.epub",
            manifest,
            """<itemref idref="unknown"/>""",
            "manifest не содержит item");
    }
}
