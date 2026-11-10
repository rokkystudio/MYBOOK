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
    /// Проверяет наследование и очистку DOCX tab stops по позициям
    /// из docDefaults, default/explicit paragraph styles, basedOn и w:pPr.
    /// Проверяет самостоятельные абзацы и вложенные таблицы внутри ячеек,
    /// сохранение совместимых плоских inlines, HTML-оформление и metadata,
    /// а также tab stops в headers/footnotes и диагностику неверного XML.
    /// Синтетические DOCX записываются только во временную директорию.
    /// </summary>
    private static void TestDocxTableParagraphTabs(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:pPrDefault>
                  <w:pPr>
                    <w:jc w:val="center"/>
                    <w:ind w:left="720"/>
                    <w:tabs>
                      <w:tab w:val="left" w:pos="720" w:leader="dot"/>
                      <w:tab w:val="center" w:pos="1440" w:leader="hyphen"/>
                    </w:tabs>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="Base" w:default="1">
                <w:pPr>
                  <w:ind w:left="1080"/>
                  <w:tabs>
                    <w:tab w:val="right" w:pos="720" w:leader="underscore"/>
                    <w:tab w:val="decimal" w:pos="2160" w:leader="dot"/>
                  </w:tabs>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Derived">
                <w:basedOn w:val="Base"/>
                <w:pPr>
                  <w:jc w:val="right"/>
                  <w:tabs>
                    <w:tab w:val="clear" w:pos="1440"/>
                    <w:tab w:val="bar" w:pos="2880" w:leader="heavy"/>
                  </w:tabs>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Independent">
                <w:pPr>
                  <w:jc w:val="left"/>
                  <w:tabs><w:tab w:val="center" w:pos="-360"/></w:tabs>
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
                <w:p><w:r><w:t>Body default</w:t><w:tab/><w:t>A</w:t></w:r></w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                        <w:r><w:t>Cell first</w:t><w:tab/><w:t>Alpha</w:t></w:r>
                      </w:p>
                      <w:p>
                        <w:pPr>
                          <w:pStyle w:val="Derived"/>
                          <w:jc w:val="left"/>
                          <w:tabs>
                            <w:tab w:val="clear" w:pos="720"/>
                            <w:tab w:val="num" w:pos="1800" w:leader="middleDot"/>
                            <w:tab w:val="center" w:pos="2880"/>
                          </w:tabs>
                        </w:pPr>
                        <w:r><w:t>Cell second</w:t></w:r>
                      </w:p>
                      <w:tbl>
                        <w:tr>
                          <w:tc>
                            <w:p><w:r><w:t>Nested table text</w:t></w:r></w:p>
                          </w:tc>
                        </w:tr>
                      </w:tbl>
                      <w:p/>
                    </w:tc>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Independent"/></w:pPr>
                        <w:r><w:t>Independent cell</w:t></w:r>
                      </w:p>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                <w:p><w:r><w:t>After table</w:t></w:r></w:p>
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
                <w:r><w:t>Header tabs</w:t></w:r>
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
                  <w:r><w:t>Footnote tabs</w:t></w:r>
                </w:p>
              </w:footnote>
            </w:footnotes>
            """;

        /// <summary>
        /// Создаёт полный тестовый DOCX с документом, styles.xml,
        /// колонтитулом, сноской и relationships в отдельном ZIP.
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
            /// Добавляет указанный OpenXML part с UTF-8 содержимым без BOM.
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

        /// <summary>
        /// Проверяет по порядку полный набор позиций, alignments и leaders
        /// итогового абзаца без fallback к несуществующим tab stops.
        /// </summary>
        void AssertTabs(
            DocumentParagraph paragraph,
            params (double Position, DocumentTabAlignment Alignment, DocumentTabLeader Leader)[] expected)
        {
            AssertEqual(
                expected.Length,
                paragraph.TabStops.Count,
                $"DOCX количество tab stops: {string.Concat(paragraph.Inlines.Select(inline => inline.Text))}");

            for (var index = 0; index < expected.Length; index++)
            {
                var tab = paragraph.TabStops[index];

                Assert(
                    tab.PositionPoints == expected[index].Position &&
                    tab.Alignment == expected[index].Alignment &&
                    tab.Leader == expected[index].Leader,
                    $"DOCX tab stop #{index} должен иметь точные position/alignment/leader.");
            }
        }

        var path = CreateDocx(
            "docx-table-paragraph-tabs.docx",
            documentXml,
            stylesXml);
        var model = SupportedFormatRegistry.ReadDocument(path);

        var body = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.Inlines.Any(inline =>
                inline.Text.Contains("Body default", StringComparison.Ordinal)));
        var afterTable = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.Inlines.Any(inline =>
                inline.Text == "After table"));

        AssertTabs(
            body,
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (72, DocumentTabAlignment.Center, DocumentTabLeader.Hyphens),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots));

        AssertTabs(
            afterTable,
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (72, DocumentTabAlignment.Center, DocumentTabLeader.Hyphens),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots));

        var table = model.Blocks
            .OfType<DocumentTable>()
            .Single();
        var firstCell = table.Rows[0].Cells[0];
        var secondCell = table.Rows[0].Cells[1];

        AssertEqual(
            4,
            firstCell.Blocks.Count,
            "DOCX table cell должна сохранять все paragraph и nested table blocks.");

        var first = (DocumentParagraph)firstCell.Blocks[0];
        var second = (DocumentParagraph)firstCell.Blocks[1];
        var nested = (DocumentTable)firstCell.Blocks[2];
        var empty = (DocumentParagraph)firstCell.Blocks[3];
        var independent = (DocumentParagraph)secondCell.Blocks.Single();

        Assert(
            first.Alignment == DocumentParagraphAlignment.Right &&
            first.LeftIndentPoints == 54 &&
            second.Alignment == DocumentParagraphAlignment.Left &&
            second.LeftIndentPoints == 54 &&
            empty.Inlines.Count == 0,
            "DOCX разные абзацы одной ячейки должны сохранять индивидуальное оформление и пустые блоки.");

        AssertTabs(
            first,
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots),
            (144, DocumentTabAlignment.Bar, DocumentTabLeader.ThickLine));

        AssertTabs(
            second,
            (90, DocumentTabAlignment.List, DocumentTabLeader.MiddleDots),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots),
            (144, DocumentTabAlignment.Center, DocumentTabLeader.None));

        Assert(
            nested.Rows.Count == 1 &&
            nested.Rows[0].Cells.Count == 1 &&
            nested.Rows[0].Cells[0].Blocks.Single() is DocumentParagraph,
            "DOCX вложенная таблица должна содержать собственные paragraph blocks.");

        AssertTabs(
            (DocumentParagraph)nested.Rows[0].Cells[0].Blocks.Single(),
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (72, DocumentTabAlignment.Center, DocumentTabLeader.Hyphens),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots));

        Assert(
            independent.Alignment == DocumentParagraphAlignment.Left &&
            independent.LeftIndentPoints == 36,
            "DOCX независимый стиль ячейки должен брать docDefaults, не default paragraph style.");

        AssertTabs(
            independent,
            (-18, DocumentTabAlignment.Center, DocumentTabLeader.None),
            (36, DocumentTabAlignment.Left, DocumentTabLeader.Dots),
            (72, DocumentTabAlignment.Center, DocumentTabLeader.Hyphens));

        Assert(
            string.Concat(firstCell.Inlines.Select(inline => inline.Text))
                .Contains("Cell first\tAlpha" + Environment.NewLine + "Cell second",
                    StringComparison.Ordinal) &&
            firstCell.Inlines.All(inline =>
                !inline.Text.Contains("Nested table text", StringComparison.Ordinal)),
            "DOCX flattened Inlines должны сохранять текст абзацев без добавления nested table content.");

        var header = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single();
        var note = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        AssertTabs(
            header,
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots),
            (144, DocumentTabAlignment.Bar, DocumentTabLeader.ThickLine));

        AssertTabs(
            note,
            (36, DocumentTabAlignment.Right, DocumentTabLeader.Underline),
            (108, DocumentTabAlignment.Decimal, DocumentTabLeader.Dots),
            (144, DocumentTabAlignment.Bar, DocumentTabLeader.ThickLine));

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "data-tab-stops=\"36:right:underline;108:decimal:dots;144:bar:thick-line\"",
            "DOCX inherited tab stops HTML metadata");
        AssertContains(
            html,
            "data-tab-stops=\"90:list:middle-dots;108:decimal:dots;144:center:none\"",
            "DOCX direct tab stops HTML metadata");
        AssertContains(
            html,
            "data-tab-stops=\"-18:center:none;36:left:dots;72:center:hyphens\"",
            "DOCX independent style tab stops HTML metadata");
        AssertContains(
            html,
            "text-align:right;margin-left:54pt;",
            "DOCX самостоятельный CSS-абзац внутри ячейки");
        AssertContains(
            html,
            "text-align:left;margin-left:54pt;",
            "DOCX direct CSS-абзац внутри той же ячейки");

        Assert(
            html.IndexOf("Cell first", StringComparison.Ordinal) <
            html.IndexOf("Cell second", StringComparison.Ordinal) &&
            html.IndexOf("Cell second", StringComparison.Ordinal) <
            html.IndexOf("Nested table text", StringComparison.Ordinal) &&
            html.IndexOf("Nested table text", StringComparison.Ordinal) <
            html.IndexOf("Independent cell", StringComparison.Ordinal),
            "DOCX HTML должен сохранять порядок paragraph/nested table/cell blocks.");

        var reread = SupportedFormatRegistry.ReadDocument(path);
        var rereadCell = reread.Blocks
            .OfType<DocumentTable>()
            .Single()
            .Rows[0].Cells[0];

        Assert(
            rereadCell.Blocks.Count == 4 &&
            ((DocumentParagraph)rereadCell.Blocks[1]).TabStops.Count == 3,
            "DOCX tab stops и порядок блоков должны быть воспроизводимы.");

        /// <summary>
        /// Проверяет конкретное исключение для отсутствующих, некорректных
        /// и неподдерживаемых w:tabs/w:tab атрибутов.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string search,
            string replacement,
            Type exceptionType,
            string diagnostic)
        {
            var brokenStyles = stylesXml.Replace(
                search,
                replacement,
                StringComparison.Ordinal);

            Assert(
                !string.Equals(stylesXml, brokenStyles, StringComparison.Ordinal),
                $"DOCX fixture {fileName} должен содержать тестируемый XML attribute.");

            var brokenPath = CreateDocx(
                fileName,
                documentXml,
                brokenStyles);
            Exception? failure = null;

            try
            {
                SupportedFormatRegistry.ReadDocument(brokenPath);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert(
                failure != null &&
                failure.GetType() == exceptionType &&
                failure.Message.Contains(diagnostic, StringComparison.Ordinal),
                $"DOCX {fileName} должен отклоняться через {exceptionType.Name}: {diagnostic}.");
        }

        AssertInvalid(
            "docx-tabs-missing-pos.docx",
            """w:pos="720" w:leader="dot" """.TrimEnd(),
            """w:leader="dot" """.TrimEnd(),
            typeof(InvalidDataException),
            "w:pos");

        AssertInvalid(
            "docx-tabs-bad-pos.docx",
            """w:pos="720" w:leader="dot" """.TrimEnd(),
            """w:pos="invalid" w:leader="dot" """.TrimEnd(),
            typeof(InvalidDataException),
            "w:pos");

        AssertInvalid(
            "docx-tabs-missing-val.docx",
            """w:val="left" w:pos="720" """.TrimEnd(),
            """w:pos="720" """.TrimEnd(),
            typeof(InvalidDataException),
            "w:val");

        AssertInvalid(
            "docx-tabs-unsupported-val.docx",
            """w:val="left" w:pos="720" """.TrimEnd(),
            """w:val="start" w:pos="720" """.TrimEnd(),
            typeof(NotSupportedException),
            "w:val=start");

        AssertInvalid(
            "docx-tabs-unsupported-leader.docx",
            """w:leader="hyphen" """.TrimEnd(),
            """w:leader="equal" """.TrimEnd(),
            typeof(NotSupportedException),
            "w:leader=equal");
    }
}
