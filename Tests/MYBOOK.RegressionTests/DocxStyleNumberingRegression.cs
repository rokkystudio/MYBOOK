using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX нумерацию через paragraph style numPr, наследование
    /// basedOn, default paragraph style, приоритет direct numPr, явное
    /// numId=0, выбор ilvl по numbering.xml lvl/pStyle, независимые numId,
    /// counters в таблицах и отдельных stories, а также malformed values.
    /// Синтетические DOCX создаются во временной директории regression suite.
    /// </summary>
    private static void TestDocxStyleNumbering(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="DefaultNumbered" w:default="1">
                <w:pPr>
                  <w:numPr><w:numId w:val="9"/></w:numPr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Ordinary"/>
              <w:style w:type="paragraph" w:styleId="ListBase">
                <w:pPr>
                  <w:numPr>
                    <w:ilvl w:val="0"/>
                    <w:numId w:val="4"/>
                  </w:numPr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="ListDerived">
                <w:basedOn w:val="ListBase"/>
                <w:pPr>
                  <w:numPr><w:ilvl w:val="1"/></w:numPr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="ListInherited">
                <w:basedOn w:val="ListBase"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="ListOverride">
                <w:basedOn w:val="ListDerived"/>
                <w:pPr>
                  <w:numPr><w:numId w:val="5"/></w:numPr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="ListMapped">
                <w:pPr>
                  <w:numPr><w:numId w:val="6"/></w:numPr>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="OnlyLevel">
                <w:pPr>
                  <w:numPr><w:ilvl w:val="1"/></w:numPr>
                </w:pPr>
              </w:style>
            </w:styles>
            """;

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
                  <w:numFmt w:val="lowerLetter"/>
                  <w:lvlText w:val="%1.%2)"/>
                </w:lvl>
                <w:lvl w:ilvl="2">
                  <w:numFmt w:val="lowerRoman"/>
                  <w:lvlText w:val="%1.%2.%3."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="20">
                <w:lvl w:ilvl="0">
                  <w:numFmt w:val="decimal"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
                <w:lvl w:ilvl="1">
                  <w:numFmt w:val="upperLetter"/>
                  <w:lvlText w:val="%1.%2."/>
                </w:lvl>
                <w:lvl w:ilvl="2">
                  <w:pStyle w:val="ListMapped"/>
                  <w:numFmt w:val="upperRoman"/>
                  <w:lvlText w:val="%1.%2.%3."/>
                </w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="30">
                <w:lvl w:ilvl="0">
                  <w:start w:val="7"/>
                  <w:numFmt w:val="decimal"/>
                  <w:lvlText w:val="%1."/>
                </w:lvl>
              </w:abstractNum>
              <w:num w:numId="4"><w:abstractNumId w:val="10"/></w:num>
              <w:num w:numId="5">
                <w:abstractNumId w:val="10"/>
                <w:lvlOverride w:ilvl="0">
                  <w:startOverride w:val="10"/>
                </w:lvlOverride>
              </w:num>
              <w:num w:numId="6"><w:abstractNumId w:val="20"/></w:num>
              <w:num w:numId="9"><w:abstractNumId w:val="30"/></w:num>
            </w:numbering>
            """;

        var body = new StringBuilder();
        var expected = new List<(string Label, string Marker)>();

        /// <summary>
        /// Добавляет paragraph с определённым w:pStyle и необязательным
        /// прямым w:numPr, сохраняя проверяемый маркер.
        /// </summary>
        void AddParagraph(
            string? styleId,
            string label,
            string marker,
            string? directNumPr = null)
        {
            var properties = new StringBuilder();

            if (styleId != null)
            {
                properties.Append(
                    $"""<w:pStyle w:val="{styleId}"/>""");
            }

            if (directNumPr != null)
            {
                properties.Append(
                    "<w:numPr>" +
                    directNumPr +
                    "</w:numPr>");
            }

            body.Append(
                "<w:p>" +
                (properties.Length > 0
                    ? "<w:pPr>" + properties + "</w:pPr>"
                    : string.Empty) +
                "<w:r><w:t>" + label + "</w:t></w:r>" +
                "</w:p>");

            expected.Add((label, marker));
        }

        AddParagraph(null, "Default first", "7.");
        AddParagraph(null, "Default second", "8.");
        AddParagraph("Ordinary", "Ordinary text", "");
        AddParagraph("ListBase", "Base first", "3.");
        AddParagraph("ListDerived", "Derived first", "3.a)");
        AddParagraph("ListDerived", "Derived second", "3.b)");
        AddParagraph("ListOverride", "Different numId", "10.a)");
        AddParagraph("ListBase", "Base second", "4.");
        AddParagraph("ListInherited", "Inherited numId and level", "5.");
        AddParagraph(
            "ListDerived",
            "Direct ilvl",
            "6.",
            """<w:ilvl w:val="0"/>""");
        AddParagraph(
            "ListBase",
            "Direct suppress",
            "",
            """<w:numId w:val="0"/>""");
        AddParagraph(
            "ListDerived",
            "Direct numId",
            "10.b)",
            """<w:numId w:val="5"/>""");
        AddParagraph("ListMapped", "Mapped first", "1.A.I.");
        AddParagraph("ListMapped", "Mapped second", "1.A.II.");
        AddParagraph(
            "ListMapped",
            "Mapped explicit level",
            "1.A.",
            """<w:ilvl w:val="1"/>""");
        AddParagraph("ListMapped", "Mapped after level one", "1.A.I.");
        AddParagraph(
            "OnlyLevel",
            "Style ilvl direct numId",
            "1.B.",
            """<w:numId w:val="6"/>""");
        AddParagraph(
            "ListDerived",
            "Direct level two",
            "6.a.i.",
            """<w:ilvl w:val="2"/>""");
        AddParagraph(
            "ListBase",
            "Canonical numId",
            "7.",
            """<w:numId w:val="04"/>""");

        body.Append(
            """
            <w:tbl>
              <w:tr>
                <w:tc>
                  <w:p>
                    <w:pPr><w:pStyle w:val="ListBase"/></w:pPr>
                    <w:r><w:t>Styled table list</w:t></w:r>
                  </w:p>
                </w:tc>
              </w:tr>
            </w:tbl>
            """);

        AddParagraph(
            null,
            "Default suppressed",
            "",
            """<w:numId w:val="0"/>""");
        AddParagraph(null, "Default third", "9.");

        body.Append(
            """
            <w:sectPr>
              <w:headerReference w:type="default" r:id="rIdHeader"/>
            </w:sectPr>
            """);

        /// <summary>
        /// Создаёт синтетический DOCX с XML частей document, styles,
        /// numbering, header и footnotes и возвращает путь к ZIP.
        /// </summary>
        string CreateDocx(
            string name,
            string documentBody,
            string styles,
            string numbering)
        {
            var path = Path.Combine(
                directory,
                name);

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
                    documentBody +
                    """
                      </w:body>
                    </w:document>
                    """);

                WriteZipEntry(
                    archive,
                    "word/styles.xml",
                    styles);

                WriteZipEntry(
                    archive,
                    "word/numbering.xml",
                    numbering);

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
                        <w:pPr><w:pStyle w:val="ListBase"/></w:pPr>
                        <w:r><w:t>Header styled one</w:t></w:r>
                      </w:p>
                      <w:p>
                        <w:pPr><w:pStyle w:val="ListBase"/></w:pPr>
                        <w:r><w:t>Header styled two</w:t></w:r>
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
                          <w:pPr><w:pStyle w:val="ListDerived"/></w:pPr>
                          <w:r><w:t>Footnote styled</w:t></w:r>
                        </w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }

            return path;
        }

        var path = CreateDocx(
            "docx-paragraph-style-numbering.docx",
            body.ToString(),
            stylesXml,
            numberingXml);

        var model = SupportedFormatRegistry.ReadDocument(path);

        /// <summary>
        /// Возвращает итоговый текст абзаца, заменяя NBSP, используемые
        /// для отступов вложенных списков.
        /// </summary>
        string ParagraphText(DocumentParagraph paragraph)
        {
            return string.Concat(
                    paragraph.Inlines.Select(inline => inline.Text))
                .Replace('\u00A0', ' ')
                .TrimStart();
        }

        var bodyTexts = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => !paragraph.IsNote)
            .Select(ParagraphText)
            .ToArray();

        foreach (var (label, marker) in expected)
        {
            var actual = bodyTexts.Single(text =>
                text.EndsWith(label, StringComparison.Ordinal));

            AssertEqual(
                marker.Length == 0
                    ? label
                    : marker + " " + label,
                actual,
                $"DOCX style numPr: {label}");
        }

        var table = model.Blocks
            .OfType<DocumentTable>()
            .Single();

        var tableText = string.Concat(
                table.Rows[0].Cells[0].Inlines
                    .Select(inline => inline.Text))
            .Replace('\u00A0', ' ')
            .TrimStart();

        AssertEqual(
            "8. Styled table list",
            tableText,
            "DOCX style-based numbering должен продолжаться в таблицах.");

        var headerTexts = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Select(ParagraphText)
            .ToArray();

        AssertEqual(
            "3. Header styled one",
            headerTexts[0],
            "DOCX header должен иметь независимый счётчик списка из style.");

        AssertEqual(
            "4. Header styled two",
            headerTexts[1],
            "DOCX header style list должен продолжаться внутри part.");

        var noteText = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => paragraph.IsNote)
            .Select(ParagraphText)
            .Single();

        AssertEqual(
            "3.a) Footnote styled",
            noteText,
            "DOCX footnote style numPr должен использовать собственные счётчики.");

        var reread = SupportedFormatRegistry.ReadDocument(path);

        AssertEqual(
            "7. Default first",
            reread.Blocks
                .OfType<DocumentParagraph>()
                .Select(ParagraphText)
                .Single(text =>
                    text.EndsWith("Default first", StringComparison.Ordinal)),
            "DOCX style-based list counters должны начинаться заново при чтении.");

        /// <summary>
        /// Проверяет явную диагностическую ошибку для повреждённого
        /// paragraph/style numPr и неоднозначного numbering.xml lvl/pStyle.
        /// </summary>
        void AssertInvalid(
            string name,
            string invalidBody,
            string invalidStyles,
            string invalidNumbering,
            string diagnostic)
        {
            var invalidPath = CreateDocx(
                name,
                invalidBody,
                invalidStyles,
                invalidNumbering);

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
            "docx-style-numid-negative.docx",
            body.ToString().Replace(
                """<w:numId w:val="0"/>""",
                """<w:numId w:val="-1"/>""",
                StringComparison.Ordinal),
            stylesXml,
            numberingXml,
            "numPr/numId");

        AssertInvalid(
            "docx-style-ilvl-range.docx",
            body.ToString(),
            stylesXml.Replace(
                """<w:ilvl w:val="1"/>""",
                """<w:ilvl w:val="9"/>""",
                StringComparison.Ordinal),
            numberingXml,
            "numPr/ilvl");

        AssertInvalid(
            "docx-direct-ilvl-range.docx",
            body.ToString().Replace(
                """<w:ilvl w:val="2"/>""",
                """<w:ilvl w:val="10"/>""",
                StringComparison.Ordinal),
            stylesXml,
            numberingXml,
            "numPr/ilvl");

        AssertInvalid(
            "docx-style-numid-without-value.docx",
            body.ToString(),
            stylesXml.Replace(
                """<w:numId w:val="4"/>""",
                """<w:numId/>""",
                StringComparison.Ordinal),
            numberingXml,
            "numPr/numId");

        AssertInvalid(
            "docx-numbering-pstyle-no-value.docx",
            body.ToString(),
            stylesXml,
            numberingXml.Replace(
                """<w:pStyle w:val="ListMapped"/>""",
                """<w:pStyle/>""",
                StringComparison.Ordinal),
            "pStyle");

        AssertInvalid(
            "docx-numbering-ambiguous-pstyle.docx",
            body.ToString(),
            stylesXml,
            numberingXml.Replace(
                """<w:numFmt w:val="upperLetter"/>""",
                """<w:pStyle w:val="ListMapped"/><w:numFmt w:val="upperLetter"/>""",
                StringComparison.Ordinal),
            "несколько lvl/pStyle");

        AssertInvalid(
            "docx-style-unknown-numid.docx",
            body.ToString(),
            stylesXml.Replace(
                """<w:numId w:val="4"/>""",
                """<w:numId w:val="999"/>""",
                StringComparison.Ordinal),
            numberingXml,
            "numId=999");
    }
}
