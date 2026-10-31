using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Book.Rendering;
using Microsoft.Web.WebView2.Core;
using MYBOOK.Services;

namespace MYBOOK;

/// <summary>
/// Управляет сохранением вертикальной CSS-pixel позиции активного документа
/// MYBOOK. Сообщения WebView2 проверяются по URI исходного файла без
/// fragment и GUID сеанса; HTML не управляет путями или настройками приложения.
/// </summary>
public partial class MainWindow
{
    private string? readingDocumentPath_;
    private Uri? readingDocumentUri_;
    private string? readingDocumentToken_;
    private ulong? readingNavigationId_;
    private bool readingPositionReady_;
    private bool readingPositionCloseInProgress_;
    private bool readingPositionCloseApproved_;
    private bool readingPositionErrorShown_;

    /// <summary>
    /// Привязывает новое открытие файла к абсолютному URI основного
    /// WebView2 frame и новому GUID, отделяя поздние сообщения прежней книги.
    /// </summary>
    private void ActivateReadingDocument(
        string documentPath,
        Uri documentUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(documentUri);

        if (!documentUri.IsAbsoluteUri)
        {
            throw new InvalidDataException(
                "Reading position document URI must be absolute.");
        }

        readingDocumentPath_ =
            Path.GetFullPath(documentPath);
        readingDocumentUri_ =
            documentUri;
        readingDocumentToken_ =
            Guid.NewGuid().ToString("N");
        readingNavigationId_ = null;
        readingPositionReady_ = false;
        readingPositionErrorShown_ = false;
    }

    /// <summary>
    /// Связывает WebView2 scroll reports и navigation lifecycle с
    /// текущим MainWindow. Для локальных HTML-документов используется
    /// тот же протокол без записи или преобразования исходного файла.
    /// </summary>
    private void RegisterReadingPositionEvents(
        CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);

