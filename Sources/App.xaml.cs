using System;
using System.IO;
using System.Windows;
using MYBOOK.Services;
using MYBOOK.UI;

namespace MYBOOK;

/// <summary>
/// Представляет приложение MYBOOK, загружает пользовательские настройки
/// и создаёт главное окно с выбранными языком и темой.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Загружает настройки, применяет локализацию и тему,
    /// затем открывает главное окно для переданного файла книги.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var settingsStore = new AppSettingsStore();
            var settings = settingsStore.Load();

            LocalizationService.Initialize(settings.Language);
            ThemeService.Apply(this, settings.Theme);

            var bookPath = e.Args.Length > 0 ? Path.GetFullPath(e.Args[0]) : null;
            var window = new MainWindow(settingsStore, settings, bookPath);
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "MYBOOK", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
