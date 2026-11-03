using System.Globalization;
using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF revision tracking: ordered revtbl, Unicode authors,
    /// insertion/deletion/formatting/move marks, 32-bit DTTM, group scoping,
    /// plain-reset и HTML data-* metadata без визуального принятия изменений.
    /// </summary>
    private static void TestRtfRevisionTracking(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-revisions.rtf");

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

        static int Dttm(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int dayOfWeek)
        {
            var raw =
                (uint)minute |
                ((uint)hour << 6) |
                ((uint)day << 11) |
                ((uint)month << 16) |
                ((uint)(year - 1900) << 20) |
                ((uint)dayOfWeek << 29);

            return unchecked((int)raw);
        }

        var insertionDate =
            Dttm(
                2024,
                2,
                29,
                13,
                45,
                4);
        var deletionDate =
            Dttm(
                2025,
                3,
                14,
                9,
                26,
                5);
        var formattingDate =
            Dttm(
                2026,
                11,
                3,
                15,
                44,
                2);
        var moveDate =
            Dttm(
                2023,
                12,
                31,
                23,
                59,
                0);

        var model =
            Read(
                "{\\rtf1\\ansi\\uc1" +
                "{\\*\\revtbl{Alice;}{\\u1041?\\u1086?\\u1073?;}}" +
                "Normal " +
                "{\\revised\\revauth0\\revdttm" +
                insertionDate.ToString(CultureInfo.InvariantCulture) +
                " Inserted}" +
                "{\\deleted\\revauthdel1\\revdttmdel" +
                deletionDate.ToString(CultureInfo.InvariantCulture) +
                " Deleted}" +
                "{\\crauth0\\crdate" +
                formattingDate.ToString(CultureInfo.InvariantCulture) +
                "\\b Formatted}" +
                "{\\mvf\\deleted\\revauthdel0\\revdttmdel" +
                moveDate.ToString(CultureInfo.InvariantCulture) +
                "\\mvauth1\\mvdate" +
                moveDate.ToString(CultureInfo.InvariantCulture) +
                " MoveFrom}" +
                "{\\mvt\\revised\\revauth1\\revdttm" +
                moveDate.ToString(CultureInfo.InvariantCulture) +
                "\\mvauth1\\mvdate" +
                moveDate.ToString(CultureInfo.InvariantCulture) +
                " MoveTo}" +
                "{\\revised\\revauth0 Scoped}\\plain Plain\\par}");

        Assert(
            model.RevisionAuthors.Count == 2 &&
            model.RevisionAuthors[0] == "Alice" &&
            model.RevisionAuthors[1] == "Боб",
            "RTF revtbl должен сохранять ordered Unicode author table.");

        var inlines =
            model.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .ToArray();

        DocumentInline Find(string text)
        {
            return inlines.Single(inline =>
                inline.Text.Contains(
                    text,
                    StringComparison.Ordinal));
        }

        var inserted =
            Find("Inserted");
        var insertion =
            inserted.Revisions.Single();

        Assert(
            insertion.Kind ==
                DocumentRevisionKind.Insertion &&
            insertion.AuthorIndex == 0 &&
            insertion.Author == "Alice" &&
            insertion.DateTimeRaw ==
                unchecked((uint)insertionDate) &&
            insertion.DateTime is
            {
                Year: 2024,
                Month: 2,
                Day: 29,
                Hour: 13,
                Minute: 45,
                DayOfWeek: 4
            },
            "RTF revised/revauth/revdttm должны сохранять insertion metadata и DTTM fields.");

        var deleted =
            Find("Deleted");
        var deletion =
            deleted.Revisions.Single();

        Assert(
            deletion.Kind ==
                DocumentRevisionKind.Deletion &&
            deletion.AuthorIndex == 1 &&
            deletion.Author == "Боб" &&
            deletion.DateTimeRaw ==
                unchecked((uint)deletionDate) &&
            deletion.DateTime is
            {
                Year: 2025,
                Month: 3,
                Day: 14,
                Hour: 9,
                Minute: 26,
                DayOfWeek: 5
            },
            "RTF deleted/revauthdel/revdttmdel должны сохранять deletion metadata.");

        var formatted =
            Find("Formatted");
        var formatting =
            formatted.Revisions.Single();

        Assert(
            formatted.Bold &&
            formatting.Kind ==
                DocumentRevisionKind.Formatting &&
            formatting.AuthorIndex == 0 &&
            formatting.Author == "Alice" &&
            formatting.DateTimeRaw ==
                unchecked((uint)formattingDate),
            "RTF crauth/crdate должны сохранять formatting revision независимо от revised/deleted flags.");

        var moveFrom =
            Find("MoveFrom");
        var moveFromKinds =
            moveFrom.Revisions
                .Select(revision => revision.Kind)
                .ToArray();

        Assert(
            moveFromKinds.SequenceEqual(
                new[]
                {
                    DocumentRevisionKind.Deletion,
                    DocumentRevisionKind.MoveFrom
                }) &&
            moveFrom.Revisions[0].Author == "Alice" &&
            moveFrom.Revisions[1].Author == "Боб" &&
            moveFrom.Revisions[1].DateTimeRaw ==
                unchecked((uint)moveDate),
            "RTF mvf должен сосуществовать с deletion metadata и использовать mvauth/mvdate.");

        var moveTo =
            Find("MoveTo");
        var moveToKinds =
            moveTo.Revisions
                .Select(revision => revision.Kind)
                .ToArray();

        Assert(
            moveToKinds.SequenceEqual(
                new[]
                {
                    DocumentRevisionKind.Insertion,
                    DocumentRevisionKind.MoveTo
                }) &&
            moveTo.Revisions[0].Author == "Боб" &&
            moveTo.Revisions[1].Author == "Боб",
            "RTF mvt должен сосуществовать с insertion metadata.");

        Assert(
            Find("Scoped").Revisions is
            [
                {
                    Kind: DocumentRevisionKind.Insertion,
                    Author: "Alice"
                }
            ] &&
            Find("Plain").Revisions.Count == 0,
            "RTF revision character properties должны следовать group scope и сбрасываться plain.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-revision-insertion=\"true\"",
            "RTF insertion HTML metadata");
        AssertContains(
            html,
            "data-revision-insertion-author=\"Alice\"",
            "RTF insertion author HTML metadata");
        AssertContains(
            html,
            "data-revision-deletion-author=\"Боб\"",
            "RTF deletion Unicode author HTML metadata");
        AssertContains(
            html,
            "data-revision-formatting=\"true\"",
            "RTF formatting revision HTML metadata");
        AssertContains(
            html,
            "data-revision-move-from=\"true\"",
            "RTF move-from HTML metadata");
        AssertContains(
            html,
            "data-revision-move-to=\"true\"",
            "RTF move-to HTML metadata");
        AssertContains(
            html,
            "data-revision-insertion-date=\"2024-02-29T13:45\"",
            "RTF DTTM decoded HTML metadata");
        AssertContains(
            html,
            "Inserted",
            "RTF tracked insertion remains visible");
        AssertContains(
            html,
            "Deleted",
            "RTF tracked deletion remains visible");

        var invalidMonthRaw =
            unchecked((int)(
                ((uint)(2024 - 1900) << 20) |
                ((uint)1 << 11)));

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}{B;}}{\revised\revauth2 X}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}{\revised\revauth X}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}{\revised\revdttm X}\par}") &&
            RejectsInvalid(
                $@"{{\rtf1\ansi{{\*\revtbl{{A;}}}}{{\revised\revauth0\revdttm{invalidMonthRaw} X}}\par}}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A}} X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}{\*\revtbl{B;}} X\par}"),
            "RTF malformed revision metadata должен завершаться structural error.");

        Assert(
            RejectsUnsupported(
                "{\\rtf1\\ansi{\\*\\revtbl{A\\'00B;}} X\\par}"),
            "RTF revtbl conflict/binary author entry должен оставаться explicit unsupported.");
    }
}
