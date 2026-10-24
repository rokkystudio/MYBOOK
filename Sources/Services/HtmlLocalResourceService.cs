using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MYBOOK.Services;

/// <summary>
/// Создаёт изолированное virtual-host mapping для локального HTML-документа.
/// Все относительные ресурсы разрешаются только внутри каталога документа,
/// который WebView2 отображает через отдельный HTTPS origin.
/// </summary>
internal static class HtmlLocalResourceService
{
    private const string HostSuffix = ".invalid";

    public static HtmlLocalDocumentMapping CreateMapping(
        string documentPath)
    {
        if (string.IsNullOrWhiteSpace(
                documentPath))
        {
            throw new ArgumentException(
                "Путь HTML-документа не задан.",
                nameof(documentPath));
        }

        var fullPath =
            Path.GetFullPath(
                documentPath);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "HTML-документ не найден.",
                fullPath);
        }

        var extension =
            Path.GetExtension(
                fullPath);

        if (!extension.Equals(
                ".html",
                StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(
                ".htm",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Локальный HTML mapping допускает только .html и .htm.");
        }

        var rootDirectory =
            Path.GetDirectoryName(
                fullPath);

        if (string.IsNullOrWhiteSpace(
                rootDirectory))
        {
            throw new InvalidDataException(
                "Не удалось определить каталог HTML-документа.");
        }

        rootDirectory =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    rootDirectory));

        var hostName =
            CreateHostName(
                rootDirectory);
        var escapedFileName =
            Uri.EscapeDataString(
                Path.GetFileName(
                    fullPath));
        var documentUri =
            new Uri(
                $"https://{hostName}/{escapedFileName}",
                UriKind.Absolute);

        return new HtmlLocalDocumentMapping(
            hostName,
            rootDirectory,
            documentUri);
    }

    private static string CreateHostName(
        string rootDirectory)
    {
        var canonicalRoot =
            rootDirectory
                .ToUpperInvariant();
        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonicalRoot));
        var token =
            Convert
                .ToHexString(
                    hash.AsSpan(
                        0,
                        12))
                .ToLowerInvariant();

        return
            $"book-{token}{HostSuffix}";
    }
}

internal sealed record HtmlLocalDocumentMapping(
    string HostName,
    string RootDirectory,
    Uri DocumentUri);
