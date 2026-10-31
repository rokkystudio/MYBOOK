using System.IO;
using System.Text.Json;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

/// <summary>
/// Проверяет per-document историю CSS-pixel scroll positions, локальное
/// settings.json и строгий WebView2 runtime протокол без WPF-окон.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Проверяет независимость путей, сохранение нулевой позиции, bounded
    /// LRU history, JSON migration/atomic persistence, protocol tokens,
    /// capture/restore JavaScript и invalid message rejection.
    /// </summary>
    private static void TestReadingPositions(
        string directory)
    {
        static void AssertInvalid(
            Action action,
            string message)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected, message);
        }

        var pathA = Path.GetFullPath(
            Path.Combine(directory, "chapter-A.epub"));
        var pathB = Path.GetFullPath(
            Path.Combine(directory, "chapter-B.pdf"));
        var pathHtml = Path.GetFullPath(
            Path.Combine(directory, "reader.html"));
        var settings = new AppSettings();

        Assert(ReadingPositionService.GetPosition(
                settings,
                pathA) == 0 &&
            settings.ReadingPositions.Count == 0,
            "Unknown documents must begin at CSS scroll offset zero without a stored entry.");

        Assert(!ReadingPositionService.RecordPosition(
                settings,
                pathA,
                0) &&
            settings.ReadingPositions.Count == 0,
            "Initial zero position must not allocate a saved reading entry.");

        Assert(ReadingPositionService.RecordPosition(
                settings,
                pathA,
                1800) &&
            ReadingPositionService.RecordPosition(
                settings,
                pathB,
                470) &&
            ReadingPositionService.RecordPosition(
                settings,
                pathHtml,
                95) &&
            settings.ReadingPositions.Count == 3 &&
            ReadingPositionService.GetPosition(
                settings,
                pathA.ToUpperInvariant()) == 1800 &&
            ReadingPositionService.GetPosition(
                settings,
                pathB) == 470 &&
            ReadingPositionService.GetPosition(
                settings,
                pathHtml) == 95,
            "EPUB, fixed PDF and direct HTML must retain independent positions keyed by case-insensitive full paths.");

        Assert(!ReadingPositionService.RecordPosition(
                settings,
                pathA,
                1800) &&
            settings.ReadingPositions[0].DocumentPath == pathHtml,
            "Unchanged scroll coordinates must not trigger new writes or reorder the history.");

        Assert(ReadingPositionService.RecordPosition(
                settings,
                pathA,
                0) &&
            ReadingPositionService.GetPosition(
                settings,
                pathA) == 0 &&
            settings.ReadingPositions[0].DocumentPath == pathA,
            "Returning to the document start must overwrite the previous saved offset.");

        var settingsPath = Path.Combine(
            directory,
            "reading-position-settings.json");
        var store = new AppSettingsStore(
            settingsPath);

        var empty = store.Load();
        Assert(empty.ReadingPositions.Count == 0 &&
            empty.ReadingTypography ==
                new DocumentReadingTypography(),
            "Absent settings file must load neutral defaults and an empty position history.");

        store.Save(settings);
        var savedJson = File.ReadAllText(
            settingsPath);
        var loaded = store.Load();
        Assert(loaded.ReadingPositions.Count == 3 &&
            ReadingPositionService.GetPosition(
                loaded,
                pathA) == 0 &&
            ReadingPositionService.GetPosition(
                loaded,
                pathB) == 470 &&
            ReadingPositionService.GetPosition(
                loaded,
                pathHtml) == 95 &&
            !File.Exists(settingsPath + ".tmp"),
            "Atomic settings save/load must preserve distinct per-document offsets without a temporary file.");

        Assert(ReadingPositionService.RecordPosition(
                loaded,
                pathA,
                int.MaxValue),
            "The full Int32 CSS-pixel offset range must be supported.");
        store.Save(loaded);
        Assert(ReadingPositionService.GetPosition(
                store.Load(),
                pathA) == int.MaxValue,
            "The maximum CSS-pixel offset must survive JSON serialization.");

        var oldSettingsJson =
            """
            {"Theme":"Light","Language":"auto","RecentDocuments":[]}
            """;
        File.WriteAllText(
            settingsPath,
            oldSettingsJson);
        var migrated = store.Load();
        Assert(migrated.ReadingPositions.Count == 0,
            "Settings saved before reading position support must load an empty history.");

        store.Save(settings);
        var originalSettingsJson =
            File.ReadAllText(settingsPath);

        AssertInvalid(
            () => ReadingPositionService.RecordPosition(
                settings,
                pathA,
                -1),
            "Negative scroll positions must be explicitly rejected.");
        AssertInvalid(
            () => ReadingPositionService.GetPosition(
                settings,
                "relative-book.epub"),
            "Relative document paths must not be used as history keys.");
        AssertInvalid(
            () => ReadingPositionService.Validate(
                null),
            "Null reading position history must be rejected.");
        AssertInvalid(
            () => ReadingPositionService.Validate(
                new List<DocumentReadingPosition>
                {
                    new()
                    {
                        DocumentPath = pathB,
                        ScrollTopPixels = -5
                    }
                }),
            "Negative persisted CSS scroll coordinates must be rejected.");

        AssertInvalid(
            () => ReadingPositionService.Validate(
                new List<DocumentReadingPosition>
                {
                    new()
                    {
                        DocumentPath = pathA,
                        ScrollTopPixels = 100
                    },
                    new()
                    {
                        DocumentPath = pathA.ToUpperInvariant(),
                        ScrollTopPixels = 200
                    }
                }),
            "Persisted case-insensitive duplicate paths must be rejected.");

        AssertInvalid(
            () => ReadingPositionService.Validate(
                new List<DocumentReadingPosition>
                {
                    new()
                    {
                        DocumentPath = Path.Combine(
                            directory,
                            ".",
                            "not-normalized.epub"),
                        ScrollTopPixels = 100
                    }
                }),
            "Persisted noncanonical absolute paths must be rejected.");

        settings.ReadingPositions.Add(
            new DocumentReadingPosition
            {
                DocumentPath = pathA,
                ScrollTopPixels = -1
            });
        AssertInvalid(
            () => store.Save(settings),
            "Invalid reading positions must reject Save before replacing the settings file.");
        Assert(File.ReadAllText(settingsPath) ==
            originalSettingsJson,
            "Invalid Save must preserve the complete previous settings JSON.");
        settings.ReadingPositions.RemoveAt(
            settings.ReadingPositions.Count - 1);

        File.WriteAllText(
            settingsPath,
            """
            {"ReadingPositions":[{"DocumentPath":"relative.epub","ScrollTopPixels":100}]}
            """);
        AssertInvalid(
            () => store.Load(),
            "Malformed persisted relative position keys must reject Load without silent fallback.");

        var capacitySettings = new AppSettings();
        for (var index = 0;
             index < ReadingPositionService.MaximumPositions + 1;
             index++)
        {
            var path = Path.Combine(
                directory,
                $"capacity-{index}.epub");
            Assert(
                ReadingPositionService.RecordPosition(
                    capacitySettings,
                    path,
                    index + 1),
                "Distinct positions must be stored until the bounded history limit.");
        }

        Assert(capacitySettings.ReadingPositions.Count ==
                ReadingPositionService.MaximumPositions &&
            ReadingPositionService.GetPosition(
                capacitySettings,
                Path.Combine(
                    directory,
                    "capacity-0.epub")) == 0 &&
            ReadingPositionService.GetPosition(
                capacitySettings,
                Path.Combine(
                    directory,
                    "capacity-1.epub")) == 2 &&
            ReadingPositionService.GetPosition(
                capacitySettings,
                Path.Combine(
                    directory,
                    "capacity-200.epub")) == 201,
            "Reading position history must retain exactly 200 newest distinct files.");

        ReadingPositionService.Validate(
            capacitySettings.ReadingPositions);

        AssertInvalid(
            () => ReadingPositionService.Validate(
                Enumerable.Range(
                    0,
                    ReadingPositionService.MaximumPositions + 1)
                    .Select(index =>
                        new DocumentReadingPosition
                        {
                            DocumentPath = Path.Combine(
                                directory,
                                $"oversized-{index}.txt"),
                            ScrollTopPixels = index
                        })
                    .ToArray()),
            "Persisted history larger than 200 records must be rejected.");

        var generatedUri = new Uri(
            "file:///C:/Users/User/AppData/Local/MYBOOK/Reader/book.html");
        var localHtmlUri = new Uri(
            "https://book-abc.invalid/chapter.html");

        Assert(DocumentReadingPositionScript.IsSameDocumentUri(
                generatedUri,
                generatedUri.AbsoluteUri + "#page-42") &&
            DocumentReadingPositionScript.IsSameDocumentUri(
                generatedUri,
                generatedUri.AbsoluteUri.ToUpperInvariant() + "#chapter") &&
            DocumentReadingPositionScript.IsSameDocumentUri(
                localHtmlUri,
                localHtmlUri.AbsoluteUri + "#notes") &&
            !DocumentReadingPositionScript.IsSameDocumentUri(
                generatedUri,
                generatedUri.AbsoluteUri + "?different=1") &&
            !DocumentReadingPositionScript.IsSameDocumentUri(
                generatedUri,
                "file:///C:/Users/User/AppData/Local/MYBOOK/Reader/other.html") &&
            !DocumentReadingPositionScript.IsSameDocumentUri(
                localHtmlUri,
                "https://other.invalid/chapter.html") &&
            !DocumentReadingPositionScript.IsSameDocumentUri(
                localHtmlUri,
                "https://book-abc.invalid/chapter.html?x=1") &&
            !DocumentReadingPositionScript.IsSameDocumentUri(
                generatedUri,
                "not-a-uri"),
            "WebView2 source validation must accept same-document anchor fragments without trusting another path, query or origin.");

        var token =
            Guid.NewGuid().ToString("N");
        var anotherToken =
            Guid.NewGuid().ToString("N");
        var installation =
            DocumentReadingPositionScript.CreateInstallScript(
                1570,
                token);

        AssertContains(
            installation,
            "top: 1570",
            "Reading position installation script must restore the exact stored pixel coordinate.");
        AssertContains(
            installation,
            "behavior: \"instant\"",
            "Reading position restore must avoid browser smooth-scroll animation.");
        AssertContains(
            installation,
            "window.addEventListener(\"scroll\"",
            "Reading position script must subscribe to scrolling.");
        AssertContains(
            installation,
            "window.addEventListener(\"pagehide\"",
            "Reading position script must flush on page navigation.");
        AssertContains(
            installation,
            "window.setTimeout(report, 350)",
            "Reading position scroll reports must be debounced.");
        AssertContains(
            installation,
            "return true;",
            "Reading position installation must return a checked completion result.");
        AssertContains(
            installation,
            token,
            "Reading position script must bind the session token.");
        Assert(!installation.Contains(
                pathA,
                StringComparison.Ordinal),
            "Reading position JavaScript must not expose local document file paths.");

        AssertContains(
            DocumentReadingPositionScript.CaptureScript,
            "document.scrollingElement",
            "Capture script must read the browser scrolling element.");

        var validMessage = JsonSerializer.Serialize(
            new
            {
                type = "mybook-reading-position-v1",
                token,
                scrollTopPixels = 2310
            });

        Assert(DocumentReadingPositionScript.TryParseMessage(
                validMessage,
                token,
                out var receivedPixels) &&
            receivedPixels == 2310 &&
            DocumentReadingPositionScript.ParseCapturedPosition(
                "2310") == 2310 &&
            DocumentReadingPositionScript.ParseCapturedPosition(
                "0") == 0 &&
            DocumentReadingPositionScript.ParseCapturedPosition(
                int.MaxValue.ToString()) == int.MaxValue,
            "WebView2 message and script result must accept exact nonnegative Int32 coordinates.");

        var invalidMessages = new[]
        {
            "{}",
            "null",
            "not-json",
            "[]",
            validMessage.Replace(
                token,
                anotherToken,
                StringComparison.Ordinal),
            validMessage.Replace(
                "mybook-reading-position-v1",
                "unknown-protocol",
                StringComparison.Ordinal),
            validMessage.Replace(
                "2310",
                "-1",
                StringComparison.Ordinal),
            validMessage.Replace(
                "2310",
                "2310.5",
                StringComparison.Ordinal),
            validMessage.Replace(
                "2310",
                "\"2310\"",
                StringComparison.Ordinal),
            validMessage.Replace(
                "2310",
                "2147483648",
                StringComparison.Ordinal),
            "{\"type\":\"mybook-reading-position-v1\",\"token\":\"" +
                token + "\"}",
            new string('X', 513)
        };

        foreach (var invalid in invalidMessages)
        {
            Assert(!DocumentReadingPositionScript.TryParseMessage(
                    invalid,
                    token,
                    out _),
                "Unrelated and malformed WebView2 messages must never change reading position history.");
        }

        foreach (var invalidCapture in
                 new[]
                 {
                     "null",
                     "\"100\"",
                     "-1",
                     "1.5",
                     "2147483648"
                 })
        {
            AssertInvalid(
                () => DocumentReadingPositionScript.ParseCapturedPosition(
                    invalidCapture),
                "WebView2 capture results outside the exact nonnegative Int32 profile must be rejected.");
        }

        AssertInvalid(
            () => DocumentReadingPositionScript.CreateInstallScript(
                -1,
                token),
            "Negative restored scroll coordinates must be rejected.");
        AssertInvalid(
            () => DocumentReadingPositionScript.CreateInstallScript(
                100,
                "script-token"),
            "Script injection must reject tokens outside exact GUID N syntax.");
    }
}
