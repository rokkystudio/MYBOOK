using System;
using System.IO;
using System.Text.Json;

namespace MYBOOK.Services;

/// <summary>
/// Загружает и атомарно сохраняет настройки MYBOOK, включая
/// типографику и историю позиций чтения, в локальный settings.json.
/// </summary>
internal sealed class AppSettingsStore
{
    private readonly string path_;

    /// <summary>
    /// Инициализирует хранилище настроек в %LOCALAPPDATA%\MYBOOK\settings.json.
    /// </summary>
    public AppSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MYBOOK",
                "settings.json"))
    {
    }

    /// <summary>
    /// Использует заданный полный путь JSON-файла для изолированного
    /// хранилища настроек, включая regression fixtures.
    /// </summary>
    internal AppSettingsStore(
        string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException(
                "Путь к settings.json не задан.",
                nameof(settingsPath));
        }

        path_ = Path.GetFullPath(
            settingsPath);
    }

    /// <summary>
    /// Возвращает настройки из локального JSON либо defaults, если файл
    /// отсутствует. Типографика и история позиций проходят строгую
    /// валидацию; некорректное содержимое не заменяется defaults.
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
        ReadingPositionService.Validate(
            settings.ReadingPositions);
        return settings;
    }

    /// <summary>
    /// Проверяет типографику и историю позиций и сохраняет весь settings
    /// JSON атомарным rename временного файла в том же каталоге.
    /// Частично записанный JSON не заменяет рабочие настройки.
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
        ReadingPositionService.Validate(
            settings.ReadingPositions);

        var directory = Path.GetDirectoryName(path_)
                        ?? throw new InvalidOperationException($"Не удалось определить каталог настроек: {path_}");

        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var temporaryPath =
            path_ + ".tmp";

        File.WriteAllText(
            temporaryPath,
            json);
        File.Move(
            temporaryPath,
            path_,
            overwrite: true);
    }
}
