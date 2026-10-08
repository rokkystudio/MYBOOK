namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Представляет подготовленный PDF ExtGState resource с поддерживаемыми
/// параметрами line state и постоянной прозрачности.
/// </summary>
internal sealed class PdfExtGraphicsState
{
    public double? LineWidth { get; init; }
    public int? LineCap { get; init; }
    public int? LineJoin { get; init; }
    public double? MiterLimit { get; init; }
    public IReadOnlyList<double>? DashArray { get; init; }
    public double? DashPhase { get; init; }
    public double? StrokeAlpha { get; init; }
    public double? FillAlpha { get; init; }
}
