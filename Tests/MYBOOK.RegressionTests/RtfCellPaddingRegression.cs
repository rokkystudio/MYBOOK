using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF cell padding overrides: четыре clpad* значения и unit
    /// controls, source-order preservation через merges/nested tables,
    /// cellx/trowd reset и HTML metadata. Unit 0 сохраняет fallback к trgaph,
    /// unit 3 предоставляет points, missing unit не интерпретируется как twips.
    /// </summary>
    private static void TestRtfCellPaddingOverrides(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-cell-padding-overrides.rtf");

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
                "\\trowd\\trgaph100" +
                "\\trpaddl20\\trpaddfl3" +
                "\\trpaddr40\\trpaddfr3" +
                "\\trpaddt60\\trpaddft3" +
                "\\trpaddb80\\trpaddfb3" +
                "\\clpadl40\\clpadfl3" +
                "\\clpadr60\\clpadfr3" +
                "\\clpadt80\\clpadft3" +
                "\\clpadb100\\clpadfb3" +
                "\\cellx1000" +
                "\\clpadl-22" +
                "\\clpadr24\\clpadfr0" +
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
            row.CellGapPoints == 5,
            "RTF trgaph должен сохраняться независимо от cell padding overrides.");
        Assert(
            row.PaddingLeftPoints == 1 &&
            row.PaddingRightPoints == 2 &&
            row.PaddingTopPoints == 3 &&
            row.PaddingBottomPoints == 4,
            "RTF row default padding должен сохраняться независимо от clpad overrides.");

        Assert(
            row.Cells.Count == 3,
            "RTF cell padding regression должна содержать три ячейки.");

        var first =
            row.Cells[0];

        Assert(
            first.PaddingTopPoints == 4 &&
            first.PaddingRightPoints == 3 &&
            first.PaddingBottomPoints == 5 &&
            first.PaddingLeftPoints == 2,
            "RTF clpad unit 3 должен предоставлять visual padding points.");
        Assert(
            first.PaddingOverrides.Count == 1,
            "RTF первая physical cell должна иметь один padding record.");

        AssertPadding(
            first.PaddingOverrides[0],
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
            "RTF first cell padding");

        var second =
            row.Cells[1];

        Assert(
            second.PaddingOverrides.Count == 1,
            "RTF частичный clpad override должен создавать один padding record.");
        AssertMeasurement(
            second.PaddingOverrides[0].Left,
            -22,
            null,
            null,
            "RTF clpadl без unit");
        AssertMeasurement(
            second.PaddingOverrides[0].Right,
            24,
            0,
            null,
            "RTF clpadr explicit Null unit");
        Assert(
            second.PaddingOverrides[0].Top == null &&
            second.PaddingOverrides[0].Bottom == null,
            "RTF omitted cell padding sides не должны синтезироваться.");
        Assert(
            second.PaddingLeftPoints == null &&
            second.PaddingRightPoints == null,
            "RTF missing unit и unit 0 не должны синтезировать padding points.");

        Assert(
            row.Cells[2].PaddingOverrides.Count == 0 &&
            row.Cells[2].PaddingTopPoints == null &&
            row.Cells[2].PaddingRightPoints == null &&
            row.Cells[2].PaddingBottomPoints == null &&
            row.Cells[2].PaddingLeftPoints == null,
            "RTF cellx должен сбрасывать cell padding state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\clpadl10\\clpadl30" +
                "\\clpadfl0\\clpadfl3" +
                "\\cellx1000" +
                "\\intbl P\\cell\\row}");

        var precedenceCell =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single()
                .Cells
                .Single();

        AssertMeasurement(
            precedenceCell.PaddingOverrides.Single().Left,
            30,
            3,
            1.5,
            "RTF cell padding last-property precedence");
        Assert(
            precedenceCell.PaddingLeftPoints == 1.5,
            "RTF final clpad unit 3 должен определять visual padding points.");

        var horizontalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\clmgf\\clpadl20\\clpadfl3\\cellx1000" +
                "\\clmrg\\clpadr40\\clpadfr0\\cellx2000" +
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
            horizontalCell.PaddingOverrides.Count == 2,
            "RTF horizontal merge должен сохранять padding records обеих физических cells.");
        AssertMeasurement(
            horizontalCell.PaddingOverrides[0].Left,
            20,
            3,
            1,
            "RTF horizontal merge first padding");
        AssertMeasurement(
            horizontalCell.PaddingOverrides[1].Right,
            40,
            0,
            null,
            "RTF horizontal merge second padding");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\clvmgf\\clpadt20\\clpadft3\\cellx1000" +
                "\\intbl Top\\cell\\row" +
                "\\trowd" +
                "\\clvmrg\\clpadb40\\clpadfb0\\cellx1000" +
                "\\intbl Bottom\\cell\\row}");

        var verticalCell =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows[0]
                .Cells
                .Single();

        Assert(
            verticalCell.RowSpan == 2 &&
            verticalCell.PaddingOverrides.Count == 2,
            "RTF vertical merge должен сохранять padding records origin/continuation в source order.");
        AssertMeasurement(
            verticalCell.PaddingOverrides[0].Top,
            20,
            3,
            1,
            "RTF vertical merge origin padding");
        AssertMeasurement(
            verticalCell.PaddingOverrides[1].Bottom,
            40,
            0,
            null,
            "RTF vertical merge continuation padding");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\clpadl100\\clpadfl3" +
                "\\cellx1000\\nestrow}" +
                "\\intbl\\itap1 Outer after\\cell\\row" +
                "\\pard\\itap0 End\\par}");

        var outerTable =
            nestedModel.Blocks
                .OfType<DocumentTable>()
                .Single();
        var nestedTable =
            outerTable.Rows
                .SelectMany(item => item.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentTable>()
                .Single();

        Assert(
            outerTable.Rows
                .Single()
                .Cells
                .Single()
                .PaddingOverrides.Count == 0,
            "RTF nested cell padding state не должен протекать в parent cell.");
        AssertMeasurement(
            nestedTable.Rows
                .Single()
                .Cells
                .Single()
                .PaddingOverrides
                .Single()
                .Left,
            100,
            3,
            5,
            "RTF nested cell padding");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-cell-padding-count=\"1\"",
            "RTF cell padding count HTML metadata");
        AssertContains(
            html,
            "data-cell-padding-0-left-value=\"40\"",
            "RTF cell padding raw value HTML metadata");
        AssertContains(
            html,
            "data-cell-padding-0-left-unit=\"3\"",
            "RTF cell padding unit HTML metadata");
        AssertContains(
            html,
            "data-cell-padding-0-left-pt=\"2\"",
            "RTF cell padding points HTML metadata");
        AssertContains(
            html,
            "data-cell-padding-0-left-value=\"-22\"",
            "RTF cell padding missing-unit raw HTML metadata");
        AssertContains(
            html,
            "data-cell-padding-0-right-unit=\"0\"",
            "RTF cell padding explicit Null HTML metadata");

        var unitZeroModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trgaph100" +
                "\\clpadl200\\clpadfl0" +
                "\\cellx1000\\intbl Z\\cell\\row}");
        var unitZeroHtml =
            DocumentHtmlRenderer.Render(
                unitZeroModel,
                CreateRenderTheme());

        var unitZeroMetadata =
            "data-cell-padding-0-left-unit=\"0\"";
        var unitZeroMetadataIndex =
            unitZeroHtml.IndexOf(
                unitZeroMetadata,
                StringComparison.Ordinal);

        Assert(
            unitZeroMetadataIndex >= 0,
            "RTF clpad unit 0 должен оставаться в HTML metadata.");

        var unitZeroTagStart =
            unitZeroHtml.LastIndexOf(
                "<td",
                unitZeroMetadataIndex,
                StringComparison.Ordinal);
        var unitZeroTagEnd =
            unitZeroHtml.IndexOf(
                '>',
                unitZeroMetadataIndex);

        Assert(
            unitZeroTagStart >= 0 &&
            unitZeroTagEnd > unitZeroTagStart &&
            !unitZeroHtml[
                unitZeroTagStart..unitZeroTagEnd]
                .Contains(
                    "padding-left:",
                    StringComparison.Ordinal),
            "RTF clpad unit 0 не должен получать CSS padding-left эмуляцию на table cell.");

        var numericControls =
            new[]
            {
                "clpadl",
                "clpadr",
                "clpadt",
                "clpadb",
                "clpadfl",
                "clpadfr",
                "clpadft",
                "clpadfb"
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
                "\\clpadfl3\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clpadft0\\cellx1000\\intbl X\\cell\\row}"),
            "RTF cell padding unit без соответствующего value должен отклоняться.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clpadl20\\clpadfl1\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\clpadt20\\clpadft2\\cellx1000\\intbl X\\cell\\row}"),
            "RTF cell padding должен принимать только документированные units 0 и 3.");
    }

    private static void AssertPadding(
        DocumentTableCellPadding padding,
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
            padding.Top,
            topValue,
            topUnit,
            topPoints,
            name + " top");
        AssertMeasurement(
            padding.Right,
            rightValue,
            rightUnit,
            rightPoints,
            name + " right");
        AssertMeasurement(
            padding.Bottom,
            bottomValue,
            bottomUnit,
            bottomPoints,
            name + " bottom");
        AssertMeasurement(
            padding.Left,
            leftValue,
            leftUnit,
            leftPoints,
            name + " left");
    }
}
