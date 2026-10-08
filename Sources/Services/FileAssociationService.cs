using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MYBOOK.Services;

/// <summary>
/// Управляет пользовательскими файловыми ассоциациями MYBOOK
/// для форматов из общего реестра без изменения системного UserChoice.
/// </summary>
internal static class FileAssociationService
{
    private const string RegisteredApplicationName = "MYBOOK";
    private const string CapabilitiesPath = @"Software\MYBOOK\Capabilities";

    /// <summary>
    /// Возвращает true, если пользовательская ассоциация расширения
    /// непосредственно указывает на обработчик MYBOOK.
    /// </summary>
    public static bool IsAssociated(string extension)
    {
        var format = SupportedFormatRegistry.GetByExtension(
            extension);

        using var extensionKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\" + format.Extension);

        var currentProgId = extensionKey?.GetValue(
            string.Empty) as string;

        return string.Equals(
            currentProgId,
            format.ProgId,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Добавляет или удаляет пользовательскую ассоциацию указанного поддерживаемого расширения.
    /// </summary>
    public static void SetAssociated(
        string extension,
        bool associated)
    {
        var format = SupportedFormatRegistry.GetByExtension(
            extension);

        if (associated)
        {
            Register(format);
        }
        else
        {
            Unregister(format);
        }

        NotifyShellAssociationChanged();
    }

    /// <summary>
    /// Открывает системную страницу приложений по умолчанию,
    /// где Windows позволяет подтвердить защищённые UserChoice-ассоциации.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:defaultapps?registeredAppUser=MYBOOK",
            UseShellExecute = true
        });
    }

    private static void Register(
        SupportedDocumentFormat format)
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                "Не удалось определить путь к MYBOOK.exe.");
        }

        RegisterProgId(
            format,
            executablePath);

        RegisterExtension(format);
        RegisterCapabilities(format);
    }

    private static void RegisterProgId(
        SupportedDocumentFormat format,
        string executablePath)
    {
        using var progId = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\" + format.ProgId);

        progId?.SetValue(
            string.Empty,
            format.DisplayName);

        using var icon = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\" +
            format.ProgId +
            @"\DefaultIcon");

        icon?.SetValue(
            string.Empty,
            Quote(executablePath) + ",0");

        using var command = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\" +
            format.ProgId +
            @"\shell\open\command");

        command?.SetValue(
            string.Empty,
            Quote(executablePath) + " \"%1\"");
    }

    private static void RegisterExtension(
        SupportedDocumentFormat format)
    {
        using var extension = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\" +
            format.Extension);

        extension?.SetValue(
            string.Empty,
            format.ProgId);

        using var openWithProgIds = extension?.CreateSubKey(
            "OpenWithProgids");

        openWithProgIds?.SetValue(
            format.ProgId,
            Array.Empty<byte>(),
            RegistryValueKind.None);
    }

    private static void RegisterCapabilities(
        SupportedDocumentFormat format)
    {
        using var capabilities = Registry.CurrentUser.CreateSubKey(
            CapabilitiesPath);

        capabilities?.SetValue(
            "ApplicationName",
            "MYBOOK");

        capabilities?.SetValue(
            "ApplicationDescription",
            "Document reader");

        using var associations = Registry.CurrentUser.CreateSubKey(
            CapabilitiesPath +
            @"\FileAssociations");

        associations?.SetValue(
            format.Extension,
            format.ProgId);

        using var registeredApplications = Registry.CurrentUser.CreateSubKey(
            @"Software\RegisteredApplications");

        registeredApplications?.SetValue(
            RegisteredApplicationName,
            CapabilitiesPath);
    }

    private static void Unregister(
        SupportedDocumentFormat format)
    {
        using (var extension = Registry.CurrentUser.OpenSubKey(
                   @"Software\Classes\" +
                   format.Extension,
                   writable: true))
        {
            var currentProgId = extension?.GetValue(
                string.Empty) as string;

            if (string.Equals(
                    currentProgId,
                    format.ProgId,
                    StringComparison.OrdinalIgnoreCase))
            {
                extension?.DeleteValue(
                    string.Empty,
                    throwOnMissingValue: false);
            }

            using var openWithProgIds = extension?.OpenSubKey(
                "OpenWithProgids",
                writable: true);

            openWithProgIds?.DeleteValue(
                format.ProgId,
                throwOnMissingValue: false);
        }

        using (var associations = Registry.CurrentUser.OpenSubKey(
                   CapabilitiesPath +
                   @"\FileAssociations",
                   writable: true))
        {
            associations?.DeleteValue(
                format.Extension,
                throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Classes\" +
            format.ProgId,
            throwOnMissingSubKey: false);
    }

    private static string Quote(string value)
    {
        return "\"" + value + "\"";
    }

    private static void NotifyShellAssociationChanged()
    {
        SHChangeNotify(
            SHCNE_ASSOCCHANGED,
            SHCNF_IDLIST,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);
}
