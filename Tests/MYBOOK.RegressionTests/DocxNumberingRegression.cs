using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX numbering.xml: независимые num instances и stories,
    /// реальную нумерацию уровней, start/startOverride, вложенные lvlText,
    /// lvlOverride, default и явный lvlRestart, decimal/decimalZero,
    /// letter/Roman/bullet formats, а также диагностику ошибочных определений.
    /// Создаёт документы только во временном каталоге regression suite.
    /// </summary>
    private static void TestDocxNumbering(string directory)
    {
        const string numberingXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="10">
                <w:lvl w:ilvl="0">
                  <w:start w:val="3"/>
                  <w:numFmt w:val="decimal"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
                <w:lvl w:ilvl="1">
                  <w:start w:val="2"/>
                  <w:numFmt w:val="lowerLetter"/>
                  <w:lvlText w:val="%1.%2)"/>
                </w:lvl>
                <w:lvl w:ilvl="2">
                  <w:start w:val="1"/>
                  <w:numFmt w:val="lowerRoman"/>
                  <w:lvlRestart w:val="0"/>
                  <w:lvlText w:val="%1.%2.%3."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="11">
                <w:lvl w:ilvl="0">
                  <w:numFmt w:val="bullet"/>
                  <w:lvlText w:val="•"/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="12">
                <w:lvl w:ilvl="0">
                  <w:start w:val="26"/>
                  <w:numFmt w:val="upperLetter"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="13">
                <w:lvl w:ilvl="0">
                  <w:start w:val="1"/>
                  <w:numFmt w:val="decimalZero"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="14">
                <w:lvl w:ilvl="0">
                  <w:start w:val="1"/>
                  <w:numFmt w:val="decimal"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
                <w:lvl w:ilvl="1">
                  <w:start w:val="1"/>
                  <w:numFmt w:val="lowerLetter"/>
                  <w:lvlText w:val="%1.%2."/>
                </w:lvl>
                <w:lvl w:ilvl="2">
                  <w:start w:val="1"/>
                  <w:numFmt w:val="upperLetter"/>
                  <w:lvlRestart w:val="1"/>
                  <w:lvlText w:val="%1.%2.%3."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="15">
                <w:lvl w:ilvl="0">
                  <w:numFmt w:val="decimal"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="16">
                <w:lvl w:ilvl="0">
                  <w:numFmt w:val="none"/>
                  <w:lvlText w:val=""/>
                </w:lvl>
              </w:abstractNum>
              <w:num w:numId="4">
                <w:abstractNumId w:val="10"/>
              </w:num>
              <w:num w:numId="5">
                <w:abstractNumId w:val="10"/>
                <w:lvlOverride w:ilvl="0">
                  <w:startOverride w:val="10"/>
                </w:lvlOverride>
              </w:num>
              <w:num w:numId="6">
                <w:abstractNumId w:val="10"/>
                <w:lvlOverride w:ilvl="0">
                  <w:lvl w:ilvl="0">
                    <w:start w:val="4"/>
                    <w:numFmt w:val="upperRoman"/>
                    <w:lvlText w:val="%1."/>
                  </w:lvl>
                  <w:startOverride w:val="7"/>
                </w:lvlOverride>
              </w:num>
              <w:num w:numId="8"><w:abstractNumId w:val="11"/></w:num>
              <w:num w:numId="9"><w:abstractNumId w:val="12"/></w:num>
              <w:num w:numId="10"><w:abstractNumId w:val="13"/></w:num>
              <w:num w:numId="11"><w:abstractNumId w:val="14"/></w:num>
              <w:num w:numId="12"><w:abstractNumId w:val="15"/></w:num>
              <w:num w:numId="13"><w:abstractNumId w:val="16"/></w:num>
            </w:numbering>
            """;

        var body = new StringBuilder();
        var expected = new List<(string Label, string Marker)>();

        /// <summary>
        /// Добавляет в тело DOCX нумерованный абзац с заданными numId, ilvl
        /// и текстом, сохраняя ожидаемый маркер для проверки модели.
        /// </summary>
        void AddItem(
            int numId,
            int level,
            string label,
            string marker)
        {
            body.Append(
                $"""
                <w:p>
                  <w:pPr>
                    <w:numPr>
                      <w:ilvl w:val="{level}"/>
                      <w:numId w:val="{numId}"/>
                    </w:numPr>
                  </w:pPr>
                  <w:r><w:t>{label}</w:t></w:r>
                </w:p>
                """);

            expected.Add((label, marker));
        }

        AddItem(4, 0, "Main one", "3.");
        AddItem(4, 1, "Nested first", "3.b)");
        AddItem(4, 1, "Nested second", "3.c)");
        AddItem(4, 2, "Deep before restart", "3.c.i.");
        AddItem(4, 0, "Main two", "4.");

        body.Append(
            """
            <w:p>
              <w:pPr>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader"/>
                </w:sectPr>
              </w:pPr>
              <w:r><w:t>Section transition</w:t></w:r>
            </w:p>
            """);

        AddItem(4, 1, "Nested restarted", "4.b)");
        AddItem(4, 2, "Deep never restarted", "4.b.ii.");
        AddItem(4, 0, "Main three after header", "5.");

        body.Append(
            """
            <w:p>
              <w:pPr>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader"/>
                </w:sectPr>
              </w:pPr>
              <w:r><w:t>Another section transition</w:t></w:r>
            </w:p>
            """);

        AddItem(4, 0, "Main four after reused header", "6.");
        AddItem(5, 0, "Other instance first", "10.");
        AddItem(5, 0, "Other instance second", "11.");

        body.Append(
            """
            <w:tbl>
              <w:tr>
                <w:tc>
                  <w:p>
                    <w:pPr>
                      <w:numPr>
                        <w:ilvl w:val="0"/>
                        <w:numId w:val="5"/>
                      </w:numPr>
                    </w:pPr>
                    <w:r><w:t>Table list entry</w:t></w:r>
                  </w:p>
                </w:tc>
              </w:tr>
            </w:tbl>
            """);

        AddItem(5, 0, "Other instance after table", "13.");
        AddItem(6, 0, "Override Roman first", "VII.");
        AddItem(6, 0, "Override Roman second", "VIII.");
        AddItem(8, 0, "Bullet entry", "•");
        AddItem(9, 0, "Alphabet Z", "Z.");
        AddItem(9, 0, "Alphabet AA", "AA.");
        AddItem(10, 0, "Padded first", "01.");
        AddItem(10, 0, "Padded second", "02.");
        AddItem(11, 0, "Custom parent one", "1.");
        AddItem(11, 1, "Custom middle one", "1.a.");
        AddItem(11, 2, "Custom deep one", "1.a.A.");
        AddItem(11, 1, "Custom middle two", "1.b.");
        AddItem(11, 2, "Custom deep two", "1.b.B.");
        AddItem(11, 0, "Custom parent two", "2.");
        AddItem(11, 1, "Custom middle after parent", "2.a.");
        AddItem(11, 2, "Custom deep restarted", "2.a.A.");
        AddItem(12, 0, "Default first", "1.");
        AddItem(12, 0, "Default second", "2.");

        body.Append(
            """
            <w:p>
              <w:pPr>
                <w:numPr><w:ilvl w:val="0"/><w:numId w:val="13"/></w:numPr>
              </w:pPr>
              <w:r><w:t>No formatted marker</w:t></w:r>
            </w:p>
            """);

        body.Append(
            """
            <w:p>
              <w:pPr>
                <w:numPr><w:numId w:val="0"/></w:numPr>
              </w:pPr>
              <w:r><w:t>Unnumbered paragraph</w:t></w:r>
            </w:p>
            """);

        /// <summary>
        /// Записывает DOCX ZIP из собственных XML part strings и не обращается
        /// к пользовательским файлам или книжной библиотеке.
        /// </summary>
        string CreateDocx(
            string fileName,
            string wordBody,
            string wordNumbering,
            string? headerXml = null)
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
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <w:body>
                    """ +
                    wordBody +
                    """
                      </w:body>
                    </w:document>
                    """);

                WriteZipEntry(
                    archive,
                    "word/numbering.xml",
                    wordNumbering);

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
                    headerXml ??
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:p>
                        <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="4"/></w:numPr></w:pPr>
                        <w:r><w:t>Header entry one</w:t></w:r>
                      </w:p>
                      <w:p>
                        <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="4"/></w:numPr></w:pPr>
                        <w:r><w:t>Header entry two</w:t></w:r>
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
                          <w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="4"/></w:numPr></w:pPr>
                          <w:r><w:t>Footnote list entry</w:t></w:r>
                        </w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }

            return path;
        }

        var path = CreateDocx(
            "docx-real-numbering.docx",
            body.ToString(),
            numberingXml);

        var document = SupportedFormatRegistry.ReadDocument(
            path);

        /// <summary>
        /// Возвращает объединённый текст абзаца с преобразованием
        /// DOCX indentation NBSP в пробелы для проверки номера.
        /// </summary>
        string ParagraphText(DocumentParagraph paragraph)
        {
            return string.Concat(
                    paragraph.Inlines.Select(inline => inline.Text))
                .Replace('\u00A0', ' ')
                .TrimStart();
        }

        var bodyParagraphs = document.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => !paragraph.IsNote)
            .ToArray();

        foreach (var item in expected)
        {
            var paragraph = bodyParagraphs.Single(
                paragraph => ParagraphText(paragraph).EndsWith(
                    " " + item.Label,
                    StringComparison.Ordinal));

            AssertEqual(
                item.Marker + " " + item.Label,
                ParagraphText(paragraph),
                $"DOCX real numbering {item.Label}");
        }

        var table = document.Blocks
            .OfType<DocumentTable>()
            .Single();

        var tableText = string.Concat(
                table.Rows[0].Cells[0].Inlines
                    .Select(inline => inline.Text))
            .Replace('\u00A0', ' ')
            .TrimStart();

        AssertEqual(
            "12. Table list entry",
            tableText,
            "DOCX numbering счётчик должен продолжаться внутри таблицы.");

        var headers = document.Blocks
            .OfType<DocumentFlowRegion>()
            .Where(region =>
                region.Kind == DocumentFlowRegionKind.Header)
            .ToArray();

        AssertEqual(
            2,
            headers.Length,
            "DOCX повторно используемый header должен отображаться в обеих секциях.");

        foreach (var header in headers)
        {
            var headerEntries = header.Blocks
                .OfType<DocumentParagraph>()
                .Select(ParagraphText)
                .ToArray();

            AssertEqual(
                "3. Header entry one",
                headerEntries[0],
                "DOCX reused header должен начинать счётчик с первого пункта.");

            AssertEqual(
                "4. Header entry two",
                headerEntries[1],
                "DOCX reused header должен сохранять одинаковые номера в секциях.");
        }

        var footnote = document.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        AssertEqual(
            "3. Footnote list entry",
            ParagraphText(footnote),
            "DOCX footnotes должны иметь отдельное состояние numbering.");

        Assert(
            bodyParagraphs.Any(paragraph =>
                ParagraphText(paragraph) == "Unnumbered paragraph"),
            "DOCX numId=0 должен означать отсутствие numbering marker.");

        Assert(
            bodyParagraphs.Any(paragraph =>
                ParagraphText(paragraph) == "No formatted marker"),
            "DOCX numFmt=none не должен добавлять текстовый marker.");

        var reread = SupportedFormatRegistry.ReadDocument(
            path);

        AssertEqual(
            "3. Main one",
            reread.Blocks
                .OfType<DocumentParagraph>()
                .Select(ParagraphText)
                .Single(text => text.EndsWith("Main one", StringComparison.Ordinal)),
            "DOCX numbering должен начинаться с w:start при повторном чтении.");

        /// <summary>
        /// Проверяет, что повреждённый numbering.xml и неизвестные
        /// numFmt приводят к явной ошибке со сведениями о параметре.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string invalidNumbering,
            string expectedError,
            bool unsupportedFormat = false)
        {
            var invalidPath = CreateDocx(
                fileName,
                body.ToString(),
                invalidNumbering);

            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    invalidPath);
            }
            catch (InvalidDataException exception)
            {
                rejected = !unsupportedFormat &&
                           exception.Message.Contains(
                               expectedError,
                               StringComparison.Ordinal);
            }
            catch (NotSupportedException exception)
            {
                rejected = unsupportedFormat &&
                           exception.Message.Contains(
                               expectedError,
                               StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"DOCX некорректная нумерация {fileName} должна завершаться ошибкой {expectedError}.");
        }

        AssertInvalid(
            "docx-numbering-invalid-start.docx",
            numberingXml.Replace(
                """<w:start w:val="3"/>""",
                """<w:start w:val="bad"/>""",
                StringComparison.Ordinal),
            "start@val");

        AssertInvalid(
            "docx-numbering-invalid-level.docx",
            numberingXml.Replace(
                """<w:lvl w:ilvl="2">""",
                """<w:lvl w:ilvl="9">""",
                StringComparison.Ordinal),
            "lvl@ilvl");

        AssertInvalid(
            "docx-numbering-invalid-restart.docx",
            numberingXml.Replace(
                """<w:lvlRestart w:val="1"/>""",
                """<w:lvlRestart w:val="10"/>""",
                StringComparison.Ordinal),
            "lvlRestart");

        AssertInvalid(
            "docx-numbering-invalid-override.docx",
            numberingXml.Replace(
                """<w:startOverride w:val="10"/>""",
                """<w:startOverride w:val="bad"/>""",
                StringComparison.Ordinal),
            "startOverride@val");

        AssertInvalid(
            "docx-numbering-invalid-template.docx",
            numberingXml.Replace(
                """<w:lvlText w:val="%1."/>""",
                """<w:lvlText w:val="%9."/>""",
                StringComparison.Ordinal),
            "lvlText");

        AssertInvalid(
            "docx-numbering-unknown-format.docx",
            numberingXml.Replace(
                """<w:numFmt w:val="decimal"/>""",
                """<w:numFmt w:val="chicago"/>""",
                StringComparison.Ordinal),
            "numFmt",
            unsupportedFormat: true);
    }
}
