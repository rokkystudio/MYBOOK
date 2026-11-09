using System.IO;
using System.IO.Compression;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX navigation outlines по HeadingN, стилям basedOn и
    /// paragraph/style outlineLvl 0..9, включая подавление heading значением 9,
    /// уровни с пропусками, source bookmarks, уникальные synthetic anchors,
    /// отсутствие heading в headers, таблицах и notes, а также HTML targets.
    /// Все DOCX fixtures создаются только во временной директории.
    /// </summary>
    private static void TestDocxOutlines(string directory)
    {
        const string documentXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                  <w:r><w:t>Main</w:t></w:r>
                  <w:r><w:t xml:space="preserve">  Chapter </w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="DerivedTwo"/></w:pPr>
                  <w:r><w:t>Child Section</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="CustomFour"/></w:pPr>
                  <w:r><w:t>Fourth-level topic</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
                  <w:bookmarkStart w:id="10" w:name="explicit-heading"/>
                  <w:r><w:t>Sibling Section</w:t></w:r>
                  <w:bookmarkEnd w:id="10"/>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="BodyStyle"/>
                    <w:outlineLvl w:val="0"/>
                  </w:pPr>
                  <w:r><w:t>Direct Level One</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading5"/></w:pPr>
                  <w:r><w:t>Fifth heading</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading7"/></w:pPr>
                  <w:r><w:t>Seventh heading</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:outlineLvl w:val="8"/></w:pPr>
                  <w:r><w:t>Ninth heading</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Heading2"/>
                    <w:outlineLvl w:val="9"/>
                  </w:pPr>
                  <w:r><w:t>Suppressed direct heading</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="SuppressedStyle"/></w:pPr>
                  <w:r><w:t>Suppressed inherited heading</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
                  <w:r><w:t xml:space="preserve">   </w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading10"/></w:pPr>
                  <w:r><w:t>Unrecognized heading level</w:t></w:r>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                        <w:r><w:t>Table heading excluded</w:t></w:r>
                      </w:p>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                <w:p>
                  <w:bookmarkStart w:id="11" w:name="docx-heading-1"/>
                  <w:r><w:t>Later reserved bookmark</w:t></w:r>
                  <w:bookmarkEnd w:id="11"/>
                </w:p>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Heading2">
                <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="DerivedTwo">
                <w:basedOn w:val="Heading2"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CustomFour">
                <w:pPr><w:outlineLvl w:val="3"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="BodyStyle"/>
              <w:style w:type="paragraph" w:styleId="SuppressedStyle">
                <w:basedOn w:val="Heading1"/>
                <w:pPr><w:outlineLvl w:val="9"/></w:pPr>
              </w:style>
            </w:styles>
            """;

        /// <summary>
        /// Записывает минимальный DOCX ZIP с document, styles, header и
        /// footnotes для проверки исключения внеосновных heading из outlines.
        /// </summary>
        string CreateDocx(
            string fileName,
            string wordDocument,
            string wordStyles)
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
                    "word/document.xml",
                    wordDocument);

                WriteZipEntry(
                    archive,
                    "word/styles.xml",
                    wordStyles);

                WriteZipEntry(
                    archive,
                    "word/_rels/document.xml.rels",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdHeader"
                                    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
                                    Target="header1.xml"/>
                    </Relationships>
                    """);

                WriteZipEntry(
                    archive,
                    "word/header1.xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:p>
                        <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                        <w:r><w:t>Header heading excluded</w:t></w:r>
                      </w:p>
                    </w:hdr>
                    """);

                WriteZipEntry(
                    archive,
                    "word/footnotes.xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:footnote w:id="1">
                        <w:p>
                          <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                          <w:r><w:t>Footnote heading excluded</w:t></w:r>
                        </w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }

            return path;
        }

        var path = CreateDocx(
            "docx-navigation-outlines.docx",
            documentXml,
            stylesXml);

        var model = SupportedFormatRegistry.ReadDocument(path);
        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        AssertEqual(
            2,
            model.Outlines.Count,
            "DOCX Heading1 должен разделять оглавление на две корневые ветви.");

        var main = model.Outlines[0];
        var direct = model.Outlines[1];

        AssertEqual(
            "Main Chapter",
            main.Title,
            "DOCX outline должен нормализовать пробелы в заголовках из нескольких runs.");

        AssertEqual(
            "docx-heading-2",
            main.TargetAnchorId!,
            "DOCX первый generated anchor должен обходить reserved source bookmark.");

        AssertEqual(
            2,
            main.Children.Count,
            "DOCX Heading1 должен содержать две Heading2 ветви.");

        AssertEqual(
            "Child Section",
            main.Children[0].Title,
            "DOCX basedOn должен наследовать outlineLevel стиля Heading2.");

        AssertEqual(
            "docx-heading-3",
            main.Children[0].TargetAnchorId!,
            "DOCX основанный на Heading2 стиль должен получить внутренний якорь.");

        AssertEqual(
            "Fourth-level topic",
            main.Children[0].Children.Single().Title,
            "DOCX нестандартный стиль с outlineLvl=3 должен стать Heading4.");

        AssertEqual(
            "docx-heading-4",
            main.Children[0].Children.Single().TargetAnchorId!,
            "DOCX вложенный четвёртый уровень должен получить собственный якорь.");

        AssertEqual(
            "Sibling Section",
            main.Children[1].Title,
            "DOCX соседний Heading2 должен стать sibling предыдущего Heading2.");

        AssertEqual(
            "explicit-heading",
            main.Children[1].TargetAnchorId!,
            "DOCX heading bookmarkStart должен использовать исходный bookmark name.");

        AssertEqual(
            "Direct Level One",
            direct.Title,
            "DOCX прямой outlineLvl=0 должен задать Heading1 обычному стилю.");

        AssertEqual(
            "docx-heading-5",
            direct.TargetAnchorId!,
            "DOCX прямой Heading1 должен получить следующий synthetic anchor.");

        AssertEqual(
            "Fifth heading",
            direct.Children.Single().Title,
            "DOCX встроенное имя Heading5 без styles.xml entry должно распознаваться.");

        AssertEqual(
            "Seventh heading",
            direct.Children.Single().Children.Single().Title,
            "DOCX встроенное имя Heading7 должно сохранять свой уровень.");

        AssertEqual(
            "Ninth heading",
            direct.Children.Single().Children.Single().Children.Single().Title,
            "DOCX outlineLvl=8 должен сформировать Heading9 с пропущенными уровнями.");

        Assert(
            paragraphs.Any(paragraph =>
                paragraph.HeadingLevel == 9 &&
                paragraph.AnchorId == "docx-heading-8") &&
            paragraphs.Any(paragraph =>
                paragraph.HeadingLevel == 0 &&
                paragraph.Inlines.Any(inline =>
                    inline.Text == "Suppressed direct heading")) &&
            paragraphs.Any(paragraph =>
                paragraph.HeadingLevel == 0 &&
                paragraph.Inlines.Any(inline =>
                    inline.Text == "Suppressed inherited heading")),
            "DOCX должен сохранять прямые и style outlineLvl, включая значение 9.");

        var allOutlines = new List<DocumentOutlineItem>();

        /// <summary>
        /// Перечисляет все DOCX outline entries вместе с дочерними пунктами.
        /// </summary>
        void Collect(
            IEnumerable<DocumentOutlineItem> items)
        {
            foreach (var item in items)
            {
                allOutlines.Add(item);
                Collect(item.Children);
            }
        }

        Collect(model.Outlines);

        AssertEqual(
            8,
            allOutlines.Count,
            "DOCX оглавление должно содержать только восемь непустых body headings.");

        Assert(
            !allOutlines.Any(item =>
                item.Title is
                    "Suppressed direct heading" or
                    "Suppressed inherited heading" or
                    "Unrecognized heading level" or
                    "Header heading excluded" or
                    "Table heading excluded" or
                    "Footnote heading excluded"),
            "DOCX outlines должны исключать подавленные heading и внеосновные stories.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        foreach (var item in allOutlines)
        {
            AssertContains(
                html,
                $"id=\"anchor-{item.TargetAnchorId}\"",
                $"DOCX outline target {item.Title} должен существовать в HTML.");
        }

        AssertContains(
            html,
            "id=\"anchor-docx-heading-1\"",
            "DOCX зарезервированный bookmark должен остаться на обычном абзаце.");

        var again = SupportedFormatRegistry.ReadDocument(
            path);

        AssertEqual(
            main.TargetAnchorId!,
            again.Outlines[0].TargetAnchorId!,
            "DOCX synthetic anchors должны быть стабильны между чтениями.");

        /// <summary>
        /// Проверяет, что некорректный DOCX outlineLvl в pPr или styles.xml
        /// приводит к InvalidDataException с конкретной диагностикой.
        /// </summary>
        void AssertInvalidLevel(
            string fileName,
            string invalidDocument,
            string invalidStyles)
        {
            var invalidPath = CreateDocx(
                fileName,
                invalidDocument,
                invalidStyles);
            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(invalidPath);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    "outlineLvl",
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"DOCX должен отклонять некорректный outlineLvl: {fileName}.");
        }

        AssertInvalidLevel(
            "docx-invalid-outline-10.docx",
            documentXml.Replace(
                """<w:outlineLvl w:val="0"/>""",
                """<w:outlineLvl w:val="10"/>""",
                StringComparison.Ordinal),
            stylesXml);

        AssertInvalidLevel(
            "docx-invalid-outline-negative.docx",
            documentXml.Replace(
                """<w:outlineLvl w:val="0"/>""",
                """<w:outlineLvl w:val="-1"/>""",
                StringComparison.Ordinal),
            stylesXml);

        AssertInvalidLevel(
            "docx-invalid-style-outline.docx",
            documentXml,
            stylesXml.Replace(
                """<w:outlineLvl w:val="3"/>""",
                """<w:outlineLvl w:val="not-a-number"/>""",
                StringComparison.Ordinal));

        AssertInvalidLevel(
            "docx-invalid-style-missing-value.docx",
            documentXml,
            stylesXml.Replace(
                """<w:outlineLvl w:val="3"/>""",
                """<w:outlineLvl/>""",
                StringComparison.Ordinal));
    }
}
