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
    /// Проверяет RTF Compared Table Cells: insertion/deletion и четыре
    /// vertical merge/split annotations, revtbl author/DTTM metadata,
    /// cell reset, nested tables, merge reconstruction и HTML export.
    /// </summary>
    private static void TestRtfComparedTableCells(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-compared-table-cells.rtf");

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

        var insertionDate =
            Dttm(2024, 1, 2, 3, 4, 2);
        var deletionDate =
            Dttm(2024, 2, 3, 4, 5, 3);
        var mergedAboveDate =
            Dttm(2024, 3, 4, 5, 6, 4);
        var mergedBelowDate =
            Dttm(2024, 4, 5, 6, 7, 5);
        var splitAboveDate =
            Dttm(2024, 5, 6, 7, 8, 6);
        var splitBelowDate =
            Dttm(2024, 6, 7, 8, 9, 0);

        var rtf =
            "{\\rtf1\\ansi\\uc1" +
            "{\\*\\revtbl{Alice;}{\\u1041?\\u1086?\\u1073?;}}" +
            "\\trowd" +
            "\\clins\\clinsauth0\\clinsdttm" +
            insertionDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx1000" +
            "\\cldel\\cldelauth1\\cldeldttm" +
            deletionDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx2000" +
            "\\clmrgd\\clmrgdauth1\\clmrgddttm" +
            mergedAboveDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx3000" +
            "\\clmrgdr\\clmrgdauth0\\clmrgddttm" +
            mergedBelowDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx4000" +
            "\\clsplit\\clmrgdauth1\\clmrgddttm" +
            splitAboveDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx5000" +
            "\\clsplitr\\clmrgdauth0\\clmrgddttm" +
            splitBelowDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx6000" +
            "\\intbl A\\cell B\\cell C\\cell D\\cell E\\cell F\\cell\\row" +
            "\\trowd\\cellx1000\\intbl Plain\\cell\\row" +
            "\\pard Between1\\par" +
            "\\trowd" +
            "\\clmgf\\clins\\clinsauth0\\cellx1000" +
            "\\clmrg\\cldel\\cldelauth1\\cellx2000" +
            "\\intbl HM1\\cell HM2\\cell\\row" +
            "\\pard Between2\\par" +
            "\\trowd\\clvmgf\\clmrgd\\clmrgdauth0\\cellx1000" +
            "\\intbl VM1\\cell\\row" +
            "\\trowd\\clvmrg\\cldel\\cldelauth1\\cellx1000" +
            "\\intbl VM2\\cell\\row" +
            "\\pard Between3\\par" +
            "\\trowd\\cellx4000" +
            "\\intbl\\itap1 Outer before\\par" +
            "\\intbl\\itap2 Nested compare\\nestcell" +
            "{\\*\\nesttableprops\\trowd" +
            "\\clsplitr\\clmrgdauth1\\clmrgddttm" +
            splitBelowDate.ToString(CultureInfo.InvariantCulture) +
            "\\cellx1000\\nestrow}" +
            "\\intbl\\itap1 Outer after\\cell\\row" +
            "\\pard\\itap0 End\\par}";

        var model =
            Read(rtf);

        var tables =
            model.Blocks
                .OfType<DocumentTable>()
                .ToArray();

        Assert(
            tables.Length >= 4,
            "RTF compared-cell fixture должен содержать четыре top-level tables.");

        var comparisonTable =
            tables[0];
        var comparedCells =
            comparisonTable.Rows[0].Cells;

        Assert(
            comparedCells.Count == 6,
            "RTF Compared Table Cells fixture должен содержать шесть физических compare cells.");

        Assert(
            comparedCells[0].Revisions.Single() is
            {
                Kind: DocumentTableCellRevisionKind.Inserted,
                AuthorIndex: 0,
                Author: "Alice",
                DateTime:
                {
                    Year: 2024,
                    Month: 1,
                    Day: 2,
                    Hour: 3,
                    Minute: 4,
                    DayOfWeek: 2
                }
            } &&
            comparedCells[0].Revisions[0].DateTimeRaw ==
                unchecked((uint)insertionDate),
            "RTF clins должен сохранять insertion author и raw+decoded DTTM.");

        Assert(
            comparedCells[1].Revisions.Single() is
            {
                Kind: DocumentTableCellRevisionKind.Deleted,
                AuthorIndex: 1,
                Author: "Боб",
                DateTime:
                {
                    Year: 2024,
                    Month: 2,
                    Day: 3,
                    Hour: 4,
                    Minute: 5,
                    DayOfWeek: 3
                }
            },
            "RTF cldel должен сохранять deletion metadata через revtbl.");

        var expectedKinds =
            new[]
            {
                DocumentTableCellRevisionKind.MergedWithCellAbove,
                DocumentTableCellRevisionKind.MergedWithCellBelow,
                DocumentTableCellRevisionKind.SplitFromCellAbove,
                DocumentTableCellRevisionKind.SplitFromCellBelow
            };

        for (var index = 0; index < expectedKinds.Length; index++)
        {
            Assert(
                comparedCells[index + 2].Revisions.Single().Kind ==
                expectedKinds[index],
                "RTF clmrgd/clmrgdr/clsplit/clsplitr должны сохранять точный compare kind.");
        }

        Assert(
            comparisonTable.Rows[1].Cells.Single().Revisions.Count == 0,
            "RTF trowd/cellx definition reset не должен переносить compare metadata в следующую ячейку.");

        var horizontalCell =
            tables[1].Rows
                .Single()
                .Cells
                .Single();

        Assert(
            horizontalCell.ColumnSpan == 2 &&
            horizontalCell.Revisions.Count == 2 &&
            horizontalCell.Revisions[0].Kind ==
                DocumentTableCellRevisionKind.Inserted &&
            horizontalCell.Revisions[1].Kind ==
                DocumentTableCellRevisionKind.Deleted,
            "RTF horizontal merge reconstruction должен сохранять revisions обеих физических ячеек в source order.");

        var verticalCell =
            tables[2].Rows
                .SelectMany(row => row.Cells)
                .Single(cell => cell.RowSpan == 2);

        Assert(
            verticalCell.Revisions.Count == 2 &&
            verticalCell.Revisions[0].Kind ==
                DocumentTableCellRevisionKind.MergedWithCellAbove &&
            verticalCell.Revisions[1].Kind ==
                DocumentTableCellRevisionKind.Deleted,
            "RTF vertical merge reconstruction должен сохранять compare metadata continuation cell.");

        var nestedTable =
            tables
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
                                "Nested compare",
                                StringComparison.Ordinal)));

        Assert(
            nestedTable.Rows.Single().Cells.Single().Revisions.Single() is
            {
                Kind: DocumentTableCellRevisionKind.SplitFromCellBelow,
                AuthorIndex: 1,
                Author: "Боб"
            },
            "RTF nested table cell должен сохранять собственную compare revision metadata.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-cell-revision-count=\"1\"",
            "RTF compared table cell revision count HTML metadata");
        AssertContains(
            html,
            "data-cell-revision-0-kind=\"inserted\"",
            "RTF insertion cell revision HTML metadata");
        AssertContains(
            html,
            "data-cell-revision-0-author=\"Alice\"",
            "RTF cell revision author HTML metadata");
        AssertContains(
            html,
            "data-cell-revision-0-date=\"2024-01-02T03:04\"",
            "RTF cell revision decoded date HTML metadata");
        AssertContains(
            html,
            "data-cell-revision-count=\"2\"",
            "RTF reconstructed merged-cell revision count HTML metadata");
        AssertContains(
            html,
            "data-cell-revision-1-kind=\"deleted\"",
            "RTF reconstructed merged-cell second revision HTML metadata");
        AssertContains(
            html,
            "Nested compare",
            "RTF compared-cell rendering должен сохранять исходное содержимое nested cell");

        var invalidDate =
            unchecked(
                (int)(
                    ((uint)(2024 - 1900) << 20) |
                    ((uint)13 << 16) |
                    ((uint)1 << 11)));

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\clinsauth0\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\cldelauth0\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\clmrgdauth0\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\clins\clinsauth1\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\clins\clinsauth\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A;}}\trowd\clmrgd\clsplit\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi{\\*\\revtbl{A;}}\\trowd\\cldel\\cldeldttm" +
                invalidDate.ToString(CultureInfo.InvariantCulture) +
                "\\cellx1000\\intbl X\\cell\\row}"),
            "RTF malformed Compared Table Cells metadata должен завершаться structural error.");
    }
}
