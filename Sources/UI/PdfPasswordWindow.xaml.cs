using System.Windows;
using MYBOOK.Services;

namespace MYBOOK.UI;

/// <summary>
/// Запрашивает пароль для password-protected PDF без сохранения его в settings.
/// </summary>
public partial class PdfPasswordWindow : Window
{
    public PdfPasswordWindow(
        Window owner,
        string documentName,
        bool previousPasswordRejected)
    {
        InitializeComponent();

        Owner = owner;
        Title = LocalizationService.Text(
            "pdf_password_title");
        PromptTextBlock.Text =
            previousPasswordRejected
                ? LocalizationService.Text(
                    "pdf_password_wrong")
                : string.Format(
                    LocalizationService.Text(
                        "pdf_password_prompt"),
                    documentName);
        CancelButton.Content =
            LocalizationService.Text(
                "cancel");
        OpenButton.Content =
            LocalizationService.Text(
                "open");

        Loaded += (_, _) =>
            PasswordInput.Focus();
    }

    public string Password =>
        PasswordInput.Password;

    private void OpenButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
