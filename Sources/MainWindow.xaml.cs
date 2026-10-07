using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using FlagsPack;
using Microsoft.Win32;
using MYBOOK.Documents;
using MYBOOK.Formats.Doc;
using MYBOOK.Formats.Docx;
using MYBOOK.Formats.Epub;
using MYBOOK.Formats.Fb2;
using MYBOOK.Formats.Markdown;
using MYBOOK.Formats.Pdf;
using MYBOOK.Formats.Rtf;
using MYBOOK.Formats.Text;
using MYBOOK.Rendering;
using MYBOOK.Services;
using MYBOOK.UI;
using NeoUI;

namespace MYBOOK;

/// <summary>
/// Отображает главное окно MYBOOK, открывает поддерживаемые документы,
/// преобразует их в нейтральную модель и рендерит через единый HTML/WebView2 слой.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettingsStore settingsStore_;
    private readonly AppSettings settings_;
    private readonly string? startupDocumentPath_;
    private bool readerInitialized_;
    private bool generatedDocumentOpen_;

    /// <summary>
    /// Инициализирует окно, загружает общую иконку приложения,
    /// настраивает панель инструментов и запоминает документ из командной строки.
    /// </summary>
    internal MainWindow(
        AppSettingsStore settingsStore,
        AppSettings settings,
        string? documentPath)
    {
        InitializeComponent();

        settingsStore_ = settingsStore;
        settings_ = settings;
        startupDocumentPath_ = documentPath;

        Icon = AppIconService.Load();
        TitleBar.IconSource = Icon;
        SettingsIcon.Source = NeoAssetService.LoadImage(NeoAssetPaths.Settings);

        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;

        ApplyLocalization();
        UpdateToolBarButtons();
    }

    /// <summary>
    /// Инициализирует WebView2 после загрузки окна
    /// и открывает документ из аргумента командной строки, если он был передан.
    /// </summary>
    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureReaderAsync();

            if (!string.IsNullOrWhiteSpace(startupDocumentPath_))
            {
                await OpenDocumentAsync(startupDocumentPath_);
            }
        }
        catch (Exception error)
        {
            ShowOpenError(error);
        }
    }

    /// <summary>
    /// Отписывает окно от глобального события локализации
    /// и освобождает инициализированный WebView2.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;

        if (readerInitialized_)
        {
            Reader.Dispose();
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Применяет локализованные строки к шапке, панели инструментов
    /// и пустому состоянию окна.
    /// </summary>
    private void ApplyLocalization()
    {
        Title = LocalizationService.Text("app_name");
        TitleBar.TitleText = LocalizationService.Text("app_name");
        TitleBar.SubtitleText = LocalizationService.Text("app_subtitle");
        TitleBar.MinimizeToolTip = LocalizationService.Text("tooltip_minimize");
        TitleBar.MaximizeToolTip = LocalizationService.Text("tooltip_maximize");
        TitleBar.CloseToolTip = LocalizationService.Text("tooltip_close");

        OpenButtonText.Text = LocalizationService.Text("open");
        OpenButton.ToolTip = LocalizationService.Text("open");
        SettingsButton.ToolTip = LocalizationService.Text("settings");
        LanguageToolButton.ToolTip = LocalizationService.Text("tooltip_language");
        ThemeToolButton.ToolTip = LocalizationService.Text("tooltip_theme");
        EmptyStateTextBlock.Text = LocalizationService.Text("book_not_selected");

        UpdateToolBarButtons();
    }

    /// <summary>
    /// Обновляет флаг текущего языка и значок активной темы
    /// на отдельной панели инструментов.
    /// </summary>
    private void UpdateToolBarButtons()
    {
        LanguageIcon.Source =
            CountryFlags.Load(WpfLanguageMenuService.GetCurrentLanguageFlagCountryCode());

        var themePath = NeoThemePalettes.IsDarkTheme(settings_.Theme)
            ? NeoAssetPaths.ThemeDark
            : NeoAssetPaths.ThemeLight;

        ThemeIcon.Source = NeoAssetService.LoadImage(themePath);
    }

    /// <summary>
    /// Инициализирует WebView2 и отключает встроенные браузерные элементы,
    /// не относящиеся к режиму чтения документа.
    /// </summary>
    private async Task EnsureReaderAsync()
    {
        if (readerInitialized_)
        {
            return;
        }

        await Reader.EnsureCoreWebView2Async();

        var core = Reader.CoreWebView2
                   ?? throw new InvalidOperationException("WebView2 не удалось инициализировать.");

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;

        readerInitialized_ = true;
    }

    /// <summary>
    /// Показывает стандартный диалог выбора поддерживаемого документа,
    /// начиная с папки последнего успешно открытого файла.
    /// </summary>
    private async Task ShowOpenDocumentDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Text("supported_filter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(settings_.LastBookDirectory) &&
            Directory.Exists(settings_.LastBookDirectory))
        {
            dialog.InitialDirectory = settings_.LastBookDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            await OpenDocumentAsync(dialog.FileName);
        }
    }

    /// <summary>
    /// Открывает поддерживаемый документ.
    /// FB2, EPUB, TXT, Markdown, RTF, DOC, DOCX и PDF преобразуются в нейтральную модель MYBOOK,
    /// а исходные HTML/HTM открываются WebView2 напрямую.
    /// </summary>
    private async Task OpenDocumentAsync(string path)
    {
        try
        {
            await EnsureReaderAsync();

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл документа не найден.", fullPath);
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            if (extension is ".html" or ".htm")
            {
                Reader.Source = new Uri(fullPath, UriKind.Absolute);
                generatedDocumentOpen_ = false;
                Title = Path.GetFileNameWithoutExtension(fullPath) + " — MYBOOK";
            }
            else
            {
                var document = ReadDocument(fullPath, extension);
                var html = DocumentHtmlRenderer.Render(document, settings_.Theme);
                var htmlPath = GetReaderHtmlPath();

                File.WriteAllText(
                    htmlPath,
                    html,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                Reader.Source = new Uri(htmlPath, UriKind.Absolute);
                generatedDocumentOpen_ = true;
                Title = document.Title + " — MYBOOK";
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                settings_.LastBookDirectory = directory;
                settingsStore_.Save(settings_);
            }

            Reader.Visibility = Visibility.Visible;
            EmptyStateTextBlock.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            ShowOpenError(error);
        }
    }

    /// <summary>
    /// Выбирает собственный reader формата и возвращает нейтральную модель документа.
    /// </summary>
    private static DocumentModel ReadDocument(string path, string extension)
    {
        return extension switch
        {
            ".fb2" => Fb2DocumentReader.Read(path),
            ".epub" => EpubDocumentReader.Read(path),
            ".txt" => TextDocumentReader.Read(path),
            ".md" => MarkdownDocumentReader.Read(path),
            ".pdf" => PdfDocumentReader.Read(path),
            ".rtf" => RtfDocumentReader.Read(path),
            ".doc" => ReadDocCompatibleDocument(path),
            ".docx" => DocxDocumentReader.Read(path),
            _ => throw new InvalidDataException(
                "MYBOOK поддерживает .fb2, .epub, .html, .htm, .txt, .md, .rtf, .doc, .docx и .pdf.")
        };
    }

    /// <summary>
    /// Открывает файл с расширением .doc по фактической сигнатуре содержимого.
    /// Word Binary/CFB передаётся собственному DOC reader, а RTF с расширением .doc —
    /// автономному RTF reader.
    /// </summary>
    private static DocumentModel ReadDocCompatibleDocument(string path)
    {
        Span<byte> header = stackalloc byte[8];

        using (var stream = File.OpenRead(path))
        {
            var read = stream.Read(header);

            if (read >= 8 &&
                header[..8].SequenceEqual(new byte[]
                {
                    0xD0, 0xCF, 0x11, 0xE0,
                    0xA1, 0xB1, 0x1A, 0xE1
                }))
            {
                return DocDocumentReader.Read(path);
            }

            if (read >= 5 &&
                header[..5].SequenceEqual("{\\rtf"u8))
            {
                return RtfDocumentReader.Read(path);
            }
        }

        throw new InvalidDataException(
            "Файл .doc не является Word Binary/CFB или RTF-документом.");
    }

    /// <summary>
    /// Возвращает путь локальной HTML-страницы,
    /// которую WebView2 использует для отображения преобразованного документа.
    /// </summary>
    private static string GetReaderHtmlPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MYBOOK",
            "Reader");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "book.html");
    }

    /// <summary>
    /// Показывает ошибку открытия документа в локализованном диалоге.
    /// </summary>
    private void ShowOpenError(Exception error)
    {
        MessageBox.Show(
            this,
            error.Message,
            LocalizationService.Text("open_error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        settings_.Language = LocalizationService.SelectedLanguage;
        settingsStore_.Save(settings_);
        ApplyLocalization();
    }

    /// <summary>
    /// Открывает меню выбора языка относительно кнопки панели инструментов.
    /// </summary>
    private void LanguageButton_OnClick(object sender, RoutedEventArgs e)
    {
        NeoContextMenu.Show(
            WpfLanguageMenuService.CreateLanguageMenu(),
            LanguageToolButton);
    }

    /// <summary>
    /// Переключает светлую и тёмную палитру NeoUI,
    /// сохраняет выбранную тему и обновляет цвета сгенерированной HTML-страницы.
    /// Исходный пользовательский HTML отображается без вмешательства в его стили.
    /// </summary>
    private async void ThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        settings_.Theme = NeoThemePalettes.IsDarkTheme(settings_.Theme)
            ? NeoThemePalettes.LightTheme
            : NeoThemePalettes.DarkTheme;

        ThemeService.Apply(Application.Current, settings_.Theme);
        settingsStore_.Save(settings_);
        UpdateToolBarButtons();

        if (generatedDocumentOpen_ && readerInitialized_ && Reader.CoreWebView2 != null)
        {
            await Reader.CoreWebView2.ExecuteScriptAsync(
                DocumentHtmlRenderer.CreateThemeScript(settings_.Theme));
        }
    }

    /// <summary>
    /// Закрывает главное окно приложения.
    /// </summary>
    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Открывает диалог выбора поддерживаемого документа.
    /// </summary>
    private async void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ShowOpenDocumentDialogAsync();
    }

    private async void OpenCommand_OnExecuted(
        object sender,
        System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        await ShowOpenDocumentDialogAsync();
    }

    /// <summary>
    /// Открывает модальное окно настроек MYBOOK.
    /// </summary>
    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }
}
