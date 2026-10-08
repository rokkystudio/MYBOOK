using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MYBOOK.Formats.Epub;

/// <summary>
/// Безопасно преобразует локальные EPUB-ресурсы для автономного HTML renderer:
/// изображения/SVG, picture/srcset, media и embedded fonts становятся data URI,
/// опасные HTML-атрибуты удаляются, а авторский CSS ограничивается главой
/// и не выполняет внешних загрузок.
/// </summary>
internal static class EpubResourceProcessor
{
    public static void RewriteChapterResources(
        ZipArchive archive,
        string chapterPath,
        XElement body)
    {
        foreach (var element in body
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName is
                             "script" or
                             "iframe" or
                             "frame" or
                             "object" or
                             "embed")
                     .ToArray())
        {
            element.Remove();
        }

        foreach (var element in body.DescendantsAndSelf())
        {
            foreach (var attribute in element
                         .Attributes()
                         .Where(attribute =>
                             attribute.Name.LocalName.StartsWith(
                                 "on",
                                 StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                attribute.Remove();
            }

            element.Attribute("autoplay")?.Remove();

            var style = element.Attribute("style");

            if (style != null)
            {
                var sanitized = SanitizeDeclarations(
                    archive,
                    chapterPath,
                    style.Value);

                if (sanitized.Length == 0)
                {
                    style.Remove();
                }
                else
                {
                    style.Value = sanitized;
                }
            }

            switch (element.Name.LocalName.ToLowerInvariant())
            {
                case "img":
                    RewriteImageAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("src"));

                    RewriteSrcSetAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("srcset"));
                    break;

                case "source":
                    RewriteResourceAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("src"));

                    RewriteSrcSetAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("srcset"));
                    break;

