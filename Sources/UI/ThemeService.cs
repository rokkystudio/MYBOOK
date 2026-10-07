using System;
using System.Windows;
using System.Windows.Media;
using NeoUI;

namespace MYBOOK.UI;

/// <summary>
/// Применяет общую цветовую палитру NeoUI к ресурсам WPF-приложения.
/// </summary>
internal static class ThemeService
{
    /// <summary>
    /// Заполняет динамические кисти приложения значениями выбранной палитры NeoUI.
    /// </summary>
    public static void Apply(Application application, string theme)
    {
        var palette = NeoThemePalettes.Get(theme);
        Set(application, "BackgroundBrush", palette.Background);
        Set(application, "SurfaceBrush", palette.Surface);
        Set(application, "RaisedSurfaceBrush", palette.SurfaceRaised);
        Set(application, "TextBrush", palette.Text);
        Set(application, "MutedTextBrush", palette.MutedText);
        Set(application, "BorderBrush", palette.Border);
        Set(application, "PrimaryBrush", palette.Primary);
        Set(application, "SuccessBrush", palette.Success);
        Set(application, "WarningBrush", palette.Warning);
        Set(application, "ErrorBrush", palette.Error);
        Set(application, "TitleBarBackgroundBrush", palette.TitleBarBackground);
        Set(application, "TitleBarBorderBrush", palette.TitleBarBorder);
        Set(application, "ButtonHoverBrush", WithAlpha(palette.Primary, 0x14));
        Set(application, "ButtonPressedBrush", WithAlpha(palette.Primary, 0x26));
    }

    private static void Set(Application application, string key, string value)
    {
        application.Resources[key] =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }

    private static string WithAlpha(string rgb, byte alpha)
    {
        return rgb.Length == 7 ? "#" + alpha.ToString("X2") + rgb[1..] : rgb;
    }
}
