using System;
using System.Globalization;
using System.Net;
using System.Text;
using MYBOOK.Documents;
using NeoUI;

namespace MYBOOK.Rendering;

/// <summary>
/// Преобразует нейтральную модель документа MYBOOK в автономную HTML-страницу для WebView2,
/// включая безопасные потоковые ссылки/якоря и фиксированную постраничную верстку.
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
                html.Append("<pre");
                AppendFlowAnchorAttribute(
                    html,
                    paragraph.AnchorId);
                html.Append('>');
                AppendInlines(html, paragraph.Inlines);
                html.AppendLine("</pre>");
                return;

            case DocumentParagraph paragraph:
            {
                var level = Math.Clamp(paragraph.HeadingLevel, 0, 6);
                var tag = level > 0 ? "h" + level : "p";

                html.Append('<')
                    .Append(tag);

                AppendFlowAnchorAttribute(
                    html,
                    paragraph.AnchorId);

                html.Append('>');
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

        html.Append("""<section class="fixed-page" id="page-""")
            .Append(page.PageNumber)
            .AppendLine("\">");
        html.Append("""<div class="fixed-page-number">Page """)
            .Append(page.PageNumber)
            .AppendLine("</div>");

        if (page.TextRuns.Count > 0 || page.ImageRuns.Count > 0 || page.PathRuns.Count > 0 || page.Links.Count > 0)
        {
            html.Append("""<svg class="fixed-page-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 """)
                .Append(FormatNumber(width))
                .Append(' ')
                .Append(FormatNumber(height))
                .AppendLine("\" preserveAspectRatio=\"xMidYMin meet\">");

            var clipCounter = 0;

            var paintItems = page.PathRuns
                .Select(item => (
                    PaintOrder: item.PaintOrder,
                    Item: (object)item))
                .Concat(page.ImageRuns.Select(item => (
                    PaintOrder: item.PaintOrder,
                    Item: (object)item)))
                .Concat(page.TextRuns.Select(item => (
                    PaintOrder: item.PaintOrder,
                    Item: (object)item)))
                .OrderBy(item => item.PaintOrder);

            foreach (var paintItem in paintItems)
            {
                switch (paintItem.Item)
                {
                    case DocumentFixedPathRun pathRun:
                        AppendFixedPathRun(
                            html,
                            page.PageNumber,
                            ref clipCounter,
                            pathRun);
                        break;

                    case DocumentFixedImageRun imageRun:
                        AppendFixedImageRun(
                            html,
                            page.PageNumber,
                            ref clipCounter,
                            imageRun);
                        break;

                    case DocumentFixedTextRun textRun:
                        AppendFixedTextRun(
                            html,
                            page.PageNumber,
                            ref clipCounter,
                            textRun);
                        break;
                }
            }

            AppendFixedLinks(
                html,
                page.Links);

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

    private static void AppendFixedLinks(
        StringBuilder html,
        IReadOnlyList<DocumentFixedLink> links)
    {
        foreach (var link in links)
        {
            string? href = null;
            var external = false;

            if (!string.IsNullOrWhiteSpace(link.Uri))
            {
                href = link.Uri;
                external = true;
            }
            else if (link.TargetPageNumber is { } pageNumber)
            {
                href = $"#page-{pageNumber}";
            }

            if (href == null ||
                link.WidthPoints <= 0 ||
                link.HeightPoints <= 0)
            {
                continue;
            }

            html.Append("<a href=\"")
                .Append(WebUtility.HtmlEncode(href))
                .Append("\"");

            if (external)
            {
                html.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
            }

            html.Append("><rect class=\"fixed-page-link\" fill=\"transparent\" pointer-events=\"all\" x=\"")
                .Append(FormatNumber(link.XPoints))
                .Append("\" y=\"")
                .Append(FormatNumber(link.YPoints))
                .Append("\" width=\"")
                .Append(FormatNumber(link.WidthPoints))
                .Append("\" height=\"")
                .Append(FormatNumber(link.HeightPoints))
                .AppendLine("\" /></a>");
        }
    }

    private static void AppendFixedPathRun(
        StringBuilder html,
        int pageNumber,
        ref int clipCounter,
        DocumentFixedPathRun pathRun)
    {
        var clipIds = AppendClipDefinitions(
            html,
            pageNumber,
            ref clipCounter,
            pathRun.ClipPaths);

        AppendClipGroupsStart(html, clipIds);

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
                .Append("\" stroke-linecap=\"")
                .Append(pathRun.StrokeLineCap)
                .Append("\" stroke-linejoin=\"")
                .Append(pathRun.StrokeLineJoin)
                .Append("\" stroke-miterlimit=\"")
                .Append(FormatNumber(pathRun.StrokeMiterLimit))
                .Append("\" stroke-opacity=\"")
                .Append(FormatNumber(pathRun.StrokeOpacity))
                .Append("\"");

            if (pathRun.StrokeDashArray.Count > 0)
            {
                html.Append(" stroke-dasharray=\"")
                    .Append(string.Join(
                        " ",
                        pathRun.StrokeDashArray.Select(FormatNumber)))
                    .Append("\" stroke-dashoffset=\"")
                    .Append(FormatNumber(pathRun.StrokeDashOffset))
                    .Append("\"");
            }
        }

        if (pathRun.Fill != null)
        {
            html.Append(" fill-opacity=\"")
                .Append(FormatNumber(pathRun.FillOpacity))
                .Append("\"");
        }

        if (pathRun.EvenOddFill)
        {
            html.Append(" fill-rule=\"evenodd\"");
        }

        html.AppendLine(" />");
        AppendClipGroupsEnd(html, clipIds.Count);
    }

    private static void AppendFixedImageRun(
        StringBuilder html,
        int pageNumber,
        ref int clipCounter,
        DocumentFixedImageRun image)
    {
        var clipIds = AppendClipDefinitions(
            html,
            pageNumber,
            ref clipCounter,
            image.ClipPaths);

        string? softMaskId = null;

        if (image.SoftMaskData is { Length: > 0 } softMaskData)
        {
            if (string.IsNullOrWhiteSpace(
                    image.SoftMaskContentType))
            {
                throw new InvalidOperationException(
                    "Document image содержит soft mask без ContentType.");
            }

            softMaskId =
                $"mask-{pageNumber}-{clipCounter++}";

            html.Append("<defs><mask id=\"")
                .Append(softMaskId)
                .Append("\" maskUnits=\"objectBoundingBox\" maskContentUnits=\"objectBoundingBox\" style=\"mask-type:luminance\"><image x=\"0\" y=\"0\" width=\"1\" height=\"1\" preserveAspectRatio=\"none\" href=\"data:")
                .Append(WebUtility.HtmlEncode(
                    image.SoftMaskContentType))
                .Append(";base64,")
                .Append(Convert.ToBase64String(
                    softMaskData))
                .AppendLine("\" /></mask></defs>");
        }

        AppendClipGroupsStart(html, clipIds);

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
            .Append(")\"");

        if (softMaskId != null)
        {
            html.Append(" mask=\"url(#")
                .Append(softMaskId)
                .Append(")\"");
        }

        html.Append(" opacity=\"")
            .Append(FormatNumber(image.Opacity))
            .AppendLine("\" />");

        AppendClipGroupsEnd(html, clipIds.Count);
    }

    private static void AppendFixedTextRun(
        StringBuilder html,
        int pageNumber,
        ref int clipCounter,
        DocumentFixedTextRun run)
    {
        var clipIds = AppendClipDefinitions(
            html,
            pageNumber,
            ref clipCounter,
            run.ClipPaths);

        AppendClipGroupsStart(html, clipIds);

        html.Append("<text xml:space=\"preserve\" x=\"")
            .Append(FormatNumber(run.XPoints))
            .Append("\" y=\"")
            .Append(FormatNumber(run.YPoints))
            .Append("\" font-size=\"")
            .Append(FormatNumber(run.FontSizePoints))
            .Append("\" opacity=\"")
            .Append(FormatNumber(run.Opacity))
            .Append("\">")
            .Append(WebUtility.HtmlEncode(run.Text))
            .AppendLine("</text>");

        AppendClipGroupsEnd(html, clipIds.Count);
    }

    private static IReadOnlyList<string> AppendClipDefinitions(
        StringBuilder html,
        int pageNumber,
        ref int clipCounter,
        IReadOnlyList<DocumentFixedClipPath> clips)
    {
        if (clips.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ids = new string[clips.Count];

        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            var id = $"clip-{pageNumber}-{clipCounter++}";
            ids[index] = id;

            html.Append("<defs><clipPath id=\"")
                .Append(id)
                .Append("\"><path d=\"")
                .Append(WebUtility.HtmlEncode(clip.PathData))
                .Append("\"");

            if (clip.EvenOdd)
            {
                html.Append(" clip-rule=\"evenodd\"");
            }

            html.AppendLine(" /></clipPath></defs>");
        }

        return ids;
    }

    private static void AppendClipGroupsStart(
        StringBuilder html,
        IReadOnlyList<string> clipIds)
    {
        foreach (var id in clipIds)
        {
            html.Append("<g clip-path=\"url(#")
                .Append(id)
                .AppendLine(")\">");
        }
    }

    private static void AppendClipGroupsEnd(
        StringBuilder html,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            html.AppendLine("</g>");
        }
    }

    private static void AppendInlines(
        StringBuilder html,
        IReadOnlyList<DocumentInline> inlines)
    {
        foreach (var inline in inlines)
        {
            var link = ResolveFlowLink(
                inline.LinkHref);

            if (link.Href != null)
            {
                html.Append("<a href=\"")
                    .Append(WebUtility.HtmlEncode(
                        link.Href))
                    .Append("\"");

                if (link.External)
                {
                    html.Append(
                        " target=\"_blank\" rel=\"noopener noreferrer\"");
                }

                html.Append('>');
            }

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

            html.Append(
                WebUtility.HtmlEncode(inline.Text)
                    .Replace(
                        "\r\n",
                        "<br>",
                        StringComparison.Ordinal)
                    .Replace(
                        "\n",
                        "<br>",
                        StringComparison.Ordinal));

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

            if (link.Href != null)
            {
                html.Append("</a>");
            }
        }
    }

    private static void AppendFlowAnchorAttribute(
        StringBuilder html,
        string? anchorId)
    {
        var id = GetFlowAnchorId(
            anchorId);

        if (id == null)
        {
            return;
        }

        html.Append(" id=\"")
            .Append(WebUtility.HtmlEncode(id))
            .Append('"');
    }

    private static ResolvedFlowLink ResolveFlowLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return new ResolvedFlowLink(
                null,
                false);
        }

        if (href.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            var id = GetFlowAnchorId(
                href[1..]);

            return new ResolvedFlowLink(
                id == null
                    ? null
                    : "#" + id,
                false);
        }

        if (!Uri.TryCreate(
                href,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return new ResolvedFlowLink(
                null,
                false);
        }

        return new ResolvedFlowLink(
            uri.AbsoluteUri,
            true);
    }

    private static string? GetFlowAnchorId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return "anchor-" +
               Uri.EscapeDataString(
                   value.Trim());
    }

    private sealed record ResolvedFlowLink(
        string? Href,
        bool External);

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
