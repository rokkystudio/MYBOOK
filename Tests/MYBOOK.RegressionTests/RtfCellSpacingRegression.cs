using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF cell spacing overrides: четыре clsp* значения и unit
    /// controls, source-order preservation через merges/nested tables,
    /// trowd/cellx reset и HTML metadata без CSS border-spacing эмуляции.
    /// </summary>
    private static void TestRtfCellSpacingOverrides(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-cell-spacing-overrides.rtf");

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
                "\\trowd" +
                "\\trspdl20\\trspdfl3" +
                "\\trspdr40\\trspdfr3" +
                "\\trspdt60\\trspdft3" +
                "\\trspdb80\\trspdfb3" +
                "\\clspl40\\clspfl3" +
                "\\clspr60\\clspfr3" +
                "\\clspt80\\clspft3" +
                "\\clspb100\\clspfb3" +
                "\\cellx1000" +
                "\\clspl-22" +
                "\\clspr24\\clspfr0" +
                "\\cellx2000" +
                "\\cellx3000" +
                "\\intbl A\\cell B\\cell C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();
        var row =
            table.Rows.Single();

        Assert(
            row.CellSpacingLeftPoints == 1 &&
            row.CellSpacingRightPoints == 2 &&
            row.CellSpacingTopPoints == 3 &&
            row.CellSpacingBottomPoints == 4,
            "RTF row default cell spacing должен сохраняться независимо от cell overrides.");

        Assert(
            row.Cells.Count == 3,
            "RTF cell spacing regression должна содержать три ячейки.");

        var first =
            row.Cells[0];

        Assert(
            first.SpacingOverrides.Count == 1,
            "RTF первая физическая cell должна иметь один spacing record.");

        AssertSpacing(
            first.SpacingOverrides[0],
            topValue: 80,
            topUnit: 3,
            topPoints: 4,
            rightValue: 60,
            rightUnit: 3,
            rightPoints: 3,
            bottomValue: 100,
            bottomUnit: 3,
            bottomPoints: 5,
            leftValue: 40,
            leftUnit: 3,
            leftPoints: 2,
            "RTF first cell spacing");

        var second =
            row.Cells[1];

        Assert(
            second.SpacingOverrides.Count == 1,
            "RTF вторая physical cell должна иметь spacing record при частичном override.");

        AssertMeasurement(
            second.SpacingOverrides[0].Left,
            -22,
            null,
            null,
            "RTF clspl без unit");
        AssertMeasurement(
            second.SpacingOverrides[0].Right,
            24,
            0,
            null,
            "RTF clspr explicit Null unit");
        Assert(
            second.SpacingOverrides[0].Top == null &&
            second.SpacingOverrides[0].Bottom == null,
            "RTF omitted cell spacing sides не должны синтезироваться.");

        Assert(
            row.Cells[2].SpacingOverrides.Count == 0,
            "RTF cellx должен сбрасывать spacing override перед следующей cell.");

        var horizontalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\clmgf\\clspl20\\clspfl3\\cellx1000" +
                "\\clmrg\\clspr40\\clspfr3\\cellx2000" +
                "\\intbl L\\cell R\\cell\\row}");

        var horizontalCell =
            horizontalMerge.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single()
                .Cells
                .Single();

        Assert(
            horizontalCell.ColumnSpan == 2 &&
            horizontalCell.SpacingOverrides.Count == 2,
            "RTF horizontal merge должен сохранять spacing records обеих физических cells.");

        AssertMeasurement(
            horizontalCell.SpacingOverrides[0].Left,
            20,
            3,
            1,
            "RTF horizontal merge first spacing");
        AssertMeasurement(
            horizontalCell.SpacingOverrides[1].Right,
            40,
            3,
            2,
            "RTF horizontal merge second spacing");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trspol200\\trspofl3" +
                "\\clvmgf\\clspt20\\clspft3\\cellx1000" +
                "\\intbl Top\\cell\\row" +
                "\\trowd" +
                "\\clvmrg\\clspb40\\clspfb3\\cellx1000" +
                "\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];
        var verticalCell =
            verticalOriginRow.Cells.Single();

        Assert(
            verticalOriginRow.OuterCellSpacingLeft is
            {
                Value: 200,
                Unit: 3,
                Points: 10
            },
            "RTF vertical merge reconstruction должна сохранять outer row measurements.");
        Assert(
            verticalCell.RowSpan == 2 &&
            verticalCell.SpacingOverrides.Count == 2,
            "RTF vertical merge должен сохранять spacing records origin/continuation в source order.");
        AssertMeasurement(
            verticalCell.SpacingOverrides[0].Top,
            20,
            3,
            1,
            "RTF vertical merge origin spacing");
        AssertMeasurement(
            verticalCell.SpacingOverrides[1].Bottom,
            40,
            3,
            2,
            "RTF vertical merge continuation spacing");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\clspl100\\clspfl3" +
                "\\cellx1000\\nestrow}" +
                "\\intbl\\itap1 Outer after\\cell\\row" +
                "\\pard\\itap0 End\\par}");

        var nestedTable =
            nestedModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .SelectMany(item => item.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single();

        AssertMeasurement(
            nestedTable.Rows
                .Single()
                .Cells
                .Single()
                .SpacingOverrides
                .Single()
                .Left,
            100,
            3,
            5,
            "RTF nested cell spacing");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-cell-spacing-count=\"1\"",
            "RTF cell spacing count HTML metadata");
        AssertContains(
            html,
            "data-cell-spacing-0-left-value=\"40\"",
            "RTF cell spacing raw value HTML metadata");
        AssertContains(
            html,
            "data-cell-spacing-0-left-unit=\"3\"",
            "RTF cell spacing unit HTML metadata");
        AssertContains(
            html,
            "data-cell-spacing-0-left-pt=\"2\"",
            "RTF cell spacing points HTML metadata");
        AssertContains(
            html,
            "data-cell-spacing-0-left-value=\"-22\"",
            "RTF cell spacing missing-unit raw HTML metadata");
        AssertContains(
            html,
            "data-cell-spacing-0-right-unit=\"0\"",
            "RTF cell spacing explicit Null HTML metadata");

        var numericControls =
            new[]
            {
                "clspl",
                "clspr",
                "clspt",
                "clspb",
                "clspfl",
                "clspfr",
                "clspft",
                "clspfb"
            };

        foreach (var control in numericControls)
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} без numeric parameter должен завершаться structural error.");
        }

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clspfl3\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clspft0\\cellx1000\\intbl X\\cell\\row}"),
            "RTF cell spacing unit без соответствующего value должен отклоняться.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clspl20\\clspfl1\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clspt20\\clspft2\\cellx1000\\intbl X\\cell\\row}"),
            "RTF cell spacing должен принимать только документированные units 0 и 3.");
    }

    private static void AssertSpacing(
        DocumentTableCellSpacing spacing,
        int topValue,
        int? topUnit,
        double? topPoints,
        int rightValue,
        int? rightUnit,
        double? rightPoints,
        int bottomValue,
        int? bottomUnit,
        double? bottomPoints,
        int leftValue,
        int? leftUnit,
        double? leftPoints,
        string name)
    {
        AssertMeasurement(
            spacing.Top,
            topValue,
            topUnit,
            topPoints,
            name + " top");
        AssertMeasurement(
            spacing.Right,
            rightValue,
            rightUnit,
            rightPoints,
            name + " right");
        AssertMeasurement(
            spacing.Bottom,
            bottomValue,
            bottomUnit,
            bottomPoints,
            name + " bottom");
        AssertMeasurement(
            spacing.Left,
            leftValue,
            leftUnit,
            leftPoints,
            name + " left");
    }
}
