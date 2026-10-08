using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using MYBOOK.Documents;
using MYBOOK.Services;

namespace MYBOOK.Formats.Docx;

/// <summary>
/// Читает DOCX напрямую как ZIP/OpenXML-пакет средствами стандартной библиотеки .NET,
/// включая document relationships, внешние hyperlinks и внутренние Word bookmarks.
/// </summary>
internal static class DocxDocumentReader
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Загружает word/document.xml и преобразует абзацы и базовое форматирование
    /// в нейтральную модель MYBOOK без DocumentFormat.OpenXml.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        ParserTrace.Write(
            "docx",
            $"entries={archive.Entries.Count}");

        var documentEntry = archive.GetEntry("word/document.xml")
                            ?? throw new InvalidDataException(
                                "DOCX не содержит word/document.xml.");

        XDocument xml;
        using (var stream = documentEntry.Open())
        {
            xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var body = xml.Root?.Element(W + "body")
                   ?? throw new InvalidDataException(
                       "DOCX не содержит основной части документа.");

        var relationships = ReadRelationships(
            archive);

        var paragraphs = body.Elements(W + "p").ToArray();

        ParserTrace.Write(
            "docx",
            $"document-bytes={documentEntry.Length} paragraphs={paragraphs.Length}");

        var blocks = new List<DocumentBlock>();

        foreach (var paragraph in paragraphs)
        {
            var model = ReadParagraph(
                paragraph,
                relationships);
            if (model.Inlines.Count > 0)
            {
                blocks.Add(model);
            }
        }

        ParserTrace.Write(
            "docx",
            $"blocks={blocks.Count}");

        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = blocks
        };
    }

    private static DocumentParagraph ReadParagraph(
        XElement paragraph,
        IReadOnlyDictionary<string, DocxRelationship> relationships)
    {
        var styleId = paragraph
            .Element(W + "pPr")?
            .Element(W + "pStyle")?
            .Attribute(W + "val")?
            .Value;

        var anchorId = paragraph
            .Descendants(W + "bookmarkStart")
            .Select(bookmark =>
                bookmark.Attribute(W + "name")?.Value)
            .FirstOrDefault(name =>
                !string.IsNullOrWhiteSpace(name) &&
                !string.Equals(
                    name,
                    "_GoBack",
                    StringComparison.OrdinalIgnoreCase));

        var inlines = new List<DocumentInline>();

        foreach (var node in paragraph.Descendants())
        {
            var hyperlink = node
                .Ancestors(W + "hyperlink")
                .FirstOrDefault();

            var linkHref = ResolveHyperlink(
                hyperlink,
                relationships);

            if (node.Name == W + "t")
            {
                var run = node
                    .Ancestors(W + "r")
                    .FirstOrDefault();

                inlines.Add(
                    ReadRun(
                        node.Value,
                        run,
                        linkHref));
            }
            else if (node.Name == W + "tab")
            {
                inlines.Add(new DocumentInline
                {
                    Text = "\t",
                    LinkHref = linkHref
                });
            }
            else if (node.Name == W + "br" ||
                     node.Name == W + "cr")
            {
                inlines.Add(new DocumentInline
                {
                    Text = Environment.NewLine,
                    LinkHref = linkHref
                });
            }
        }

        return new DocumentParagraph
        {
            AnchorId = anchorId,
            HeadingLevel = GetHeadingLevel(styleId),
            Inlines = MergeAdjacent(inlines)
        };
    }

    private static string? ResolveHyperlink(
        XElement? hyperlink,
        IReadOnlyDictionary<string, DocxRelationship> relationships)
    {
        if (hyperlink == null)
        {
            return null;
        }

        var anchor = hyperlink
            .Attribute(W + "anchor")?
            .Value
            .Trim();

        if (!string.IsNullOrWhiteSpace(anchor))
        {
            return "#" + anchor;
        }

        var relationshipId = hyperlink
            .Attribute(R + "id")?
            .Value;

        if (string.IsNullOrWhiteSpace(
                relationshipId) ||
            !relationships.TryGetValue(
                relationshipId,
                out var relationship) ||
            !relationship.External)
        {
            return null;
        }

        if (!Uri.TryCreate(
                relationship.Target,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static DocumentInline ReadRun(
        string text,
        XElement? run,
        string? linkHref)
    {
        var properties = run?.Element(W + "rPr");

        return new DocumentInline
        {
            Text = text,
            LinkHref = linkHref,
            Bold = IsEnabled(properties?.Element(W + "b")),
            Italic = IsEnabled(properties?.Element(W + "i")),
            Underline = IsUnderline(properties?.Element(W + "u")),
            Superscript = string.Equals(
                properties?.Element(W + "vertAlign")?.Attribute(W + "val")?.Value,
                "superscript",
                StringComparison.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<DocumentInline> MergeAdjacent(List<DocumentInline> source)
    {
        var result = new List<DocumentInline>();

        foreach (var item in source)
        {
            if (item.Text.Length == 0)
            {
                continue;
            }

            if (result.Count > 0)
            {
                var previous = result[^1];
                if (SameStyle(previous, item))
                {
                    result[^1] = new DocumentInline
                    {
                        Text = previous.Text + item.Text,
                        LinkHref = previous.LinkHref,
                        Bold = previous.Bold,
                        Italic = previous.Italic,
                        Underline = previous.Underline,
                        Superscript = previous.Superscript,
                        Code = previous.Code
                    };
                    continue;
                }
            }

            result.Add(item);
        }

        return result;
    }

    private static bool SameStyle(DocumentInline left, DocumentInline right)
    {
        return string.Equals(
                   left.LinkHref,
                   right.LinkHref,
                   StringComparison.Ordinal) &&
               left.Bold == right.Bold &&
               left.Italic == right.Italic &&
               left.Underline == right.Underline &&
               left.Superscript == right.Superscript &&
               left.Code == right.Code;
    }

    private static bool IsEnabled(XElement? element)
    {
        if (element == null)
        {
            return false;
        }

        var value = element.Attribute(W + "val")?.Value;
        return value == null ||
               (!string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderline(XElement? element)
    {
        if (element == null)
        {
            return false;
        }

        var value = element.Attribute(W + "val")?.Value;
        return !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, DocxRelationship> ReadRelationships(
        ZipArchive archive)
    {
        var entry = archive.GetEntry(
            "word/_rels/document.xml.rels");

        if (entry == null)
        {
            return new Dictionary<string, DocxRelationship>(
                StringComparer.Ordinal);
        }

        XDocument xml;

        using (var stream = entry.Open())
        {
            xml = XDocument.Load(
                stream,
                LoadOptions.PreserveWhitespace);
        }

        var result = new Dictionary<string, DocxRelationship>(
            StringComparer.Ordinal);

        foreach (var relationship in xml
                     .Root?
                     .Elements(PackageRelationships + "Relationship")
                 ?? Enumerable.Empty<XElement>())
        {
            var id = relationship
                .Attribute("Id")?
                .Value;

            var target = relationship
                .Attribute("Target")?
                .Value;

            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var targetMode = relationship
                .Attribute("TargetMode")?
                .Value;

            result[id] = new DocxRelationship(
                target,
                string.Equals(
                    targetMode,
                    "External",
                    StringComparison.OrdinalIgnoreCase));
        }

        ParserTrace.Write(
            "docx",
            $"relationships={result.Count}");

        return result;
    }

    private static int GetHeadingLevel(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return 0;
        }

        var normalized = styleId.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized["Heading".Length..], out var level))
        {
            return Math.Clamp(level, 1, 6);
        }

        return 0;
    }

    private sealed record DocxRelationship(
        string Target,
        bool External);
}
