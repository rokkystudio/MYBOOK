namespace MYBOOK.Rendering;

/// <summary>
/// Представляет нейтральную цветовую палитру HTML/SVG-рендера документа
/// без зависимости от UI-фреймворка или платформенной темы приложения.
/// </summary>
internal sealed class DocumentRenderTheme
{
    public required string Background { get; init; }
    public required string Surface { get; init; }
    public required string Text { get; init; }
    public required string MutedText { get; init; }
    public required string Border { get; init; }
    public required string Primary { get; init; }
}
