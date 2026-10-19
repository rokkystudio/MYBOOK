using System.Collections.Generic;

namespace MYBOOK.Services;

/// <summary>
/// Хранит пользовательские настройки интерфейса, открытия книг
/// и список недавно открытых документов MYBOOK.
/// </summary>
internal sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "auto";
    public string LastBookDirectory { get; set; } = string.Empty;
    public List<string> RecentDocuments { get; set; } = new();
}
