using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет document-level RTF footnote/endnote settings и служебные
    /// separator/continuation destinations. Layout properties сохраняются
    /// как neutral metadata, separator text не попадает в document body,
    /// а chftnsep/chftnsepc остаются точными anchoring markers.
    /// </summary>
    private static void TestRtfNoteSettings(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-note-settings.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        bool RejectsInvalid(string source)
        {
            try
            {
                Read(source);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        bool RejectsUnsupported(string source)
        {
            try
            {
                Read(source);
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }

        static string BlocksText(
            IReadOnlyList<DocumentBlock> blocks)
        {
            return string.Concat(
                blocks.OfType<DocumentParagraph>()
                    .SelectMany(paragraph => paragraph.Inlines)
                    .Select(inline => inline.Text));
        }

        var model =
            Read(
                """
                {\rtf1\ansi
                \fet2\ftnstart3\aftnstart5\ftnrstpg\aftnrestart\ftnnalc\aftnnruc\ftnbj\aenddoc
                {\ftnsep\pard\plain\b FootSep\b0 \chftnsep Tail\par}
                {\ftnsepc\pard\plain FootCont \chftnsepc Tail\par}
                {\ftncn\pard\plain FootNotice\line Next\par}
                {\aftnsep\pard\plain EndSep\par}
                {\aftnsepc\pard\plain EndCont\par}
                {\aftncn\pard\plain EndNotice\par}
                Body\par}
                """);

        var notes =
            model.Notes ??
            throw new InvalidOperationException(
                "RTF note settings должны создавать DocumentNoteSettings.");

        Assert(
            notes.Type ==
                DocumentNoteType.FootnotesAndEndnotes &&
            notes.Footnotes.StartNumber == 3 &&
            notes.Footnotes.Restart ==
                DocumentNoteRestartMode.Page &&
            notes.Footnotes.NumberFormat ==
                DocumentNoteNumberFormat.LowerLatin &&
            notes.Footnotes.Placement ==
                DocumentNotePlacement.BottomOfPage &&
            notes.Endnotes.StartNumber == 5 &&
            notes.Endnotes.Restart ==
                DocumentNoteRestartMode.Section &&
            notes.Endnotes.NumberFormat ==
                DocumentNoteNumberFormat.UpperRoman &&
            notes.Endnotes.Placement ==
                DocumentNotePlacement.EndOfDocument,
            "RTF note settings должны сохранять fet/start/restart/format/placement без layout-эмуляции.");

        Assert(
            BlocksText(notes.FootnoteSeparator)
                .Contains(
                    "FootSep",
                    StringComparison.Ordinal) &&
            BlocksText(notes.FootnoteContinuationSeparator)
                .Contains(
                    "FootCont",
                    StringComparison.Ordinal) &&
            BlocksText(notes.FootnoteContinuationNotice) ==
                "FootNotice" + Environment.NewLine + "Next" &&
            BlocksText(notes.EndnoteSeparator) ==
                "EndSep" &&
            BlocksText(notes.EndnoteContinuationSeparator) ==
                "EndCont" &&
            BlocksText(notes.EndnoteContinuationNotice) ==
                "EndNotice",
            "RTF note separator destinations должны сохранять text/line content отдельно от body.");

        var footnoteSeparator =
            notes.FootnoteSeparator
                .OfType<DocumentParagraph>()
                .Single();
        var continuationSeparator =
            notes.FootnoteContinuationSeparator
                .OfType<DocumentParagraph>()
                .Single();

        Assert(
            footnoteSeparator.Inlines.Any(inline =>
                inline.Text == "FootSep" &&
                inline.Bold) &&
            footnoteSeparator.Inlines.Any(inline =>
                inline.NoteSeparatorAnchor ==
                    DocumentNoteSeparatorAnchor.FootnoteSeparator &&
                inline.Text.Length == 0) &&
            continuationSeparator.Inlines.Any(inline =>
                inline.NoteSeparatorAnchor ==
                    DocumentNoteSeparatorAnchor.FootnoteContinuation &&
                inline.Text.Length == 0),
            "RTF chftnsep/chftnsepc должны сохраняться отдельными anchor markers и не терять соседнее formatting.");

        var bodyText =
            FlattenText(model);

        Assert(
            bodyText.Contains(
                "Body",
                StringComparison.Ordinal) &&
            !bodyText.Contains(
                "FootSep",
                StringComparison.Ordinal) &&
            !bodyText.Contains(
                "EndNotice",
                StringComparison.Ordinal),
            "RTF separator/continuation destinations не должны попадать в document body.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-note-type=\"footnotes-and-endnotes\"",
            "RTF note type HTML metadata");
        AssertContains(
            html,
            "data-footnote-start=\"3\"",
            "RTF footnote start HTML metadata");
        AssertContains(
            html,
            "data-footnote-restart=\"page\"",
            "RTF footnote restart HTML metadata");
        AssertContains(
            html,
            "data-footnote-number-format=\"lower-latin\"",
            "RTF footnote number format HTML metadata");
        AssertContains(
            html,
            "data-footnote-placement=\"bottom-of-page\"",
            "RTF footnote placement HTML metadata");
        AssertContains(
            html,
            "data-endnote-start=\"5\"",
            "RTF endnote start HTML metadata");
        AssertContains(
            html,
            "data-endnote-restart=\"section\"",
            "RTF endnote restart HTML metadata");
        AssertContains(
            html,
            "data-endnote-number-format=\"upper-roman\"",
            "RTF endnote number format HTML metadata");
        AssertContains(
            html,
            "data-endnote-placement=\"end-of-document\"",
            "RTF endnote placement HTML metadata");

        foreach (var destination in new[]
                 {
                     "footnote-separator",
                     "footnote-continuation-separator",
                     "footnote-continuation-notice",
                     "endnote-separator",
                     "endnote-continuation-separator",
                     "endnote-continuation-notice"
                 })
        {
            AssertContains(
                html,
                "data-note-destination=\"" +
                destination +
                "\"",
                "RTF note destination HTML template");
        }

        AssertContains(
            html,
            "data-note-separator-anchor=\"footnote-separator\"",
            "RTF footnote separator anchor HTML metadata");
        AssertContains(
            html,
            "data-note-separator-anchor=\"footnote-continuation\"",
            "RTF footnote continuation anchor HTML metadata");

        var defaults =
            Read(
                @"{\rtf1\ansi\fet1 Plain\par}");

        Assert(
            defaults.Notes is
            {
                Type: DocumentNoteType.EndnotesOnly
            } &&
            defaults.Notes.Footnotes.StartNumber == 1 &&
            defaults.Notes.Footnotes.Restart ==
                DocumentNoteRestartMode.Continuous &&
            defaults.Notes.Footnotes.NumberFormat ==
                DocumentNoteNumberFormat.Arabic &&
            defaults.Notes.Footnotes.Placement ==
                DocumentNotePlacement.EndOfSection &&
            defaults.Notes.Endnotes.StartNumber == 1 &&
            defaults.Notes.Endnotes.Restart ==
                DocumentNoteRestartMode.Continuous &&
            defaults.Notes.Endnotes.NumberFormat ==
                DocumentNoteNumberFormat.Arabic &&
            defaults.Notes.Endnotes.Placement ==
                DocumentNotePlacement.EndOfSection,
            "RTF note settings должны использовать документированные defaults для неуказанных свойств.");

        var numberFormats =
            new (string Control, DocumentNoteNumberFormat Format)[]
            {
                ("ftnnar", DocumentNoteNumberFormat.Arabic),
                ("ftnnalc", DocumentNoteNumberFormat.LowerLatin),
                ("ftnnauc", DocumentNoteNumberFormat.UpperLatin),
                ("ftnnrlc", DocumentNoteNumberFormat.LowerRoman),
                ("ftnnruc", DocumentNoteNumberFormat.UpperRoman),
                ("ftnnchi", DocumentNoteNumberFormat.Chicago),
                ("ftnnchosung", DocumentNoteNumberFormat.Chosung),
                ("ftnncnum", DocumentNoteNumberFormat.Circle),
                ("ftnndbnum", DocumentNoteNumberFormat.KanjiNoDigit),
                ("ftnndbnumd", DocumentNoteNumberFormat.KanjiDigit),
                ("ftnndbnumt", DocumentNoteNumberFormat.Kanji3),
                ("ftnndbnumk", DocumentNoteNumberFormat.Kanji4),
                ("ftnndbar", DocumentNoteNumberFormat.DoubleByte),
                ("ftnnganada", DocumentNoteNumberFormat.Ganada),
                ("ftnngbnum", DocumentNoteNumberFormat.Chinese1),
                ("ftnngbnumd", DocumentNoteNumberFormat.Chinese2),
                ("ftnngbnuml", DocumentNoteNumberFormat.Chinese3),
                ("ftnngbnumk", DocumentNoteNumberFormat.Chinese4),
                ("ftnnzodiac", DocumentNoteNumberFormat.Zodiac1),
                ("ftnnzodiacd", DocumentNoteNumberFormat.Zodiac2),
                ("ftnnzodiacl", DocumentNoteNumberFormat.Zodiac3)
            };

        foreach (var (control, format) in numberFormats)
        {
            var footnoteFormat =
                Read(
                    $@"{{\rtf1\ansi\{control} X\par}}");
            var endnoteFormat =
                Read(
                    $@"{{\rtf1\ansi\a{control} X\par}}");

            Assert(
                footnoteFormat.Notes?.Footnotes.NumberFormat ==
                    format &&
                endnoteFormat.Notes?.Endnotes.NumberFormat ==
                    format,
                "RTF note numbering control " +
                control +
                " должен иметь точное neutral enum отображение.");
        }

        var placementCases =
            new (string Control, bool Endnote, DocumentNotePlacement Placement)[]
            {
                ("endnotes", false, DocumentNotePlacement.EndOfSection),
                ("enddoc", false, DocumentNotePlacement.EndOfDocument),
                ("ftntj", false, DocumentNotePlacement.BeneathText),
                ("ftnbj", false, DocumentNotePlacement.BottomOfPage),
                ("aendnotes", true, DocumentNotePlacement.EndOfSection),
                ("aenddoc", true, DocumentNotePlacement.EndOfDocument),
                ("aftntj", true, DocumentNotePlacement.BeneathText),
                ("aftnbj", true, DocumentNotePlacement.BottomOfPage)
            };

        foreach (var (control, endnote, placement) in placementCases)
        {
            var placementModel =
                Read(
                    $@"{{\rtf1\ansi\{control} X\par}}");
            var actual =
                endnote
                    ? placementModel.Notes?.Endnotes.Placement
                    : placementModel.Notes?.Footnotes.Placement;

            Assert(
                actual == placement,
                "RTF note placement control " +
                control +
                " должен иметь точное neutral enum отображение.");
        }

        var restartCases =
            new (string Control, bool Endnote, DocumentNoteRestartMode Restart)[]
            {
                ("ftnrstcont", false, DocumentNoteRestartMode.Continuous),
                ("ftnrestart", false, DocumentNoteRestartMode.Section),
                ("ftnrstpg", false, DocumentNoteRestartMode.Page),
                ("aftnrstcont", true, DocumentNoteRestartMode.Continuous),
                ("aftnrestart", true, DocumentNoteRestartMode.Section)
            };

        foreach (var (control, endnote, restart) in restartCases)
        {
            var restartModel =
                Read(
                    $@"{{\rtf1\ansi\{control} X\par}}");
            var actual =
                endnote
                    ? restartModel.Notes?.Endnotes.Restart
                    : restartModel.Notes?.Footnotes.Restart;

            Assert(
                actual == restart,
                "RTF note restart control " +
                control +
                " должен иметь точное neutral enum отображение.");
        }

        for (var type = 0;
             type <= 2;
             type++)
        {
            var typeModel =
                Read(
                    $@"{{\rtf1\ansi\fet{type} X\par}}");
            var expected =
                type switch
                {
                    0 => DocumentNoteType.FootnotesOnly,
                    1 => DocumentNoteType.EndnotesOnly,
                    2 => DocumentNoteType.FootnotesAndEndnotes,
                    _ => throw new InvalidOperationException()
                };

            Assert(
                typeModel.Notes?.Type == expected,
                "RTF fet" +
                type +
                " должен иметь точное neutral note type.");
        }

        Assert(
            RejectsInvalid(@"{\rtf1\ansi\fet3 Body\par}") &&
            RejectsInvalid(@"{\rtf1\ansi\fet Body\par}") &&
            RejectsInvalid(@"{\rtf1\ansi\ftnstart0 Body\par}") &&
            RejectsInvalid(@"{\rtf1\ansi\ftnstart Body\par}") &&
            RejectsInvalid(@"{\rtf1\ansi\aftnstart-1 Body\par}") &&
            RejectsInvalid(@"{\rtf1\ansi\chftnsep Body\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\ftnsep A\chftnsepc B\par}}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\ftnsep A\par}{\ftnsep B\par}}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\ftnsep A{\aftnsep B\par}\par}}"),
            "RTF malformed/duplicate note settings должны завершаться structural error.");

        Assert(
            RejectsUnsupported(
                @"{\rtf1\ansi{\ftnsep\trowd\cellx1000\intbl A\cell\row}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi{\ftnsep A\page B\par}}"),
            "RTF table/flow-break content внутри note separator должен оставаться explicit unsupported.");
    }
}
