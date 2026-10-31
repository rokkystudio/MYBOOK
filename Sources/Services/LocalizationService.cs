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
        { "recent_documents", "Recent" },
        { "recent_documents_empty", "No recent documents" },
        { "clear_recent_documents", "Clear recent documents" },
        { "navigation", "Navigation" },
        { "contents", "Contents" },
        { "pages", "Pages" },
        { "page", "Page" },
        { "go", "Go" },
        { "page_number_error", "Page number must be between 1 and {0}." },
        { "tooltip_navigation", "Show / hide navigation" },
        { "tooltip_fullscreen", "Full screen reading mode (F11, F, Ctrl+F, Enter or middle click)" },
        { "tooltip_zoom_out", "Zoom out (Ctrl+- or Ctrl+mouse wheel)" },
        { "tooltip_zoom_reset", "Reset zoom to 100% (Ctrl+0)" },
        { "tooltip_zoom_in", "Zoom in (Ctrl++ or Ctrl+mouse wheel)" },
        { "settings", "Settings" },
        { "file_formats", "File formats" },
        { "file_formats_description", "Choose which supported extensions should be associated with MYBOOK for the current Windows user. Formats are never enabled automatically." },
        { "file_formats_status", "Changes are applied immediately. Windows UserChoice protection may still require confirmation in Default Apps." },
        { "windows_default_apps", "Open Windows Default Apps" },
        { "reading_typography", "Reading" },
        { "reading_typography_description", "Choose the base typography for generated flowing documents. Source inline formatting remains intact; PDF/DjVu fixed pages and original HTML are unaffected. Changes apply to the open book after closing Settings." },
        { "reading_font_family", "Base font" },
        { "reading_font_size", "Font size" },
        { "reading_content_width", "Reading column width" },
        { "reading_line_height", "Line spacing" },
        { "reading_preview", "The quick brown fox jumps over the lazy dog. Adjust the typography to make long passages comfortable to read." },
        { "reading_reset", "Defaults" },
        { "reading_apply", "Apply" },
        { "reading_saved", "Reading settings saved. Close Settings to apply them to the open book." },
        { "reading_reset_preview", "Defaults selected. Click Apply to save them." },
        { "reading_settings_error", "Unable to save reading settings" },
        { "book_not_selected", "Open a supported document to start reading." },
        { "system_language", "System language - {0}" },
        { "tooltip_language", "Interface language" },
        { "tooltip_theme", "Switch theme" },
        { "tooltip_close", "Close" },
        { "tooltip_minimize", "Minimize" },
        { "tooltip_maximize", "Maximize / restore" },
        { "open_error", "Unable to open document" },
        { "pdf_password_title", "PDF password" },
        { "pdf_password_prompt", "Enter the password for “{0}”." },
        { "pdf_password_wrong", "The password is incorrect. Try again or cancel." },
        { "cancel", "Cancel" },
        { "association_error", "Unable to change file association" },
        { "supported_documents", "Supported documents" }
    };

    private static readonly Dictionary<string, string> RussianTexts = new()
    {
        { "app_name", "MYBOOK" },
        { "app_subtitle", "Читалка документов" },
        { "open", "Открыть" },
        { "recent_documents", "Недавние" },
        { "recent_documents_empty", "Нет недавних документов" },
        { "clear_recent_documents", "Очистить недавние документы" },
        { "navigation", "Навигация" },
        { "contents", "Оглавление" },
        { "pages", "Страницы" },
        { "page", "Страница" },
        { "go", "Перейти" },
        { "page_number_error", "Номер страницы должен быть от 1 до {0}." },
        { "tooltip_navigation", "Показать / скрыть навигацию" },
        { "tooltip_fullscreen", "Полноэкранный режим чтения (F11, F, Ctrl+F, Enter или средняя кнопка мыши)" },
        { "tooltip_zoom_out", "Уменьшить масштаб (Ctrl+- или Ctrl+колесо мыши)" },
        { "tooltip_zoom_reset", "Вернуть масштаб 100% (Ctrl+0)" },
        { "tooltip_zoom_in", "Увеличить масштаб (Ctrl++ или Ctrl+колесо мыши)" },
        { "settings", "Настройки" },
        { "file_formats", "Форматы файлов" },
        { "file_formats_description", "Выберите расширения, которые нужно связать с MYBOOK для текущего пользователя Windows. Форматы никогда не включаются автоматически." },
        { "file_formats_status", "Изменения применяются сразу. Защищённая настройка Windows UserChoice может дополнительно потребовать подтверждения в приложениях по умолчанию." },
        { "windows_default_apps", "Открыть приложения по умолчанию Windows" },
        { "reading_typography", "Чтение" },
        { "reading_typography_description", "Настройте основную типографику потоковых документов MYBOOK. Исходное форматирование текста сохраняется; фиксированные страницы PDF/DjVu и исходный HTML не затрагиваются. Изменения применяются к открытой книге после закрытия настроек." },
        { "reading_font_family", "Основной шрифт" },
        { "reading_font_size", "Размер шрифта" },
        { "reading_content_width", "Ширина текстовой колонки" },
        { "reading_line_height", "Межстрочный интервал" },
        { "reading_preview", "Съешь ещё этих мягких французских булок, да выпей чаю. Удобная типографика помогает читать большие тексты." },
        { "reading_reset", "По умолчанию" },
        { "reading_apply", "Применить" },
        { "reading_saved", "Настройки чтения сохранены. Закройте окно, чтобы применить их к открытой книге." },
        { "reading_reset_preview", "Выбраны значения по умолчанию. Нажмите «Применить», чтобы сохранить их." },
        { "reading_settings_error", "Не удалось сохранить настройки чтения" },
        { "book_not_selected", "Откройте поддерживаемый документ, чтобы начать чтение." },
        { "system_language", "Системный язык - {0}" },
        { "tooltip_language", "Язык интерфейса" },
        { "tooltip_theme", "Сменить тему" },
        { "tooltip_close", "Закрыть" },
        { "tooltip_minimize", "Свернуть" },
        { "tooltip_maximize", "Развернуть / восстановить" },
        { "open_error", "Не удалось открыть документ" },
        { "pdf_password_title", "Пароль PDF" },
        { "pdf_password_prompt", "Введите пароль для «{0}»." },
        { "pdf_password_wrong", "Пароль неверен. Повторите ввод или отмените открытие." },
        { "cancel", "Отмена" },
        { "association_error", "Не удалось изменить привязку формата" },
        { "supported_documents", "Поддерживаемые документы" }
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
