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
    /// Проверяет разрешение WordprocessingML paragraph theme colors из
    /// DrawingML clrScheme: srgbClr, sysClr/lastClr, 12 slots, settings.xml
    /// clrSchemeMapping, themeTint/themeShade на HSL luminance и приоритет
    /// theme references перед literal RGB. Проверяет наследование из
    /// docDefaults/paragraph styles, direct formatting, heading,
    /// table cell, header, footnote, HTML CSS/metadata и диагностические
    /// исключения для повреждённых theme/relationship/settings данных.
    /// Все тестовые OPC packages создаются во временной директории.
    /// </summary>
    private static void TestDocxThemeColors(string directory)
    {
        const string themeXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Regression">
              <a:themeElements>
                <a:clrScheme name="Regression">
                  <a:dk1><a:sysClr val="windowText" lastClr="101010"/></a:dk1>
                  <a:lt1><a:srgbClr val="EFEFEF"/></a:lt1>
                  <a:dk2><a:srgbClr val="223344"/></a:dk2>
                  <a:lt2><a:srgbClr val="DDDDDD"/></a:lt2>
                  <a:accent1><a:srgbClr val="808080"/></a:accent1>
                  <a:accent2><a:srgbClr val="112233"/></a:accent2>
                  <a:accent3><a:srgbClr val="404040"/></a:accent3>
                  <a:accent4><a:srgbClr val="00FF00"/></a:accent4>
                  <a:accent5><a:srgbClr val="0000FF"/></a:accent5>
                  <a:accent6><a:srgbClr val="FF0000"/></a:accent6>
                  <a:hlink><a:srgbClr val="336699"/></a:hlink>
                  <a:folHlink><a:srgbClr val="993366"/></a:folHlink>
                </a:clrScheme>
              </a:themeElements>
            </a:theme>
            """;

        const string settingsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:clrSchemeMapping w:t1="light1" w:accent2="accent3"/>
            </w:settings>
            """;

        const string stylesXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:pPrDefault>
                  <w:pPr>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="8" w:color="FF0000" w:themeColor="accent1"/>
                      <w:left w:val="dotted" w:sz="8" w:themeColor="text1"/>
                    </w:pBdr>
                    <w:shd w:val="clear" w:color="FF0000" w:themeColor="accent1"
                           w:themeShade="80" w:fill="FF0000" w:themeFill="accent2"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:styleId="Base" w:default="1">
                <w:pPr>
                  <w:pBdr>
                    <w:right w:val="double" w:sz="16" w:color="DEADBE"
                             w:themeColor="accent1" w:themeTint="80"/>
                  </w:pBdr>
                  <w:shd w:val="clear" w:fill="FFFFFF" w:themeFill="accent1"
                         w:themeFillTint="80"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Derived">
                <w:basedOn w:val="Base"/>
                <w:pPr>
                  <w:pBdr>
                    <w:bottom w:val="dashed" w:sz="8" w:color="FFFFFF"
                              w:themeColor="accent1" w:themeShade="80"
                              w:themeTint="00"/>
                  </w:pBdr>
                  <w:shd w:val="pct50" w:color="000000" w:themeColor="accent1"
                         w:themeTint="80" w:fill="000000" w:themeFill="accent2"
                         w:themeFillShade="80"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Independent">
                <w:pPr>
                  <w:pBdr>
                    <w:bottom w:val="single" w:sz="8" w:themeColor="background1"/>
                    <w:right w:val="single" w:sz="8" w:themeColor="text2"/>
                  </w:pBdr>
                  <w:shd w:val="clear" w:fill="123456" w:themeFill="accent2"/>
                </w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:basedOn w:val="Derived"/>
              </w:style>
            </w:styles>
            """;

        const string documentXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:r><w:t>Default theme</w:t></w:r></w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                  <w:r><w:t>Derived theme</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Derived"/>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="16" w:color="FF0000"
                             w:themeColor="accent2"/>
                      <w:right w:val="single" w:sz="8" w:themeColor="accent1"
                               w:themeTint="00"/>
                      <w:bar w:val="single" w:sz="8" w:themeColor="hyperlink"/>
                    </w:pBdr>
                    <w:shd w:val="solid" w:color="888888" w:themeColor="accent1"
                           w:themeShade="80" w:themeTint="FF"
                           w:fill="000000" w:themeFill="accent2"/>
                  </w:pPr>
                  <w:r><w:t>Direct theme</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Independent"/></w:pPr>
                  <w:r><w:t>Independent theme</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                  <w:r><w:t>Heading theme</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="8" w:color="FF8800" w:themeColor="none"/>
                    </w:pBdr>
                  </w:pPr>
                  <w:r><w:t>Theme none</w:t></w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pBdr>
                      <w:top w:val="single" w:sz="8" w:themeColor="accent6" w:themeTint="80"/>
                      <w:bottom w:val="single" w:sz="8" w:themeColor="accent5" w:themeShade="80"/>
                      <w:right w:val="single" w:sz="8" w:themeColor="dark1"/>
                      <w:bar w:val="single" w:sz="8" w:themeColor="followedHyperlink"/>
                    </w:pBdr>
                    <w:shd w:val="clear" w:themeFill="accent4" w:themeFillShade="80"/>
                  </w:pPr>
                  <w:r><w:t>Theme hue samples</w:t></w:r>
                </w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:pStyle w:val="Derived"/></w:pPr>
                        <w:r><w:t>Cell theme</w:t></w:r>
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
                <w:r><w:t>Header theme</w:t></w:r>
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
                    <w:shd w:val="clear" w:fill="FFFFFF" w:themeFill="accent1"
                           w:themeFillShade="80"/>
                  </w:pPr>
                  <w:r><w:t>Footnote theme</w:t></w:r>
                </w:p>
              </w:footnote>
            </w:footnotes>
            """;

        const string relationshipsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdTheme"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme"
                            Target="theme/theme1.xml"/>
              <Relationship Id="rIdSettings"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings"
                            Target="settings.xml"/>
              <Relationship Id="rIdHeader"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header"
                            Target="header1.xml"/>
            </Relationships>
            """;

        /// <summary>
        /// Записывает синтетический DOCX ZIP со всеми документными stories
        /// и указанными theme, settings, styles и relationship parts.
        /// Null theme/settings не создаёт соответствующий ZIP entry.
        /// </summary>
        string CreateDocx(
            string fileName,
            string body = documentXml,
            string styles = stylesXml,
            string? theme = themeXml,
            string? settings = settingsXml,
            string relationships = relationshipsXml)
        {
            var path = Path.Combine(
                directory,
                fileName);

            using var archive = ZipFile.Open(
                path,
                ZipArchiveMode.Create);

            /// <summary>
            /// Добавляет UTF-8 XML ZIP part без BOM.
            /// </summary>
            void AddPart(
                string name,
                string content)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(name).Open(),
                    new UTF8Encoding(false));
                writer.Write(content);
            }

            AddPart("word/document.xml", body);
            AddPart("word/styles.xml", styles);

            if (theme != null)
            {
                AddPart("word/theme/theme1.xml", theme);
            }

            if (settings != null)
            {
                AddPart("word/settings.xml", settings);
            }

            AddPart("word/header1.xml", headerXml);
            AddPart("word/footnotes.xml", footnotesXml);
            AddPart("word/_rels/document.xml.rels", relationships);

            return path;
        }

        /// <summary>
        /// Выбирает основной абзац DOCX по единственному точному text marker.
        /// </summary>
        DocumentParagraph Paragraph(
            DocumentModel document,
            string text)
        {
            return document.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline =>
                        inline.Text == text));
        }

        /// <summary>
        /// Сопоставляет тип границы и разрешённый CSS-RGB без преобразования
        /// исходной строки цветового атрибута WordprocessingML.
        /// </summary>
        void AssertBorder(
            DocumentBorder? border,
            byte type,
            string color,
            string scenario)
        {
            Assert(
                border != null &&
                border.Type == type &&
                border.Color == color,
                $"DOCX {scenario}: ожидается type={type} и RGB={color}, получено type={border?.Type}, RGB={border?.Color}.");
        }

        var path = CreateDocx("docx-theme-colors.docx");
        var model = SupportedFormatRegistry.ReadDocument(path);
        var defaultParagraph = Paragraph(model, "Default theme");
        var derived = Paragraph(model, "Derived theme");
        var direct = Paragraph(model, "Direct theme");
        var independent = Paragraph(model, "Independent theme");
        var heading = Paragraph(model, "Heading theme");

        AssertBorder(
            defaultParagraph.BorderTop,
            0x01,
            "#808080",
            "docDefaults accent1 ignores literal RGB");
        AssertBorder(
            defaultParagraph.BorderLeft,
            0x06,
            "#EFEFEF",
            "docDefaults text1 mapped by settings.xml");
        AssertBorder(
            defaultParagraph.BorderRight,
            0x03,
            "#BFBFBF",
            "default paragraph style themeTint=80");
        Assert(
            defaultParagraph.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#BFBFBF",
                ForegroundColor: null
            },
            "DOCX w:themeFillTint=80 должен изменять HSL lightness до #BFBFBF.");

        AssertBorder(
            derived.BorderBottom,
            0x07,
            "#404040",
            "basedOn themeShade=80 has precedence over themeTint=00");
        Assert(
            derived.BorderRight?.Color == "#BFBFBF" &&
            derived.Shading is
            {
                Pattern: 0x0008,
                ForegroundColor: "#BFBFBF",
                BackgroundColor: "#202020"
            },
            "DOCX paragraph style должен наследовать и разрешать foreground/background theme transforms.");

        AssertBorder(
            direct.BorderTop,
            0x01,
            "#404040",
            "direct accent2 maps to accent3");
        AssertBorder(
            direct.BorderRight,
            0x01,
            "#FFFFFF",
            "direct themeTint=00");
        AssertBorder(
            direct.BorderBar,
            0x01,
            "#336699",
            "hyperlink theme slot");
        Assert(
            direct.Shading is
            {
                Pattern: 0x0001,
                ForegroundColor: "#404040",
                BackgroundColor: "#404040"
            },
            "DOCX direct themeShade должен иметь приоритет над themeTint и сохранять themeFill mapping.");

        AssertBorder(
            independent.BorderBottom,
            0x01,
            "#EFEFEF",
            "background1 defaults to lt1");
        AssertBorder(
            independent.BorderRight,
            0x01,
            "#223344",
            "text2 defaults to dk2");
        Assert(
            independent.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#404040"
            },
            "DOCX explicit independent style должен брать только docDefaults и clrSchemeMapping.");

        Assert(
            heading.HeadingLevel == 1 &&
            heading.AnchorId == "docx-heading-1" &&
            heading.BorderBottom?.Color == "#404040" &&
            heading.Shading?.BackgroundColor == "#202020",
            "DOCX heading synthetic anchor должен сохранять resolved theme colors.");

        AssertBorder(
            Paragraph(model, "Theme none").BorderTop,
            0x01,
            "#FF8800",
            "themeColor=none uses literal fallback");

        var hueSamples = Paragraph(
            model,
            "Theme hue samples");

        AssertBorder(
            hueSamples.BorderTop,
            0x01,
            "#FF7F7F",
            "red themeTint must preserve HSL hue and saturation");
        AssertBorder(
            hueSamples.BorderBottom,
            0x01,
            "#000080",
            "blue themeShade must preserve HSL hue and saturation");
        AssertBorder(
            hueSamples.BorderRight,
            0x01,
            "#101010",
            "dk1 system color lastClr");
        AssertBorder(
            hueSamples.BorderBar,
            0x01,
            "#993366",
            "followedHyperlink theme slot");
        Assert(
            hueSamples.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#008000"
            },
            "DOCX green themeFillShade должен сохранять цветовой тон.");

        var tableParagraph = model.Blocks
            .OfType<DocumentTable>()
            .Single()
            .Rows[0].Cells[0].Blocks
            .OfType<DocumentParagraph>()
            .Single();

        Assert(
            tableParagraph.BorderRight?.Color == "#BFBFBF" &&
            tableParagraph.Shading?.ForegroundColor == "#BFBFBF",
            "DOCX table cell должен получать inherited mapped theme colors.");

        var header = model.Blocks
            .OfType<DocumentFlowRegion>()
            .Single(region =>
                region.Kind == DocumentFlowRegionKind.Header)
            .Blocks
            .OfType<DocumentParagraph>()
            .Single();

        Assert(
            header.BorderBottom?.Color == "#404040" &&
            header.Shading?.BackgroundColor == "#202020",
            "DOCX header должен получать theme colors из document relationships.");

        var footnote = model.Blocks
            .OfType<DocumentParagraph>()
            .Single(paragraph => paragraph.IsNote);

        Assert(
            footnote.BorderTop?.Color == "#808080" &&
            footnote.Shading is
            {
                Pattern: 0,
                BackgroundColor: "#404040"
            },
            "DOCX footnote должен переносить HSL themeFillShade=80.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        AssertContains(
            html,
            "border-top:1pt solid #808080;",
            "DOCX themed border CSS");
        AssertContains(
            html,
            "border-left:1pt dotted #EFEFEF;",
            "DOCX theme mapping CSS");
        AssertContains(
            html,
            "border-top:1pt solid #FF7F7F;",
            "DOCX colored themeTint CSS");
        AssertContains(
            html,
            "border-bottom:1pt solid #000080;",
            "DOCX colored themeShade CSS");
        AssertContains(
            html,
            "border-bottom:1pt dashed #404040;",
            "DOCX themeShade=80 border CSS");
        AssertContains(
            html,
            "border-right:1pt solid #FFFFFF;",
            "DOCX themeTint=00 border CSS");
        AssertContains(
            html,
            "background-color:#BFBFBF;",
            "DOCX themeFillTint=80 CSS");
        AssertContains(
            html,
            "background-color:#404040;",
            "DOCX themed solid shading CSS");
        AssertContains(
            html,
            "data-shading-foreground=\"#BFBFBF\"",
            "DOCX theme foreground HTML metadata");
        AssertContains(
            html,
            "data-shading-background=\"#202020\"",
            "DOCX theme background HTML metadata");

        var rootRelativeRelationships = relationshipsXml.Replace(
            """Target="theme/theme1.xml" """.TrimEnd(),
            """Target="/word/theme/theme1.xml" """.TrimEnd(),
            StringComparison.Ordinal);
        var rootRelative = CreateDocx(
            "docx-theme-root-relative.docx",
            relationships: rootRelativeRelationships);

        Assert(
            Paragraph(
                SupportedFormatRegistry.ReadDocument(rootRelative),
                "Default theme").BorderTop?.Color == "#808080",
            "DOCX root-relative theme relationship должен разрешаться от корня пакета.");

        var reread = SupportedFormatRegistry.ReadDocument(path);

        Assert(
            Paragraph(reread, "Direct theme").BorderTop?.Color == "#404040",
            "DOCX повторное чтение должно сохранять разрешённые цвета темы.");

        var specificationSample = CreateDocx(
            "docx-theme-tint-specification.docx",
            styles: stylesXml.Replace(
                """w:themeTint="80" """.TrimEnd(),
                """w:themeTint="99" """.TrimEnd(),
                StringComparison.Ordinal),
            theme: themeXml.Replace(
                """val="808080" """.TrimEnd(),
                """val="4F81BD" """.TrimEnd(),
                StringComparison.Ordinal));
        var specificationParagraph = Paragraph(
            SupportedFormatRegistry.ReadDocument(specificationSample),
            "Default theme");

        AssertBorder(
            specificationParagraph.BorderRight,
            0x03,
            "#95B3D7",
            "OOXML themeTint=99 specification color 4F81BD yields 95B3D7");

        /// <summary>
        /// Проверяет точный тип исключения и диагностическую подстроку
        /// при повреждённом Theme/Settings/WordprocessingML XML.
        /// </summary>
        void AssertInvalid(
            string name,
            string body,
            string styles,
            string? theme,
            string? settings,
            string relationships,
            Type type,
            string diagnostic)
        {
            var filePath = CreateDocx(
                name,
                body,
                styles,
                theme,
                settings,
                relationships);
            Exception? failure = null;

            try
            {
                SupportedFormatRegistry.ReadDocument(filePath);
            }
            catch (Exception error)
            {
                failure = error;
            }

            Assert(
                failure != null &&
                failure.GetType() == type &&
                failure.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal),
                $"DOCX {name} должен отклоняться через {type.Name}: {diagnostic}. Фактически: {failure}");
        }

        const string defaultBorder =
            """<w:top w:val="single" w:sz="8" w:color="FF0000" w:themeColor="accent1"/>""";
        AssertInvalid(
            "docx-theme-invalid-rgb.docx",
            documentXml,
            stylesXml,
            themeXml.Replace(
                """val="808080" """.TrimEnd(),
                """val="GGGGGG" """.TrimEnd(),
                StringComparison.Ordinal),
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "RGB");

        AssertInvalid(
            "docx-theme-no-lastclr.docx",
            documentXml,
            stylesXml,
            themeXml.Replace(
                " lastClr=\"101010\"",
                "",
                StringComparison.Ordinal),
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "RGB");

        AssertInvalid(
            "docx-theme-missing-colors.docx",
            documentXml,
            stylesXml,
            themeXml.Replace(
                """<a:accent6><a:srgbClr val="FF0000"/></a:accent6>""",
                "",
                StringComparison.Ordinal),
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "12 color slots");

        AssertInvalid(
            "docx-theme-unsupported-slot.docx",
            documentXml,
            stylesXml,
            themeXml.Replace(
                """<a:accent6><a:srgbClr val="FF0000"/></a:accent6>""",
                """<a:unmapped><a:srgbClr val="FF0000"/></a:unmapped>""",
                StringComparison.Ordinal),
            settingsXml,
            relationshipsXml,
            typeof(NotSupportedException),
            "a:unmapped");

        AssertInvalid(
            "docx-theme-unsupported-type.docx",
            documentXml,
            stylesXml,
            themeXml.Replace(
                """<a:accent1><a:srgbClr val="808080"/></a:accent1>""",
                """<a:accent1><a:prstClr val="red"/></a:accent1>""",
                StringComparison.Ordinal),
            settingsXml,
            relationshipsXml,
            typeof(NotSupportedException),
            "prstClr");

        AssertInvalid(
            "docx-theme-no-part.docx",
            documentXml,
            stylesXml,
            null,
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "отсутствующий part");

        AssertInvalid(
            "docx-theme-no-relationship.docx",
            documentXml,
            stylesXml,
            themeXml,
            settingsXml,
            relationshipsXml.Replace(
                """<Relationship Id="rIdTheme" """.TrimEnd(),
                """<Relationship Id="rIdNotTheme" """.TrimEnd(),
                StringComparison.Ordinal).Replace(
                    """officeDocument/2006/relationships/theme""",
                    """officeDocument/2006/relationships/not-theme""",
                    StringComparison.Ordinal),
            typeof(InvalidDataException),
            "отсутствующий Theme color slot");

        AssertInvalid(
            "docx-theme-external.docx",
            documentXml,
            stylesXml,
            themeXml,
            settingsXml,
            relationshipsXml.Replace(
                """Target="theme/theme1.xml" """.TrimEnd(),
                """Target="https://example.org/theme.xml" TargetMode="External" """.TrimEnd(),
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "внешний");

        AssertInvalid(
            "docx-theme-traversal.docx",
            documentXml,
            stylesXml,
            themeXml,
            settingsXml,
            relationshipsXml.Replace(
                """Target="theme/theme1.xml" """.TrimEnd(),
                """Target="../../../theme1.xml" """.TrimEnd(),
                StringComparison.Ordinal),
            typeof(InvalidDataException),
            "за пределы");

        AssertInvalid(
            "docx-theme-invalid-tint.docx",
            documentXml,
            stylesXml.Replace(
                """w:themeTint="80" """.TrimEnd(),
                """w:themeTint="bad" """.TrimEnd(),
                StringComparison.Ordinal),
            themeXml,
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "ST_UcharHexNumber");

        AssertInvalid(
            "docx-theme-unknown-name.docx",
            documentXml,
            stylesXml.Replace(
                defaultBorder,
                defaultBorder.Replace(
                    """w:themeColor="accent1" """.TrimEnd(),
                    """w:themeColor="rainbow" """.TrimEnd(),
                    StringComparison.Ordinal),
                StringComparison.Ordinal),
            themeXml,
            settingsXml,
            relationshipsXml,
            typeof(NotSupportedException),
            "w:themeColor=rainbow");

        AssertInvalid(
            "docx-theme-without-reference.docx",
            documentXml,
            stylesXml.Replace(
                defaultBorder,
                defaultBorder.Replace(
                    """w:themeColor="accent1" """.TrimEnd(),
                    """w:themeTint="80" """.TrimEnd(),
                    StringComparison.Ordinal),
                StringComparison.Ordinal),
            themeXml,
            settingsXml,
            relationshipsXml,
            typeof(InvalidDataException),
            "без theme color");

        AssertInvalid(
            "docx-theme-invalid-mapping.docx",
            documentXml,
            stylesXml,
            themeXml,
            settingsXml.Replace(
                """w:t1="light1" """.TrimEnd(),
                """w:t1="unknown" """.TrimEnd(),
                StringComparison.Ordinal),
            relationshipsXml,
            typeof(InvalidDataException),
            "недопустимый цвет");

        AssertInvalid(
            "docx-theme-mapping-cycle.docx",
            documentXml,
            stylesXml,
            themeXml,
            settingsXml.Replace(
                """w:t1="light1" w:accent2="accent3" """.TrimEnd(),
                """w:t1="text2" w:t2="text1" w:accent2="accent3" """.TrimEnd(),
                StringComparison.Ordinal),
            relationshipsXml,
            typeof(InvalidDataException),
            "цикл");

        AssertInvalid(
            "docx-theme-no-settings-part.docx",
            documentXml,
            stylesXml,
            themeXml,
            null,
            relationshipsXml,
            typeof(InvalidDataException),
            "отсутствующий part");
    }
}
