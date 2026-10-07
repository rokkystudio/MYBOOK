using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Epub;

/// <summary>
/// Читает EPUB напрямую как ZIP-контейнер, разбирает container.xml, OPF manifest/spine
/// и объединяет XHTML-главы в нейтральную модель MYBOOK.
/// </summary>
internal static class EpubDocumentReader
{
    private static readonly XNamespace ContainerNs =
        "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace OpfNs =
        "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNs =
        "http://purl.org/dc/elements/1.1/";

    /// <summary>
    /// Загружает EPUB, извлекает метаданные, обложку и spine-главы.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        var opfPath = ReadPackagePath(archive);
        var opf = LoadXml(archive, opfPath);
        var package = opf.Root
                      ?? throw new InvalidDataException("EPUB package document пуст.");

        var manifest = package.Element(OpfNs + "manifest")
                       ?? throw new InvalidDataException("EPUB не содержит manifest.");

        var items = manifest.Elements(OpfNs + "item")
            .Where(item => item.Attribute("id") != null && item.Attribute("href") != null)
            .ToDictionary(
                item => item.Attribute("id")!.Value,
                item => new ManifestItem(
                    item.Attribute("href")!.Value,
                    item.Attribute("media-type")?.Value ?? string.Empty,
                    item.Attribute("properties")?.Value ?? string.Empty),
                StringComparer.Ordinal);

        var baseDirectory = GetDirectory(opfPath);
        var blocks = new List<DocumentBlock>();

        var spine = package.Element(OpfNs + "spine");
        if (spine != null)
        {
            foreach (var itemRef in spine.Elements(OpfNs + "itemref"))
            {
                var idRef = itemRef.Attribute("idref")?.Value;
                if (string.IsNullOrWhiteSpace(idRef) || !items.TryGetValue(idRef, out var item))
                {
                    continue;
                }

                if (!item.MediaType.Contains("xhtml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var chapterPath = CombinePath(baseDirectory, item.Href);
                var chapter = LoadXml(archive, chapterPath);
                var body = chapter.Descendants().FirstOrDefault(element =>
                    element.Name.LocalName == "body");

                if (body == null)
                {
                    continue;
                }

                var html = string.Concat(body.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)));
                html = RewriteEmbeddedResources(archive, chapterPath, html);

                if (!string.IsNullOrWhiteSpace(html))
                {
                    blocks.Add(new DocumentHtmlBlock
                    {
                        Html = "<section class=\"epub-chapter\">" + html + "</section>"
                    });
                }
            }
        }

        var metadata = package.Element(OpfNs + "metadata");
        var title = metadata?.Elements(DcNs + "title").FirstOrDefault()?.Value?.Trim();
        var author = string.Join(", ",
            metadata?.Elements(DcNs + "creator")
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
            ?? Array.Empty<string>());

        return new DocumentModel
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(path)
                : title,
            Author = author,
            Cover = ReadCover(archive, package, items, baseDirectory),
            Blocks = blocks
        };
    }

    private static string ReadPackagePath(ZipArchive archive)
    {
        var container = LoadXml(archive, "META-INF/container.xml");
        var rootFile = container
            .Descendants(ContainerNs + "rootfile")
            .FirstOrDefault();

        return rootFile?.Attribute("full-path")?.Value
               ?? throw new InvalidDataException(
                   "EPUB container.xml не содержит путь к OPF.");
    }

    private static DocumentImage? ReadCover(
        ZipArchive archive,
        XElement package,
        IReadOnlyDictionary<string, ManifestItem> items,
        string baseDirectory)
    {
        var coverItem = items.Values.FirstOrDefault(item =>
            item.Properties.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("cover-image", StringComparer.OrdinalIgnoreCase));

        if (coverItem == null)
        {
            var metadata = package.Element(OpfNs + "metadata");
            var coverId = metadata?
                .Elements(OpfNs + "meta")
                .FirstOrDefault(meta =>
                    string.Equals(
                        meta.Attribute("name")?.Value,
                        "cover",
                        StringComparison.OrdinalIgnoreCase))?
                .Attribute("content")?
                .Value;

            if (!string.IsNullOrWhiteSpace(coverId))
            {
                items.TryGetValue(coverId, out coverItem);
            }
        }

        if (coverItem == null || !coverItem.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var entry = archive.GetEntry(CombinePath(baseDirectory, coverItem.Href));
        if (entry == null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return new DocumentImage
        {
            Data = memory.ToArray(),
            ContentType = coverItem.MediaType
        };
    }

    private static string RewriteEmbeddedResources(
        ZipArchive archive,
        string chapterPath,
        string html)
    {
        var chapterDirectory = GetDirectory(chapterPath);

        foreach (var entry in archive.Entries)
        {
            var relative = MakeRelativePath(chapterDirectory, entry.FullName);
            if (relative == null)
            {
                continue;
            }

            var contentType = GetImageContentType(entry.FullName);
            if (contentType == null)
            {
                continue;
            }

            if (!html.Contains(relative, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var dataUri = "data:" + contentType + ";base64," +
                          Convert.ToBase64String(memory.ToArray());

            html = html.Replace(relative, dataUri, StringComparison.Ordinal);
        }

        return html;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(NormalizePath(path))
                    ?? throw new InvalidDataException(
                        $"EPUB не содержит обязательный файл: {path}");

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string CombinePath(string directory, string path)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return NormalizePath(path);
        }

        var segments = (directory + "/" + path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

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

        return string.Join("/", stack.Reverse());
    }

    private static string GetDirectory(string path)
    {
        var normalized = NormalizePath(path);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string? MakeRelativePath(string directory, string fullPath)
    {
        var baseUri = new Uri(
            "http://epub.local/" +
            (string.IsNullOrEmpty(directory) ? string.Empty : NormalizePath(directory) + "/"));

        var targetUri = new Uri("http://epub.local/" + NormalizePath(fullPath));
        var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString());

        return relative.StartsWith("../", StringComparison.Ordinal)
            ? null
            : relative;
    }

    private static string? GetImageContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private sealed record ManifestItem(
        string Href,
        string MediaType,
        string Properties);
}
