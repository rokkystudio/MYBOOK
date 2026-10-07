using System;
using System.Windows;
using System.Windows.Controls;
using MYBOOK.Services;
using MYBOOK.UI;

namespace MYBOOK;

/// <summary>
/// Отображает настройки MYBOOK. Первая вкладка управляет
/// пользовательскими файловыми ассоциациями поддерживаемых форматов.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool loading_;

    /// <summary>
    /// Инициализирует окно настроек, загружает общую иконку приложения
    /// и считывает текущее состояние ассоциаций Windows.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        Icon = AppIconService.Load();
        TitleBar.IconSource = Icon;

        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;

        ApplyLocalization();
        LoadAssociations();
    }

    /// <summary>
    /// Отписывает окно настроек от события локализации.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        base.OnClosed(e);
    }

    /// <summary>
    /// Применяет локализованные подписи к окну настроек и вкладке форматов файлов.
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
    }

    /// <summary>
    /// Считывает из реестра текущие ассоциации MYBOOK
    /// и отражает их в переключателях форматов.
    /// </summary>
    private void LoadAssociations()
    {
        loading_ = true;
        try
        {
            Fb2AssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".fb2");
            EpubAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".epub");
            HtmlAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".html");
            HtmAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".htm");
            TxtAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".txt");
            MarkdownAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".md");
            RtfAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".rtf");
            DocAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".doc");
            DocxAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".docx");
            PdfAssociationCheckBox.IsChecked = FileAssociationService.IsAssociated(".pdf");
            AssociationStatusText.Text = LocalizationService.Text("file_formats_status");
        }
        finally
        {
            loading_ = false;
        }
    }

    /// <summary>
    /// Добавляет или удаляет ассоциацию выбранного расширения с MYBOOK.
    /// </summary>
    private void AssociationCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (loading_ || sender is not CheckBox checkBox || checkBox.Tag is not string extension)
        {
            return;
        }

        try
        {
            FileAssociationService.SetAssociated(extension, checkBox.IsChecked == true);
            AssociationStatusText.Text = LocalizationService.Text("file_formats_status");
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                error.Message,
                LocalizationService.Text("association_error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            LoadAssociations();
        }
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
    }

    /// <summary>
    /// Открывает системную страницу Windows для защищённых ассоциаций UserChoice.
    /// </summary>
    private void WindowsDefaultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FileAssociationService.OpenDefaultAppsSettings();
    }

    /// <summary>
    /// Закрывает окно настроек.
    /// </summary>
    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