        core.NavigationStarting +=
            Reader_OnReadingNavigationStarting;
        core.NavigationCompleted +=
            Reader_OnReadingNavigationCompleted;
        core.WebMessageReceived +=
            Reader_OnReadingWebMessageReceived;
        core.Settings.IsWebMessageEnabled = true;
    }

    /// <summary>
    /// Отписывает MainWindow от scroll и navigation событий перед
    /// освобождением WebView2.
    /// </summary>
    private void UnregisterReadingPositionEvents(
        CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);

        core.NavigationStarting -=
            Reader_OnReadingNavigationStarting;
        core.NavigationCompleted -=
            Reader_OnReadingNavigationCompleted;
        core.WebMessageReceived -=
            Reader_OnReadingWebMessageReceived;
    }

    /// <summary>
    /// Запоминает NavigationId только для исходного URI активного файла,
    /// допуская внутренние #anchor transitions. Внешняя навигация не
    /// получает доступ к позициям книги.
    /// </summary>
    private void Reader_OnReadingNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        readingNavigationId_ =
            readingDocumentUri_ != null &&
            DocumentReadingPositionScript.IsSameDocumentUri(
                readingDocumentUri_,
                e.Uri)
                ? e.NavigationId
                : null;
        readingPositionReady_ = false;
    }

    /// <summary>
    /// После успешной навигации основного frame устанавливает listener
    /// с GUID сеанса и восстанавливает точный сохранённый vertical scroll.
    /// Неуспешные и устаревшие навигации не меняют текущий документ.
    /// </summary>
    private async void Reader_OnReadingNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        var core =
            Reader.CoreWebView2;
        var path =
            readingDocumentPath_;
        var uri =
            readingDocumentUri_;
        var token =
            readingDocumentToken_;

        if (!e.IsSuccess ||
            core == null ||
            path == null ||
            uri == null ||
            token == null ||
            readingNavigationId_ != e.NavigationId ||
            !DocumentReadingPositionScript.IsSameDocumentUri(
                uri,
                core.Source))
        {
            return;
        }

        try
        {
            var restorePixels =
                ReadingPositionService.GetPosition(
                    settings_,
                    path);

            var result =
                await core.ExecuteScriptAsync(
                    DocumentReadingPositionScript.CreateInstallScript(
                        restorePixels,
                        token));

            if (result != "true")
            {
                throw new InvalidDataException(
                    "WebView2 не подтвердил установку отслеживания позиции чтения.");
            }

            if (readingDocumentPath_ == path &&
                readingDocumentToken_ == token &&
                readingNavigationId_ == e.NavigationId)
            {
                readingPositionReady_ = true;
            }
        }
        catch (Exception error)
        {
            ShowReadingPositionError(
                error);
        }
    }

    /// <summary>
    /// Принимает только сообщения активной страницы с ожидаемым
    /// сеансовым GUID и целочисленным vertical scroll offset.
    /// Сторонние HTML scripts и поздние сообщения прежних книг игнорируются.
    /// </summary>
    private void Reader_OnReadingWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        var path =
            readingDocumentPath_;
        var uri =
            readingDocumentUri_;
        var token =
            readingDocumentToken_;

        if (path == null ||
            uri == null ||
            token == null ||
            !DocumentReadingPositionScript.IsSameDocumentUri(
                uri,
                e.Source) ||
            !DocumentReadingPositionScript.TryParseMessage(
                e.WebMessageAsJson,
                token,
                out var scrollTopPixels))
        {
            return;
        }

        try
        {
            StoreReadingPosition(
                path,
                scrollTopPixels);
        }
        catch (Exception error)
        {
            ShowReadingPositionError(
                error);
        }
    }

    /// <summary>
    /// Сохраняет текущую вертикальную координату WebView2 до переключения
    /// документа и при закрытии приложения. Перед записью проверяет,
    /// что результат относится к тому же URI и GUID, что и запрос.
    /// </summary>
    private async Task PersistCurrentReadingPositionAsync()
    {
        var core =
            Reader.CoreWebView2;
        var path =
            readingDocumentPath_;
        var uri =
            readingDocumentUri_;
        var token =
            readingDocumentToken_;

        if (!readerInitialized_ ||
            !readingPositionReady_ ||
            core == null ||
            path == null ||
            uri == null ||
            token == null ||
            !DocumentReadingPositionScript.IsSameDocumentUri(
                uri,
                core.Source))
        {
            return;
        }

        var result =
            await core.ExecuteScriptAsync(
                DocumentReadingPositionScript.CaptureScript);

        if (readingDocumentPath_ != path ||
            readingDocumentToken_ != token ||
            readingDocumentUri_ != uri)
        {
            return;
        }

        var position =
            DocumentReadingPositionScript.ParseCapturedPosition(
                result);
        StoreReadingPosition(
            path,
            position);
    }

    /// <summary>
    /// Обновляет ограниченную per-document историю в AppSettings и
    /// сохраняет её атомарно. При ошибке записи возвращает предыдущий
    /// набор позиций, поэтому следующее событие может повторить запись.
    /// </summary>
    private void StoreReadingPosition(
        string documentPath,
        int scrollTopPixels)
    {
        var previousPositions =
            settings_.ReadingPositions.ToList();

        if (!ReadingPositionService.RecordPosition(
            settings_,
            documentPath,
            scrollTopPixels))
        {
            return;
        }

        try
        {
            settingsStore_.Save(
                settings_);
            readingPositionErrorShown_ = false;
        }
        catch
        {
            settings_.ReadingPositions =
                previousPositions;
            throw;
        }
    }

    /// <summary>
    /// Показывает одну локализованную ошибку сохранения позиции за
    /// последовательность неудачных WebView2 сообщений; успешная запись
    /// разрешает показ следующей независимой ошибки.
    /// </summary>
    private void ShowReadingPositionError(
        Exception error)
    {
        if (readingPositionErrorShown_)
        {
            return;
        }

        readingPositionErrorShown_ = true;
        MessageBox.Show(
            this,
            error.Message,
            LocalizationService.Text(
                "reading_position_error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Перед закрытием окна выполняет финальное асинхронное чтение
    /// scrollTop через WebView2 и запись settings. Ошибка сохранения
    /// предлагает явный выбор: остаться или закрыть без последней позиции.
    /// </summary>
    protected override async void OnClosing(
        CancelEventArgs e)
    {
        if (readingPositionCloseApproved_ ||
            !readerInitialized_ ||
            readingDocumentPath_ == null ||
            Reader.CoreWebView2 == null)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);

        if (readingPositionCloseInProgress_)
        {
            return;
        }

        readingPositionCloseInProgress_ = true;

        try
        {
            await PersistCurrentReadingPositionAsync();
            readingPositionCloseApproved_ = true;
            Close();
        }
        catch (Exception error)
        {
            readingPositionCloseInProgress_ = false;

            var result = MessageBox.Show(
                this,
                error.Message +
                    Environment.NewLine +
                    Environment.NewLine +
                    LocalizationService.Text(
                        "reading_position_close_without_save"),
                LocalizationService.Text(
                    "reading_position_error"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                readingPositionCloseApproved_ = true;
                Close();
            }
        }
    }
}
