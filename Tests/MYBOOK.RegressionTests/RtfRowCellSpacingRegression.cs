using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF row cell spacing: raw trspd* value/unit, unit 0 ignore,
    /// unit 3 twips-to-points, missing unit, trowd reset, nested rows,
    /// vertical-merge reconstruction и HTML metadata.
    /// </summary>
    private static void TestRtfRowCellSpacing(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-row-cell-spacing.rtf");

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
                "\\trspdl40\\trspdfl3" +
                "\\trspdr60\\trspdfr3" +
                "\\trspdt80\\trspdft3" +
                "\\trspdb100\\trspdfb3" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd" +
                "\\trspdl-22" +
                "\\trspdr24\\trspdfr0" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd\\cellx1000\\intbl C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 3,
            "RTF row cell spacing regression должна содержать три строки.");

        var first =
            table.Rows[0];

        AssertMeasurement(
            first.CellSpacingTop,
            80,
            3,
            4,
            "RTF row cell spacing top");
        AssertMeasurement(
            first.CellSpacingRight,
            60,
            3,
            3,
            "RTF row cell spacing right");
        AssertMeasurement(
            first.CellSpacingBottom,
            100,
            3,
            5,
            "RTF row cell spacing bottom");
        AssertMeasurement(
            first.CellSpacingLeft,
            40,
            3,
            2,
            "RTF row cell spacing left");
        Assert(
            first.CellSpacingTopPoints == 4 &&
            first.CellSpacingRightPoints == 3 &&
            first.CellSpacingBottomPoints == 5 &&
            first.CellSpacingLeftPoints == 2,
            "RTF row cell spacing unit 3 должен предоставлять points.");

        var second =
            table.Rows[1];

        AssertMeasurement(
            second.CellSpacingLeft,
            -22,
            null,
            null,
            "RTF row cell spacing missing unit");
        AssertMeasurement(
            second.CellSpacingRight,
            24,
            0,
            null,
            "RTF row cell spacing explicit Null unit");
        Assert(
            second.CellSpacingTop == null &&
            second.CellSpacingBottom == null,
            "RTF omitted row cell spacing sides не должны синтезироваться.");
        Assert(
            second.CellSpacingLeftPoints == null &&
            second.CellSpacingRightPoints == null,
            "RTF missing unit и unit 0 не должны синтезировать row cell spacing points.");

        var third =
            table.Rows[2];

        Assert(
            third.CellSpacingTop == null &&
            third.CellSpacingRight == null &&
            third.CellSpacingBottom == null &&
            third.CellSpacingLeft == null,
            "RTF trowd должен сбрасывать row cell spacing source state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trspdl10\\trspdl30" +
                "\\trspdfl0\\trspdfl3" +
                "\\cellx1000\\intbl P\\cell\\row}");

        var precedenceRow =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        AssertMeasurement(
            precedenceRow.CellSpacingLeft,
            30,
            3,
            1.5,
            "RTF row cell spacing last-property precedence");
        Assert(
            precedenceRow.CellSpacingLeftPoints == 1.5,
            "RTF final trspdfl3 должен определять row cell spacing points.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trspdl200\\trspdfl3" +
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
            verticalOriginRow.CellSpacingLeft,
            200,
            3,
            10,
            "RTF vertical merge row cell spacing");
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
                "\\trspdl100\\trspdfl3" +
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
            outerTable.Rows.Single().CellSpacingLeft == null,
            "RTF nested row cell spacing state не должен протекать в parent row.");
        AssertMeasurement(
            nestedTable.Rows.Single().CellSpacingLeft,
            100,
            3,
            5,
            "RTF nested row cell spacing");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-cell-spacing-left-value=\"40\"",
            "RTF row cell spacing raw value HTML metadata");
        AssertContains(
            html,
            "data-row-cell-spacing-left-unit=\"3\"",
            "RTF row cell spacing unit HTML metadata");
        AssertContains(
            html,
            "data-row-cell-spacing-left-pt=\"2\"",
            "RTF row cell spacing points HTML metadata");
        AssertContains(
            html,
            "data-row-cell-spacing-left-value=\"-22\"",
            "RTF row cell spacing missing-unit HTML metadata");
        AssertContains(
            html,
            "data-row-cell-spacing-right-unit=\"0\"",
            "RTF row cell spacing explicit Null HTML metadata");

        var numericControls =
            new[]
            {
                "trspdl",
                "trspdr",
                "trspdt",
                "trspdb",
                "trspdfl",
                "trspdfr",
                "trspdft",
                "trspdfb"
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
                "\\trspdfl3\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trspdft0\\cellx1000\\intbl X\\cell\\row}"),
            "RTF row cell spacing unit без соответствующего value должен отклоняться.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trspdl20\\trspdfl1\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd" +
                "\\trspdt20\\trspdft2\\cellx1000\\intbl X\\cell\\row}"),
            "RTF row cell spacing должен принимать только документированные units 0 и 3.");
    }
}
