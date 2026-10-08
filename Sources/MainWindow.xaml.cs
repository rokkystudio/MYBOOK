using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
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
/// преобразует их в нейтральную модель, рендерит через единый HTML/WebView2 слой
/// и предоставляет боковую навигацию по оглавлению и фиксированным страницам.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettingsStore settingsStore_;
    private readonly AppSettings settings_;
    private readonly string? startupDocumentPath_;
    private bool readerInitialized_;
    private bool generatedDocumentOpen_;
    private bool navigationPanelOpen_;
    private NavigationMode navigationMode_ = NavigationMode.Contents;

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
        NavigationButtonText.Text = LocalizationService.Text("navigation");
        NavigationButton.ToolTip = LocalizationService.Text("tooltip_navigation");
        ContentsModeButtonText.Text = LocalizationService.Text("contents");
        PagesModeButtonText.Text = LocalizationService.Text("pages");
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
                UpdateNavigationPanel(null);
                Title = Path.GetFileNameWithoutExtension(fullPath) + " — MYBOOK";
            }
            else
            {
                var document = ReadDocument(fullPath, extension);
                UpdateNavigationPanel(document);

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
    /// Перестраивает боковую панель навигации для текущего документа:
    /// оглавление и виртуализированную плёнку фиксированных страниц.
    /// </summary>
    private void UpdateNavigationPanel(DocumentModel? document)
    {
        OutlineTree.Items.Clear();
        PagePreviewList.ItemsSource = null;

        if (document?.Outlines.Count > 0)
        {
            AddOutlineItems(
                OutlineTree,
                document.Outlines,
                depth: 0);
        }

        var fixedPages = document?.Blocks
            .OfType<DocumentFixedPage>()
            .Select(page => new PagePreviewItem(page))
            .ToArray()
            ?? Array.Empty<PagePreviewItem>();

        if (fixedPages.Length > 0)
        {
            PagePreviewList.ItemsSource = fixedPages;
        }

        var hasContents = OutlineTree.Items.Count > 0;
        var hasPages = fixedPages.Length > 0;

        ContentsModeButton.Visibility = hasContents
            ? Visibility.Visible
            : Visibility.Collapsed;

        PagesModeButton.Visibility = hasPages
            ? Visibility.Visible
            : Visibility.Collapsed;

        NavigationButton.IsEnabled =
            hasContents || hasPages;

        if (!NavigationButton.IsEnabled)
        {
            SetNavigationPanelVisibility(false);
            return;
        }

        if (navigationMode_ == NavigationMode.Contents &&
            !hasContents)
        {
            navigationMode_ = NavigationMode.Pages;
        }
        else if (navigationMode_ == NavigationMode.Pages &&
                 !hasPages)
        {
            navigationMode_ = NavigationMode.Contents;
        }

        SetNavigationMode(navigationMode_);
        SetNavigationPanelVisibility(
            navigationPanelOpen_);
    }

    /// <summary>
    /// Добавляет иерархические элементы оглавления в WPF TreeView.
    /// </summary>
    private static void AddOutlineItems(
        ItemsControl parent,
        IReadOnlyList<DocumentOutlineItem> items,
        int depth)
    {
        foreach (var outline in items)
        {
            var item = new TreeViewItem
            {
                Header = outline.Title,
                Tag = outline,
                IsExpanded = depth == 0
            };

            parent.Items.Add(item);

            if (outline.Children.Count > 0)
            {
                AddOutlineItems(
                    item,
                    outline.Children,
                    depth + 1);
            }
        }
    }

    /// <summary>
    /// Показывает или скрывает общую боковую панель навигации.
    /// </summary>
    private void SetNavigationPanelVisibility(bool visible)
    {
        navigationPanelOpen_ =
            visible &&
            NavigationButton.IsEnabled;

        var visibility = navigationPanelOpen_
            ? Visibility.Visible
            : Visibility.Collapsed;

        NavigationPanel.Visibility = visibility;
        NavigationSplitter.Visibility = visibility;
    }

    /// <summary>
    /// Переключает видимость общей боковой панели навигации.
    /// </summary>
    private void NavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        SetNavigationPanelVisibility(
            !navigationPanelOpen_);
    }

    /// <summary>
    /// Переключает содержимое боковой панели между оглавлением
    /// и виртуализированной плёнкой фиксированных страниц.
    /// </summary>
    private void SetNavigationMode(NavigationMode mode)
    {
        navigationMode_ = mode;

        OutlineTree.Visibility =
            mode == NavigationMode.Contents
                ? Visibility.Visible
                : Visibility.Collapsed;

        PagePreviewList.Visibility =
            mode == NavigationMode.Pages
                ? Visibility.Visible
                : Visibility.Collapsed;

        ContentsModeButtonText.FontWeight =
            mode == NavigationMode.Contents
                ? FontWeights.SemiBold
                : FontWeights.Normal;

        PagesModeButtonText.FontWeight =
            mode == NavigationMode.Pages
                ? FontWeights.SemiBold
                : FontWeights.Normal;
    }

    /// <summary>
    /// Показывает режим оглавления боковой панели.
    /// </summary>
    private void ContentsModeButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        SetNavigationMode(
            NavigationMode.Contents);
    }

    /// <summary>
    /// Показывает режим виртуализированных превью страниц.
    /// </summary>
    private void PagesModeButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        SetNavigationMode(
            NavigationMode.Pages);
    }

    /// <summary>
    /// Переходит к фиксированной странице, выбранной в плёнке превью.
    /// </summary>
    private async void PagePreviewList_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PagePreviewList.SelectedItem is not PagePreviewItem item)
        {
            return;
        }

        try
        {
            await ScrollToPageAsync(
                item.PageNumber);
        }
        catch (Exception error)
        {
            ShowOpenError(error);
        }
    }

    /// <summary>
    /// Строит облегчённый vector thumbnail только для реально созданного
    /// виртуализированного элемента списка страниц.
    /// </summary>
    private void PagePreviewCanvas_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Canvas canvas ||
            canvas.DataContext is not PagePreviewItem item ||
            canvas.Children.Count > 0)
        {
            return;
        }

        PopulatePagePreview(
            canvas,
            item.Page);
    }

    /// <summary>
    /// Освобождает визуалы thumbnail, когда виртуализированный элемент
    /// покидает viewport и может быть переиспользован.
    /// </summary>
    private void PagePreviewCanvas_OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Canvas canvas)
        {
            canvas.Children.Clear();
        }
    }

    /// <summary>
    /// Создаёт лёгкое структурное превью фиксированной страницы:
    /// ограниченное число текстовых штрихов и блоков изображений
    /// в исходной геометрии страницы.
    /// </summary>
    private static void PopulatePagePreview(
        Canvas canvas,
        DocumentFixedPage page)
    {
        foreach (var image in page.ImageRuns.Take(8))
        {
            var points = new[]
            {
                TransformPreviewPoint(image, 0, 0),
                TransformPreviewPoint(image, 1, 0),
                TransformPreviewPoint(image, 0, 1),
                TransformPreviewPoint(image, 1, 1)
            };

            var left = points.Min(point => point.X);
            var top = points.Min(point => point.Y);
            var right = points.Max(point => point.X);
            var bottom = points.Max(point => point.Y);

            var rectangle = new Rectangle
            {
                Width = Math.Max(1, right - left),
                Height = Math.Max(1, bottom - top),
                Fill = Brushes.Gainsboro,
                Stroke = Brushes.DarkGray,
                StrokeThickness = 0.8,
                Opacity = 0.8
            };

            Canvas.SetLeft(
                rectangle,
                left);

            Canvas.SetTop(
                rectangle,
                top);

            canvas.Children.Add(rectangle);
        }

        foreach (var run in page.TextRuns.Take(96))
        {
            if (string.IsNullOrWhiteSpace(run.Text))
            {
                continue;
            }

            var availableWidth = Math.Max(
                1,
                page.WidthPoints - run.XPoints);

            var width = Math.Min(
                availableWidth,
                Math.Max(
                    3,
                    run.Text.Length *
                    run.FontSizePoints *
                    0.30));

            var height = Math.Clamp(
                run.FontSizePoints * 0.16,
                0.8,
                4);

            var line = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = Brushes.DimGray,
                Opacity = 0.48
            };

            Canvas.SetLeft(
                line,
                Math.Max(0, run.XPoints));

            Canvas.SetTop(
                line,
                Math.Max(
                    0,
                    run.YPoints -
                    run.FontSizePoints * 0.72));

            canvas.Children.Add(line);
        }
    }

    private static Point TransformPreviewPoint(
        DocumentFixedImageRun image,
        double x,
        double y)
    {
        return new Point(
            image.TransformA * x +
            image.TransformC * y +
            image.TransformE,
            image.TransformB * x +
            image.TransformD * y +
            image.TransformF);
    }

    /// <summary>
    /// Переходит к выбранному пункту оглавления:
    /// внутренние PDF destinations прокручивают WebView2 до страницы,
    /// разрешённые внешние URI открываются стандартным приложением Windows.
    /// </summary>
    private async void OutlineTree_OnSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem treeItem ||
            treeItem.Tag is not DocumentOutlineItem outline)
        {
            return;
        }

        try
        {
            if (outline.TargetPageNumber is { } pageNumber)
            {
                await ScrollToPageAsync(pageNumber);
                return;
            }

            if (!string.IsNullOrWhiteSpace(outline.Uri))
            {
                OpenOutlineUri(outline.Uri);
            }
        }
        catch (Exception error)
        {
            ShowOpenError(error);
        }
    }

    /// <summary>
    /// Прокручивает сгенерированный документ к фиксированной странице.
    /// </summary>
    private async Task ScrollToPageAsync(int pageNumber)
    {
        if (pageNumber <= 0 ||
            !generatedDocumentOpen_ ||
            !readerInitialized_ ||
            Reader.CoreWebView2 == null)
        {
            return;
        }

        await Reader.CoreWebView2.ExecuteScriptAsync(
            $"document.getElementById('page-{pageNumber}')?.scrollIntoView({{behavior:'smooth',block:'start'}});");
    }

    /// <summary>
    /// Открывает безопасный внешний URI из outline через оболочку Windows.
    /// </summary>
    private static void OpenOutlineUri(string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
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
    /// Режим содержимого боковой панели навигации.
    /// </summary>
    private enum NavigationMode
    {
        Contents,
        Pages
    }

    /// <summary>
    /// Лёгкая запись страницы для виртуализированной плёнки.
    /// Хранит ссылку на уже разобранную фиксированную страницу,
    /// а thumbnail строится только при появлении элемента в viewport.
    /// </summary>
    private sealed class PagePreviewItem
    {
        public PagePreviewItem(DocumentFixedPage page)
        {
            Page = page;
        }

        public DocumentFixedPage Page { get; }

        public int PageNumber => Page.PageNumber;

        public double PreviewWidth =>
            Math.Max(
                1,
                Page.WidthPoints);

        public double PreviewHeight =>
            Math.Max(
                1,
                Page.HeightPoints);
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
