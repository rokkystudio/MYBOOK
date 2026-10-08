using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Fb2;

/// <summary>
/// Читает FB2 и преобразует метаданные, обложку, текст, ссылки и внутренние якоря
/// в нейтральную модель MYBOOK.
/// </summary>
internal static class Fb2DocumentReader
{
    private static readonly XNamespace XLinkNamespace = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// Загружает FB2-файл и возвращает нейтральную модель документа.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = xml.Root
                   ?? throw new InvalidDataException("FB2-документ не содержит корневого элемента.");

        var titleInfo = Child(Child(root, "description"), "title-info");
        var title = Value(Child(titleInfo, "book-title"));

        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(path);
        }

        return new DocumentModel
        {
            Title = title,
            Author = ReadAuthors(titleInfo),
            Cover = ReadCover(root, titleInfo),
            Blocks = ReadBlocks(root)
        };
    }

    private static DocumentImage? ReadCover(XElement root, XElement? titleInfo)
    {
        var image = Child(Child(titleInfo, "coverpage"), "image");
        var href = image?.Attribute(XLinkNamespace + "href")?.Value;

        if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        var id = href[1..];
        var binary = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "binary" &&
            string.Equals((string?)element.Attribute("id"), id, StringComparison.Ordinal));

        if (binary == null)
        {
            return null;
        }

        var base64 = string.Concat(binary.Value.Where(character => !char.IsWhiteSpace(character)));
        if (base64.Length == 0)
        {
            return null;
        }

        return new DocumentImage
        {
            Data = Convert.FromBase64String(base64),
            ContentType = NormalizeContentType((string?)binary.Attribute("content-type"))
        };
    }

    private static IReadOnlyList<DocumentBlock> ReadBlocks(XElement root)
    {
        var blocks = new List<DocumentBlock>();
        var assignedSectionAnchors = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var body in root.Elements().Where(element => element.Name.LocalName == "body"))
        {
            var isNoteBody = string.Equals(
                (string?)body.Attribute("name"),
                "notes",
                StringComparison.OrdinalIgnoreCase);

            foreach (var element in body.Descendants())
            {
                if (element.Name.LocalName == "title")
                {
                    var inlines = new List<DocumentInline>();
                    var paragraphs = element.Elements().Where(child => child.Name.LocalName == "p").ToArray();

                    for (var index = 0; index < paragraphs.Length; index++)
                    {
                        if (index > 0)
                        {
                            inlines.Add(new DocumentInline { Text = Environment.NewLine });
                        }

                        inlines.AddRange(ReadInlines(paragraphs[index]));
                    }

                    if (inlines.Count > 0)
                    {
                        blocks.Add(new DocumentParagraph
                        {
                            AnchorId = ResolveAnchorId(
                                element,
                                assignedSectionAnchors),
                            HeadingLevel = 1,
                            Inlines = inlines,
                            IsNote = isNoteBody
                        });
                    }

                    continue;
                }

                if (element.Name.LocalName != "p" ||
                    element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "title"))
                {
                    continue;
                }

                var paragraph = ReadInlines(element);
                if (paragraph.Count > 0)
                {
                    blocks.Add(new DocumentParagraph
                    {
                        AnchorId = ResolveAnchorId(
                            element,
                            assignedSectionAnchors),
                        Inlines = paragraph,
                        IsNote = isNoteBody
                    });
                }
            }
        }

        return blocks;
    }

    private static IReadOnlyList<DocumentInline> ReadInlines(XElement paragraph)
    {
        var result = new List<DocumentInline>();

        AppendNodes(
            paragraph.Nodes(),
            result,
            italic: false,
            superscript: false,
            linkHref: null);

        if (result.Count > 0)
        {
            result[0] = CloneWithText(result[0], result[0].Text.TrimStart());
            var last = result.Count - 1;
            result[last] = CloneWithText(result[last], result[last].Text.TrimEnd());
            result.RemoveAll(inline => inline.Text.Length == 0);
        }

        return result;
    }

    private static void AppendNodes(
        IEnumerable<XNode> nodes,
        List<DocumentInline> result,
        bool italic,
        bool superscript,
        string? linkHref)
    {
        foreach (var node in nodes)
        {
            if (node is XText textNode)
            {
                var text = Regex.Replace(textNode.Value, @"\s+", " ");
                if (text.Length > 0)
                {
                    AppendInline(
                        result,
                        text,
                        italic,
                        superscript,
                        linkHref);
                }

                continue;
            }

            if (node is XElement element)
            {
                var nestedLinkHref =
                    element.Name.LocalName == "a"
                        ? element.Attribute(
                            XLinkNamespace + "href")?.Value
                        : linkHref;

                AppendNodes(
                    element.Nodes(),
                    result,
                    italic || element.Name.LocalName == "emphasis",
                    superscript || element.Name.LocalName == "sup",
                    nestedLinkHref);
            }
        }
    }

    private static void AppendInline(
        List<DocumentInline> result,
        string text,
        bool italic,
        bool superscript,
        string? linkHref)
    {
        if (result.Count > 0)
        {
            var previous = result[^1];
            if (previous.Italic == italic &&
                previous.Superscript == superscript &&
                string.Equals(
                    previous.LinkHref,
                    linkHref,
                    StringComparison.Ordinal) &&
                !previous.Bold &&
                !previous.Underline &&
                !previous.Code)
            {
                result[^1] = CloneWithText(previous, previous.Text + text);
                return;
            }
        }

        result.Add(new DocumentInline
        {
            Text = text,
            LinkHref = linkHref,
            Italic = italic,
            Superscript = superscript
        });
    }

    private static DocumentInline CloneWithText(DocumentInline source, string text)
    {
        return new DocumentInline
        {
            Text = text,
            LinkHref = source.LinkHref,
            Bold = source.Bold,
            Italic = source.Italic,
            Underline = source.Underline,
            Superscript = source.Superscript,
            Code = source.Code
        };
    }

    private static string? ResolveAnchorId(
        XElement element,
        HashSet<string> assignedSectionAnchors)
    {
        var ownId =
            ((string?)element.Attribute("id"))?
            .Trim();

        if (!string.IsNullOrWhiteSpace(ownId))
        {
            return ownId;
        }

        foreach (var section in element.Ancestors().Where(
                     ancestor =>
                         ancestor.Name.LocalName == "section"))
        {
            var sectionId =
                ((string?)section.Attribute("id"))?
                .Trim();

            if (string.IsNullOrWhiteSpace(sectionId) ||
                !assignedSectionAnchors.Add(sectionId))
            {
                continue;
            }

            return sectionId;
        }

        return null;
    }

    private static string ReadAuthors(XElement? titleInfo)
    {
        if (titleInfo == null)
        {
            return string.Empty;
        }

        return string.Join(", ", titleInfo.Elements()
            .Where(element => element.Name.LocalName == "author")
            .Select(author => string.Join(" ", new[]
            {
                Value(Child(author, "first-name")),
                Value(Child(author, "middle-name")),
                Value(Child(author, "last-name"))
            }.Where(part => !string.IsNullOrWhiteSpace(part))))
            .Where(author => !string.IsNullOrWhiteSpace(author)));
    }

    private static XElement? Child(XElement? parent, string localName)
    {
        return parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
    }

    private static string Value(XElement? element)
    {
        return element?.Value.Trim() ?? string.Empty;
    }

    private static string NormalizeContentType(string? value)
    {
        return string.Equals(value, "image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : value ?? "application/octet-stream";
    }
}
