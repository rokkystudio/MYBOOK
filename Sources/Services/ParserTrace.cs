using System.Globalization;
using System.IO;
using System.Text;

namespace MYBOOK.Services;

/// <summary>
/// Предоставляет opt-in диагностический журнал этапов разбора документов.
/// Режим включается при запуске приложения с аргументом <c>--parser-trace</c>;
/// без него журналирование не создаёт файлов и не выполняет дисковый I/O.
/// </summary>
internal static class ParserTrace
{
    private static readonly AsyncLocal<TraceSession?> CurrentSession = new();
    private static bool enabled_;

    /// <summary>
    /// Включает или отключает диагностический trace для последующих чтений документов.
    /// </summary>
    public static void Configure(bool enabled)
    {
        enabled_ = enabled;
    }

    /// <summary>
    /// Начинает отдельный trace-сеанс для документа и возвращает scope,
    /// который завершает и закрывает журнал при Dispose.
    /// </summary>
    public static IDisposable BeginDocument(
        string path,
        SupportedDocumentFormat format)
    {
        if (!enabled_)
        {
            return EmptyScope.Instance;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MYBOOK",
            "Logs");

        Directory.CreateDirectory(directory);

        var fileName =
            DateTimeOffset.Now.ToString(
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture) +
            "-" +
            SanitizeFileName(
                Path.GetFileNameWithoutExtension(path)) +
            "-" +
            format.Extension.TrimStart('.') +
            ".log";

        var logPath = Path.Combine(
            directory,
            fileName);

        var session = new TraceSession(
            logPath);

        CurrentSession.Value = session;

        session.Write(
            "session",
            $"BEGIN path={Path.GetFullPath(path)} format={format.Extension}");

        return session;
    }

    /// <summary>
    /// Записывает один диагностический этап в активный trace-сеанс.
    /// </summary>
    public static void Write(
        string category,
        string message)
    {
        CurrentSession.Value?.Write(
            category,
            message);
    }

    /// <summary>
    /// Записывает сведения об исключении в активный trace-сеанс.
    /// </summary>
    public static void WriteException(
        string category,
        Exception error)
    {
        Write(
            category,
            $"ERROR type={error.GetType().FullName} message={error.Message}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(
            Math.Max(
                value.Length,
                8));

        foreach (var character in value)
        {
            builder.Append(
                invalid.Contains(character)
                    ? '_'
                    : character);
        }

        var result = builder.ToString().Trim();

        return result.Length == 0
            ? "document"
            : result;
    }

    private sealed class TraceSession : IDisposable
    {
        private readonly object sync_ = new();
        private readonly StreamWriter writer_;
        private bool disposed_;

        public TraceSession(string path)
        {
            writer_ = new StreamWriter(
                path,
                append: false,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
        }

        public void Write(
            string category,
            string message)
        {
            lock (sync_)
            {
                if (disposed_)
                {
                    return;
                }

                writer_.Write(
                    DateTimeOffset.Now.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                writer_.Write(" [");
                writer_.Write(category);
                writer_.Write("] ");
                writer_.WriteLine(message);
                writer_.Flush();
            }
        }

        public void Dispose()
        {
            lock (sync_)
            {
                if (disposed_)
                {
                    return;
                }

                Write(
                    "session",
                    "END");

                disposed_ = true;
                writer_.Dispose();

                if (ReferenceEquals(
                        CurrentSession.Value,
                        this))
                {
                    CurrentSession.Value = null;
                }
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}
