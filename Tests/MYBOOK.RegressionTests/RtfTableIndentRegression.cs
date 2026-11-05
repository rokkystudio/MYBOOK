using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF table indentation через tblind/tblindtype: source pair,
    /// documented defaults, dxa/nil/pct/auto units, ignored invalid unit,
    /// first-row table projection, RTL leading edge, nested tables и reset.
    /// </summary>
    private static void TestRtfTableIndent(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-indent.rtf");

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
                "\\trowd\\tblind720\\tblindtype1" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\tblind-200" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd\\tblindtype2" +
                "\\cellx1000\\intbl C\\cell\\row" +
                "\\trowd\\cellx1000\\intbl D\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 4,
            "RTF table-indent fixture должна содержать четыре строки.");

        AssertIndent(
            table.Indent,
            720,
            1,
            720,
            1,
            36,
            null,
            "RTF table first-row indent projection");

        AssertIndent(
            table.Rows[0].TableIndent,
            720,
            1,
            720,
            1,
            36,
            null,
            "RTF row dxa table indent");

        AssertIndent(
            table.Rows[1].TableIndent,
            -200,
            null,
            -200,
            1,
            -10,
            null,
            "RTF tblind default tblindtype");

        AssertIndent(
            table.Rows[2].TableIndent,
            null,
            2,
            0,
            2,
            null,
            null,
            "RTF tblindtype nil default tblind value");

        Assert(
            table.Rows[3].TableIndent == null,
            "RTF trowd должен сбрасывать explicit table-indent source state.");

        var autoModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tblind500\\tblindtype0" +
                "\\cellx1000\\intbl Auto\\cell\\row}");

        AssertIndent(
            autoModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Indent,
            500,
            0,
            500,
            0,
            null,
            null,
            "RTF auto table indent");

        var percentModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\taprtl\\tblind1250\\tblindtype3" +
                "\\cellx1000\\intbl RTL\\cell\\row}");

        var percentTable =
            percentModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            percentTable.RightToLeft,
            "RTF table-indent RTL fixture должна сохранять taprtl.");
        AssertIndent(
            percentTable.Indent,
            1250,
            3,
            1250,
            3,
            null,
            25,
            "RTF pct table indent");

        var ignoredUnitModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tblind100\\tblindtype9" +
                "\\cellx1000\\intbl InvalidUnit\\cell\\row}");

        AssertIndent(
            ignoredUnitModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Indent,
            100,
            9,
            100,
            1,
            5,
            null,
            "RTF invalid tblindtype ignored to default dxa");

        var negativeUnitModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tblind-40\\tblindtype-1" +
                "\\cellx1000\\intbl NegativeUnit\\cell\\row}");

        AssertIndent(
            negativeUnitModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Indent,
            -40,
            -1,
            -40,
            1,
            -2,
            null,
            "RTF negative invalid tblindtype ignored to default dxa");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tblind300\\tblindtype1" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd\\tblind400\\tblindtype1" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();

        AssertIndent(
            verticalTable.Rows[0].TableIndent,
            300,
            1,
            300,
            1,
            15,
            null,
            "RTF vertical merge origin table indent");
        Assert(
            verticalTable.Rows[0].Cells.Single().RowSpan == 2,
            "RTF table-indent vertical merge fixture должна создать RowSpan=2.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tblind200\\tblindtype1\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\taprtl" +
                "\\tblind500\\tblindtype3" +
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

        AssertIndent(
            outerTable.Indent,
            200,
            1,
            200,
            1,
            10,
            null,
            "RTF parent table indent");
        Assert(
            !outerTable.RightToLeft,
            "RTF parent table-indent fixture должна оставаться LTR.");

        AssertIndent(
            nestedTable.Indent,
            500,
            3,
            500,
            3,
            null,
            10,
            "RTF nested pct table indent");
        Assert(
            nestedTable.RightToLeft,
            "RTF nested table-indent fixture должна сохранять RTL direction.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-table-indent-source-value=\"720\"",
            "RTF table indent source value HTML metadata");
        AssertContains(
            html,
            "data-table-indent-source-unit=\"1\"",
            "RTF table indent source unit HTML metadata");
        AssertContains(
            html,
            "data-table-indent-kind=\"dxa\"",
            "RTF table indent dxa HTML metadata");
        AssertContains(
            html,
            "data-table-indent-pt=\"36\"",
            "RTF table indent points HTML metadata");
        AssertContains(
            html,
            "data-table-indent-leading-edge=\"left\"",
            "RTF LTR table indent leading edge HTML metadata");
        AssertContains(
            html,
            "data-row-table-indent-source-value=\"-200\"",
            "RTF row table indent signed source HTML metadata");
        AssertContains(
            html,
            "data-row-table-indent-unit=\"1\"",
            "RTF row table indent default effective unit HTML metadata");
        AssertContains(
            html,
            "data-row-table-indent-kind=\"nil\"",
            "RTF row table indent nil HTML metadata");

        var percentHtml =
            DocumentHtmlRenderer.Render(
                percentModel,
                CreateRenderTheme());

        AssertContains(
            percentHtml,
            "data-table-indent-percent=\"25\"",
            "RTF pct table indent HTML metadata");
        AssertContains(
            percentHtml,
            "data-table-indent-leading-edge=\"right\"",
            "RTF RTL table indent leading edge HTML metadata");

        var ignoredUnitHtml =
            DocumentHtmlRenderer.Render(
                ignoredUnitModel,
                CreateRenderTheme());

        AssertContains(
            ignoredUnitHtml,
            "data-table-indent-source-unit=\"9\"",
            "RTF ignored tblindtype source unit HTML metadata");
        AssertContains(
            ignoredUnitHtml,
            "data-table-indent-unit=\"1\"",
            "RTF ignored tblindtype effective unit HTML metadata");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd\\tblind" +
                "\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd\\tblindtype" +
                "\\cellx1000\\intbl X\\cell\\row}"),
            "RTF tblind/tblindtype без numeric parameter должны завершаться structural error.");
    }

    /// <summary>
    /// Проверяет source и effective representation одного RTF table indent.
    /// </summary>
    private static void AssertIndent(
        DocumentTableIndent? indent,
        int? sourceValue,
        int? sourceUnit,
        int value,
        int unit,
        double? points,
        double? percent,
        string message)
    {
        Assert(
            indent is
            {
                SourceValue: var actualSourceValue,
                SourceUnit: var actualSourceUnit,
                Value: var actualValue,
                Unit: var actualUnit,
                Points: var actualPoints,
                Percent: var actualPercent
            } &&
            actualSourceValue == sourceValue &&
            actualSourceUnit == sourceUnit &&
            actualValue == value &&
            actualUnit == unit &&
            actualPoints == points &&
            actualPercent == percent,
            message);
    }
}
