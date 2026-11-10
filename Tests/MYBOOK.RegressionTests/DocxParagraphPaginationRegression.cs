using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX paragraph pagination controls: keepLines, keepNext,
    /// widowControl, bidi и pageBreakBefore. Проверка охватывает
    /// docDefaults, default/explicit paragraph styles, basedOn, прямые
    /// OnOff-значения, заголовки, таблицы, headers, footnotes и CSS.
    /// Пустой абзац с pageBreakBefore остаётся в модели.
    /// Явный w:br type=page остаётся отдельным DocumentFlowBreak.
    /// Неверные OnOff-значения дают диагностический InvalidDataException.
    /// Все OpenXML fixtures создаются во временной директории.
    /// </summary>
    private static void TestDocxParagraphPagination(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:pPrDefault>
                  <w:pPr>
                    <w:keepLines/>
                    <w:keepNext w:val="true"/>
                    <w:widowControl w:val="on"/>
                    <w:bidi w:val="1"/>
                    <w:pageBreakBefore w:val="0"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="Base" w:default="1">
                <w:pPr>
                  <w:keepLines w:val="0"/>
                  <w:keepNext w:val="false"/>
                  <w:widowControl w:val="off"/>
                  <w:bidi w:val="0"/>
                  <w:pageBreakBefore/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Derived">
                <w:basedOn w:val="Base"/>
                <w:pPr>
                  <w:keepLines w:val="on"/>
                  <w:keepNext/>
                  <w:bidi w:val="true"/>
                  <w:pageBreakBefore w:val="false"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Independent">
                <w:pPr><w:widowControl w:val="false"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:basedOn w:val="Derived"/>
                <w:pPr><w:pageBreakBefore w:val="on"/></w:pPr>
              </w:style>
            </w:styles>
            """;

        const string documentXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:r><w:t>Default paragraph</w:t></w:r></w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                  <w:r><w:t>Derived paragraph</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:keepLines w:val="false"/>
                    <w:keepNext w:val="off"/>
                    <w:widowControl w:val="true"/>
                    <w:bidi w:val="0"/>
                    <w:pageBreakBefore/>
                  </w:pPr>
                  <w:r><w:t>Direct override</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Independent"/></w:pPr>
                  <w:r><w:t>Independent paragraph</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                  <w:r><w:t>Heading with pagination</w:t></w:r>
                </w:p>
                <w:p>
                  <w:r><w:t>Manual break</w:t><w:br w:type="page"/></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:pageBreakBefore/>
                  </w:pPr>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                        <w:r><w:t>Table styled</w:t></w:r>
                      </w:p>
                      <w:p>
                        <w:pPr>
                          <w:pStyle w:val="Derived"/>
                          <w:pageBreakBefore w:val="1"/>
                          <w:widowControl w:val="on"/>
                        </w:pPr>
                        <w:r><w:t>Table direct</w:t></w:r>
                      </w:p>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        const string headerXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p>
                <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                <w:r><w:t>Header styled</w:t></w:r>
              </w:p>
            </w:hdr>
            """;

        const string footnotesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:footnote w:id="1">
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:keepLines w:val="0"/>
                    <w:pageBreakBefore w:val="1"/>
                  </w:pPr>
                  <w:r><w:t>Footnote direct</w:t></w:r>
                </w:p>
              </w:footnote>
            </w:footnotes>
            """;

        /// <summary>
        /// Создаёт DOCX из XML parts, включая отдельные header/footnote
        /// stories, для воспроизводимого чтения независимо от Books.
        /// </summary>
        string CreateDocx(
            string fileName,
            string body,
            string styles)
        {
            var path = Path.Combine(
                directory,
                fileName);

            using var archive = ZipFile.Open(
                path,
                ZipArchiveMode.Create);

            /// <summary>
            /// Записывает OpenXML part в ZIP с кодировкой UTF-8 без BOM.
            /// </summary>
            void AddPart(string name, string content)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(name).Open(),
                    new UTF8Encoding(false));
                writer.Write(content);
            }

            AddPart("word/document.xml", body);
            AddPart("word/styles.xml", styles);
            AddPart("word/header1.xml", headerXml);
            AddPart("word/footnotes.xml", footnotesXml);
            AddPart(
                "word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHeader"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
                                Target="header1.xml"/>
                </Relationships>
                """);

            return path;
        }

        /// <summary>
        /// Сопоставляет все пять модельных OnOff-свойств DOCX абзаца
        /// с явно заданными ожидаемыми значениями.
        /// </summary>
        void AssertControls(
            DocumentParagraph paragraph,
            bool keepTogether,
            bool keepWithNext,
            bool widowControl,
            bool rightToLeft,
            bool pageBreakBefore,
            string scenario)
        {
            Assert(
                paragraph.KeepTogether == keepTogether &&
                paragraph.KeepWithNext == keepWithNext &&
                paragraph.WidowControl == widowControl &&
                paragraph.RightToLeft == rightToLeft &&
                paragraph.PageBreakBefore == pageBreakBefore,
                $"DOCX {scenario} должен сохранять keepLines/keepNext/widowControl/bidi/pageBreakBefore.");
        }

        var path = CreateDocx(
            "docx-paragraph-pagination.docx",
            documentXml,
            stylesXml);
        var model = SupportedFormatRegistry.ReadDocument(path);

        /// <summary>
        /// Выбирает самостоятельный абзац основного документа по точному тексту.
        /// </summary>
        DocumentParagraph Paragraph(string text)
        {
            return model.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline => inline.Text == text));
        }

        AssertControls(
            Paragraph("Default paragraph"),
            false,
            false,
            false,
            false,
            true,
            "default style over docDefaults");

        AssertControls(
            Paragraph("Derived paragraph"),
            true,
            true,
            false,
            true,
            false,
            "derived style with inherited false");

        AssertControls(
            Paragraph("Direct override"),
            false,
            false,
            true,
            false,
            true,
            "direct OnOff overrides");

        AssertControls(
            Paragraph("Independent paragraph"),
            true,
            true,
            false,
            true,
            false,
            "explicit unrelated style with document defaults");

        var heading = Paragraph("Heading with pagination");

        Assert(
            heading.HeadingLevel == 1 &&
            heading.AnchorId == "docx-heading-1",
            "DOCX heading должен сохранять synthetic anchor при каскаде OnOff.");
        AssertControls(
            heading,
            true,
            true,
            false,
            true,
            true,
            "generated heading");

        var cellParagraphs = model.Blocks
            .OfType<DocumentTable>()
            .Single()
            .Rows[0].Cells[0]
            .Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        AssertEqual(
            2,
            cellParagraphs.Length,
            "DOCX table должна сохранять два отдельных paragraph blocks.");

        AssertControls(
            cellParagraphs[0],
            true,
            true,
            false,
            true,
            false,
            "table inherited style");

        AssertControls(
            cellParagraphs[1],
            true,
            true,
            true,
            true,
            true,
            "table direct controls");

        var header = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single();

        AssertControls(
            header,
            true,
            true,
            false,
            true,
            false,
            "header style");

        var footnote = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        AssertControls(
            footnote,
            false,
            true,
            false,
            true,
            true,
            "footnote direct controls");

        Assert(
            model.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.Inlines.Count == 0 &&
                    paragraph.PageBreakBefore),
            "DOCX пустой абзац с pageBreakBefore должен оставаться в модели.");

        AssertEqual(
            1,
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Count(block => block.Kind == DocumentFlowBreakKind.Page),
            "DOCX только явный w:br type=page создаёт самостоятельный page flow break.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "break-inside:avoid;page-break-inside:avoid;break-after:avoid;page-break-after:avoid;direction:rtl;unicode-bidi:plaintext;",
            "DOCX keepLines/keepNext/bidi CSS");
        AssertContains(
            html,
            "orphans:2;widows:2;break-before:page;page-break-before:always;",
            "DOCX widowControl/pageBreakBefore CSS");
        AssertContains(
            html,
            "break-inside:avoid;page-break-inside:avoid;break-after:avoid;page-break-after:avoid;orphans:2;widows:2;direction:rtl;unicode-bidi:plaintext;break-before:page;page-break-before:always;",
            "DOCX independent table paragraph CSS");
        Assert(
            html.Contains("Heading with pagination", StringComparison.Ordinal) &&
            html.Contains("Footnote direct", StringComparison.Ordinal),
            "DOCX HTML должен содержать heading и footnote с сохранёнными свойствами.");

        var reread = SupportedFormatRegistry.ReadDocument(path);

        Assert(
            reread.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.PageBreakBefore &&
                    paragraph.Inlines.Any(inline => inline.Text == "Direct override")),
            "DOCX повторное чтение должно возвращать тот же effective pageBreakBefore.");

        /// <summary>
        /// Проверяет отклонение недопустимого w:val в document defaults,
        /// paragraph styles и direct paragraph properties.
        /// </summary>
        void AssertInvalid(
            string name,
            string original,
            string replacement,
            bool inStyles,
            string diagnostic)
        {
            var invalidStyles = inStyles
                ? stylesXml.Replace(original, replacement, StringComparison.Ordinal)
                : stylesXml;
            var invalidBody = inStyles
                ? documentXml
                : documentXml.Replace(original, replacement, StringComparison.Ordinal);

            Assert(
                inStyles
                    ? !string.Equals(stylesXml, invalidStyles, StringComparison.Ordinal)
                    : !string.Equals(documentXml, invalidBody, StringComparison.Ordinal),
                $"DOCX {name}: тестируемый XML token должен присутствовать.");

            var invalidPath = CreateDocx(
                name,
                invalidBody,
                invalidStyles);
            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(invalidPath);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"DOCX {name} должен отклоняться с диагностикой {diagnostic}.");
        }

        AssertInvalid(
            "docx-pagination-default-invalid.docx",
            """<w:keepLines/>""",
            """<w:keepLines w:val="unknown"/>""",
            true,
            "w:keepLines");

        AssertInvalid(
            "docx-pagination-style-invalid.docx",
            """<w:keepNext w:val="false"/>""",
            """<w:keepNext w:val="2"/>""",
            true,
            "w:keepNext");

        AssertInvalid(
            "docx-pagination-widow-invalid.docx",
            """<w:widowControl w:val="off"/>""",
            """<w:widowControl w:val="maybe"/>""",
            true,
            "w:widowControl");

        AssertInvalid(
            "docx-pagination-bidi-invalid.docx",
            """<w:bidi w:val="0"/>""",
            """<w:bidi w:val="yes"/>""",
            false,
            "w:bidi");

        AssertInvalid(
            "docx-pagination-break-invalid.docx",
            """<w:pageBreakBefore/>""",
            """<w:pageBreakBefore w:val="2"/>""",
            false,
            "w:pageBreakBefore");
    }
}
