using System.IO;
using MYBOOK.Documents;
using MYBOOK.Formats.Doc;
using MYBOOK.Formats.Docx;
using MYBOOK.Formats.Epub;
using MYBOOK.Formats.Fb2;
using MYBOOK.Formats.Markdown;
using MYBOOK.Formats.Pdf;
using MYBOOK.Formats.Rtf;
using MYBOOK.Formats.Text;

namespace MYBOOK.Services;

/// <summary>
/// Единственный реестр форматов документов, которые MYBOOK умеет открывать.
/// Хранит расширение, отображаемое имя, Windows ProgID и способ чтения.
/// </summary>
internal static class SupportedFormatRegistry
{
    private static readonly IReadOnlyList<SupportedDocumentFormat> Formats =
    [
        new(".fb2", "FictionBook 2", "MYBOOK.Fb2File", Fb2DocumentReader.Read),
        new(".epub", "EPUB publication", "MYBOOK.EpubFile", EpubDocumentReader.Read),
        new(".html", "HTML document", "MYBOOK.HtmlFile", Reader: null, OpenDirectlyInWebView: true),
        new(".htm", "HTML document", "MYBOOK.HtmFile", Reader: null, OpenDirectlyInWebView: true),
        new(".txt", "Text document", "MYBOOK.TxtFile", TextDocumentReader.Read),
        new(".md", "Markdown document", "MYBOOK.MarkdownFile", MarkdownDocumentReader.Read),
        new(".rtf", "Rich Text Format document", "MYBOOK.RtfFile", RtfDocumentReader.Read),
        new(".doc", "Microsoft Word document", "MYBOOK.DocFile", ReadDocCompatibleDocument),
        new(".docx", "Word OpenXML document", "MYBOOK.DocxFile", DocxDocumentReader.Read),
        new(".pdf", "PDF document", "MYBOOK.PdfFile", PdfDocumentReader.Read)
    ];

    private static readonly IReadOnlyDictionary<string, SupportedDocumentFormat> FormatsByExtension =
        Formats.ToDictionary(
            format => format.Extension,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Возвращает форматы в стабильном порядке, используемом интерфейсом и настройками.
    /// </summary>
    public static IReadOnlyList<SupportedDocumentFormat> All => Formats;

    /// <summary>
    /// Возвращает описание формата по расширению или выдаёт явную ошибку.
    /// </summary>
    public static SupportedDocumentFormat GetByExtension(string extension)
    {
        if (FormatsByExtension.TryGetValue(
                NormalizeExtension(extension),
                out var format))
        {
            return format;
        }

        throw new InvalidDataException(
            $"Формат {extension} не поддерживается MYBOOK.");
    }

    /// <summary>
    /// Определяет зарегистрированный формат для пути документа.
    /// </summary>
    public static SupportedDocumentFormat GetByPath(string path)
    {
        return GetByExtension(
            Path.GetExtension(path));
    }

    /// <summary>
    /// Читает документ через reader, зарегистрированный для его расширения.
    /// Direct HTML/HTM не проходят через нейтральную модель.
    /// </summary>
    public static DocumentModel ReadDocument(string path)
    {
        var format = GetByPath(path);

        if (format.Reader == null)
        {
            throw new InvalidOperationException(
                $"Формат {format.Extension} открывается напрямую и не имеет DocumentModel reader.");
        }

        return format.Reader(path);
    }

    /// <summary>
    /// Строит фильтр OpenFileDialog из того же реестра,
    /// который используется readers и файловыми ассоциациями.
    /// </summary>
    public static string BuildOpenFileDialogFilter(string supportedDocumentsLabel)
    {
        var allPatterns = string.Join(
            ';',
            Formats.Select(format => "*" + format.Extension));

        var parts = new List<string>
        {
            $"{supportedDocumentsLabel} ({allPatterns})",
            allPatterns
        };

        foreach (var format in Formats)
        {
            parts.Add(
                $"{format.DisplayName} (*{format.Extension})");
            parts.Add(
                "*" + format.Extension);
        }

        return string.Join(
            '|',
            parts);
    }

    /// <summary>
    /// Открывает .doc по фактической сигнатуре:
    /// Word Binary/CFB собственным DOC reader или RTF собственным RTF reader.
    /// </summary>
    private static DocumentModel ReadDocCompatibleDocument(string path)
    {
        Span<byte> header = stackalloc byte[8];

        using var stream = File.OpenRead(path);
        var read = stream.Read(header);

        if (read >= 8 &&
            header[..8].SequenceEqual(new byte[]
            {
                0xD0, 0xCF, 0x11, 0xE0,
                0xA1, 0xB1, 0x1A, 0xE1
            }))
        {
            return DocDocumentReader.Read(path);
        }

        if (read >= 5 &&
            header[..5].SequenceEqual("{\\rtf"u8))
        {
            return RtfDocumentReader.Read(path);
        }

        throw new InvalidDataException(
            "Файл .doc не является Word Binary/CFB или RTF-документом.");
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith(
            ".",
            StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : "." + extension.ToLowerInvariant();
    }
}

/// <summary>
/// Описывает один поддерживаемый формат MYBOOK.
/// </summary>
internal sealed record SupportedDocumentFormat(
    string Extension,
    string DisplayName,
    string ProgId,
    Func<string, DocumentModel>? Reader,
    bool OpenDirectlyInWebView = false);
