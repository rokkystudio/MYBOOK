using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет базовые RTF row measurements: signed trleft/trgaph twips,
    /// trrh auto/minimum/exact semantics, trowd reset, nested rows,
    /// vertical-merge reconstruction и точные HTML source metadata.
    /// </summary>
    private static void TestRtfBasicRowMeasurements(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-basic-row-measurements.rtf");

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
                "\\trowd\\trleft-120\\trgaph115\\trrh0" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\trleft240\\trgaph40\\trrh200" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd\\trleft-60\\trgaph-20\\trrh-300" +
                "\\cellx1000\\intbl C\\cell\\row" +
                "\\trowd\\cellx1000\\intbl D\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 4,
            "RTF basic row measurement regression должна содержать четыре строки.");

        var autoRow =
            table.Rows[0];

        Assert(
            autoRow.LeftOffsetTwips == -120 &&
            autoRow.LeftOffsetPoints == -6,
            "RTF trleft должен сохранять signed raw twips и points.");
        Assert(
            autoRow.CellGapTwips == 115 &&
            autoRow.CellGapPoints == 5.75,
            "RTF trgaph должен сохранять half-cell-gap raw twips и points.");
        Assert(
            autoRow.HeightTwips == 0 &&
            autoRow.HeightPoints == 0 &&
            !autoRow.HeightExact,
            "RTF trrh0 должен сохранять explicit auto-height source value.");

        var minimumRow =
            table.Rows[1];

        Assert(
            minimumRow.LeftOffsetTwips == 240 &&
            minimumRow.LeftOffsetPoints == 12,
            "RTF positive trleft должен сохраняться без нормализации.");
        Assert(
            minimumRow.CellGapTwips == 40 &&
            minimumRow.CellGapPoints == 2,
            "RTF positive trgaph должен сохраняться без нормализации.");
        Assert(
            minimumRow.HeightTwips == 200 &&
            minimumRow.HeightPoints == 10 &&
            !minimumRow.HeightExact,
            "RTF positive trrh должен означать minimum row height.");

        var exactRow =
            table.Rows[2];

        Assert(
            exactRow.LeftOffsetTwips == -60 &&
            exactRow.LeftOffsetPoints == -3,
            "RTF negative trleft должен сохранять signed source value.");
        Assert(
            exactRow.CellGapTwips == -20 &&
            exactRow.CellGapPoints == -1,
            "RTF signed trgaph должен сохраняться без скрытого clamp.");
        Assert(
            exactRow.HeightTwips == -300 &&
            exactRow.HeightPoints == 15 &&
            exactRow.HeightExact,
            "RTF negative trrh должен означать exact absolute row height.");

        var resetRow =
            table.Rows[3];

        Assert(
            resetRow.LeftOffsetTwips == null &&
            resetRow.LeftOffsetPoints == null &&
            resetRow.CellGapTwips == null &&
            resetRow.CellGapPoints == null &&
            resetRow.HeightTwips == null &&
            resetRow.HeightPoints == null &&
            !resetRow.HeightExact,
            "RTF trowd должен сбрасывать basic row measurement state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\trleft10\\trleft30" +
                "\\trgaph20\\trgaph50" +
                "\\trrh100\\trrh-200" +
                "\\cellx1000\\intbl P\\cell\\row}");

        var precedenceRow =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            precedenceRow.LeftOffsetTwips == 30 &&
            precedenceRow.CellGapTwips == 50 &&
            precedenceRow.HeightTwips == -200 &&
            precedenceRow.HeightPoints == 10 &&
            precedenceRow.HeightExact,
            "RTF basic row controls должны использовать последнее source значение.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trleft-100\\trgaph50\\trrh-200" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalOriginRow =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows[0];

        Assert(
            verticalOriginRow.LeftOffsetTwips == -100 &&
            verticalOriginRow.CellGapTwips == 50 &&
            verticalOriginRow.HeightTwips == -200 &&
            verticalOriginRow.HeightPoints == 10 &&
            verticalOriginRow.HeightExact,
            "RTF vertical merge reconstruction должен сохранять raw row measurements.");
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
                "\\trleft-80\\trgaph30\\trrh120" +
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
            outerRow.LeftOffsetTwips == null &&
            outerRow.CellGapTwips == null &&
            outerRow.HeightTwips == null,
            "RTF nested row measurement state не должен протекать в parent row.");
        Assert(
            nestedRow.LeftOffsetTwips == -80 &&
            nestedRow.LeftOffsetPoints == -4 &&
            nestedRow.CellGapTwips == 30 &&
            nestedRow.CellGapPoints == 1.5 &&
            nestedRow.HeightTwips == 120 &&
            nestedRow.HeightPoints == 6 &&
            !nestedRow.HeightExact,
            "RTF nested row должен сохранять собственные basic measurements.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-left-offset-twips=\"-120\"",
            "RTF trleft raw HTML metadata");
        AssertContains(
            html,
            "data-row-left-offset-pt=\"-6\"",
            "RTF trleft points HTML metadata");
        AssertContains(
            html,
            "data-row-cell-gap-twips=\"115\"",
            "RTF trgaph raw HTML metadata");
        AssertContains(
            html,
            "data-row-cell-gap-pt=\"5.75\"",
            "RTF trgaph points HTML metadata");
        AssertContains(
            html,
            "data-row-height-twips=\"0\"",
            "RTF trrh0 raw HTML metadata");
        AssertContains(
            html,
            "data-row-height-rule=\"auto\"",
            "RTF trrh0 auto HTML metadata");
        AssertContains(
            html,
            "data-row-height-twips=\"200\"",
            "RTF positive trrh raw HTML metadata");
        AssertContains(
            html,
            "data-row-height-rule=\"at-least\"",
            "RTF positive trrh rule HTML metadata");
        AssertContains(
            html,
            "data-row-height-twips=\"-300\"",
            "RTF negative trrh raw HTML metadata");
        AssertContains(
            html,
            "data-row-height-rule=\"exact\"",
            "RTF negative trrh rule HTML metadata");

        var autoMarker =
            "data-row-height-rule=\"auto\"";
        var autoMarkerIndex =
            html.IndexOf(
                autoMarker,
                StringComparison.Ordinal);
        var autoTagStart =
            html.LastIndexOf(
                "<tr",
                autoMarkerIndex,
                StringComparison.Ordinal);
        var autoTagEnd =
            html.IndexOf(
                '>',
                autoMarkerIndex);

        Assert(
            autoMarkerIndex >= 0 &&
            autoTagStart >= 0 &&
            autoTagEnd > autoTagStart &&
            !html[
                autoTagStart..autoTagEnd]
                .Contains(
                    "min-height:",
                    StringComparison.Ordinal) &&
            !html[
                autoTagStart..autoTagEnd]
                .Contains(
                    "height:",
                    StringComparison.Ordinal),
            "RTF trrh0 не должен получать CSS row-height constraint.");

        foreach (var control in new[]
                 {
                     "trleft",
                     "trgaph",
                     "trrh"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} без numeric parameter должен завершаться structural error.");
        }
    }
}
