using System.Collections.Generic;

namespace MYBOOK.Documents;

/// <summary>
/// Представляет документ в нейтральной модели MYBOOK независимо от исходного формата.
/// </summary>
internal sealed class DocumentModel
{
    public required string Title { get; init; }
    public string Author { get; init; } = string.Empty;
    public DocumentImage? Cover { get; init; }
    public required IReadOnlyList<DocumentBlock> Blocks { get; init; }
}

/// <summary>
/// Представляет базовый блочный элемент документа.
/// </summary>
internal abstract class DocumentBlock
{
}

/// <summary>
/// Представляет текстовый абзац или заголовок.
/// </summary>
internal sealed class DocumentParagraph : DocumentBlock
{
    public required IReadOnlyList<DocumentInline> Inlines { get; init; }
    public int HeadingLevel { get; init; }
    public bool Preformatted { get; init; }
}

/// <summary>
/// Представляет готовый фрагмент HTML, созданный доверенным внутренним преобразователем.
/// </summary>
internal sealed class DocumentHtmlBlock : DocumentBlock
{
    public required string Html { get; init; }
}

/// <summary>
/// Представляет фиксированную страницу документа с размерами в typographic points
/// и извлечённым текстовым слоем.
/// </summary>
internal sealed class DocumentFixedPage : DocumentBlock
{
    public required int PageNumber { get; init; }
    public required double WidthPoints { get; init; }
    public required double HeightPoints { get; init; }
    public int RotationDegrees { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<DocumentFixedTextRun> TextRuns { get; init; } =
        Array.Empty<DocumentFixedTextRun>();
}

/// <summary>
/// Представляет позиционированный текстовый фрагмент фиксированной страницы.
/// Координаты задаются в typographic points от верхнего левого угла страницы.
/// </summary>
internal sealed class DocumentFixedTextRun
{
    public required string Text { get; init; }
    public required double XPoints { get; init; }
    public required double YPoints { get; init; }
    public required double FontSizePoints { get; init; }
}

/// <summary>
/// Представляет текстовый фрагмент с базовым форматированием.
/// </summary>
internal sealed class DocumentInline
{
    public required string Text { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Superscript { get; init; }
    public bool Code { get; init; }
}

/// <summary>
/// Представляет встроенное изображение документа.
/// </summary>
internal sealed class DocumentImage
{
    public required byte[] Data { get; init; }
    public required string ContentType { get; init; }
}
