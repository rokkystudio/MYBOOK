namespace MYBOOK.Services;

/// <summary>
/// Хранит пользовательские настройки интерфейса и открытия книг MYBOOK.
/// </summary>
internal sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "auto";
    public string LastBookDirectory { get; set; } = string.Empty;
}
