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
    /// Проверяет выравнивание, физические и first-line/hanging отступы,
    /// интервалы до/после абзаца и lineRule auto/exact в DOCX.
    /// Проверка охватывает document defaults, paragraph style basedOn,
    /// default и независимые стили, direct properties, заголовки,
    /// header/footnote stories, HTML CSS и некорректные XML-значения.
    /// Все DOCX fixtures создаются во временной директории.
    /// </summary>
    private static void TestDocxParagraphLayout(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:pPrDefault>
                  <w:pPr>
                    <w:jc w:val="center"/>
                    <w:ind w:left="720" w:right="240"/>
                    <w:spacing w:before="120" w:after="240" w:line="360" w:lineRule="auto"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="Base" w:default="1">
                <w:pPr>
                  <w:ind w:left="1080" w:firstLine="360"/>
                  <w:spacing w:after="480"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Derived">
                <w:basedOn w:val="Base"/>
                <w:pPr>
                  <w:jc w:val="right"/>
                  <w:spacing w:lineRule="exact"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Independent">
                <w:pPr><w:jc w:val="left"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:pPr>
                  <w:jc w:val="right"/>
                  <w:ind w:left="1440"/>
                </w:pPr>
              </w:style>
            </w:styles>
            """;

        const string documentXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:r><w:t>Default layout</w:t></w:r></w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                  <w:r><w:t>Derived layout</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:jc w:val="both"/>
                    <w:ind w:right="600" w:hanging="240"/>
                    <w:spacing w:before="0" w:line="480" w:lineRule="auto"/>
                  </w:pPr>
                  <w:r><w:t>Direct layout</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Independent"/></w:pPr>
                  <w:r><w:t>Independent layout</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                  <w:r><w:t>Heading layout</w:t></w:r>
                </w:p>
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
                <w:r><w:t>Header layout</w:t></w:r>
              </w:p>
            </w:hdr>
            """;

        const string footnotesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:footnote w:id="1">
                <w:p>
                  <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                  <w:r><w:t>Footnote layout</w:t></w:r>
                </w:p>
              </w:footnote>
            </w:footnotes>
            """;

        /// <summary>
        /// Записывает самостоятельный DOCX из XML parts для чтения reader.
        /// Styles и document XML передаются явно; header/footnote остаются
        /// постоянными частями fixture во всех сценариях.
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
            /// Создаёт ZIP entry с XML в UTF-8 без BOM.
            /// </summary>
            void AddPart(
                string name,
                string xml)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(name).Open(),
                    new UTF8Encoding(false));
                writer.Write(xml);
            }

            AddPart(
                "word/document.xml",
                body);
            AddPart(
                "word/styles.xml",
                styles);
            AddPart(
                "word/header1.xml",
                headerXml);
            AddPart(
                "word/footnotes.xml",
                footnotesXml);
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

        var path = CreateDocx(
            "docx-paragraph-layout.docx",
            documentXml,
            stylesXml);

        var model = SupportedFormatRegistry.ReadDocument(path);
        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => !paragraph.IsNote)
            .ToArray();

        /// <summary>
        /// Выбирает основной абзац по точному тексту единственного inline.
        /// </summary>
        DocumentParagraph Paragraph(string text)
        {
            return paragraphs.Single(paragraph =>
                paragraph.Inlines.Any(inline =>
                    inline.Text == text));
        }

        var defaultParagraph = Paragraph("Default layout");

        AssertEqual(
            DocumentParagraphAlignment.Center,
            defaultParagraph.Alignment,
            "DOCX docDefaults alignment");
        Assert(
            defaultParagraph.LeftIndentPoints == 54 &&
            defaultParagraph.RightIndentPoints == 12 &&
            defaultParagraph.FirstLineIndentPoints == 18,
            "DOCX default style должен перекрывать только объявленный left indent.");
        Assert(
            defaultParagraph.SpaceBeforePoints == 6 &&
            defaultParagraph.SpaceAfterPoints == 24 &&
            defaultParagraph.LineSpacingMultiple == 1.5 &&
            defaultParagraph.LineSpacingPoints == null,
            "DOCX docDefaults и default paragraph style должны объединять интервалы.");

        var derived = Paragraph("Derived layout");

        AssertEqual(
            DocumentParagraphAlignment.Right,
            derived.Alignment,
            "DOCX derived w:jc");
        Assert(
            derived.LeftIndentPoints == 54 &&
            derived.FirstLineIndentPoints == 18 &&
            derived.SpaceBeforePoints == 6 &&
            derived.SpaceAfterPoints == 24 &&
            derived.LineSpacingPoints == 18 &&
            derived.LineSpacingMultiple == null,
            "DOCX basedOn должен наследовать значения и независимо переопределять lineRule.");

        var direct = Paragraph("Direct layout");

        AssertEqual(
            DocumentParagraphAlignment.Justify,
            direct.Alignment,
            "DOCX direct w:jc=both");
        Assert(
            direct.LeftIndentPoints == 54 &&
            direct.RightIndentPoints == 30 &&
            direct.FirstLineIndentPoints == -12 &&
            direct.SpaceBeforePoints == 0 &&
            direct.SpaceAfterPoints == 24 &&
            direct.LineSpacingPoints == null &&
            direct.LineSpacingMultiple == 2,
            "DOCX direct w:ind и w:spacing должны перекрывать отдельные свойства style.");

        var independent = Paragraph("Independent layout");

        AssertEqual(
            DocumentParagraphAlignment.Left,
            independent.Alignment,
            "DOCX explicit independent style");
        Assert(
            independent.LeftIndentPoints == 36 &&
            independent.FirstLineIndentPoints == null &&
            independent.SpaceAfterPoints == 12 &&
            independent.LineSpacingMultiple == 1.5,
            "DOCX independent style должен использовать docDefaults без default paragraph style.");

        var heading = Paragraph("Heading layout");

        Assert(
            heading.HeadingLevel == 1 &&
            heading.AnchorId == "docx-heading-1" &&
            heading.Alignment == DocumentParagraphAlignment.Right &&
            heading.LeftIndentPoints == 72 &&
            heading.RightIndentPoints == 12,
            "DOCX synthetic heading anchor должен сохранять paragraph layout.");

        var header = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single();

        Assert(
            header.Alignment == DocumentParagraphAlignment.Right &&
            header.LineSpacingPoints == 18,
            "DOCX header paragraph должен использовать style layout.");

        var footnote = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        Assert(
            footnote.Alignment == DocumentParagraphAlignment.Right &&
            footnote.LeftIndentPoints == 54 &&
            footnote.LineSpacingPoints == 18,
            "DOCX footnote paragraph должен сохранять style layout.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "text-align:center;margin-left:54pt;margin-right:12pt;text-indent:18pt;margin-top:6pt;margin-bottom:24pt;line-height:1.5;",
            "DOCX default paragraph CSS");
        AssertContains(
            html,
            "text-align:justify;margin-left:54pt;margin-right:30pt;text-indent:-12pt;margin-top:0pt;margin-bottom:24pt;line-height:2;",
            "DOCX direct paragraph CSS");
        AssertContains(
            html,
            "text-align:right;margin-left:72pt;margin-right:12pt;",
            "DOCX heading paragraph CSS");

        var reread = SupportedFormatRegistry.ReadDocument(path);

        Assert(
            reread.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.Alignment == DocumentParagraphAlignment.Justify &&
                    paragraph.FirstLineIndentPoints == -12),
            "DOCX layout должен совпадать между повторными чтениями.");

        /// <summary>
        /// Проверяет явное исключение для некорректного или недоступного
        /// в flow renderer форматирования абзаца DOCX.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string body,
            string styles,
            Type exceptionType,
            string diagnostic)
        {
            var invalidPath = CreateDocx(
                fileName,
                body,
                styles);
            Exception? failure = null;

            try
            {
                SupportedFormatRegistry.ReadDocument(invalidPath);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert(
                failure != null &&
                failure.GetType() == exceptionType &&
                failure.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal),
                $"DOCX {fileName} должен отклоняться через {exceptionType.Name}: {diagnostic}.");
        }

        AssertInvalid(
            "docx-jc-no-value.docx",
            documentXml,
            stylesXml.Replace(
                """<w:jc w:val="left"/>""",
                """<w:jc/>""",
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "w:jc");

        AssertInvalid(
            "docx-jc-unsupported.docx",
            documentXml,
            stylesXml.Replace(
                """<w:jc w:val="left"/>""",
                """<w:jc w:val="distribute"/>""",
                StringComparison.Ordinal),
            typeof(NotSupportedException),
            "w:jc=distribute");

        AssertInvalid(
            "docx-ind-invalid.docx",
            documentXml,
            stylesXml.Replace(
                """w:left="1080" """,
                """w:left="invalid" """,
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "w:left");

        AssertInvalid(
            "docx-spacing-line-invalid.docx",
            documentXml,
            stylesXml.Replace(
                """w:line="360" """,
                """w:line="bad" """,
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "w:line");

        AssertInvalid(
            "docx-spacing-rule-invalid.docx",
            documentXml,
            stylesXml.Replace(
                """<w:spacing w:lineRule="exact"/>""",
                """<w:spacing w:lineRule="invalid"/>""",
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "w:lineRule");

        AssertInvalid(
            "docx-line-rule-at-least.docx",
            documentXml,
            stylesXml.Replace(
                """<w:spacing w:lineRule="exact"/>""",
                """<w:spacing w:lineRule="atLeast"/>""",
                StringComparison.Ordinal),
            typeof(NotSupportedException),
            "atLeast");
    }
}
