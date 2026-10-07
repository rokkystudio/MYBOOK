namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Базовое представление синтаксического объекта PDF.
/// </summary>
internal abstract record PdfObject;

internal sealed record PdfNull : PdfObject;
internal sealed record PdfBoolean(bool Value) : PdfObject;
internal sealed record PdfNumber(double Value) : PdfObject;
internal sealed record PdfName(string Value) : PdfObject;
internal sealed record PdfString(byte[] Data) : PdfObject;
internal sealed record PdfArray(IReadOnlyList<PdfObject> Items) : PdfObject;
internal sealed record PdfDictionary(IReadOnlyDictionary<string, PdfObject> Items) : PdfObject;
internal sealed record PdfReference(int ObjectNumber, int Generation) : PdfObject;
internal sealed record PdfStream(PdfDictionary Dictionary, byte[] Data) : PdfObject;
