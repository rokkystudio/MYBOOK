using System.Collections.Generic;
using Book.Rendering;

namespace MYBOOK.Services;

/// <summary>
/// Хранит пользовательские настройки интерфейса и потоковой типографики,
/// каталог последней книги и список недавно открытых документов MYBOOK.
/// </summary>
internal sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "auto";
    public DocumentReadingTypography ReadingTypography { get; set; } =
        new();
    public string LastBookDirectory { get; set; } = string.Empty;
    public List<string> RecentDocuments { get; set; } = new();
}
