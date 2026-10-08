using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using MYBOOK.Documents;
using MYBOOK.Services;

namespace MYBOOK.Formats.Epub;

/// <summary>
/// Читает EPUB напрямую как ZIP-контейнер, разбирает container.xml, OPF manifest/spine,
/// объединяет XHTML-главы, нормализует anchors/relative links, читает EPUB3 navigation/EPUB2 NCX,
/// landmarks/page-list и семантику сносок, а также безопасно подключает scoped CSS
/// и встроенные image/SVG resources без внешних загрузок.
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

        ParserTrace.Write(
            "epub",
            $"entries={archive.Entries.Count}");

        var opfPath = ReadPackagePath(archive);

        ParserTrace.Write(
            "epub",
            $"package={opfPath}");

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

        ParserTrace.Write(
            "epub",
            $"manifest-items={items.Count}");

        var baseDirectory = GetDirectory(opfPath);
        var blocks = new List<DocumentBlock>();

        var chapters = ReadSpineChapters(
            archive,
            package,
            items,
            baseDirectory);

        var chapterAnchors = chapters.ToDictionary(
            chapter => chapter.Path,
            chapter => chapter.AnchorId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var chapter in chapters)
        {
            ParserTrace.Write(
                "epub-spine",
                $"index={chapter.Index} path={chapter.Path}");

            RewriteChapterNavigation(
                chapter.Body,
                chapter.Path,
                chapter.Index,
                chapterAnchors);

            EpubResourceProcessor.RewriteChapterResources(
                archive,
                chapter.Path,
                chapter.Body);

            var chapterCss = EpubResourceProcessor.ReadChapterCss(
                archive,
                chapter.Path,
                chapter.Head,
                chapter.AnchorId);

            var html = string.Concat(
                chapter.Body.Nodes().Select(
                    node => node.ToString(
                        SaveOptions.DisableFormatting)));

            if (!string.IsNullOrWhiteSpace(html))
            {
                blocks.Add(new DocumentHtmlBlock
                {
                    Html =
                        (chapterCss.Length == 0
                            ? string.Empty
                            : "<style>" + chapterCss + "</style>") +
                        "<section class=\"epub-chapter\" id=\"" +
                        chapter.AnchorId +
                        "\">" +
                        html +
                        "</section>"
                });
            }
        }

        var spineItemCount = chapters.Count;

        var outlines = ReadNavigationOutlines(
            archive,
            package,
            items,
            baseDirectory,
            chapterAnchors);

        var metadata = package.Element(OpfNs + "metadata");
        var title = metadata?.Elements(DcNs + "title").FirstOrDefault()?.Value?.Trim();
        var author = string.Join(", ",
            metadata?.Elements(DcNs + "creator")
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
            ?? Array.Empty<string>());

        ParserTrace.Write(
            "epub",
            $"spine-xhtml={spineItemCount} blocks={blocks.Count} outlines={outlines.Count} title={title ?? string.Empty}");

        return new DocumentModel
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(path)
                : title,
            Author = author,
            Cover = ReadCover(archive, package, items, baseDirectory),
            Blocks = blocks,
            Outlines = outlines
        };
    }

    private static IReadOnlyList<SpineChapter> ReadSpineChapters(
        ZipArchive archive,
        XElement package,
        IReadOnlyDictionary<string, ManifestItem> items,
        string baseDirectory)
    {
        var result = new List<SpineChapter>();
        var spine = package.Element(OpfNs + "spine");

        if (spine == null)
        {
            return result;
        }

        foreach (var itemRef in spine.Elements(OpfNs + "itemref"))
        {
            var idRef = itemRef.Attribute("idref")?.Value;

            if (string.IsNullOrWhiteSpace(idRef) ||
                !items.TryGetValue(idRef, out var item) ||
                !item.MediaType.Contains(
                    "xhtml",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var chapterPath = CombinePath(
                baseDirectory,
                item.Href);

            var chapter = LoadXml(
                archive,
                chapterPath);

            var body = chapter
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "body");

            if (body == null)
            {
                continue;
            }

            var head = chapter
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "head");

            var index = result.Count + 1;

            result.Add(new SpineChapter(
                index,
                chapterPath,
                $"epub-chapter-{index}",
                head,
                body));
        }

        return result;
    }

    private static void RewriteChapterNavigation(
        XElement body,
        string chapterPath,
        int chapterIndex,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        foreach (var element in body.DescendantsAndSelf())
        {
            var semanticTypes = element
                .Attributes()
                .FirstOrDefault(attribute =>
                    string.Equals(
                        attribute.Name.LocalName,
                        "type",
                        StringComparison.OrdinalIgnoreCase))?
                .Value
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                ?? Array.Empty<string>();

            if (semanticTypes.Contains(
                    "footnote",
                    StringComparer.OrdinalIgnoreCase) ||
                semanticTypes.Contains(
                    "endnote",
                    StringComparer.OrdinalIgnoreCase))
            {
                AddCssClass(
                    element,
                    "epub-note");
            }

            if (semanticTypes.Contains(
                    "noteref",
                    StringComparer.OrdinalIgnoreCase))
            {
                AddCssClass(
                    element,
                    "epub-noteref");
            }

            if (semanticTypes.Contains(
                    "pagebreak",
                    StringComparer.OrdinalIgnoreCase))
            {
                AddCssClass(
                    element,
                    "epub-pagebreak");
            }

            var id = element.Attribute("id")?.Value;

            if (string.IsNullOrWhiteSpace(id) &&
                string.Equals(
                    element.Name.LocalName,
                    "a",
                    StringComparison.OrdinalIgnoreCase))
            {
                id = element.Attribute("name")?.Value;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                element.SetAttributeValue(
                    "id",
                    BuildChapterElementAnchor(
                        chapterIndex,
                        id));
            }
        }

        foreach (var anchor in body
                     .Descendants()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "a",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var hrefAttribute = anchor.Attribute("href");

            if (hrefAttribute == null ||
                string.IsNullOrWhiteSpace(hrefAttribute.Value))
            {
                continue;
            }

            var rewritten = RewriteChapterHref(
                hrefAttribute.Value,
                chapterPath,
                chapterAnchors);

            if (rewritten == null)
            {
                hrefAttribute.Remove();
            }
            else
            {
                hrefAttribute.Value = rewritten;
            }
        }
    }

    private static void AddCssClass(
        XElement element,
        string className)
    {
        var current = element.Attribute("class")?
            .Value
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .ToList()
            ?? new List<string>();

        if (!current.Contains(
                className,
                StringComparer.Ordinal))
        {
            current.Add(className);
        }

        element.SetAttributeValue(
            "class",
            string.Join(
                " ",
                current));
    }

    private static string? RewriteChapterHref(
        string href,
        string chapterPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var trimmed = href.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (Uri.TryCreate(
                trimmed,
                UriKind.Absolute,
                out var absolute))
        {
            return absolute.Scheme switch
            {
                "http" or "https" or "mailto" =>
                    absolute.AbsoluteUri,
                _ => null
            };
        }

        var hashIndex = trimmed.IndexOf('#');
        var pathPart = hashIndex >= 0
            ? trimmed[..hashIndex]
            : trimmed;

        var fragment = hashIndex >= 0 &&
                       hashIndex + 1 < trimmed.Length
            ? Uri.UnescapeDataString(
                trimmed[(hashIndex + 1)..])
            : null;

        var queryIndex = pathPart.IndexOf('?');

        if (queryIndex >= 0)
        {
            pathPart = pathPart[..queryIndex];
        }

        var targetPath = pathPart.Length == 0
            ? NormalizePath(chapterPath)
            : CombinePath(
                GetDirectory(chapterPath),
                Uri.UnescapeDataString(pathPart));

        if (!chapterAnchors.TryGetValue(
                targetPath,
                out var chapterAnchor))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(fragment))
        {
            return "#" + chapterAnchor;
        }

        var chapterIndex = ParseChapterIndex(
            chapterAnchor);

        return "#" +
               BuildChapterElementAnchor(
                   chapterIndex,
                   fragment);
    }

    private static string BuildChapterElementAnchor(
        int chapterIndex,
        string sourceId)
    {
        return $"epub-chapter-{chapterIndex}-" +
               Uri.EscapeDataString(
                   sourceId.Trim());
    }

    private static int ParseChapterIndex(string chapterAnchor)
    {
        const string prefix = "epub-chapter-";

        if (!chapterAnchor.StartsWith(
                prefix,
                StringComparison.Ordinal) ||
            !int.TryParse(
                chapterAnchor[prefix.Length..],
                out var index) ||
            index <= 0)
        {
            throw new InvalidDataException(
                $"Некорректный EPUB chapter anchor: {chapterAnchor}");
        }

        return index;
    }

    private static IReadOnlyList<DocumentOutlineItem> ReadNavigationOutlines(
        ZipArchive archive,
        XElement package,
        IReadOnlyDictionary<string, ManifestItem> items,
        string baseDirectory,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var tocOutlines = Array.Empty<DocumentOutlineItem>();
        var supplemental = new List<DocumentOutlineItem>();
        var hasNavigationLandmarks = false;

        var navigationItem = items.Values.FirstOrDefault(item =>
            item.Properties
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Contains(
                    "nav",
                    StringComparer.OrdinalIgnoreCase));

        if (navigationItem != null)
        {
            var navPath = CombinePath(
                baseDirectory,
                navigationItem.Href);

            var navigation = LoadXml(
                archive,
                navPath);

            var navigationElements = navigation
                .Descendants()
                .Where(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "nav",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var toc = navigationElements.FirstOrDefault(element =>
                HasNavigationType(
                    element,
                    "toc"));

            var tocList = GetNavigationList(
                toc);

            if (tocList != null)
            {
                tocOutlines = ReadNavigationList(
                        tocList,
                        navPath,
                        chapterAnchors)
                    .ToArray();
            }

            hasNavigationLandmarks = navigationElements.Any(element =>
                HasNavigationType(
                    element,
                    "landmarks"));

            AddSupplementalNavigationGroup(
                supplemental,
                navigationElements,
                "landmarks",
                "Landmarks",
                navPath,
                chapterAnchors);

            AddSupplementalNavigationGroup(
                supplemental,
                navigationElements,
                "page-list",
                "Pages",
                navPath,
                chapterAnchors);

            ParserTrace.Write(
                "epub-nav",
                $"source=nav path={navPath} toc={tocOutlines.Length} supplemental={supplemental.Count}");
        }

        if (!hasNavigationLandmarks)
        {
            var guide = ReadOpfGuideGroup(
                package,
                baseDirectory,
                chapterAnchors);

            if (guide != null)
            {
                supplemental.Add(guide);
            }
        }

        IReadOnlyList<DocumentOutlineItem> primary =
            tocOutlines.Length > 0
                ? tocOutlines
                : ReadNcxOutlines(
                    archive,
                    package,
                    items,
                    baseDirectory,
                    chapterAnchors);

        if (supplemental.Count == 0)
        {
            return primary;
        }

        return primary
            .Concat(supplemental)
            .ToArray();
    }

    private static bool HasNavigationType(
        XElement navigation,
        string type)
    {
        return navigation
            .Attributes()
            .Where(attribute =>
                string.Equals(
                    attribute.Name.LocalName,
                    "type",
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(attribute =>
                attribute.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .Contains(
                type,
                StringComparer.OrdinalIgnoreCase);
    }

    private static XElement? GetNavigationList(
        XElement? navigation)
    {
        return navigation?
            .Elements()
            .FirstOrDefault(element =>
                string.Equals(
                    element.Name.LocalName,
                    "ol",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static void AddSupplementalNavigationGroup(
        List<DocumentOutlineItem> result,
        IReadOnlyList<XElement> navigationElements,
        string type,
        string fallbackTitle,
        string ownerPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var navigation = navigationElements.FirstOrDefault(element =>
            HasNavigationType(
                element,
                type));

        var list = GetNavigationList(
            navigation);

        if (navigation == null ||
            list == null)
        {
            return;
        }

        var children = ReadNavigationList(
            list,
            ownerPath,
            chapterAnchors);

        if (children.Count == 0)
        {
            return;
        }

        var heading = navigation
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName is
                    "h1" or "h2" or "h3" or "h4" or "h5" or "h6");

        var title = NormalizeNavigationText(
            heading?.Value);

        result.Add(new DocumentOutlineItem
        {
            Title = title.Length == 0
                ? fallbackTitle
                : title,
            Children = children
        });
    }

    private static DocumentOutlineItem? ReadOpfGuideGroup(
        XElement package,
        string baseDirectory,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var guide = package.Element(OpfNs + "guide");

        if (guide == null)
        {
            return null;
        }

        var ownerPath = CombinePath(baseDirectory, "_package.opf");
        var children = new List<DocumentOutlineItem>();

        foreach (var reference in guide.Elements(OpfNs + "reference"))
        {
            var href = reference.Attribute("href")?.Value;
            var title = NormalizeNavigationText(reference.Attribute("title")?.Value);

            if (title.Length == 0)
            {
                title = NormalizeNavigationText(reference.Attribute("type")?.Value);
            }

            if (title.Length == 0)
            {
                continue;
            }

            var target = ResolveNavigationTarget(href, ownerPath, chapterAnchors);

            children.Add(new DocumentOutlineItem
            {
                Title = title,
                TargetAnchorId = target.AnchorId,
                Uri = target.Uri
            });
        }

        if (children.Count == 0)
        {
            return null;
        }

        return new DocumentOutlineItem
        {
            Title = "Landmarks",
            Children = children
        };
    }
    private static IReadOnlyList<DocumentOutlineItem> ReadNavigationList(
        XElement list,
        string ownerPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var result = new List<DocumentOutlineItem>();

        foreach (var item in list
                     .Elements()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "li",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var labelElement = item
                .Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName is "a" or "span");

            var nestedList = item
                .Elements()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "ol",
                        StringComparison.OrdinalIgnoreCase));

            var children = nestedList == null
                ? Array.Empty<DocumentOutlineItem>()
                : ReadNavigationList(
                    nestedList,
                    ownerPath,
                    chapterAnchors);

            var title = NormalizeNavigationText(
                labelElement?.Value);

            if (title.Length == 0)
            {
                result.AddRange(children);
                continue;
            }

            var href = labelElement != null &&
                       string.Equals(
                           labelElement.Name.LocalName,
                           "a",
                           StringComparison.OrdinalIgnoreCase)
                ? labelElement.Attribute("href")?.Value
                : null;

            var target = ResolveNavigationTarget(
                href,
                ownerPath,
                chapterAnchors);

            result.Add(new DocumentOutlineItem
            {
                Title = title,
                TargetAnchorId = target.AnchorId,
                Uri = target.Uri,
                Children = children
            });
        }

        return result;
    }

    private static IReadOnlyList<DocumentOutlineItem> ReadNcxOutlines(
        ZipArchive archive,
        XElement package,
        IReadOnlyDictionary<string, ManifestItem> items,
        string baseDirectory,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        ManifestItem? ncxItem = null;

        var tocId = package
            .Element(OpfNs + "spine")?
            .Attribute("toc")?
            .Value;

        if (!string.IsNullOrWhiteSpace(tocId) &&
            items.TryGetValue(
                tocId,
                out var referencedNcx))
        {
            ncxItem = referencedNcx;
        }

        ncxItem ??= items.Values.FirstOrDefault(item =>
            item.MediaType.Contains(
                "ncx",
                StringComparison.OrdinalIgnoreCase));

        if (ncxItem == null)
        {
            return Array.Empty<DocumentOutlineItem>();
        }

        var ncxPath = CombinePath(
            baseDirectory,
            ncxItem.Href);

        var ncx = LoadXml(
            archive,
            ncxPath);

        var result = new List<DocumentOutlineItem>();

        var navMap = ncx
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(
                    element.Name.LocalName,
                    "navMap",
                    StringComparison.OrdinalIgnoreCase));

        if (navMap != null)
        {
            result.AddRange(
                ReadNcxPoints(
                    navMap,
                    ncxPath,
                    chapterAnchors));
        }

        var pageList = ncx
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(
                    element.Name.LocalName,
                    "pageList",
                    StringComparison.OrdinalIgnoreCase));

        if (pageList != null)
        {
            var pages = ReadNcxPageTargets(
                pageList,
                ncxPath,
                chapterAnchors);

            if (pages.Count > 0)
            {
                var heading = NormalizeNavigationText(
                    pageList
                        .Elements()
                        .FirstOrDefault(element =>
                            string.Equals(
                                element.Name.LocalName,
                                "navLabel",
                                StringComparison.OrdinalIgnoreCase))?
                        .Descendants()
                        .FirstOrDefault(element =>
                            string.Equals(
                                element.Name.LocalName,
                                "text",
                                StringComparison.OrdinalIgnoreCase))?
                        .Value);

                result.Add(new DocumentOutlineItem
                {
                    Title = heading.Length == 0
                        ? "Pages"
                        : heading,
                    Children = pages
                });
            }
        }

        ParserTrace.Write(
            "epub-nav",
            $"source=ncx path={ncxPath} items={result.Count}");

        return result;
    }

    private static IReadOnlyList<DocumentOutlineItem> ReadNcxPageTargets(
        XElement pageList,
        string ownerPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var result = new List<DocumentOutlineItem>();

        foreach (var target in pageList
                     .Elements()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "pageTarget",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var title = NormalizeNavigationText(
                target
                    .Elements()
                    .FirstOrDefault(element =>
                        string.Equals(
                            element.Name.LocalName,
                            "navLabel",
                            StringComparison.OrdinalIgnoreCase))?
                    .Descendants()
                    .FirstOrDefault(element =>
                        string.Equals(
                            element.Name.LocalName,
                            "text",
                            StringComparison.OrdinalIgnoreCase))?
                    .Value);

            var href = target
                .Elements()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "content",
                        StringComparison.OrdinalIgnoreCase))?
                .Attribute("src")?
                .Value;

            if (title.Length == 0)
            {
                continue;
            }

            var resolved = ResolveNavigationTarget(
                href,
                ownerPath,
                chapterAnchors);

            result.Add(new DocumentOutlineItem
            {
                Title = title,
                TargetAnchorId = resolved.AnchorId,
                Uri = resolved.Uri
            });
        }

        return result;
    }

    private static IReadOnlyList<DocumentOutlineItem> ReadNcxPoints(
        XElement parent,
        string ownerPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        var result = new List<DocumentOutlineItem>();

        foreach (var point in parent
                     .Elements()
                     .Where(element =>
                         string.Equals(
                             element.Name.LocalName,
                             "navPoint",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var title = NormalizeNavigationText(
                point
                    .Elements()
                    .FirstOrDefault(element =>
                        string.Equals(
                            element.Name.LocalName,
                            "navLabel",
                            StringComparison.OrdinalIgnoreCase))?
                    .Descendants()
                    .FirstOrDefault(element =>
                        string.Equals(
                            element.Name.LocalName,
                            "text",
                            StringComparison.OrdinalIgnoreCase))?
                    .Value);

            var href = point
                .Elements()
                .FirstOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "content",
                        StringComparison.OrdinalIgnoreCase))?
                .Attribute("src")?
                .Value;

            var children = ReadNcxPoints(
                point,
                ownerPath,
                chapterAnchors);

            if (title.Length == 0)
            {
                result.AddRange(children);
                continue;
            }

            var target = ResolveNavigationTarget(
                href,
                ownerPath,
                chapterAnchors);

            result.Add(new DocumentOutlineItem
            {
                Title = title,
                TargetAnchorId = target.AnchorId,
                Uri = target.Uri,
                Children = children
            });
        }

        return result;
    }

    private static NavigationTarget ResolveNavigationTarget(
        string? href,
        string ownerPath,
        IReadOnlyDictionary<string, string> chapterAnchors)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return new NavigationTarget(
                null,
                null);
        }

        var rewritten = RewriteChapterHref(
            href,
            ownerPath,
            chapterAnchors);

        if (string.IsNullOrWhiteSpace(rewritten))
        {
            return new NavigationTarget(
                null,
                null);
        }

        if (rewritten.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            return new NavigationTarget(
                rewritten[1..],
                null);
        }

        return new NavigationTarget(
            null,
            rewritten);
    }

    private static string NormalizeNavigationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
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

    private sealed record NavigationTarget(
        string? AnchorId,
        string? Uri);

    private sealed record SpineChapter(
        int Index,
        string Path,
        string AnchorId,
        XElement? Head,
        XElement Body);

    private sealed record ManifestItem(
        string Href,
        string MediaType,
        string Properties);
}
