using System.IO;
using System.IO.Compression;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет WordprocessingML docDefaults/w:rPrDefault и объявленный
    /// w:default paragraph style: каскад до explicit paragraph, character
    /// и direct run properties, наследование basedOn, отсутствие переноса
    /// default paragraph style на явно указанный другой style, оформление
    /// в hyperlink, fldSimple, таблицах, headers/footnotes и HTML.
    /// Также проверяет default heading, отсутствие styles.xml и ошибки
    /// дублированных и недопустимых style/default declarations.
    /// Все fixtures создаются исключительно во временной директории.
    /// </summary>
    private static void TestDocxStyleDefaults(string directory)
    {
        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:b/>
                    <w:i/>
                    <w:u w:val="single"/>
                    <w:strike/>
                    <w:vertAlign w:val="superscript"/>
                  </w:rPr>
                </w:rPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="DefaultBody" w:default="1">
                <w:rPr>
                  <w:b w:val="0"/>
                  <w:u w:val="none"/>
                  <w:vertAlign w:val="baseline"/>
                </w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="DerivedBody">
                <w:basedOn w:val="DefaultBody"/>
                <w:rPr>
                  <w:i w:val="0"/>
                  <w:strike w:val="0"/>
                </w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="IndependentBody">
                <w:rPr><w:i w:val="0"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="OtherBody" w:default="0">
                <w:rPr><w:strike w:val="0"/></w:rPr>
              </w:style>
              <w:style w:type="character" w:styleId="CharacterAccent">
                <w:rPr>
                  <w:b/>
                  <w:u w:val="single"/>
                  <w:vertAlign w:val="subscript"/>
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
                  <w:r><w:t>Implicit paragraph</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="DerivedBody"/></w:pPr>
                  <w:r><w:t>Inherited paragraph</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="IndependentBody"/></w:pPr>
                  <w:r><w:t>Independent paragraph</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="OtherBody"/></w:pPr>
                  <w:r><w:t>Explicit not default</w:t></w:r>
                </w:p>
                <w:p>
                  <w:r>
                    <w:rPr><w:rStyle w:val="CharacterAccent"/></w:rPr>
                    <w:t>Character accent</w:t>
                  </w:r>
                </w:p>
                <w:p>
                  <w:r>
                    <w:rPr>
                      <w:b/>
                      <w:i w:val="0"/>
                      <w:strike w:val="0"/>
                      <w:vertAlign w:val="subscript"/>
                    </w:rPr>
                    <w:t>Direct style override</w:t>
                  </w:r>
                </w:p>
                <w:p>
                  <w:r><w:t>Implicit after override</w:t></w:r>
                </w:p>
                <w:p>
                  <w:bookmarkStart w:id="1" w:name="anchor"/>
                  <w:r><w:t>Bookmark target</w:t></w:r>
                  <w:bookmarkEnd w:id="1"/>
                </w:p>
                <w:p>
                  <w:hyperlink w:anchor="anchor">
                    <w:r><w:t>Default hyperlink</w:t></w:r>
                  </w:hyperlink>
                </w:p>
                <w:p>
                  <w:fldSimple w:instr="PAGE">
                    <w:r>
                      <w:rPr><w:rStyle w:val="CharacterAccent"/></w:rPr>
                      <w:t>Default field</w:t>
                    </w:r>
                  </w:fldSimple>
                </w:p>
                <w:p>
                  <w:r><w:tab/><w:t>Default tab</w:t><w:br/></w:r>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p><w:r><w:t>Default table cell</w:t></w:r></w:p>
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
        /// Создаёт автономный DOCX ZIP с document.xml, необязательным
        /// styles.xml, header и footnotes для проверки оформления во всех
        /// text parts без доступа к пользовательским книгам.
        /// </summary>
        string CreateDocx(
            string name,
            string document,
            string? styles)
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

                if (styles != null)
                {
                    WriteZipEntry(
                        archive,
                        "word/styles.xml",
                        styles);
                }

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
                      <w:p><w:r><w:t>Default header</w:t></w:r></w:p>
                    </w:hdr>
                    """);

                WriteZipEntry(
                    archive,
                    "word/footnotes.xml",
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:footnote w:id="1">
                        <w:p><w:r><w:t>Default footnote</w:t></w:r></w:p>
                      </w:footnote>
                    </w:footnotes>
                    """);
            }

            return path;
        }

        var path = CreateDocx(
            "docx-document-style-defaults.docx",
            documentXml,
            stylesXml);

        var model = SupportedFormatRegistry.ReadDocument(
            path);

        var paragraphs = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => !paragraph.IsNote)
            .ToArray();

        /// <summary>
        /// Находит text inline по точному содержимому внутри основного
        /// документа и не включает table, header и note stories.
        /// </summary>
        DocumentInline Inline(string text)
        {
            return paragraphs
                .SelectMany(paragraph => paragraph.Inlines)
                .Single(inline => inline.Text == text);
        }

        /// <summary>
        /// Проверяет шесть признаков оформления после каскада Word styles.
        /// </summary>
        void AssertFormatting(
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
                $"DOCX {label}: b={inline.Bold}, i={inline.Italic}, " +
                $"u={inline.Underline}, strike={inline.Strikethrough}, " +
                $"sup={inline.Superscript}, sub={inline.Subscript}.");
        }

        /// <summary>
        /// Проверяет оформление, получаемое при чтении docDefaults вместе
        /// с default paragraph style, который выключает bold/underline/sup.
        /// </summary>
        void AssertImplicitFormatting(
            DocumentInline inline,
            string label)
        {
            AssertFormatting(
                inline,
                bold: false,
                italic: true,
                underline: false,
                strikethrough: true,
                superscript: false,
                subscript: false,
                label);
        }

        AssertImplicitFormatting(
            Inline("Implicit paragraph"),
            "implicit paragraph");

        AssertFormatting(
            Inline("Inherited paragraph"),
            bold: false,
            italic: false,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: false,
            "basedOn explicit paragraph");

        AssertFormatting(
            Inline("Independent paragraph"),
            bold: true,
            italic: false,
            underline: true,
            strikethrough: true,
            superscript: true,
            subscript: false,
            "independent explicit paragraph");

        AssertFormatting(
            Inline("Explicit not default"),
            bold: true,
            italic: true,
            underline: true,
            strikethrough: false,
            superscript: true,
            subscript: false,
            "w:default=0 paragraph not treated as implicit default");

        AssertFormatting(
            Inline("Character accent"),
            bold: true,
            italic: true,
            underline: true,
            strikethrough: true,
            superscript: false,
            subscript: true,
            "character style over default paragraph");

        AssertFormatting(
            Inline("Direct style override"),
            bold: true,
            italic: false,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: true,
            "direct run rPr over all defaults");

        AssertImplicitFormatting(
            Inline("Implicit after override"),
            "direct formatting does not leak");

        var hyperlink = Inline("Default hyperlink");

        AssertImplicitFormatting(
            hyperlink,
            "hyperlink inherits document defaults");

        AssertEqual(
            "#anchor",
            hyperlink.LinkHref!,
            "DOCX default-styled hyperlink должен сохранять w:anchor.");

        AssertFormatting(
            Inline("Default field"),
            bold: true,
            italic: true,
            underline: true,
            strikethrough: true,
            superscript: false,
            subscript: true,
            "fldSimple character style over doc defaults");

        var tabInline = paragraphs
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline =>
                inline.Text.Contains(
                    "Default tab",
                    StringComparison.Ordinal) &&
                inline.Text.Contains('\t') &&
                inline.Text.Contains(
                    Environment.NewLine,
                    StringComparison.Ordinal));

        AssertImplicitFormatting(
            tabInline,
            "tab/break text inherits document defaults");

        var cellInline = model.Blocks
            .OfType<DocumentTable>()
            .Single()
            .Rows[0]
            .Cells[0]
            .Inlines
            .Single(inline => inline.Text == "Default table cell");

        AssertImplicitFormatting(
            cellInline,
            "default table paragraph");

        var headerInline = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region => region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline => inline.Text == "Default header");

        AssertImplicitFormatting(
            headerInline,
            "default header paragraph");

        var noteInline = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph => paragraph.IsNote)
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline => inline.Text == "Default footnote");

        AssertImplicitFormatting(
            noteInline,
            "default footnote paragraph");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "href=\"#anchor-anchor\"",
            "DOCX docDefaults styled hyperlink должен сохранять HTML anchor.");

        var reread = SupportedFormatRegistry.ReadDocument(
            path);

        Assert(
            reread.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Any(inline =>
                    inline.Text == "Implicit paragraph" &&
                    inline.Italic &&
                    inline.Strikethrough &&
                    !inline.Bold &&
                    !inline.Underline),
            "DOCX default styles должны одинаково применяться между чтениями.");

        var missingStylesPath = CreateDocx(
            "docx-no-styles-part.docx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Implicit paragraph</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """,
            styles: null);

        var noStyles = SupportedFormatRegistry.ReadDocument(
            missingStylesPath);

        var noStylesInline = noStyles.Blocks
            .OfType<DocumentParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .Single(inline => inline.Text == "Implicit paragraph");

        AssertFormatting(
            noStylesInline,
            bold: false,
            italic: false,
            underline: false,
            strikethrough: false,
            superscript: false,
            subscript: false,
            "no styles.xml defaults");

        var implicitHeadingPath = CreateDocx(
            "docx-default-heading-style.docx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Implicit Heading</w:t></w:r></w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="BodyText"/></w:pPr>
                  <w:r><w:t>Regular Paragraph</w:t></w:r>
                </w:p>
              </w:body>
            </w:document>
            """,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="Heading2" w:default="1">
                <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="BodyText"/>
            </w:styles>
            """);

        var defaultHeading = SupportedFormatRegistry.ReadDocument(
            implicitHeadingPath);

        AssertEqual(
            1,
            defaultHeading.Outlines.Count,
            "DOCX default Heading2 style должен создавать navigation outline.");

        AssertEqual(
            "Implicit Heading",
            defaultHeading.Outlines[0].Title,
            "DOCX heading из default paragraph style должен сохранять заголовок.");

        AssertEqual(
            "docx-heading-1",
            defaultHeading.Outlines[0].TargetAnchorId!,
            "DOCX default Heading2 должен получить synthetic HTML anchor.");

        Assert(
            defaultHeading.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.HeadingLevel == 2 &&
                    paragraph.AnchorId == "docx-heading-1") &&
            defaultHeading.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.HeadingLevel == 0 &&
                    paragraph.Inlines.Any(inline =>
                        inline.Text == "Regular Paragraph")),
            "DOCX implicit heading не должен распространяться на explicit BodyText style.");

        /// <summary>
        /// Проверяет, что ошибочные объявления w:default или повторяющиеся
        /// style IDs отклоняются с понятным InvalidDataException.
        /// </summary>
        void AssertInvalid(
            string fileName,
            string invalidStyles,
            string diagnostic)
        {
            var invalidPath = CreateDocx(
                fileName,
                documentXml,
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
                $"DOCX {fileName} должен отклонять {diagnostic}.");
        }

        AssertInvalid(
            "docx-invalid-default-toggle.docx",
            stylesXml.Replace(
                """w:default="1">""",
                """w:default="unknown">""",
                StringComparison.Ordinal),
            "w:default");

        AssertInvalid(
            "docx-duplicate-default-paragraph.docx",
            stylesXml.Replace(
                """w:styleId="IndependentBody">""",
                """w:styleId="IndependentBody" w:default="1">""",
                StringComparison.Ordinal),
            "несколько default paragraph");

        AssertInvalid(
            "docx-duplicate-style-id.docx",
            stylesXml.Replace(
                """w:styleId="DerivedBody">""",
                """w:styleId="DefaultBody">""",
                StringComparison.Ordinal),
            "повторный styleId");

        AssertInvalid(
            "docx-malformed-styles-root.docx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:wrong xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>
            """,
            "корневой w:styles");
    }
}
