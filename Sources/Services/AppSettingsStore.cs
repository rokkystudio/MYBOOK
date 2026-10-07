using System;
using System.IO;
using System.Text.Json;

namespace MYBOOK.Services;

/// <summary>
/// Загружает и сохраняет пользовательские настройки MYBOOK
/// в каталоге локальных данных текущего пользователя.
/// </summary>
internal sealed class AppSettingsStore
{
    private readonly string path_;

    /// <summary>
    /// Инициализирует хранилище настроек в %LOCALAPPDATA%\MYBOOK\settings.json.
    /// </summary>
    public AppSettingsStore()
    {
        path_ = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MYBOOK",
            "settings.json");
    }

    /// <summary>
    /// Возвращает сохранённые настройки или значения по умолчанию,
    /// если файл настроек ещё не существует.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(path_))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path_);
        return JsonSerializer.Deserialize<AppSettings>(json)
               ?? throw new InvalidDataException($"Не удалось прочитать настройки: {path_}");
    }

    /// <summary>
    /// Сохраняет текущие настройки интерфейса в локальный JSON-файл.
    /// </summary>
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path_)
                        ?? throw new InvalidOperationException($"Не удалось определить каталог настроек: {path_}");

        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path_, json);
    }
}
