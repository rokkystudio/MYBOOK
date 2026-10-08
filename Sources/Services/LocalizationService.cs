using System;
using System.Collections.Generic;
using System.Globalization;

namespace MYBOOK.Services;

/// <summary>
/// Предоставляет русские и английские строки интерфейса
/// и поддерживает автоматический выбор языка по системной культуре.
/// </summary>
internal static class LocalizationService
{
    public const string AutomaticLanguage = "auto";
    public const string EnglishLanguage = "en";
    public const string RussianLanguage = "ru";

    private static readonly Dictionary<string, string> EnglishTexts = new()
    {
        { "app_name", "MYBOOK" },
        { "app_subtitle", "Document Reader" },
        { "open", "Open" },
        { "contents", "Contents" },
        { "tooltip_contents", "Show / hide contents" },
        { "settings", "Settings" },
        { "file_formats", "File formats" },
        { "file_formats_description", "Choose which supported extensions should be associated with MYBOOK for the current Windows user. Formats are never enabled automatically." },
        { "file_formats_status", "Changes are applied immediately. Windows UserChoice protection may still require confirmation in Default Apps." },
        { "windows_default_apps", "Open Windows Default Apps" },
        { "book_not_selected", "Open a supported document to start reading." },
        { "system_language", "System language - {0}" },
        { "tooltip_language", "Interface language" },
        { "tooltip_theme", "Switch theme" },
        { "tooltip_close", "Close" },
        { "tooltip_minimize", "Minimize" },
        { "tooltip_maximize", "Maximize / restore" },
        { "open_error", "Unable to open document" },
        { "association_error", "Unable to change file association" },
        { "supported_filter", "Supported documents (*.fb2;*.epub;*.html;*.htm;*.txt;*.md;*.rtf;*.doc;*.docx;*.pdf)|*.fb2;*.epub;*.html;*.htm;*.txt;*.md;*.rtf;*.doc;*.docx;*.pdf|Books (*.fb2;*.epub)|*.fb2;*.epub|HTML documents (*.html;*.htm)|*.html;*.htm|Text documents (*.txt)|*.txt|Markdown (*.md)|*.md|Rich Text Format (*.rtf)|*.rtf|Microsoft Word Binary (*.doc)|*.doc|Word OpenXML (*.docx)|*.docx|PDF documents (*.pdf)|*.pdf" }
    };

    private static readonly Dictionary<string, string> RussianTexts = new()
    {
        { "app_name", "MYBOOK" },
        { "app_subtitle", "Читалка документов" },
        { "open", "Открыть" },
        { "contents", "Оглавление" },
        { "tooltip_contents", "Показать / скрыть оглавление" },
        { "settings", "Настройки" },
        { "file_formats", "Форматы файлов" },
        { "file_formats_description", "Выберите расширения, которые нужно связать с MYBOOK для текущего пользователя Windows. Форматы никогда не включаются автоматически." },
        { "file_formats_status", "Изменения применяются сразу. Защищённая настройка Windows UserChoice может дополнительно потребовать подтверждения в приложениях по умолчанию." },
        { "windows_default_apps", "Открыть приложения по умолчанию Windows" },
        { "book_not_selected", "Откройте поддерживаемый документ, чтобы начать чтение." },
        { "system_language", "Системный язык - {0}" },
        { "tooltip_language", "Язык интерфейса" },
        { "tooltip_theme", "Сменить тему" },
        { "tooltip_close", "Закрыть" },
        { "tooltip_minimize", "Свернуть" },
        { "tooltip_maximize", "Развернуть / восстановить" },
        { "open_error", "Не удалось открыть документ" },
        { "association_error", "Не удалось изменить привязку формата" },
        { "supported_filter", "Поддерживаемые документы (*.fb2;*.epub;*.html;*.htm;*.txt;*.md;*.rtf;*.doc;*.docx;*.pdf)|*.fb2;*.epub;*.html;*.htm;*.txt;*.md;*.rtf;*.doc;*.docx;*.pdf|Книги (*.fb2;*.epub)|*.fb2;*.epub|HTML-документы (*.html;*.htm)|*.html;*.htm|Текстовые документы (*.txt)|*.txt|Markdown (*.md)|*.md|Rich Text Format (*.rtf)|*.rtf|Microsoft Word Binary (*.doc)|*.doc|Word OpenXML (*.docx)|*.docx|PDF documents (*.pdf)|*.pdf" }
    };

    private static string selectedLanguage_ = AutomaticLanguage;

    public static event EventHandler? LanguageChanged;

    public static string SelectedLanguage => NormalizeLanguage(selectedLanguage_);
    public static string CurrentLanguage => ResolveLanguage(SelectedLanguage);
    public static string DetectedLanguage => GetSystemLanguage();
    public static bool IsAutomaticLanguageSelection =>
        string.Equals(SelectedLanguage, AutomaticLanguage, StringComparison.Ordinal);

    public static void Initialize(string language)
    {
        selectedLanguage_ = NormalizeLanguage(language);
    }

    public static void SelectLanguage(string language)
    {
        var normalized = NormalizeLanguage(language);
        if (string.Equals(selectedLanguage_, normalized, StringComparison.Ordinal))
        {
            return;
        }

        selectedLanguage_ = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, AutomaticLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return AutomaticLanguage;
        }

        if (string.Equals(language, RussianLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return RussianLanguage;
        }

        if (string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EnglishLanguage;
        }

        return AutomaticLanguage;
    }

    public static string ResolveLanguage(string language)
    {
        var normalized = NormalizeLanguage(language);
        return string.Equals(normalized, AutomaticLanguage, StringComparison.Ordinal)
            ? GetSystemLanguage()
            : normalized;
    }

    public static bool IsRussianLanguage(string language)
    {
        return string.Equals(ResolveLanguage(language), RussianLanguage, StringComparison.Ordinal);
    }

    public static string Text(string key)
    {
        var dictionary = IsRussianLanguage(CurrentLanguage) ? RussianTexts : EnglishTexts;
        return dictionary.TryGetValue(key, out var value) ? value : key;
    }

    public static string LanguageDisplayName(string language)
    {
        return IsRussianLanguage(language) ? "Русский" : "English";
    }

    public static string SystemLanguageDisplayText()
    {
        return string.Format(Text("system_language"), LanguageDisplayName(DetectedLanguage));
    }

    private static string GetSystemLanguage()
    {
        return string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            RussianLanguage,
            StringComparison.OrdinalIgnoreCase)
            ? RussianLanguage
            : EnglishLanguage;
    }
}
