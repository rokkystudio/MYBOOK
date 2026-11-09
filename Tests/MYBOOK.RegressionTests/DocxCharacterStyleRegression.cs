using System.IO;
using System.IO.Compression;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет DOCX w:rStyle: каскад paragraph/character/direct formatting,
    /// наследование character basedOn, выключение свойства через w:val="0",
    /// superscript/subscript, hyperlinks, fields, tabs, breaks, tables,
    /// headers и footnotes. Ошибочные character style references, циклы
    /// и type mismatches должны давать InvalidDataException.
    /// Синтетические DOCX создаются только во временном каталоге.
    /// </summary>
    private static void TestDocxCharacterStyles(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="ParagraphBase">
                <w:rPr>
                  <w:b/>
                  <w:i/>
                </w:rPr>
              </w:style>
              <w:style w:type="character" w:styleId="CharacterBase">
                <w:rPr>
                  <w:i w:val="0"/>
                  <w:u w:val="single"/>
                  <w:strike/>
                </w:rPr>
              </w:style>
              <w:style w:type="character" w:styleId="CharacterDerived">
                <w:basedOn w:val="CharacterBase"/>
                <w:rPr>
                  <w:b w:val="0"/>
                  <w:vertAlign w:val="superscript"/>
                </w:rPr>
              </w:style>
            </w:styles>
            """;

        const string documentXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p>
                  <w:pPr><w:pStyle w:val="ParagraphBase"/></w:pPr>
                  <w:r><w:t>Paragraph only</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="ParagraphBase"/></w:pPr>
                  <w:r>
                    <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                    <w:t>Character inherited</w:t>
                  </w:r>
                  <w:r>
                    <w:rPr>
                      <w:rStyle w:val="CharacterDerived"/>
                      <w:b/>
                      <w:i/>
                      <w:u w:val="none"/>
                      <w:strike w:val="0"/>
                      <w:vertAlign w:val="subscript"/>
                    </w:rPr>
                    <w:t>Direct overrides</w:t>
                  </w:r>
                  <w:r><w:t>Paragraph after</w:t></w:r>
                </w:p>
                <w:p>
                  <w:bookmarkStart w:id="1" w:name="target"/>
                  <w:r><w:t>Bookmark destination</w:t></w:r>
                  <w:bookmarkEnd w:id="1"/>
                </w:p>
                <w:p>
                  <w:hyperlink w:anchor="target">
                    <w:r>
                      <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                      <w:t>Styled hyperlink</w:t>
                    </w:r>
                  </w:hyperlink>
                </w:p>
                <w:p>
                  <w:fldSimple w:instr="PAGE">
                    <w:r>
                      <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                      <w:t>Styled field</w:t>
                    </w:r>
                  </w:fldSimple>
                </w:p>
                <w:p>
                  <w:r>
                    <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                    <w:tab/>
                    <w:t>Styled tab text</w:t>
                    <w:br/>
                    <w:t>Styled line text</w:t>
                  </w:r>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:r>
                          <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                          <w:t>Styled table cell</w:t>
                        </w:r>
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

        /// <summary>
        /// Создаёт DOCX ZIP с document.xml, styles.xml и независимыми
        /// header/footnotes, использующими тот же character style.
        /// </summary>
        string CreateDocx(
            string name,
            string document,
            string styles)
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
                    document);

                WriteZipEntry(
                    archive,
                    "word/styles.xml",
                    styles);

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
                        <w:r>
                          <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                          <w:t>Styled header</w:t>
                        </w:r>
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
                          <w:r>
                            <w:rPr><w:rStyle w:val="CharacterDerived"/></w:rPr>
                            <w:t>Styled footnote</w:t>
                          </w:r>
                        </w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }

            return path;
        }

        var path = CreateDocx(
            "docx-character-style-cascade.docx",
            documentXml,
            stylesXml);

        var model = SupportedFormatRegistry.ReadDocument(
            path);

        var inlines = model.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .ToArray();

        /// <summary>
        /// Проверяет шесть boolean-свойств оформления одного inline
        /// и исходную целевую ссылку; label определяет тестовый текст.
        /// </summary>
        void AssertInlineFormatting(
            DocumentInline inline,
            bool bold,
            bool italic,
            bool underline,
            bool strikethrough,
            bool superscript,
            bool subscript,
            string label)
        {
            Assert(
                inline.Bold == bold &&
                inline.Italic == italic &&
                inline.Underline == underline &&
                inline.Strikethrough == strikethrough &&
                inline.Superscript == superscript &&
                inline.Subscript == subscript,
                $"DOCX character formatting {label}: " +
                $"b={inline.Bold}, i={inline.Italic}, u={inline.Underline}, " +
                $"strike={inline.Strikethrough}, sup={inline.Superscript}, sub={inline.Subscript}.");
        }

        /// <summary>
        /// Находит единственный текстовый inline с заданной меткой,
        /// не учитывая содержимое таблиц и дополнительных DOCX stories.
        /// </summary>
        DocumentInline FindInline(string label)
        {
            return inlines.Single(inline =>
                inline.Text.Contains(
                    label,
                    StringComparison.Ordinal));
        }

        AssertInlineFormatting(
            FindInline("Paragraph only"),
            bold: true,
            italic: true,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: false,
            "paragraph inheritance");

        AssertInlineFormatting(
            FindInline("Character inherited"),
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "character basedOn over paragraph");

        AssertInlineFormatting(
            FindInline("Direct overrides"),
            bold: true,
            italic: true,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: true,
            "direct rPr over character");

        AssertInlineFormatting(
            FindInline("Paragraph after"),
            bold: true,
            italic: true,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: false,
            "character style does not leak into later runs");

        var hyperlink = FindInline("Styled hyperlink");

        AssertInlineFormatting(
            hyperlink,
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "hyperlink character style");

        AssertEqual(
            "#target",
            hyperlink.LinkHref!,
            "DOCX character style должен сохранять hyperlink w:anchor.");

        AssertInlineFormatting(
            FindInline("Styled field"),
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "fldSimple character style");

        var separators = FindInline("Styled tab text");

        Assert(
            separators.Text.Contains('\t') &&
            separators.Text.Contains(
                Environment.NewLine,
                StringComparison.Ordinal),
            "DOCX styled tab и break должны оставаться в inline тексте.");

        AssertInlineFormatting(
            separators,
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "styled tab and break");

        var tableInline = model.Blocks
            .OfType<DocumentTable>()
            .Single()
            .Rows[0]
            .Cells[0]
            .Inlines
            .Single(inline =>
                inline.Text.Contains(
                    "Styled table cell",
                    StringComparison.Ordinal));

        AssertInlineFormatting(
            tableInline,
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "table character style");

        var headerInline = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region =>
                region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline =>
                inline.Text.Contains(
                    "Styled header",
                    StringComparison.Ordinal));

        AssertInlineFormatting(
            headerInline,
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "header character style");

        var footnoteInline = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => paragraph.IsNote)
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline =>
                inline.Text.Contains(
                    "Styled footnote",
                    StringComparison.Ordinal));

        AssertInlineFormatting(
            footnoteInline,
            bold: false,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "footnote character style");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "href=\"#anchor-target\"",
            "DOCX styled hyperlink должен рендериться с внутренним anchor.");

        var reread = SupportedFormatRegistry.ReadDocument(
            path);

        Assert(
            reread.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    inline.Text.Contains(
                        "Character inherited",
                        StringComparison.Ordinal) &&
                    inline.Superscript &&
                    inline.Strikethrough &&
                    !inline.Bold),
            "DOCX character style basedOn должен повторяться между загрузками.");

        /// <summary>
        /// Проверяет InvalidDataException при некорректном character style
        /// reference, basedOn, w:type или циклическом наследовании.
        /// </summary>
        void AssertInvalid(
            string name,
            string invalidDocument,
            string invalidStyles,
            string diagnostic)
        {
            var invalidPath = CreateDocx(
                name,
                invalidDocument,
                invalidStyles);

            var rejected = false;

            try
            {
                SupportedFormatRegistry.ReadDocument(
                    invalidPath);
            }
            catch (InvalidDataException exception)
            {
                rejected = exception.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal);
            }

            Assert(
                rejected,
                $"DOCX {name} должен явно отклонять ошибку {diagnostic}.");
        }

        AssertInvalid(
            "docx-missing-rstyle.docx",
            documentXml.Replace(
                """<w:rStyle w:val="CharacterDerived"/>""",
                """<w:rStyle w:val="MissingCharacter"/>""",
                StringComparison.Ordinal),
            stylesXml,
            "character style");

        AssertInvalid(
            "docx-rstyle-paragraph-kind.docx",
            documentXml.Replace(
                """<w:rStyle w:val="CharacterDerived"/>""",
                """<w:rStyle w:val="ParagraphBase"/>""",
                StringComparison.Ordinal),
            stylesXml,
            "требуется character");

        AssertInvalid(
            "docx-pstyle-character-kind.docx",
            documentXml.Replace(
                """<w:pStyle w:val="ParagraphBase"/>""",
                """<w:pStyle w:val="CharacterBase"/>""",
                StringComparison.Ordinal),
            stylesXml,
            "требуется paragraph");

        AssertInvalid(
            "docx-rstyle-empty-val.docx",
            documentXml.Replace(
                """<w:rStyle w:val="CharacterDerived"/>""",
                """<w:rStyle/>""",
                StringComparison.Ordinal),
            stylesXml,
            "character style");

        AssertInvalid(
            "docx-character-basedon-missing.docx",
            documentXml,
            stylesXml.Replace(
                """<w:basedOn w:val="CharacterBase"/>""",
                """<w:basedOn w:val="AbsentParent"/>""",
                StringComparison.Ordinal),
            "отсутствующий basedOn");

        AssertInvalid(
            "docx-character-basedon-wrong-kind.docx",
            documentXml,
            stylesXml.Replace(
                """<w:basedOn w:val="CharacterBase"/>""",
                """<w:basedOn w:val="ParagraphBase"/>""",
                StringComparison.Ordinal),
            "требуется character");

        AssertInvalid(
            "docx-character-basedon-cycle.docx",
            documentXml,
            stylesXml.Replace(
                """<w:style w:type="character" w:styleId="CharacterBase">""",
                """<w:style w:type="character" w:styleId="CharacterBase"><w:basedOn w:val="CharacterDerived"/>""",
                StringComparison.Ordinal),
            "цикл");

        AssertInvalid(
            "docx-character-style-no-type.docx",
            documentXml,
            stylesXml.Replace(
                """<w:style w:type="character" w:styleId="CharacterBase">""",
                """<w:style w:styleId="CharacterBase">""",
                StringComparison.Ordinal),
            "w:type");
    }
}
