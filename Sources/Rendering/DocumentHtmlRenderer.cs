using System;
using System.Globalization;
using System.Net;
using System.Text;
using MYBOOK.Documents;
using NeoUI;

namespace MYBOOK.Rendering;

/// <summary>
/// Преобразует нейтральную модель документа MYBOOK в автономную HTML-страницу для WebView2.
/// </summary>
internal static class DocumentHtmlRenderer
{
    /// <summary>
    /// Создаёт HTML-документ с общей типографикой, темой NeoUI,
    /// обложкой, потоковыми блоками и фиксированными страницами.
    /// </summary>
    public static string Render(DocumentModel document, string theme)
    {
        var palette = NeoThemePalettes.Get(theme);
        var html = new StringBuilder(Math.Max(4096, document.Blocks.Count * 128));

        html.Append("""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>
""");
        html.Append(WebUtility.HtmlEncode(document.Title));
        html.Append("""
</title>
<style>
:root {
""");
        AppendCssVariable(html, "background", palette.Background);
        AppendCssVariable(html, "surface", palette.Surface);
        AppendCssVariable(html, "text", palette.Text);
        AppendCssVariable(html, "muted", palette.MutedText);
        AppendCssVariable(html, "border", palette.Border);
        AppendCssVariable(html, "primary", palette.Primary);
        html.Append("""
}
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: var(--background); color: var(--text); }
body {
    font-family: Georgia, "Times New Roman", serif;
    font-size: 19px;
    line-height: 1.62;
    text-rendering: optimizeLegibility;
}
main {
    width: min(100%, 980px);
    margin: 0 auto;
    padding: 40px 56px 64px;
}
.cover {
    display: block;
    width: 100%;
    height: auto;
    margin: 0 0 34px;
    object-fit: contain;
}
.document-title {
    margin: 0 0 8px;
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 2rem;
    line-height: 1.24;
    font-weight: 650;
}
.author {
    margin: 0 0 34px;
    color: var(--muted);
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 0.88rem;
    line-height: 1.45;
}
h1, h2, h3, h4, h5, h6 {
    font-family: "Segoe UI", Arial, sans-serif;
    line-height: 1.34;
}
h1 { font-size: 1.55rem; }
h2 { font-size: 1.34rem; }
h3 { font-size: 1.18rem; }
p { margin: 0 0 0.72em; text-align: justify; }
pre {
    margin: 0 0 1em;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
    font-family: Consolas, "Cascadia Mono", monospace;
    font-size: 0.92rem;
    line-height: 1.55;
}
code { font-family: Consolas, "Cascadia Mono", monospace; }
sup { font-size: 0.66em; line-height: 0; vertical-align: super; }
blockquote {
    margin: 1em 0;
    padding-left: 1em;
    border-left: 3px solid var(--border);
    color: var(--muted);
}
table { border-collapse: collapse; width: 100%; }
th, td { border: 1px solid var(--border); padding: 0.45em 0.6em; text-align: left; }
img { max-width: 100%; height: auto; }
a { color: var(--primary); }
.fixed-page {
    width: 100%;
    margin: 0 auto 28px;
    background: var(--surface);
    border: 1px solid var(--border);
    box-shadow: 0 8px 24px rgba(0,0,0,0.12);
    overflow: hidden;
}
.fixed-page-number {
    padding: 8px 12px;
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 0.74rem;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
}
.fixed-page-svg {
    display: block;
    width: 100%;
    height: auto;
    background: white;
}
.fixed-page-svg text {
    fill: #111;
    font-family: "Times New Roman", serif;
}
.fixed-page-fallback {
    width: 100%;
    aspect-ratio: var(--page-ratio);
    padding: 5.5%;
    overflow: auto;
    background: white;
    color: #111;
}
.fixed-page-text {
    margin: 0;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
    font-family: "Times New Roman", serif;
    font-size: 1rem;
    line-height: 1.35;
}
::selection { background: var(--primary); color: white; }
@media (max-width: 700px) { main { padding: 24px 24px 42px; } }
</style>
</head>
<body>
<main>
""");

        if (document.Cover is { Data.Length: > 0 })
        {
            html.Append("""<img class="cover" alt="" src="data:""")
                .Append(WebUtility.HtmlEncode(document.Cover.ContentType))
                .Append(";base64,")
                .Append(Convert.ToBase64String(document.Cover.Data))
                .Append('"')
                .AppendLine(">");
        }

        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            html.Append("""<div class="document-title">""")
                .Append(WebUtility.HtmlEncode(document.Title))
                .AppendLine("</div>");
        }

        if (!string.IsNullOrWhiteSpace(document.Author))
        {
            html.Append("""<div class="author">""")
                .Append(WebUtility.HtmlEncode(document.Author))
                .AppendLine("</div>");
        }

        foreach (var block in document.Blocks)
        {
            AppendBlock(html, block);
        }

        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    /// <summary>
    /// Создаёт JavaScript для обновления цветов уже открытой сгенерированной страницы.
    /// </summary>
    public static string CreateThemeScript(string theme)
    {
        var palette = NeoThemePalettes.Get(theme);

        return "document.documentElement.style.setProperty('--background','" + palette.Background + "');" +
               "document.documentElement.style.setProperty('--surface','" + palette.Surface + "');" +
               "document.documentElement.style.setProperty('--text','" + palette.Text + "');" +
               "document.documentElement.style.setProperty('--muted','" + palette.MutedText + "');" +
               "document.documentElement.style.setProperty('--border','" + palette.Border + "');" +
               "document.documentElement.style.setProperty('--primary','" + palette.Primary + "');";
    }

