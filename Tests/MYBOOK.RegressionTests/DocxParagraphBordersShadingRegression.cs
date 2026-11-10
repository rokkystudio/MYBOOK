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
    /// Проверяет импорт DOCX w:pBdr/w:shd с каскадом docDefaults,
    /// default/explicit paragraph styles, basedOn и прямого w:pPr.
    /// Охватывает независимые стороны, nil/none, размеры и space,
    /// RGB и auto colors, shadow/frame, clear/solid/percentage/stripe
    /// shading, border-between группы, CSS и source metadata.
    /// Заголовки, пустые абзацы, ячейки, header/footnote и ошибочные
    /// XML declarations проверяются без обращения к каталогу Books.
    /// </summary>
    private static void TestDocxParagraphBordersShading(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:pPrDefault>
                  <w:pPr>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="16" w:space="2" w:color="223344"/>
                      <w:left w:val="dotted" w:sz="8" w:space="3" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="8" w:space="1" w:color="445566"/>
                    </w:pBdr>
                    <w:shd w:val="clear" w:fill="FFF2CC"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="Base" w:default="1">
                <w:pPr>
                  <w:pBdr>
                    <w:top w:val="double" w:sz="24" w:space="4" w:color="112233"/>
                    <w:right w:val="dashed" w:sz="12" w:space="2" w:color="334455"/>
                    <w:between w:val="single" w:sz="8" w:space="1" w:color="123456"/>
                  </w:pBdr>
                  <w:shd w:val="pct50" w:color="FF0000" w:fill="FFFFFF"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Derived">
                <w:basedOn w:val="Base"/>
                <w:pPr>
                  <w:pBdr>
                    <w:left w:val="nil"/>
                    <w:bar w:val="single" w:sz="12" w:space="1" w:color="00AA00"/>
                  </w:pBdr>
                  <w:shd w:val="horzStripe" w:color="0000FF" w:fill="EEEEEE"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Independent">
                <w:pPr>
                  <w:pBdr><w:bottom w:val="none"/></w:pBdr>
                  <w:shd w:val="solid" w:color="00FF00"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:basedOn w:val="Derived"/>
                <w:pPr>
                  <w:pBdr>
                    <w:top w:val="dashed" w:sz="16" w:space="1" w:color="ABCDEF"/>
                  </w:pBdr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Group">
                <w:pPr>
                  <w:pBdr>
                    <w:top w:val="single" w:sz="16" w:space="2" w:color="999999"/>
                    <w:left w:val="single" w:sz="8" w:space="1" w:color="999999"/>
                    <w:bottom w:val="single" w:sz="16" w:space="2" w:color="999999"/>
                    <w:right w:val="single" w:sz="8" w:space="1" w:color="999999"/>
                    <w:between w:val="dotted" w:sz="6" w:space="2" w:color="AABBCC"/>
                  </w:pBdr>
                  <w:shd w:val="clear" w:fill="F0F0F0"/>
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
                <w:p><w:r><w:t>Default borders</w:t></w:r></w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                  <w:r><w:t>Derived borders</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="8" w:space="5" w:color="abcdef"
                             w:shadow="1" w:frame="on"/>
                      <w:right w:val="none"/>
                      <w:between w:val="nil"/>
                    </w:pBdr>
                    <w:shd w:val="clear" w:fill="123456"/>
                  </w:pPr>
                  <w:r><w:t>Direct borders</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Independent"/></w:pPr>
                  <w:r><w:t>Independent borders</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                  <w:r><w:t>Heading borders</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:pBdr>
                      <w:top w:val="none"/>
                      <w:left w:val="nil"/>
                      <w:bottom w:val="none"/>
                      <w:right w:val="nil"/>
                      <w:between w:val="nil"/>
                      <w:bar w:val="none"/>
                    </w:pBdr>
                    <w:shd w:val="nil"/>
                  </w:pPr>
                  <w:r><w:t>Clear inherited appearance</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:shd w:fill="00CCFF"/></w:pPr>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Group"/></w:pPr>
                  <w:r><w:t>Group first</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Group"/></w:pPr>
                  <w:r><w:t>Group second</w:t></w:r>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                        <w:r><w:t>Table inherited</w:t></w:r>
                      </w:p>
                      <w:p>
                        <w:pPr>
                          <w:pStyle w:val="Derived"/>
                          <w:pBdr>
                            <w:top w:val="none"/>
                            <w:right w:val="single" w:sz="16" w:space="3" w:color="1122AA"/>
                          </w:pBdr>
                          <w:shd w:val="solid" w:color="112233"/>
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
                <w:r><w:t>Header borders</w:t></w:r>
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
                    <w:pBdr><w:right w:val="nil"/></w:pBdr>
                    <w:shd w:val="clear" w:fill="CAFE00"/>
                  </w:pPr>
                  <w:r><w:t>Footnote borders</w:t></w:r>
                </w:p>
              </w:footnote>
            </w:footnotes>
            """;

        /// <summary>
        /// Записывает DOCX из независимых XML parts для регрессионного
        /// чтения без установленного Microsoft Word и каталога Books.
        /// </summary>
        string CreateDocx(
            string fileName,
            string body,
            string? styles)
        {
            var path = Path.Combine(
                directory,
                fileName);

            using var archive = ZipFile.Open(
                path,
                ZipArchiveMode.Create);

            /// <summary>
            /// Создаёт XML ZIP part, записанный как UTF-8 без BOM.
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

            AddPart("word/document.xml", body);

            if (styles != null)
            {
                AddPart("word/styles.xml", styles);
            }

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

        var path = CreateDocx(
            "docx-borders-shading.docx",
            documentXml,
            stylesXml);
        var model = SupportedFormatRegistry.ReadDocument(path);

        /// <summary>
        /// Возвращает основной абзац с точным текстом одного из inlines.
        /// </summary>
        DocumentParagraph Paragraph(string text)
        {
            return model.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline => inline.Text == text));
        }

        /// <summary>
        /// Проверяет тип, ширину, RGB, расстояние и дополнительные
        /// признаки одной стороны границы DOCX.
        /// </summary>
        void AssertBorder(
            DocumentBorder? border,
            byte type,
            double width,
            string? color,
            double spacing,
            bool shadow,
            bool frame,
            string scenario)
        {
            Assert(
                border != null &&
                border.Type == type &&
                border.WidthPoints == width &&
                border.Color == color &&
                border.SpacingPoints == spacing &&
                border.Shadow == shadow &&
                border.Frame == frame,
                $"DOCX {scenario} должен сохранять тип/ширину/цвет/space/shadow/frame.");
        }

        var defaultParagraph = Paragraph("Default borders");

        AssertBorder(
            defaultParagraph.BorderTop,
            0x03,
            3,
            "#112233",
            4,
            false,
            false,
            "default paragraph top");
        AssertBorder(
            defaultParagraph.BorderLeft,
            0x06,
            1,
            null,
            3,
            false,
            false,
            "docDefaults left with auto color");
        AssertBorder(
            defaultParagraph.BorderBottom,
            0x01,
            1,
            "#445566",
            1,
            false,
            false,
            "docDefaults bottom");
        AssertBorder(
            defaultParagraph.BorderRight,
            0x07,
            1.5,
            "#334455",
            2,
            false,
            false,
            "default paragraph right");
        AssertBorder(
            defaultParagraph.BorderBetween,
            0x01,
            1,
            "#123456",
            1,
            false,
            false,
            "default paragraph between");
        Assert(
            defaultParagraph.Shading is
            {
                Pattern: 0x0008,
                ForegroundColor: "#FF0000",
                BackgroundColor: "#FFFFFF"
            },
            "DOCX pct50 shading должна наследоваться из default paragraph style.");

        var derived = Paragraph("Derived borders");

        AssertBorder(
            derived.BorderTop,
            0x03,
            3,
            "#112233",
            4,
            false,
            false,
            "derived top");
        AssertBorder(
            derived.BorderLeft,
            0x00,
            0,
            null,
            0,
            false,
            false,
            "derived nil left");
        AssertBorder(
            derived.BorderBar,
            0x01,
            1.5,
            "#00AA00",
            1,
            false,
            false,
            "derived bar");
        Assert(
            derived.Shading is
            {
                Pattern: 0x000E,
                ForegroundColor: "#0000FF",
                BackgroundColor: "#EEEEEE"
            },
            "DOCX horzStripe shading должна перекрывать inherited pct50.");

        var direct = Paragraph("Direct borders");

        AssertBorder(
            direct.BorderTop,
            0x01,
            1,
            "#ABCDEF",
            5,
            true,
            true,
            "direct top with shadow/frame");
        AssertBorder(
            direct.BorderRight,
            0x00,
            0,
            null,
            0,
            false,
            false,
            "direct none right");
        AssertBorder(
            direct.BorderBetween,
            0x00,
            0,
            null,
            0,
            false,
            false,
            "direct nil between");
        Assert(
            direct.BorderBottom?.Color == "#445566" &&
            direct.BorderLeft?.Type == 0 &&
            direct.BorderBar?.Color == "#00AA00" &&
            direct.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#123456"
            },
            "DOCX direct w:pBdr/w:shd должны перекрывать только объявленные стороны и shading.");

        var independent = Paragraph("Independent borders");

        AssertBorder(
            independent.BorderTop,
            0x01,
            2,
            "#223344",
            2,
            false,
            false,
            "independent style uses docDefaults top");
        AssertBorder(
            independent.BorderBottom,
            0x00,
            0,
            null,
            0,
            false,
            false,
            "independent style none bottom");
        Assert(
            independent.BorderRight == null &&
            independent.Shading is
            {
                Pattern: 1,
                ForegroundColor: "#00FF00"
            },
            "DOCX independent style не должен наследовать default paragraph style.");

        var heading = Paragraph("Heading borders");

        Assert(
            heading.AnchorId == "docx-heading-1" &&
            heading.HeadingLevel == 1,
            "DOCX heading должен участвовать в outlines с synthetic anchor.");
        AssertBorder(
            heading.BorderTop,
            0x07,
            2,
            "#ABCDEF",
            1,
            false,
            false,
            "heading style top");
        Assert(
            heading.BorderLeft?.Type == 0 &&
            heading.Shading?.Pattern == 0x000E,
            "DOCX heading должен сохранять inherited border/shading.");

        var cleared = Paragraph("Clear inherited appearance");

        Assert(
            cleared.BorderTop?.Type == 0 &&
            cleared.BorderBottom?.Type == 0 &&
            cleared.BorderLeft?.Type == 0 &&
            cleared.BorderRight?.Type == 0 &&
            cleared.BorderBetween?.Type == 0 &&
            cleared.BorderBar?.Type == 0 &&
            cleared.Shading is
            {
                Pattern: 0,
                BackgroundColor: null
            },
            "DOCX none/nil должны отменять inherited borders и shading.");

        var empty = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph =>
                paragraph.Inlines.Count == 0 &&
                paragraph.Shading is
                {
                    Pattern: 0,
                    BackgroundColor: "#00CCFF"
                });

        Assert(
            empty.BorderTop?.Type == 0x03,
            "DOCX пустой форматированный абзац должен сохранять style border.");

        var table = model.Blocks.OfType<DocumentTable>().Single();
        var cells = table.Rows[0].Cells[0].Blocks
            .OfType<DocumentParagraph>()
            .ToArray();

        AssertEqual(
            2,
            cells.Length,
            "DOCX table должна сохранять каждый оформленный абзац.");
        Assert(
            cells[0].BorderBar?.Color == "#00AA00" &&
            cells[0].Shading?.Pattern == 0x000E &&
            cells[1].BorderTop?.Type == 0 &&
            cells[1].BorderRight?.Color == "#1122AA" &&
            cells[1].Shading is
            {
                Pattern: 1,
                ForegroundColor: "#112233"
            },
            "DOCX styled/direct cell paragraphs должны иметь индивидуальные borders/shading.");

        var header = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single();

        Assert(
            header.BorderBar?.Color == "#00AA00" &&
            header.Shading?.Pattern == 0x000E,
            "DOCX header должен сохранять оформление resolved paragraph style.");

        var footnote = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        Assert(
            footnote.BorderTop?.Type == 0x03 &&
            footnote.BorderRight?.Type == 0 &&
            footnote.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#CAFE00"
            },
            "DOCX footnote должен сохранять styled borders и direct clear shading.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "border-top:3pt double #112233;",
            "DOCX w:pBdr top CSS width in eighths of a point");
        AssertContains(
            html,
            "border-left:1pt dotted currentColor;",
            "DOCX border auto color CSS");
        AssertContains(
            html,
            "border-top:1pt solid #ABCDEF;",
            "DOCX direct top CSS");
        AssertContains(
            html,
            "padding-top:5pt;",
            "DOCX border spacing CSS padding in points");
        AssertContains(
            html,
            "background-color:#123456;",
            "DOCX clear shading CSS");
        AssertContains(
            html,
            "background-color:#00FF00;",
            "DOCX solid shading CSS");
        AssertContains(
            html,
            "background-image:linear-gradient(rgba(255,0,0,0.5),rgba(255,0,0,0.5));",
            "DOCX pct50 shading CSS");
        AssertContains(
            html,
            "background-image:repeating-linear-gradient(0deg,",
            "DOCX horzStripe shading CSS");
        AssertContains(
            html,
            "data-shading-pattern=\"14\"",
            "DOCX shading source pattern metadata");
        AssertContains(
            html,
            "data-border-top-type=\"1\" data-border-top-width-pt=\"1\" data-border-top-color=\"#ABCDEF\" data-border-top-spacing-pt=\"5\" data-border-top-shadow=\"true\" data-border-top-frame=\"true\"",
            "DOCX border top raw metadata");
        AssertContains(
            html,
            "data-border-between-type=\"0\" data-border-between-width-pt=\"0\"",
            "DOCX inherited between nil metadata");
        AssertContains(
            html,
            "data-border-bar-type=\"1\" data-border-bar-width-pt=\"1.5\"",
            "DOCX bar border metadata");

        /// <summary>
        /// Выбирает HTML-тег абзаца перед заданным text fragment.
        /// </summary>
        string GetOpeningTag(string text)
        {
            var end = html.IndexOf(
                ">" + text + "</p>",
                StringComparison.Ordinal);

            Assert(
                end >= 0,
                $"DOCX HTML должен содержать абзац {text}.");

            var start = html.LastIndexOf(
                "<p",
                end,
                StringComparison.Ordinal);

            Assert(
                start >= 0,
                $"DOCX HTML должен иметь открывающий тег абзаца {text}.");

            return html.Substring(start, end - start + 1);
        }

        var groupFirst = GetOpeningTag("Group first");
        var groupSecond = GetOpeningTag("Group second");
        var clearedTag = GetOpeningTag("Clear inherited appearance");

        Assert(
            groupFirst.Contains(
                "border-bottom:0.75pt dotted #AABBCC;",
                StringComparison.Ordinal) &&
            !groupSecond.Contains("border-top:", StringComparison.Ordinal) &&
            groupSecond.Contains(
                "border-bottom:2pt solid #999999;",
                StringComparison.Ordinal) &&
            groupFirst.Contains("padding-bottom:2pt;", StringComparison.Ordinal),
            "DOCX grouped paragraph CSS должен использовать border-between между одинаковыми pBdr.");

        Assert(
            !clearedTag.Contains("border-top:", StringComparison.Ordinal) &&
            !clearedTag.Contains("border-bottom:", StringComparison.Ordinal) &&
            !clearedTag.Contains("background-color:", StringComparison.Ordinal) &&
            !clearedTag.Contains("background-image:", StringComparison.Ordinal),
            "DOCX nil/none должны очищать видимую CSS-границу и shading.");

        var withoutStyles = CreateDocx(
            "docx-borders-without-styles.docx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Without styles</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """,
            null);
        var noStyleParagraph = SupportedFormatRegistry.ReadDocument(withoutStyles)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph =>
                paragraph.Inlines.Any(inline => inline.Text == "Without styles"));

        Assert(
            noStyleParagraph.BorderTop == null &&
            noStyleParagraph.BorderBottom == null &&
            noStyleParagraph.BorderLeft == null &&
            noStyleParagraph.BorderRight == null &&
            noStyleParagraph.Shading == null,
            "DOCX без styles.xml и без pPr должен сохранять пустой formatting.");

        /// <summary>
        /// Проверяет точный тип исключения для неверных или
        /// неподдерживаемых border/shading XML declarations.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string original,
            string replacement,
            Type exceptionType,
            string diagnostic)
        {
            var brokenStyles = stylesXml.Replace(
                original,
                replacement,
                StringComparison.Ordinal);

            Assert(
                !string.Equals(stylesXml, brokenStyles, StringComparison.Ordinal),
                $"DOCX fixture {fileName} должен содержать тестируемый XML fragment.");

            var brokenPath = CreateDocx(
                fileName,
                documentXml,
                brokenStyles);
            Exception? failure = null;

            try
            {
                SupportedFormatRegistry.ReadDocument(brokenPath);
            }
            catch (Exception error)
            {
                failure = error;
            }

            Assert(
                failure != null &&
                failure.GetType() == exceptionType &&
                failure.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal),
                $"DOCX fixture {fileName} ожидает {exceptionType.Name} c диагностикой {diagnostic}.");
        }

        const string defaultTop =
            """<w:top w:val="single" w:sz="16" w:space="2" w:color="223344"/>""";
        const string baseShading =
            """<w:shd w:val="pct50" w:color="FF0000" w:fill="FFFFFF"/>""";

        foreach (var pattern in new (string Name, ushort Code, string Alpha)[]
                 {
                     ("pct12", 0x0025, "0.125"),
                     ("pct37", 0x002C, "0.375"),
                     ("pct95", 0x003C, "0.95")
                 })
        {
            var patternedPath = CreateDocx(
                "docx-borders-shading-" + pattern.Name + ".docx",
                documentXml,
                stylesXml.Replace(
                    baseShading,
                    baseShading.Replace(
                        "pct50",
                        pattern.Name,
                        StringComparison.Ordinal),
                    StringComparison.Ordinal));
            var patternedModel = SupportedFormatRegistry.ReadDocument(patternedPath);
            var firstParagraph = patternedModel.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline => inline.Text == "Default borders"));

            Assert(
                firstParagraph.Shading?.Pattern == pattern.Code,
                $"DOCX shading {pattern.Name} должна сохранять точный Ipat pattern code.");

            AssertContains(
                DocumentHtmlRenderer.Render(
                    patternedModel,
                    CreateRenderTheme()),
                "rgba(255,0,0," + pattern.Alpha + ")",
                $"DOCX shading {pattern.Name} должна создавать процентный CSS overlay.");
        }

        var crossPath = CreateDocx(
            "docx-borders-shading-thin-cross.docx",
            documentXml,
            stylesXml.Replace(
                """w:val="horzStripe" w:color="0000FF" """.TrimEnd(),
                """w:val="thinDiagCross" w:color="0000FF" """.TrimEnd(),
                StringComparison.Ordinal));
        var crossModel = SupportedFormatRegistry.ReadDocument(crossPath);
        var crossParagraph = crossModel.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph =>
                paragraph.Inlines.Any(inline => inline.Text == "Derived borders"));

        Assert(
            crossParagraph.Shading?.Pattern == 0x0019,
            "DOCX thinDiagCross должен сохранять нейтральный pattern code.");

        AssertContains(
            DocumentHtmlRenderer.Render(
                crossModel,
                CreateRenderTheme()),
            "repeating-linear-gradient(45deg,",
            "DOCX thinDiagCross должен формировать diagonal line CSS.");

        AssertInvalid(
            "docx-border-missing-val.docx",
            defaultTop,
            """<w:top w:sz="16" w:space="2" w:color="223344"/>""",
            typeof(InvalidDataException),
            "w:val");
        AssertInvalid(
            "docx-border-unsupported-style.docx",
            defaultTop,
            """<w:top w:val="wave" w:sz="16" w:space="2" w:color="223344"/>""",
            typeof(NotSupportedException),
            "w:val=wave");
        AssertInvalid(
            "docx-border-invalid-width.docx",
            defaultTop,
            """<w:top w:val="single" w:sz="abc" w:space="2" w:color="223344"/>""",
            typeof(InvalidDataException),
            "w:sz");
        AssertInvalid(
            "docx-border-missing-width.docx",
            defaultTop,
            """<w:top w:val="single" w:space="2" w:color="223344"/>""",
            typeof(InvalidDataException),
            "w:sz");
        AssertInvalid(
            "docx-border-invalid-space.docx",
            defaultTop,
            """<w:top w:val="single" w:sz="16" w:space="-2" w:color="223344"/>""",
            typeof(InvalidDataException),
            "w:space");
        AssertInvalid(
            "docx-border-invalid-color.docx",
            defaultTop,
            """<w:top w:val="single" w:sz="16" w:space="2" w:color="GGGGGG"/>""",
            typeof(InvalidDataException),
            "RGB");
        AssertInvalid(
            "docx-border-unsupported-theme.docx",
            defaultTop,
            """<w:top w:val="single" w:sz="16" w:space="2" w:color="223344" w:themeColor="accent1"/>""",
            typeof(NotSupportedException),
            "theme color");
        AssertInvalid(
            "docx-border-invalid-toggle.docx",
            defaultTop,
            """<w:top w:val="single" w:sz="16" w:space="2" w:color="223344" w:shadow="invalid"/>""",
            typeof(InvalidDataException),
            "w:shadow");
        AssertInvalid(
            "docx-border-duplicate-side.docx",
            defaultTop,
            defaultTop + defaultTop,
            typeof(InvalidDataException),
            "повторную сторону");
        AssertInvalid(
            "docx-border-unsupported-side.docx",
            defaultTop,
            """<w:start w:val="single" w:sz="16" w:space="2" w:color="223344"/>""",
            typeof(NotSupportedException),
            "w:start");
        AssertInvalid(
            "docx-shading-unknown-pattern.docx",
            baseShading,
            """<w:shd w:val="unknown" w:color="FF0000" w:fill="FFFFFF"/>""",
            typeof(NotSupportedException),
            "w:val=unknown");
        AssertInvalid(
            "docx-shading-invalid-rgb.docx",
            baseShading,
            """<w:shd w:val="pct50" w:color="FF0000" w:fill="invalid"/>""",
            typeof(InvalidDataException),
            "w:shd/w:fill");
        AssertInvalid(
            "docx-shading-unsupported-theme.docx",
            baseShading,
            """<w:shd w:val="pct50" w:color="FF0000" w:fill="FFFFFF" w:themeFill="accent3"/>""",
            typeof(NotSupportedException),
            "themeColor/themeFill");
    }
}
