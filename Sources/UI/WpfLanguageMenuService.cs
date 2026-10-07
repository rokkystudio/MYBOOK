using System;
using System.Windows.Controls;
using MYBOOK.Services;
using NeoUI;

namespace MYBOOK.UI;

/// <summary>
/// Создаёт меню выбора языка через общий NeoUI и каталог флагов FlagsPack.
/// </summary>
internal static class WpfLanguageMenuService
{
    /// <summary>
    /// Создаёт пункты системного языка, английского и русского языка.
    /// </summary>
    public static ContextMenu CreateLanguageMenu()
    {
        var builder = new NeoLanguageMenuBuilder();

        builder.AddLanguage(
            LocalizationService.AutomaticLanguage,
            LocalizationService.SystemLanguageDisplayText(),
            GetLanguageFlagCountryCode(LocalizationService.DetectedLanguage),
            LocalizationService.IsAutomaticLanguageSelection,
            LocalizationService.SelectLanguage);

        builder.AddSeparator();

        builder.AddLanguage(
            LocalizationService.EnglishLanguage,
            "English",
            "US",
            string.Equals(
                LocalizationService.SelectedLanguage,
                LocalizationService.EnglishLanguage,
                StringComparison.Ordinal),
            LocalizationService.SelectLanguage);

        builder.AddLanguage(
            LocalizationService.RussianLanguage,
            "Русский",
            "RU",
            string.Equals(
                LocalizationService.SelectedLanguage,
                LocalizationService.RussianLanguage,
                StringComparison.Ordinal),
            LocalizationService.SelectLanguage);

        return builder.Build();
    }

    /// <summary>
    /// Возвращает код страны флага для текущего фактического языка.
    /// </summary>
    public static string GetCurrentLanguageFlagCountryCode()
    {
        return GetLanguageFlagCountryCode(LocalizationService.CurrentLanguage);
    }

    private static string GetLanguageFlagCountryCode(string language)
    {
        return LocalizationService.IsRussianLanguage(language) ? "RU" : "US";
    }
}