                case "video":
                    RewriteResourceAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("src"));

                    RewriteImageAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("poster"));
                    break;

                case "audio":
                    RewriteResourceAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("src"));
                    break;

                case "track":
                    RewriteResourceAttribute(
                        archive,
                        chapterPath,
                        element.Attribute("src"));
                    break;

                case "image":
                    foreach (var attribute in element
                                 .Attributes()
                                 .Where(attribute =>
                                     string.Equals(
                                         attribute.Name.LocalName,
                                         "href",
                                         StringComparison.OrdinalIgnoreCase))
                                 .ToArray())
                    {
                        RewriteImageAttribute(
                            archive,
                            chapterPath,
                            attribute);
                    }

                    break;
            }
        }
    }

    public static string ReadChapterCss(
        ZipArchive archive,
        string chapterPath,
        XElement? head,
        string chapterAnchor)
    {
        if (head == null)
        {
            return string.Empty;
        }

        var result = new StringBuilder();

        foreach (var style in head
                     .Descendants()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "style",
                             StringComparison.OrdinalIgnoreCase)))
        {
            AppendSanitizedCss(
                result,
                archive,
                chapterPath,
                style.Value,
                chapterAnchor);
        }

        foreach (var link in head
                     .Descendants()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "link",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var rel = link.Attribute("rel")?.Value ?? string.Empty;

            if (!rel.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Contains(
                    "stylesheet",
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var cssPath = ResolveArchivePath(
                chapterPath,
                link.Attribute("href")?.Value);

            if (cssPath == null)
            {
                continue;
            }

            var entry = archive.GetEntry(cssPath);

            if (entry == null)
            {
                continue;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            AppendSanitizedCss(
                result,
                archive,
                cssPath,
                reader.ReadToEnd(),
                chapterAnchor);
        }

        return result.ToString();
    }

    private static void AppendSanitizedCss(
        StringBuilder result,
        ZipArchive archive,
        string ownerPath,
        string css,
        string chapterAnchor)
    {
        css = RemoveStatementAtRules(
            css);

        var position = 0;

        while (position < css.Length)
        {
            var open = css.IndexOf(
                '{',
                position);

            if (open < 0)
            {
                break;
            }

            var close = FindBlockEnd(
                css,
                open);

            if (close < 0)
            {
                break;
            }

            var selector = css[position..open]
                .Trim();

            var body = css[(open + 1)..close];
            position = close + 1;

            if (selector.Length == 0 ||
                selector.Contains('<') ||
                selector.Contains('>') ||
                body.Contains('{'))
            {
                continue;
            }

            if (string.Equals(
                    selector,
                    "@font-face",
                    StringComparison.OrdinalIgnoreCase))
            {
                var fontFace = SanitizeFontFaceDeclarations(
                    archive,
                    ownerPath,
                    body);

                if (fontFace.Length > 0)
                {
                    result.Append("@font-face{")
                        .Append(fontFace)
                        .AppendLine("}");
                }

                continue;
            }

            if (selector.StartsWith(
                    "@",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var declarations = SanitizeDeclarations(
                archive,
                ownerPath,
                body);

            if (declarations.Length == 0)
            {
                continue;
            }

            var scoped = ScopeSelectors(
                selector,
                chapterAnchor);

            if (scoped.Length == 0)
            {
                continue;
            }

            result.Append(scoped)
                .Append('{')
                .Append(declarations)
                .AppendLine("}");
        }
    }

    private static string RemoveStatementAtRules(string css)
    {
        var result = new StringBuilder(css.Length);
        var position = 0;

        while (position < css.Length)
        {
            var at = css.IndexOf(
                '@',
                position);

            if (at < 0)
            {
                result.Append(
                    css[position..]);

                break;
            }

            result.Append(
                css[position..at]);

            var semicolon = css.IndexOf(
                ';',
                at + 1);

            var openBrace = css.IndexOf(
                '{',
                at + 1);

            if (semicolon >= 0 &&
                (openBrace < 0 ||
                 semicolon < openBrace))
            {
                position = semicolon + 1;
                continue;
            }

            result.Append('@');
            position = at + 1;
        }

        return result.ToString();
    }

    private static string ScopeSelectors(
        string selectors,
        string chapterAnchor)
    {
        var scope = "#" + chapterAnchor;
        var result = new List<string>();

        foreach (var raw in SplitTopLevel(
                     selectors,
                     ','))
        {
            var selector = raw.Trim();

            if (selector.Length == 0 ||
                selector.Contains('<') ||
                selector.Contains('>'))
            {
                continue;
            }

            if (selector is "html" or "body" or ":root")
            {
                result.Add(scope);
            }
            else if (selector.StartsWith(
                         "body ",
                         StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    scope +
                    selector[4..]);
            }
            else
            {
                result.Add(
                    scope +
                    " " +
                    selector);
            }
        }

        return string.Join(
            ",",
            result);
    }

    private static string SanitizeFontFaceDeclarations(
        ZipArchive archive,
        string ownerPath,
        string source)
    {
        var result = new StringBuilder();

        foreach (var raw in SplitTopLevel(
                     source,
                     ';'))
        {
            var separator = raw.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            var property = raw[..separator]
                .Trim()
                .ToLowerInvariant();

            var value = raw[(separator + 1)..]
                .Trim();

            if (value.Length == 0 ||
                value.Contains('<') ||
                value.Contains('>') ||
                value.Contains(
                    "javascript:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? sanitized = property switch
            {
                "font-family" => value,
                "font-style" => value,
                "font-weight" => value,
                "font-stretch" => value,
                "unicode-range" => value,
                "font-display" => value,
                "src" => RewriteFontSources(
                    archive,
                    ownerPath,
                    value),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(
                    sanitized))
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(';');
            }

            result.Append(property)
                .Append(':')
                .Append(sanitized);
        }

        return result.ToString();
    }

    private static string? RewriteFontSources(
        ZipArchive archive,
        string ownerPath,
        string value)
    {
        var sources = new List<string>();

        foreach (var rawSource in SplitTopLevel(
                     value,
                     ','))
        {
            var source = rawSource.Trim();

            if (source.Length == 0 ||
                source.StartsWith(
                    "local(",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var marker = source.IndexOf(
                "url(",
                StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                continue;
            }

            var end = source.IndexOf(
                ')',
                marker + 4);

            if (end < 0)
            {
                continue;
            }

            var reference = source[
                    (marker + 4)..end]
                .Trim()
                .Trim(
                    '"',
                    '\'');

            var dataUri = ResolveFontDataUri(
                archive,
                ownerPath,
                reference);

            if (dataUri == null)
            {
                continue;
            }

            var suffix = source[(end + 1)..]
                .Trim();

            if (suffix.Length > 0 &&
                !suffix.StartsWith(
                    "format(",
                    StringComparison.OrdinalIgnoreCase))
            {
                suffix = string.Empty;
            }

            sources.Add(
                "url(" +
                dataUri +
                ")" +
                (suffix.Length == 0
                    ? string.Empty
                    : " " + suffix));
        }

        return sources.Count == 0
            ? null
            : string.Join(
                ",",
                sources);
    }

    private static string? ResolveFontDataUri(
        ZipArchive archive,
        string ownerPath,
        string? reference)
    {
        var path = ResolveArchivePath(
            ownerPath,
            reference);

        if (path == null)
        {
            return null;
        }

        var contentType = GetFontContentType(
            path);

        if (contentType == null)
        {
            return null;
        }

        var entry = archive.GetEntry(path);

        if (entry == null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return "data:" +
               contentType +
               ";base64," +
               Convert.ToBase64String(
                   memory.ToArray());
    }

    private static string SanitizeDeclarations(
        ZipArchive archive,
        string ownerPath,
        string source)
    {
        var result = new StringBuilder();

        foreach (var raw in SplitTopLevel(
                     source,
                     ';'))
        {
            var separator = raw.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            var property = raw[..separator]
                .Trim()
                .ToLowerInvariant();

            var value = raw[(separator + 1)..]
                .Trim();

            if (!IsAllowedProperty(property) ||
                value.Length == 0 ||
                value.Contains('<') ||
                value.Contains('>') ||
                value.Contains(
                    "expression(",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Contains(
                    "javascript:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rewritten = RewriteUrls(
                archive,
                ownerPath,
                value);

            if (rewritten == null)
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(';');
            }

            result.Append(property)
                .Append(':')
                .Append(rewritten);
        }

        return result.ToString();
    }

    private static string? RewriteUrls(
        ZipArchive archive,
        string ownerPath,
        string value)
    {
        var result = new StringBuilder();
        var position = 0;

        while (position < value.Length)
        {
            var marker = value.IndexOf(
                "url(",
                position,
                StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                result.Append(
                    value[position..]);

                break;
            }

            result.Append(
                value[position..marker]);

            var end = value.IndexOf(
                ')',
                marker + 4);

            if (end < 0)
            {
                return null;
            }

            var reference = value[
                    (marker + 4)..end]
                .Trim()
                .Trim(
                    '"',
                    '\'');

            if (reference.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                result.Append("url(")
                    .Append(reference)
                    .Append(')');
            }
            else
            {
                var dataUri = ResolveImageDataUri(
                    archive,
                    ownerPath,
                    reference);

                if (dataUri == null)
                {
                    return null;
                }

                result.Append("url(")
                    .Append(dataUri)
                    .Append(')');
            }

            position = end + 1;
        }

        return result.ToString();
    }

    private static bool IsAllowedProperty(string property)
    {
        return property is
                   "color" or
                   "background" or
                   "background-color" or
                   "background-image" or
                   "font-family" or
                   "font-size" or
                   "font-style" or
                   "font-weight" or
                   "line-height" or
                   "text-align" or
                   "text-decoration" or
                   "text-indent" or
                   "text-transform" or
                   "letter-spacing" or
                   "word-spacing" or
                   "white-space" or
                   "width" or
                   "min-width" or
                   "max-width" or
                   "height" or
                   "min-height" or
                   "max-height" or
                   "display" or
                   "vertical-align" or
                   "float" or
                   "clear" or
                   "opacity" ||
               property.StartsWith(
                   "margin",
                   StringComparison.Ordinal) ||
               property.StartsWith(
                   "padding",
                   StringComparison.Ordinal) ||
               property.StartsWith(
                   "border",
                   StringComparison.Ordinal) ||
               property.StartsWith(
                   "list-style",
                   StringComparison.Ordinal);
    }

    private static int FindBlockEnd(
        string css,
        int openIndex)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;

        for (var index = openIndex;
             index < css.Length;
             index++)
        {
            var character = css[index];

            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' &&
                     --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevel(
        string value,
        char delimiter)
    {
        var result = new List<string>();
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        var quote = '\0';
        var escaped = false;

        for (var index = 0;
             index < value.Length;
             index++)
        {
            var character = value[index];

            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            switch (character)
            {
                case '(':
                    parentheses++;
                    break;

                case ')':
                    parentheses = Math.Max(
                        0,
                        parentheses - 1);
                    break;

                case '[':
                    brackets++;
                    break;

                case ']':
                    brackets = Math.Max(
                        0,
                        brackets - 1);
                    break;

                default:
                    if (character == delimiter &&
                        parentheses == 0 &&
                        brackets == 0)
                    {
                        result.Add(
                            value[start..index]);

                        start = index + 1;
                    }

                    break;
            }
        }

        result.Add(
            value[start..]);

        return result;
    }

    private static void RewriteSrcSetAttribute(
        ZipArchive archive,
        string ownerPath,
        XAttribute? attribute)
    {
        if (attribute == null)
        {
            return;
        }

        var candidates = new List<string>();

        foreach (var raw in attribute.Value.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = raw.Trim();

            if (candidate.Length == 0)
            {
                continue;
            }

            var separator = candidate.LastIndexOfAny(
                [' ', '\t', '\r', '\n']);

            var reference = separator < 0
                ? candidate
                : candidate[..separator].Trim();

            var descriptor = separator < 0
                ? string.Empty
                : candidate[(separator + 1)..].Trim();

            if (descriptor.Length > 0 &&
                !IsValidSrcSetDescriptor(
                    descriptor))
            {
                continue;
            }

            var dataUri = ResolveImageDataUri(
                archive,
                ownerPath,
                reference);

            if (dataUri == null)
            {
                continue;
            }

            candidates.Add(
                dataUri +
                (descriptor.Length == 0
                    ? string.Empty
                    : " " + descriptor));
        }

        if (candidates.Count == 0)
        {
            attribute.Remove();
        }
        else
        {
            attribute.Value = string.Join(
                ", ",
                candidates);
        }
    }

    private static bool IsValidSrcSetDescriptor(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        var suffix = value[^1];

        if (suffix == 'w')
        {
            return int.TryParse(
                       value[..^1],
                       out var width) &&
                   width > 0;
        }

        if (suffix == 'x')
        {
            return double.TryParse(
                       value[..^1],
                       System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out var scale) &&
                   scale > 0;
        }

        return false;
    }

    private static void RewriteResourceAttribute(
        ZipArchive archive,
        string ownerPath,
        XAttribute? attribute)
    {
        if (attribute == null)
        {
            return;
        }

        var dataUri = ResolveResourceDataUri(
            archive,
            ownerPath,
            attribute.Value);

        if (dataUri == null)
        {
            attribute.Remove();
        }
        else
        {
            attribute.Value = dataUri;
        }
    }

    private static string? ResolveResourceDataUri(
        ZipArchive archive,
        string ownerPath,
        string? reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            return null;
        }

        var value = reference.Trim();

        if (value.StartsWith(
                "data:",
                StringComparison.OrdinalIgnoreCase))
        {
            return IsAllowedDataResource(
                    value)
                ? value
                : null;
        }

        var path = ResolveArchivePath(
            ownerPath,
            value);

        if (path == null)
        {
            return null;
        }

        var contentType = GetResourceContentType(
            path);

        if (contentType == null)
        {
            return null;
        }

        var entry = archive.GetEntry(path);

        if (entry == null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return "data:" +
               contentType +
               ";base64," +
               Convert.ToBase64String(
                   memory.ToArray());
    }

    private static bool IsAllowedDataResource(string value)
    {
        return value.StartsWith(
                   "data:image/",
                   StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(
                   "data:audio/",
                   StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(
                   "data:video/",
                   StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(
                   "data:text/vtt",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void RewriteImageAttribute(
        ZipArchive archive,
        string ownerPath,
        XAttribute? attribute)
    {
        if (attribute == null)
        {
            return;
        }

        var dataUri = ResolveImageDataUri(
            archive,
            ownerPath,
            attribute.Value);

        if (dataUri == null)
        {
            attribute.Remove();
        }
        else
        {
            attribute.Value = dataUri;
        }
    }

    private static string? ResolveImageDataUri(
        ZipArchive archive,
        string ownerPath,
        string? reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            return null;
        }

        var value = reference.Trim();

        if (value.StartsWith(
                "data:image/",
                StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var path = ResolveArchivePath(
            ownerPath,
            value);

        if (path == null)
        {
            return null;
        }

        var contentType = GetImageContentType(path);

        if (contentType == null)
        {
            return null;
        }

        var entry = archive.GetEntry(path);

        if (entry == null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return "data:" +
               contentType +
               ";base64," +
               Convert.ToBase64String(
                   memory.ToArray());
    }

    private static string? ResolveArchivePath(
        string ownerPath,
        string? reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            return null;
        }

        var value = reference.Trim();

        if (Uri.TryCreate(
                value,
                UriKind.Absolute,
                out _))
        {
            return null;
        }

        var cut = value.IndexOfAny(
            ['?', '#']);

        if (cut >= 0)
        {
            value = value[..cut];
        }

        if (value.Length == 0)
        {
            return null;
        }

        return CombinePath(
            GetDirectory(ownerPath),
            Uri.UnescapeDataString(value));
    }

    private static string CombinePath(
        string directory,
        string path)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return NormalizePath(path);
        }

        var segments = (directory + "/" + path)
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        var stack = new Stack<string>();

        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.Pop();
                }

                continue;
            }

            stack.Push(segment);
        }

        return string.Join(
            "/",
            stack.Reverse());
    }

    private static string GetDirectory(string path)
    {
        var normalized = NormalizePath(path);
        var index = normalized.LastIndexOf('/');

        return index < 0
            ? string.Empty
            : normalized[..index];
    }

    private static string NormalizePath(string path)
    {
        return path
            .Replace(
                '\\',
                '/')
            .TrimStart('/');
    }

    private static string? GetResourceContentType(string path)
    {
        return GetImageContentType(path) ??
               Path.GetExtension(path)
                   .ToLowerInvariant() switch
               {
                   ".mp3" => "audio/mpeg",
                   ".m4a" or ".aac" => "audio/mp4",
                   ".ogg" or ".oga" => "audio/ogg",
                   ".wav" => "audio/wav",
                   ".mp4" or ".m4v" => "video/mp4",
                   ".webm" => "video/webm",
                   ".ogv" => "video/ogg",
                   ".vtt" => "text/vtt",
                   _ => null
               };
    }

    private static string? GetFontContentType(string path)
    {
        return Path.GetExtension(path)
            .ToLowerInvariant() switch
        {
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            _ => null
        };
    }

    private static string? GetImageContentType(string path)
    {
        return Path.GetExtension(path)
            .ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null
        };
    }
}
