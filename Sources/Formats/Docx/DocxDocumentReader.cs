using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using MYBOOK.Documents;
using MYBOOK.Services;

namespace MYBOOK.Formats.Docx;

/// <summary>
/// Читает DOCX напрямую как ZIP/OpenXML-пакет средствами стандартной библиотеки .NET.
/// </summary>
internal static class DocxDocumentReader
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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

        var paragraphs = body.Elements(W + "p").ToArray();

        ParserTrace.Write(
            "docx",
            $"document-bytes={documentEntry.Length} paragraphs={paragraphs.Length}");

        var blocks = new List<DocumentBlock>();

        foreach (var paragraph in paragraphs)
        {
            var model = ReadParagraph(paragraph);
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

    private static DocumentParagraph ReadParagraph(XElement paragraph)
    {
        var styleId = paragraph
            .Element(W + "pPr")?
            .Element(W + "pStyle")?
            .Attribute(W + "val")?
            .Value;

        var inlines = new List<DocumentInline>();

        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "t")
            {
                var run = node.Ancestors(W + "r").FirstOrDefault();
                inlines.Add(ReadRun(node.Value, run));
            }
            else if (node.Name == W + "tab")
            {
                inlines.Add(new DocumentInline { Text = "\t" });
            }
            else if (node.Name == W + "br" || node.Name == W + "cr")
            {
                inlines.Add(new DocumentInline { Text = Environment.NewLine });
            }
        }

        return new DocumentParagraph
        {
            HeadingLevel = GetHeadingLevel(styleId),
            Inlines = MergeAdjacent(inlines)
        };
    }

    private static DocumentInline ReadRun(string text, XElement? run)
    {
        var properties = run?.Element(W + "rPr");

        return new DocumentInline
        {
            Text = text,
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
        return left.Bold == right.Bold &&
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
}
