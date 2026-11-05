using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF row padding: raw trpadd* value/unit, unit 0 fallback к
    /// legacy trgaph, unit 3 twips-to-points, missing unit, trowd reset,
    /// nested rows, vertical-merge reconstruction и HTML metadata.
    /// </summary>
    private static void TestRtfRowPadding(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-row-padding.rtf");

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
                "\\trpaddl40\\trpaddfl3" +
                "\\trpaddr60\\trpaddfr3" +
                "\\trpaddt80\\trpaddft3" +
                "\\trpaddb100\\trpaddfb3" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\trgaph120" +
                "\\trpaddl-22" +
                "\\trpaddr24\\trpaddfr0" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd\\cellx1000\\intbl C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 3,
            "RTF row padding regression должна содержать три строки.");

        var first =
            table.Rows[0];

        Assert(
            first.CellGapPoints == 5,
            "RTF trgaph первой строки должен сохраняться отдельно.");
        AssertMeasurement(
            first.PaddingTop,
            80,
            3,
            4,
            "RTF row padding top");
        AssertMeasurement(
            first.PaddingRight,
            60,
            3,
            3,
            "RTF row padding right");
        AssertMeasurement(
            first.PaddingBottom,
            100,
            3,
            5,
            "RTF row padding bottom");
        AssertMeasurement(
            first.PaddingLeft,
            40,
            3,
            2,
            "RTF row padding left");
        Assert(
            first.PaddingTopPoints == 4 &&
            first.PaddingRightPoints == 3 &&
            first.PaddingBottomPoints == 5 &&
            first.PaddingLeftPoints == 2,
            "RTF row padding unit 3 должен предоставлять points.");

        var second =
            table.Rows[1];

        Assert(
            second.CellGapPoints == 6,
            "RTF trgaph второй строки должен сохраняться для unit 0 fallback.");
        AssertMeasurement(
            second.PaddingLeft,
            -22,
            null,
            null,
            "RTF row padding missing unit");
        AssertMeasurement(
            second.PaddingRight,
            24,
            0,
            null,
            "RTF row padding explicit Null unit");
        Assert(
            second.PaddingTop == null &&
            second.PaddingBottom == null,
            "RTF omitted row padding sides не должны синтезироваться.");
        Assert(
            second.PaddingLeftPoints == null &&
            second.PaddingRightPoints == null,
            "RTF missing unit и unit 0 не должны синтезировать row padding points.");

        var third =
            table.Rows[2];

        Assert(
            third.PaddingTop == null &&
            third.PaddingRight == null &&
            third.PaddingBottom == null &&
            third.PaddingLeft == null,
            "RTF trowd должен сбрасывать row padding source state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trpaddl10\\trpaddl30" +
                "\\trpaddfl0\\trpaddfl3" +
                "\\cellx1000\\intbl P\\cell\\row}");

        var precedenceRow =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        AssertMeasurement(
            precedenceRow.PaddingLeft,
            30,
            3,
            1.5,
            "RTF row padding last-property precedence");
        Assert(
            precedenceRow.PaddingLeftPoints == 1.5,
            "RTF final trpaddfl3 должен определять row padding points.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trpaddl200\\trpaddfl3" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];

        AssertMeasurement(
            verticalOriginRow.PaddingLeft,
            200,
            3,
            10,
            "RTF vertical merge row padding");
        Assert(
            verticalOriginRow.Cells.Single().RowSpan == 2,
            "RTF vertical merge regression должна создать RowSpan=2.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\trpaddl100\\trpaddfl3" +
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

        Assert(
            outerTable.Rows.Single().PaddingLeft == null,
            "RTF nested row padding state не должен протекать в parent row.");
        AssertMeasurement(
            nestedTable.Rows.Single().PaddingLeft,
            100,
            3,
            5,
            "RTF nested row padding");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-padding-left-value=\"40\"",
            "RTF row padding raw value HTML metadata");
        AssertContains(
            html,
            "data-row-padding-left-unit=\"3\"",
            "RTF row padding unit HTML metadata");
        AssertContains(
            html,
            "data-row-padding-left-pt=\"2\"",
            "RTF row padding points HTML metadata");
        AssertContains(
            html,
            "data-row-padding-left-value=\"-22\"",
            "RTF row padding missing-unit HTML metadata");
        AssertContains(
            html,
            "data-row-padding-right-unit=\"0\"",
            "RTF row padding explicit Null HTML metadata");

        var numericControls =
            new[]
            {
                "trpaddl",
                "trpaddr",
                "trpaddt",
                "trpaddb",
                "trpaddfl",
                "trpaddfr",
                "trpaddft",
                "trpaddfb"
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
                "\\trpaddfl3\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trpaddft0\\cellx1000\\intbl X\\cell\\row}"),
            "RTF row padding unit без соответствующего value должен отклоняться.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trpaddl20\\trpaddfl1\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trpaddt20\\trpaddft2\\cellx1000\\intbl X\\cell\\row}"),
            "RTF row padding должен принимать только документированные units 0 и 3.");
    }
}
