using System.IO;
using System.IO.Compression;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет percent-encoded URI в EPUB OPF manifest для XHTML spine,
    /// foreign fallback, EPUB3 nav, EPUB2 NCX, cover, локального CSS и image.
    /// Проверяет текстовые URI с пробелами, UTF-8 и escaped literal percent,
    /// а также явные ошибки для malformed percent escapes и внешних URLs.
    /// ZIP fixtures создаются только во временной директории.
    /// </summary>
    private static void TestEpubManifestUris(string directory)
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlQ2ioAAAAASUVORK5CYII=");

        /// <summary>
        /// Создаёт EPUB ZIP с OPF manifest, содержащим URI-encoded href,
        /// двумя главами, foreign fallback, обложкой, CSS и nav/NCX.
        /// </summary>
        string CreateEpub(
            string fileName,
            bool useNcx,
            string firstHref = "Text/Part%201.xhtml",
            string firstArchivePath = "OPS/Text/Part 1.xhtml")
        {
            var path = Path.Combine(
                directory,
                fileName);

            var manifest =
                $"""
                <item id="foreign" href="Text/Foreign%20Part.bin" media-type="application/x-foreign" fallback="first"/>
                <item id="first" href="{firstHref}" media-type="application/xhtml+xml"/>
                <item id="second" href="Text/%D0%93%D0%BB%D0%B0%D0%B2%D0%B0%202.xhtml" media-type="application/xhtml+xml"/>
                <item id="cover" href="Images/Cover%20Image.png" media-type="image/png" properties="cover-image"/>
                """ +
                (useNcx
                    ? """
                      <item id="ncx" href="Navigation/toc%20map.ncx" media-type="application/x-dtbncx+xml"/>
                      """
                    : """
                      <item id="nav" href="Navigation/Table%20of%20Contents.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                      """);

            var package =
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package version="3.0" xmlns="http://www.idpf.org/2007/opf">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Encoded EPUB References</dc:title>
                  </metadata>
                  <manifest>
                """ +
                manifest +
                """
                  </manifest>
                  <spine
                """ +
                (useNcx ? " toc=\"ncx\"" : string.Empty) +
                """
                >
                    <itemref idref="foreign"/>
                    <itemref idref="second"/>
                  </spine>
                </package>
                """;

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
                        <rootfile full-path="OPS/content.opf"
                                  media-type="application/oebps-package+xml"/>
                      </rootfiles>
                    </container>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/content.opf",
                    package);

                WriteZipEntry(
                    archive,
                    "OPS/Text/Foreign Part.bin",
                    "Foreign content with percent-encoded manifest URI");

                WriteZipEntry(
                    archive,
                    firstArchivePath,
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head>
                        <title>Part One</title>
                        <link rel="stylesheet" href="../Styles/main%20sheet.css"/>
                      </head>
                      <body>
                        <h1 id="first">Encoded path chapter one</h1>
                        <p class="with-resource">Chapter one content.</p>
                        <img src="../Images/Cover%20Image.png" alt="cover"/>
                        <a href="%D0%93%D0%BB%D0%B0%D0%B2%D0%B0%202.xhtml#second">Next</a>
                      </body>
                    </html>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/Text/Глава 2.xhtml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head><title>Chapter Two</title></head>
                      <body>
                        <h1 id="second">UTF-8 filename chapter two</h1>
                        <a href="Foreign%20Part.bin">Previous foreign item</a>
                      </body>
                    </html>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/Styles/main sheet.css",
                    ".with-resource { color: rgb(5, 6, 7); }");

                WriteZipBytes(
                    archive,
                    "OPS/Images/Cover Image.png",
                    imageBytes);

                WriteZipEntry(
                    archive,
                    "OPS/Navigation/Table of Contents.xhtml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml"
                          xmlns:epub="http://www.idpf.org/2007/ops">
                      <body>
                        <nav epub:type="toc">
                          <ol>
                            <li><a href="../Text/Foreign%20Part.bin">Encoded first chapter</a></li>
                            <li><a href="../Text/%D0%93%D0%BB%D0%B0%D0%B2%D0%B0%202.xhtml#second">UTF-8 chapter</a></li>
                          </ol>
                        </nav>
                      </body>
                    </html>
                    """);

                WriteZipEntry(
                    archive,
                    "OPS/Navigation/toc map.ncx",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                      <navMap>
                        <navPoint id="one" playOrder="1">
                          <navLabel><text>Encoded first chapter</text></navLabel>
                          <content src="../Text/Foreign%20Part.bin"/>
                        </navPoint>
                        <navPoint id="two" playOrder="2">
                          <navLabel><text>UTF-8 chapter</text></navLabel>
                          <content src="../Text/%D0%93%D0%BB%D0%B0%D0%B2%D0%B0%202.xhtml#second"/>
                        </navPoint>
                      </navMap>
                    </ncx>
                    """);
            }

            return path;
        }

        /// <summary>
        /// Проверяет содержимое и навигацию EPUB с percent-encoded OPF href.
        /// </summary>
        void AssertPublication(
            string path)
        {
            var model =
                SupportedFormatRegistry.ReadDocument(
                    path);

            var chapters = model.Blocks
                .OfType<DocumentHtmlBlock>()
                .ToArray();

            AssertEqual(
                2,
                chapters.Length,
                "EPUB должен читать две главы по URI-encoded manifest href.");

            AssertContains(
                chapters[0].Html,
                "Encoded path chapter one",
                "EPUB должен декодировать %20 в имени первой XHTML-главы.");

            AssertContains(
                chapters[1].Html,
                "UTF-8 filename chapter two",
                "EPUB должен декодировать UTF-8 percent escapes в пути второй главы.");

            AssertContains(
                chapters[0].Html,
                "href=\"#epub-chapter-2-second\"",
                "EPUB должен разрешать относительную ссылку на UTF-8 имя главы.");

            AssertContains(
                chapters[1].Html,
                "href=\"#epub-chapter-1\"",
                "EPUB должен разрешать ссылку на foreign item через fallback alias.");

            AssertContains(
                chapters[0].Html,
                "#epub-chapter-1 .with-resource{color:rgb(5, 6, 7)}",
                "EPUB должен читать CSS из percent-encoded локального ресурса.");

            AssertContains(
                chapters[0].Html,
                "src=\"data:image/png;base64,",
                "EPUB должен читать URI-encoded image resource.");

            Assert(
                model.Cover != null &&
                model.Cover.Data.SequenceEqual(
                    imageBytes) &&
                model.Cover.ContentType == "image/png",
                "EPUB должен загружать cover-image из URI-encoded manifest href.");

            Assert(
                model.Outlines.Any(item =>
                    item.Title == "Encoded first chapter" &&
                    item.TargetAnchorId == "epub-chapter-1") &&
                model.Outlines.Any(item =>
                    item.Title == "UTF-8 chapter" &&
                    item.TargetAnchorId == "epub-chapter-2-second"),
                "EPUB должен разрешать ссылки EPUB3 nav / EPUB2 NCX с percent-encoded путями.");
        }

        AssertPublication(
            CreateEpub(
                "epub-manifest-uri-epub3.epub",
                useNcx: false));

        AssertPublication(
            CreateEpub(
                "epub-manifest-uri-epub2-ncx.epub",
                useNcx: true));

        AssertPublication(
            CreateEpub(
                "epub-manifest-escaped-literal-percent.epub",
                useNcx: false,
                firstHref: "Text/Part%2520.xhtml",
                firstArchivePath: "OPS/Text/Part%20.xhtml"));

        var invalidHrefs = new[]
        {
            "Text/Part%ZZ.xhtml",
            "Text/Part%2.xhtml",
            "https://example.invalid/chapter.xhtml",
            "Text/Part%201.xhtml#fragment",
            "Text/Part%201.xhtml?query=1"
        };

        for (var index = 0; index < invalidHrefs.Length; index++)
        {
            var invalidHref = invalidHrefs[index];
            var path = CreateEpub(
                $"epub-invalid-manifest-{index}.epub",
                useNcx: false,
                firstHref: invalidHref);

            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    path);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    "manifest href",
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"EPUB должен явно отклонять некорректный manifest href: {invalidHref}.");
        }
    }
}
