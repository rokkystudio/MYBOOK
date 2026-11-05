using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF trautofit toggle: обязательный 0/1 parameter, row source
    /// state, last-value precedence, trowd reset, nested rows, vertical merge,
    /// preferred-width override и effective DocumentTable.AutoFit/HTML metadata.
    /// </summary>
    private static void TestRtfRowAutoFit(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-row-autofit.rtf");

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

        var mixedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit0" +
                "\\cellx1000\\intbl Off\\cell\\row" +
                "\\trowd\\trautofit1" +
                "\\cellx1000\\intbl On\\cell\\row" +
                "\\trowd" +
                "\\cellx1000\\intbl Default\\cell\\row}");

        var mixedTable =
            mixedModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            mixedTable.Rows.Count == 3 &&
            mixedTable.Rows[0].AutoFit == false &&
            mixedTable.Rows[1].AutoFit == true &&
            mixedTable.Rows[2].AutoFit == null,
            "RTF trautofit0/1 должен сохранять exact row source state, а trowd — сбрасывать его.");

        Assert(
            !mixedTable.AutoFit,
            "RTF effective table AutoFit должен использовать first-row projection.");

        var onModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1" +
                "\\cellx1000\\intbl Auto\\cell\\row}");

        var onTable =
            onModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            onTable.Rows.Single().AutoFit == true &&
            onTable.AutoFit,
            "RTF trautofit1 без preferred-width override должен включать effective table AutoFit.");

        var defaultModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\cellx1000\\intbl Default\\cell\\row}");

        var defaultTable =
            defaultModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            defaultTable.Rows.Single().AutoFit == null &&
            !defaultTable.AutoFit,
            "RTF отсутствие trautofit должно сохранять nullable row source state и default AutoFit off.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1\\trautofit0\\trautofit1" +
                "\\cellx1000\\intbl Last\\cell\\row}");

        Assert(
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single()
                .AutoFit == true,
            "RTF repeated trautofit должен использовать последнее toggle value.");

        var widthOverrideModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1" +
                "\\cellx1000\\intbl First\\cell\\row" +
                "\\trowd\\trftsWidth3\\trwWidth720" +
                "\\cellx1000\\intbl Fixed\\cell\\row}");

        var widthOverrideTable =
            widthOverrideModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            widthOverrideTable.Rows[0].AutoFit == true &&
            widthOverrideTable.Rows[1].AutoFit == null &&
            !widthOverrideTable.AutoFit,
            "RTF explicit trwWidth в любой row должен override effective table AutoFit.");

        var cellWidthOverrideModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1" +
                "\\clftsWidth3\\clwWidth600\\cellx1000" +
                "\\intbl Fixed cell\\cell\\row}");

        var cellWidthOverrideTable =
            cellWidthOverrideModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            cellWidthOverrideTable.Rows.Single().AutoFit == true &&
            !cellWidthOverrideTable.AutoFit,
            "RTF explicit clwWidth должен override effective table AutoFit.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd\\trautofit0" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];

        Assert(
            verticalOriginRow.AutoFit == true &&
            verticalOriginRow.Cells.Single().RowSpan == 2,
            "RTF vertical merge reconstruction должен сохранять origin row trautofit state.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trautofit1\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\trautofit0" +
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
            outerTable.Rows.Single().AutoFit == true &&
            outerTable.AutoFit,
            "RTF nested trautofit state не должен протекать в parent table.");
        Assert(
            nestedTable.Rows.Single().AutoFit == false &&
            !nestedTable.AutoFit,
            "RTF nested table должен сохранять собственный trautofit toggle.");

        var mixedHtml =
            DocumentHtmlRenderer.Render(
                mixedModel,
                CreateRenderTheme());

        AssertContains(
            mixedHtml,
            "data-autofit=\"false\"",
            "RTF effective table AutoFit false HTML metadata");
        AssertContains(
            mixedHtml,
            "data-row-autofit=\"false\"",
            "RTF trautofit0 row HTML metadata");
        AssertContains(
            mixedHtml,
            "data-row-autofit=\"true\"",
            "RTF trautofit1 row HTML metadata");

        var onHtml =
            DocumentHtmlRenderer.Render(
                onModel,
                CreateRenderTheme());

        AssertContains(
            onHtml,
            "data-autofit=\"true\"",
            "RTF effective table AutoFit true HTML metadata");

        foreach (var control in new[]
                 {
                     "trautofit",
                     "trautofit-1",
                     "trautofit2",
                     "trautofit2147483647"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться: trautofit принимает только numeric 0 или 1.");
        }
    }
}
