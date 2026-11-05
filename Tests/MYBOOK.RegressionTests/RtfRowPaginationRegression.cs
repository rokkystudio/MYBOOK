using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF row pagination flags trhdr, trkeep и trkeepfollow:
    /// parameterless grammar, trowd reset, nested rows, vertical-merge
    /// reconstruction и точные HTML metadata/print-break equivalents.
    /// </summary>
    private static void TestRtfRowPagination(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-row-pagination.rtf");

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

        var model =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trhdr\\trkeep\\trkeepfollow" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\cellx1000\\intbl B\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 2,
            "RTF row-pagination fixture должна содержать две строки.");

        var controlledRow =
            table.Rows[0];

        Assert(
            controlledRow.RepeatAsHeader &&
            controlledRow.PreventPageSplit &&
            controlledRow.KeepWithNext,
            "RTF trhdr/trkeep/trkeepfollow должны сохранять pagination semantics.");

        var resetRow =
            table.Rows[1];

        Assert(
            !resetRow.RepeatAsHeader &&
            !resetRow.PreventPageSplit &&
            !resetRow.KeepWithNext,
            "RTF trowd должен сбрасывать row pagination state.");

        var repeatedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trhdr\\trhdr\\trkeep\\trkeep\\trkeepfollow\\trkeepfollow" +
                "\\cellx1000\\intbl Repeat\\cell\\row}");

        var repeatedRow =
            repeatedModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            repeatedRow.RepeatAsHeader &&
            repeatedRow.PreventPageSplit &&
            repeatedRow.KeepWithNext,
            "RTF повторные row pagination flags должны сохранять присутствие property.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trhdr\\trkeep\\trkeepfollow" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];

        Assert(
            verticalOriginRow.RepeatAsHeader &&
            verticalOriginRow.PreventPageSplit &&
            verticalOriginRow.KeepWithNext &&
            verticalOriginRow.Cells.Single().RowSpan == 2,
            "RTF vertical merge reconstruction должен сохранять origin row pagination metadata.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trhdr\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\trkeep\\trkeepfollow" +
                "\\cellx1000\\nestrow}" +
                "\\intbl\\itap1 Outer after\\cell\\row" +
                "\\pard\\itap0 End\\par}");

        var outerTable =
            nestedModel.Blocks
                .OfType<DocumentTable>()
                .Single();
        var nestedTable =
            outerTable.Rows
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single();

        var outerRow =
            outerTable.Rows.Single();
        var nestedRow =
            nestedTable.Rows.Single();

        Assert(
            outerRow.RepeatAsHeader &&
            !outerRow.PreventPageSplit &&
            !outerRow.KeepWithNext,
            "RTF nested pagination flags не должны протекать в parent row.");
        Assert(
            !nestedRow.RepeatAsHeader &&
            nestedRow.PreventPageSplit &&
            nestedRow.KeepWithNext,
            "RTF nested row должен сохранять собственные pagination flags.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-repeat-header=\"true\"",
            "RTF trhdr HTML metadata");
        AssertContains(
            html,
            "data-row-prevent-page-split=\"true\"",
            "RTF trkeep HTML metadata");
        AssertContains(
            html,
            "data-row-keep-with-next=\"true\"",
            "RTF trkeepfollow HTML metadata");
        AssertContains(
            html,
            "break-inside:avoid;page-break-inside:avoid;",
            "RTF trkeep print-break equivalent");
        AssertContains(
            html,
            "break-after:avoid;page-break-after:avoid;",
            "RTF trkeepfollow print-break equivalent");

        foreach (var control in new[]
                 {
                     "trhdr0",
                     "trhdr1",
                     "trkeep0",
                     "trkeep1",
                     "trkeepfollow0",
                     "trkeepfollow1"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как parameterized row pagination flag.");
        }
    }
}
