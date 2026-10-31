using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Book.Rendering;
using MYBOOK.Services;
using MYBOOK.UI;

namespace MYBOOK;

/// <summary>
/// Отображает настройки MYBOOK: пользовательские Windows file associations
/// и базовую типографику потокового ридера с предварительным просмотром.
/// Настройки типографики сохраняются только по кнопке применения.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettingsStore settingsStore_;
    private readonly AppSettings settings_;
    private bool loading_ = true;

    /// <summary>
    /// Инициализирует окно настроек из текущего AppSettings, создаёт
    /// переключатели форматов и шрифтов и отображает сохранённую
    /// типографику без изменения исходных документов.
    /// </summary>
    internal SettingsWindow(
        AppSettingsStore settingsStore,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(settings);

        settingsStore_ = settingsStore;
        settings_ = settings;
        settings_.ReadingTypography.Validate();

        InitializeComponent();

        Icon = AppIconService.Load();
        TitleBar.IconSource = Icon;

        var version =
            typeof(SettingsWindow).Assembly.GetName().Version
            ?? throw new InvalidOperationException(
                "MYBOOK assembly version is unavailable.");
        TitleBar.VersionText =
            $"{version.Major}.{version.Minor}.{version.Build}";

        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;

        ApplyLocalization();
        BuildFileFormatControls();
        BuildReadingFontControls();
        LoadAssociations();
        LoadReadingTypography(
            settings_.ReadingTypography);
    }

    /// <summary>
    /// Отписывает окно настроек от события изменения языка приложения.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        base.OnClosed(e);
    }

    /// <summary>
    /// Обновляет локализованные подписи обеих вкладок, предварительного
    /// просмотра и кнопок без изменения выбранных значений.
    /// </summary>
    private void ApplyLocalization()
    {
        Title = LocalizationService.Text("settings");
        TitleBar.TitleText = LocalizationService.Text("settings");
        TitleBar.SubtitleText = LocalizationService.Text("app_name");
        TitleBar.CloseToolTip = LocalizationService.Text("tooltip_close");

        FileFormatsTab.Header = LocalizationService.Text("file_formats");
        FileFormatsDescriptionText.Text = LocalizationService.Text("file_formats_description");
        WindowsDefaultsButton.Content = LocalizationService.Text("windows_default_apps");

        ReadingTab.Header = LocalizationService.Text("reading_typography");
        ReadingDescriptionText.Text = LocalizationService.Text("reading_typography_description");
        ReadingFontLabelText.Text = LocalizationService.Text("reading_font_family");
        ReadingFontSizeLabelText.Text = LocalizationService.Text("reading_font_size");
        ReadingWidthLabelText.Text = LocalizationService.Text("reading_content_width");
        ReadingLineHeightLabelText.Text = LocalizationService.Text("reading_line_height");
        ReadingPreviewText.Text = LocalizationService.Text("reading_preview");
        ReadingResetButton.Content = LocalizationService.Text("reading_reset");
        ReadingApplyButton.Content = LocalizationService.Text("reading_apply");
    }

    /// <summary>
    /// Создаёт переключатели файловых ассоциаций из общего реестра форматов.
    /// </summary>
    private void BuildFileFormatControls()
    {
        FileFormatsPanel.Children.Clear();

        foreach (var format in SupportedFormatRegistry.All)
        {
            var checkBox = new CheckBox
            {
                Tag = format.Extension,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14,
                Content = $"{format.Extension} — {format.DisplayName}"
            };

            checkBox.Checked += AssociationCheckBox_OnChanged;
            checkBox.Unchecked += AssociationCheckBox_OnChanged;

            FileFormatsPanel.Children.Add(checkBox);
        }
    }

    /// <summary>
    /// Заполняет ComboBox только допустимыми системными семействами шрифтов
    /// из модели типографики. Каждый элемент хранит точное значение enum.
    /// </summary>
    private void BuildReadingFontControls()
    {
        ReadingFontComboBox.Items.Clear();

        foreach (var font in
                 Enum.GetValues<DocumentReadingFontFamily>())
        {
            ReadingFontComboBox.Items.Add(
                new ComboBoxItem
                {
                    Content = new DocumentReadingTypography
                    {
                        FontFamily = font
                    }.GetFontFamilyName(),
                    Tag = font
                });
        }
    }

    /// <summary>
    /// Считывает из реестра Windows текущие ассоциации MYBOOK
    /// и отражает их в переключателях форматов.
    /// </summary>
    private void LoadAssociations()
    {
        loading_ = true;

        try
        {
            foreach (var child in FileFormatsPanel.Children)
            {
                if (child is not CheckBox checkBox ||
                    checkBox.Tag is not string extension)
                {
                    continue;
                }

                checkBox.IsChecked =
                    FileAssociationService.IsAssociated(
                        extension);
            }

            AssociationStatusText.Text =
                LocalizationService.Text(
                    "file_formats_status");
        }
        finally
        {
            loading_ = false;
        }
    }

    /// <summary>
    /// Отображает сохранённую или стандартную типографику в селекторе
    /// семейства и слайдерах без сохранения изменений.
    /// </summary>
    private void LoadReadingTypography(
        DocumentReadingTypography typography)
    {
        ArgumentNullException.ThrowIfNull(typography);
        typography.Validate();

        loading_ = true;

        try
        {
            ComboBoxItem? selectedFont = null;

            foreach (var item in
                     ReadingFontComboBox.Items)
            {
                if (item is ComboBoxItem
                    {
                        Tag: DocumentReadingFontFamily family
                    } candidate &&
                    family == typography.FontFamily)
                {
                    selectedFont = candidate;
                    break;
                }
            }

            ReadingFontComboBox.SelectedItem =
                selectedFont
                ?? throw new InvalidOperationException(
                    "Reading font selection is not available.");

            ReadingFontSizeSlider.Value =
                typography.FontSizePixels;
            ReadingWidthSlider.Value =
                typography.ContentWidthPixels;
            ReadingLineHeightSlider.Value =
                typography.LineHeightPercent;
        }
        finally
        {
            loading_ = false;
        }

        UpdateReadingTypographyPreview();
    }

    /// <summary>
    /// Возвращает точные значения пользовательских элементов типографики
    /// без округления, fallback и скрытого изменения диапазонов.
    /// </summary>
    private DocumentReadingTypography ReadReadingTypography()
    {
        if (ReadingFontComboBox.SelectedItem is not ComboBoxItem
            {
                Tag: DocumentReadingFontFamily font
            })
        {
            throw new InvalidOperationException(
                "Reading font selection is invalid.");
        }

        var typography = new DocumentReadingTypography
        {
            FontFamily = font,
            FontSizePixels =
                checked((int)ReadingFontSizeSlider.Value),
            ContentWidthPixels =
                checked((int)ReadingWidthSlider.Value),
            LineHeightPercent =
                checked((int)ReadingLineHeightSlider.Value)
        };

        typography.Validate();
        return typography;
    }

    /// <summary>
    /// Отображает выбранную типографику в локальном WPF preview
    /// и числовых подписях; несохранённые настройки не меняют книгу.
    /// </summary>
    private void UpdateReadingTypographyPreview()
    {
        if (loading_)
        {
            return;
        }

        var typography =
            ReadReadingTypography();

        ReadingFontSizeValueText.Text =
            $"{typography.FontSizePixels} px";
        ReadingWidthValueText.Text =
            $"{typography.ContentWidthPixels} px";
        ReadingLineHeightValueText.Text =
            $"{typography.LineHeightPercent}%";

        ReadingPreviewText.FontFamily =
            new FontFamily(
                typography.GetFontFamilyName());
        ReadingPreviewText.FontSize =
            typography.FontSizePixels;
        ReadingPreviewText.LineHeight =
            typography.FontSizePixels *
            typography.LineHeightPercent / 100.0;

        ReadingStatusText.Text = string.Empty;
    }

    /// <summary>
    /// Обновляет preview при выборе другого допустимого семейства шрифтов.
    /// </summary>
    private void ReadingTypography_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!loading_)
        {
            UpdateReadingTypographyPreview();
        }
    }

    /// <summary>
    /// Обновляет preview и точные числовые значения при движении
    /// слайдеров размера, ширины и межстрочного интервала.
    /// </summary>
    private void ReadingTypography_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loading_)
        {
            UpdateReadingTypographyPreview();
        }
    }

    /// <summary>
    /// Показывает стандартные параметры чтения в форме и preview.
    /// Сохранённые настройки остаются неизменными до применения.
    /// </summary>
    private void ReadingResetButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        LoadReadingTypography(
            new DocumentReadingTypography());
        ReadingStatusText.Text =
            LocalizationService.Text(
                "reading_reset_preview");
    }

    /// <summary>
    /// Сохраняет validated reading typography в AppSettings и settings.json.
    /// При ошибке записи предыдущие настройки в памяти сохраняются;
    /// ошибка отображается пользователю в отдельном диалоге.
    /// </summary>
    private void ReadingApplyButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var typography =
                ReadReadingTypography();
            var previous =
                settings_.ReadingTypography;

            settings_.ReadingTypography =
                typography;

            try
            {
                settingsStore_.Save(
                    settings_);
            }
            catch
            {
                settings_.ReadingTypography =
                    previous;
                throw;
            }

            ReadingStatusText.Text =
                LocalizationService.Text(
                    "reading_saved");
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                error.Message,
                LocalizationService.Text(
                    "reading_settings_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Добавляет или удаляет ассоциацию выбранного расширения с MYBOOK.
    /// </summary>
    private void AssociationCheckBox_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (loading_ ||
            sender is not CheckBox checkBox ||
            checkBox.Tag is not string extension)
        {
            return;
        }

        try
        {
            FileAssociationService.SetAssociated(
                extension,
                checkBox.IsChecked == true);
            AssociationStatusText.Text =
                LocalizationService.Text(
                    "file_formats_status");
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                error.Message,
                LocalizationService.Text(
                    "association_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            LoadAssociations();
        }
    }

    /// <summary>
    /// Переводит подписи настроек при смене языка интерфейса.
    /// </summary>
    private void LocalizationService_LanguageChanged(
        object? sender,
        EventArgs e)
    {
        ApplyLocalization();
    }

    /// <summary>
    /// Открывает системную страницу Windows для защищённых ассоциаций UserChoice.
    /// </summary>
    private void WindowsDefaultsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        FileAssociationService.OpenDefaultAppsSettings();
    }

    /// <summary>
    /// Закрывает окно настроек MYBOOK.
    /// </summary>
    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