    private static void AppendBlock(StringBuilder html, DocumentBlock block)
    {
        switch (block)
        {
            case DocumentHtmlBlock raw:
                html.AppendLine(raw.Html);
                return;

            case DocumentFixedPage page:
                AppendFixedPage(html, page);
                return;

            case DocumentParagraph paragraph when paragraph.Preformatted:
                html.Append("<pre>");
                AppendInlines(html, paragraph.Inlines);
                html.AppendLine("</pre>");
                return;

            case DocumentParagraph paragraph:
            {
                var level = Math.Clamp(paragraph.HeadingLevel, 0, 6);
                var tag = level > 0 ? "h" + level : "p";
                html.Append('<').Append(tag).Append('>');
                AppendInlines(html, paragraph.Inlines);
                html.Append("</").Append(tag).AppendLine(">");
                return;
            }
        }
    }

    private static void AppendFixedPage(
        StringBuilder html,
        DocumentFixedPage page)
    {
        var width = Math.Max(page.WidthPoints, 1.0);
        var height = Math.Max(page.HeightPoints, 1.0);

        html.AppendLine("""<section class="fixed-page">""");
        html.Append("""<div class="fixed-page-number">Page """)
            .Append(page.PageNumber)
            .AppendLine("</div>");

        if (page.TextRuns.Count > 0 || page.ImageRuns.Count > 0 || page.PathRuns.Count > 0)
        {
            html.Append("""<svg class="fixed-page-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 """)
                .Append(FormatNumber(width))
                .Append(' ')
                .Append(FormatNumber(height))
                .AppendLine("\" preserveAspectRatio=\"xMidYMin meet\">");

            foreach (var pathRun in page.PathRuns)
            {
                html.Append("<path d=\"")
                    .Append(WebUtility.HtmlEncode(pathRun.PathData))
                    .Append("\" fill=\"")
                    .Append(pathRun.Fill ?? "none")
                    .Append("\" stroke=\"")
                    .Append(pathRun.Stroke ?? "none")
                    .Append("\"");

                if (pathRun.Stroke != null)
                {
                    html.Append(" stroke-width=\"")
                        .Append(FormatNumber(pathRun.StrokeWidthPoints))
                        .Append("\"");
                }

                if (pathRun.EvenOddFill)
                {
                    html.Append(" fill-rule=\"evenodd\"");
                }

                html.AppendLine(" />");
            }

            foreach (var image in page.ImageRuns)
            {
                html.Append("<image x=\"0\" y=\"0\" width=\"1\" height=\"1\" preserveAspectRatio=\"none\" href=\"data:")
                    .Append(WebUtility.HtmlEncode(image.ContentType))
                    .Append(";base64,")
                    .Append(Convert.ToBase64String(image.Data))
                    .Append("\" transform=\"matrix(")
                    .Append(FormatNumber(image.TransformA))
                    .Append(' ')
                    .Append(FormatNumber(image.TransformB))
                    .Append(' ')
                    .Append(FormatNumber(image.TransformC))
                    .Append(' ')
                    .Append(FormatNumber(image.TransformD))
                    .Append(' ')
                    .Append(FormatNumber(image.TransformE))
                    .Append(' ')
                    .Append(FormatNumber(image.TransformF))
                    .AppendLine(")\" />");
            }

            foreach (var run in page.TextRuns)
            {
                html.Append("<text xml:space=\"preserve\" x=\"")
                    .Append(FormatNumber(run.XPoints))
                    .Append("\" y=\"")
                    .Append(FormatNumber(run.YPoints))
                    .Append("\" font-size=\"")
                    .Append(FormatNumber(run.FontSizePoints))
                    .Append("\">")
                    .Append(WebUtility.HtmlEncode(run.Text))
                    .AppendLine("</text>");
            }

            html.AppendLine("</svg>");
        }
        else
        {
            var ratio = (width / height).ToString(
                "0.######",
                CultureInfo.InvariantCulture);

            html.Append("""<div class="fixed-page-fallback" style="--page-ratio:""")
                .Append(ratio)
                .AppendLine("\">");

            html.Append("""<pre class="fixed-page-text">""")
                .Append(WebUtility.HtmlEncode(page.Text))
                .AppendLine("</pre></div>");
        }

        html.AppendLine("</section>");
    }

    private static void AppendInlines(StringBuilder html, IReadOnlyList<DocumentInline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline.Bold)
            {
                html.Append("<strong>");
            }

            if (inline.Italic)
            {
                html.Append("<em>");
            }

            if (inline.Underline)
            {
                html.Append("<u>");
            }

            if (inline.Superscript)
            {
                html.Append("<sup>");
            }

            if (inline.Code)
            {
                html.Append("<code>");
            }

            html.Append(WebUtility.HtmlEncode(inline.Text).Replace("\\r\\n", "<br>").Replace("\\n", "<br>"));

            if (inline.Code)
            {
                html.Append("</code>");
            }

            if (inline.Superscript)
            {
                html.Append("</sup>");
            }

            if (inline.Underline)
            {
                html.Append("</u>");
            }

            if (inline.Italic)
            {
                html.Append("</em>");
            }

            if (inline.Bold)
            {
                html.Append("</strong>");
            }
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
    }

    private static void AppendCssVariable(StringBuilder html, string name, string value)
    {
        html.Append("    --")
            .Append(name)
            .Append(": ")
            .Append(value)
            .AppendLine(";");
    }
}
