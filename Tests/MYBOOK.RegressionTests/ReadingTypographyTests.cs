using System.IO;
using System.Text.Json;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

/// <summary>
/// Проверяет типизированные, сохраняемые в JSON настройки потокового ридера,
/// source-preserving CSS scope и отказ при недопустимых значениях.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Проверяет настройки шрифта, размера, ширины и line-height, JSON
    /// round-trip, CSS для flow documents и live JavaScript обновление.
    /// Fixed-page layout и explicit source inline styles остаются независимыми.
    /// </summary>
    private static void TestReadingTypographySettings()
    {
        var defaults = new DocumentReadingTypography();
        defaults.Validate();

        Assert(defaults.FontFamily ==
                DocumentReadingFontFamily.Georgia &&
            defaults.FontSizePixels == 19 &&
            defaults.ContentWidthPixels == 980 &&
            defaults.LineHeightPercent == 162 &&
            defaults.GetCssLineHeight() == "1.62",
            "Reading typography defaults must match the existing flowing HTML baseline.");

        foreach (var font in
                 Enum.GetValues<DocumentReadingFontFamily>())
        {
            var typography = new DocumentReadingTypography
            {
                FontFamily = font
            };
            typography.Validate();
            Assert(!string.IsNullOrWhiteSpace(
                    typography.GetFontFamilyName()) &&
                !string.IsNullOrWhiteSpace(
                    typography.GetCssFontFamily()),
                $"Reading typography preset {font} must have explicit WPF/CSS font names.");
        }

        var custom = new DocumentReadingTypography
        {
            FontFamily = DocumentReadingFontFamily.SegoeUI,
            FontSizePixels = 24,
            ContentWidthPixels = 760,
            LineHeightPercent = 185
        };
        custom.Validate();

        var settings = new AppSettings
        {
            ReadingTypography = custom,
            Language = "ru",
            Theme = "Dark"
        };
        var settingsJson = JsonSerializer.Serialize(
            settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(
            settingsJson);

        Assert(restored != null &&
            restored.ReadingTypography == custom &&
            restored.Language == "ru" &&
            restored.Theme == "Dark",
            "Reading typography must round-trip through AppSettings JSON without losing font or layout values.");

        var legacyJson =
            """
            {"Theme":"Light","Language":"auto","RecentDocuments":[]}
            """;
        var legacySettings = JsonSerializer.Deserialize<AppSettings>(
            legacyJson);
        Assert(legacySettings?.ReadingTypography ==
            new DocumentReadingTypography(),
            "Older AppSettings JSON without reading typography must preserve neutral defaults.");

        var document = new DocumentModel
        {
            Title = "Typography",
            Blocks =
            [
                new DocumentParagraph
                {
                    Inlines =
                    [
                        new DocumentInline
                        {
                            Text = "Source text",
                            FontFamily = "Times New Roman",
                            FontSizePoints = 10
                        }
                    ]
                }
            ]
        };

        var html = DocumentHtmlRenderer.Render(
            document,
            CreateRenderTheme(),
            custom);

        AssertContains(
            html,
            "<body class=\"flow-document\">",
            "Reading typography must mark generated flowing documents.");
        AssertContains(
            html,
            "--reading-font-family: \"Segoe UI\", Arial, sans-serif;",
            "Reading typography must select its fixed CSS font stack.");
        AssertContains(
            html,
            "--reading-font-size: 24px;",
            "Reading typography must apply the configured base font size.");
        AssertContains(
            html,
            "--reading-content-width: 760px;",
            "Reading typography must apply the configured text-column width.");
        AssertContains(
            html,
            "--reading-line-height: 1.85;",
            "Reading typography must apply the configured unitless line spacing.");
        AssertContains(
            html,
            "body.flow-document main {",
            "Reading width must be scoped to reflow content.");
        AssertContains(
            html,
            "font-family:&#39;Times New Roman&#39;;",
            "Reading typography must preserve explicit source inline font formatting.");
        AssertContains(
            html,
            "Source text",
            "Reading typography must preserve original content.");

        var defaultHtml = DocumentHtmlRenderer.Render(
            document,
            CreateRenderTheme());
        AssertContains(
            defaultHtml,
            "--reading-font-size: 19px;",
            "Default typography must match the prior base font size.");
        AssertContains(
            defaultHtml,
            "--reading-content-width: 980px;",
            "Default typography must match the prior content width.");
        AssertContains(
            defaultHtml,
            "--reading-line-height: 1.62;",
            "Default typography must match the prior line height.");

        var fixedDocument = new DocumentModel
        {
            Title = "Fixed layout",
            Blocks =
            [
                new DocumentFixedPage
                {
                    PageNumber = 1,
                    WidthPoints = 200,
                    HeightPoints = 250
                }
            ]
        };

        var fixedHtml = DocumentHtmlRenderer.Render(
            fixedDocument,
            CreateRenderTheme(),
            custom);
        AssertContains(
            fixedHtml,
            "<body>",
            "Fixed-page HTML must keep the original non-flow body.");
        Assert(!fixedHtml.Contains(
                "<body class=\"flow-document\">",
                StringComparison.Ordinal),
            "Reading typography must not change fixed-page source geometry.");

        var script =
            DocumentHtmlRenderer.CreateTypographyScript(
                custom);
        AssertContains(
            script,
            "style.setProperty('--reading-font-family',",
            "Typography script must update CSS custom properties on the open generated page.");
        AssertContains(
            script,
            "style.setProperty('--reading-font-size',\"24px\")",
            "Typography script must update validated base font dimensions.");
        AssertContains(
            script,
            "style.setProperty('--reading-content-width',\"760px\")",
            "Typography script must update validated text width.");
        AssertContains(
            script,
            "style.setProperty('--reading-line-height',\"1.85\")",
            "Typography script must update unitless line spacing.");
        Assert(!script.Contains(
                "Source text",
                StringComparison.Ordinal),
            "Typography script must not embed document text.");

        var invalidSettings = new[]
        {
            custom with
            {
                FontFamily =
                    (DocumentReadingFontFamily)int.MaxValue
            },
            custom with
            {
                FontSizePixels = 11
            },
            custom with
            {
                FontSizePixels = 37
            },
            custom with
            {
                ContentWidthPixels = 479
            },
            custom with
            {
                ContentWidthPixels = 1401
            },
            custom with
            {
                LineHeightPercent = 109
            },
            custom with
            {
                LineHeightPercent = 251
            }
        };

        foreach (var invalid in invalidSettings)
        {
            var rejected = false;
            try
            {
                invalid.Validate();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected,
                "Reading typography values outside explicit bounds must be rejected instead of silently clamped.");

            var rendererRejected = false;
            try
            {
                _ = DocumentHtmlRenderer.Render(
                    document,
                    CreateRenderTheme(),
                    invalid);
            }
            catch (InvalidDataException)
            {
                rendererRejected = true;
            }

            Assert(rendererRejected,
                "HTML renderer must reject invalid typography before emitting CSS.");

            var scriptRejected = false;
            try
            {
                _ = DocumentHtmlRenderer.CreateTypographyScript(
                    invalid);
            }
            catch (InvalidDataException)
            {
                scriptRejected = true;
            }

            Assert(scriptRejected,
                "Runtime CSS updates must reject invalid persisted typography values.");
        }
    }
}
