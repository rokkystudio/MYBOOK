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
    /// Проверяет RTF paragraph/table-row property revisions: \prauth/\prdate
    /// и \trauth/\trdate с revtbl author resolution, DTTM, reset semantics,
    /// nested-table propagation и HTML metadata без визуальной интерпретации.
    /// </summary>
    private static void TestRtfPropertyRevisions(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-property-revisions.rtf");

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

        static int Dttm(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int dayOfWeek)
        {
            return unchecked(
                (int)(
                    (uint)minute |
                    ((uint)hour << 6) |
                    ((uint)day << 11) |
                    ((uint)month << 16) |
                    ((uint)(year - 1900) << 20) |
                    ((uint)dayOfWeek << 29)));
        }

        var paragraphDate =
            Dttm(
                2024,
                6,
                30,
                12,
                34,
                0);
        var rowDate =
            Dttm(
                2025,
                1,
                2,
                3,
                4,
                4);
        var nestedRowDate =
            Dttm(
                2026,
                11,
                3,
                15,
                44,
                2);

        var rtf =
            "{\\rtf1\\ansi\\uc1" +
            "{\\*\\revtbl{Alice;}{\\u1041?\\u1086?\\u1073?;}}" +
            "\\pard\\prauth1\\prdate" +
            paragraphDate.ToString(CultureInfo.InvariantCulture) +
            " Revised paragraph\\par" +
            "\\pard Plain paragraph\\par" +
            "\\trowd\\trauth0\\trdate" +
            rowDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx2000\\intbl Revised row\\cell\\row" +
            "\\trowd\\cellx2000\\intbl Plain row\\cell\\row" +
            "\\pard Between\\par" +
            "\\trowd\\cellx4000" +
            "\\intbl\\itap1 Outer before\\par" +
            "\\intbl\\itap2 Nested revised\\nestcell" +
            "{\\*\\nesttableprops\\trowd\\trauth1\\trdate" +
            nestedRowDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx1000\\nestrow}" +
            "\\intbl\\itap1 Outer after\\cell\\row" +
            "\\pard\\itap0 End\\par}";

        var model =
            Read(rtf);

        var paragraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Where(paragraph =>
                    paragraph.Inlines.Count > 0)
                .ToArray();

        var revisedParagraph =
            paragraphs.Single(paragraph =>
                string.Concat(
                        paragraph.Inlines.Select(inline => inline.Text))
                    .Contains(
                        "Revised paragraph",
                        StringComparison.Ordinal));
        var plainParagraph =
            paragraphs.Single(paragraph =>
                string.Concat(
                        paragraph.Inlines.Select(inline => inline.Text))
                    .Contains(
                        "Plain paragraph",
                        StringComparison.Ordinal));

        Assert(
            revisedParagraph.PropertyRevision is
            {
                AuthorIndex: 1,
                Author: "Боб",
                DateTimeRaw: not null,
                DateTime:
                {
                    Year: 2024,
                    Month: 6,
                    Day: 30,
                    Hour: 12,
                    Minute: 34,
                    DayOfWeek: 0
                }
            } &&
            revisedParagraph.PropertyRevision.DateTimeRaw ==
                unchecked((uint)paragraphDate) &&
            plainParagraph.PropertyRevision == null,
            "RTF prauth/prdate должны сохранять paragraph-property revision и сбрасываться pard.");

        var topLevelTables =
            model.Blocks
                .OfType<DocumentTable>()
                .ToArray();

        Assert(
            topLevelTables.Length >= 2,
            "RTF fixture должен содержать отдельную обычную и nested table.");

        var revisedRow =
            topLevelTables[0].Rows[0];
        var plainRow =
            topLevelTables[0].Rows[1];

        Assert(
            revisedRow.PropertyRevision is
            {
                AuthorIndex: 0,
                Author: "Alice",
                DateTime:
                {
                    Year: 2025,
                    Month: 1,
                    Day: 2,
                    Hour: 3,
                    Minute: 4,
                    DayOfWeek: 4
                }
            } &&
            revisedRow.PropertyRevision.DateTimeRaw ==
                unchecked((uint)rowDate) &&
            plainRow.PropertyRevision == null,
            "RTF trauth/trdate должны сохранять row-property revision и сбрасываться trowd.");

        var nestedTable =
            topLevelTables
                .SelectMany(table => table.Rows)
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single(table =>
                    table.Rows
                        .SelectMany(row => row.Cells)
                        .SelectMany(cell => cell.Inlines)
                        .Any(inline =>
                            inline.Text.Contains(
                                "Nested revised",
                                StringComparison.Ordinal)));

        Assert(
            nestedTable.Rows.Single().PropertyRevision is
            {
                AuthorIndex: 1,
                Author: "Боб",
                DateTime:
                {
                    Year: 2026,
                    Month: 11,
                    Day: 3,
                    Hour: 15,
                    Minute: 44,
                    DayOfWeek: 2
                }
            },
            "RTF nested table row должен сохранять собственные trauth/trdate metadata.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-paragraph-revision=\"true\"",
            "RTF paragraph property revision HTML metadata");
        AssertContains(
            html,
            "data-paragraph-revision-author=\"Боб\"",
            "RTF paragraph property revision author HTML metadata");
        AssertContains(
            html,
            "data-paragraph-revision-date=\"2024-06-30T12:34\"",
            "RTF paragraph property revision date HTML metadata");
        AssertContains(
            html,
            "data-row-revision=\"true\"",
            "RTF row property revision HTML metadata");
        AssertContains(
            html,
            "data-row-revision-author=\"Alice\"",
            "RTF row property revision author HTML metadata");
        AssertContains(
            html,
            "data-row-revision-author=\"Боб\"",
            "RTF nested row property revision author HTML metadata");

        var invalidDate =
            unchecked(
                (int)(
                    ((uint)(2024 - 1900) << 20) |
                    ((uint)13 << 16) |
                    ((uint)1 << 11)));

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\pard\prauth1 X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\pard\prauth X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\pard\prdate X\par}") &&
            RejectsInvalid(
                $@"{{\rtf1\ansi{{\*\revtbl{{A;}}}}\pard\prauth0\prdate{invalidDate} X\par}}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\trauth1\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\trdate\cellx1000\intbl X\cell\row}"),
            "RTF malformed paragraph/table-row revision metadata должен завершаться structural error.");
    }
}
