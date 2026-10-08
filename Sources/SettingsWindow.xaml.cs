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
        BuildFileFormatControls();
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
    /// Считывает из реестра Windows текущие ассоциации MYBOOK
    /// и отражает их в динамически созданных переключателях форматов.
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
