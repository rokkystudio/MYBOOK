using System;
using System.IO;
using System.Windows;
using MYBOOK.Services;
using MYBOOK.UI;

namespace MYBOOK;

/// <summary>
/// Представляет приложение MYBOOK, загружает пользовательские настройки,
/// включает opt-in parser trace по аргументу командной строки и создаёт главное окно
/// с выбранными языком и темой.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Загружает настройки, применяет локализацию и тему, включает parser trace
    /// по аргументу <c>--parser-trace</c> и открывает переданный документ.
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

            var parserTraceEnabled = e.Args.Any(argument =>
                string.Equals(
                    argument,
                    "--parser-trace",
                    StringComparison.OrdinalIgnoreCase));

            ParserTrace.Configure(
                parserTraceEnabled);

            var documentArgument = e.Args.FirstOrDefault(argument =>
                !string.Equals(
                    argument,
                    "--parser-trace",
                    StringComparison.OrdinalIgnoreCase));

            var bookPath = string.IsNullOrWhiteSpace(documentArgument)
                ? null
                : Path.GetFullPath(documentArgument);

            var window = new MainWindow(
                settingsStore,
                settings,
                bookPath);

            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(
                error.Message,
                "MYBOOK",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}
