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
    /// Возвращает настройки из локального JSON либо defaults, если файл
    /// отсутствует. Сохранённые значения типографики валидируются явно;
    /// некорректная версия settings.json не подменяется defaults.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(path_))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path_);
        var settings =
            JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new InvalidDataException(
                $"Не удалось прочитать настройки: {path_}");

        if (settings.ReadingTypography == null)
        {
            throw new InvalidDataException(
                "В настройках отсутствует объект типографики.");
        }

        settings.ReadingTypography.Validate();
        return settings;
    }

    /// <summary>
    /// Сохраняет настройки интерфейса и validated reading typography
    /// в локальный JSON-файл; некорректные значения не записываются.
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ReadingTypography == null)
        {
            throw new InvalidDataException(
                "В настройках отсутствует объект типографики.");
        }

        settings.ReadingTypography.Validate();

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
