using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MYBOOK.UI;

/// <summary>
/// Загружает единственную иконку приложения из Resources/app.ico
/// для окна и общей панели NeoUI.
/// </summary>
internal static class AppIconService
{
    /// <summary>
    /// Возвращает изображение иконки приложения из встроенного WPF-ресурса.
    /// </summary>
    public static ImageSource Load()
    {
        return BitmapFrame.Create(
            new Uri(
                "pack://application:,,,/Resources/app.ico",
                UriKind.Absolute));
    }
}
