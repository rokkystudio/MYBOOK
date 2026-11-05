using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет table-level projection RTF row definitions: alignment и
    /// preferred width берутся из первой строки, AutoFit учитывает trautofit
    /// первой строки и fixed row/cell width overrides во всей таблице.
    /// </summary>
    private static void TestRtfTableProjection(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-projection.rtf");

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
                "\\trowd\\trqc\\trautofit1\\trftsWidth2\\trwWidth2500" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\trqr\\trautofit1\\trftsWidth3\\trwWidth1440" +
                "\\cellx1000\\intbl B\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Alignment ==
            DocumentTableAlignment.Center,
            "RTF DocumentTable.Alignment должен проецироваться из первой row definition.");
        Assert(
            table.PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 50
            },
            "RTF DocumentTable.PreferredWidth должен проецироваться из первой row definition.");
        Assert(
            !table.AutoFit,
            "RTF explicit trwWidth в таблице должен отключать effective table AutoFit.");

        Assert(
            table.Rows.Count == 2 &&
            table.Rows[0].Alignment ==
                DocumentTableAlignment.Center &&
            table.Rows[1].Alignment ==
                DocumentTableAlignment.Right &&
            table.Rows[0].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 50
            } &&
            table.Rows[1].PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 72
            } &&
            table.Rows[0].AutoFit == true &&
            table.Rows[1].AutoFit == true,
            "RTF row definitions должны сохранять собственные source values независимо от table projection.");

        var autoModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trqr\\trautofit1\\trftsWidth1" +
                "\\cellx1000\\intbl Auto\\cell\\row}");

        var autoTable =
            autoModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            autoTable.Alignment ==
                DocumentTableAlignment.Right &&
            autoTable.PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Auto,
                Value: null
            } &&
            autoTable.AutoFit,
            "RTF Auto preferred row width не должен отключать table AutoFit.");

        var cellWidthModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trql\\trautofit1" +
                "\\clftsWidth3\\clwWidth720\\cellx1000" +
                "\\intbl Cell width\\cell\\row}");

        var cellWidthTable =
            cellWidthModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            cellWidthTable.Alignment ==
                DocumentTableAlignment.Left &&
            cellWidthTable.PreferredWidth == null &&
            !cellWidthTable.AutoFit,
            "RTF explicit cell preferred width должен отключать effective table AutoFit.");

        var laterWidthModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trqc\\trautofit1\\cellx1000\\intbl First\\cell\\row" +
                "\\trowd\\trqr\\trautofit1\\trftsWidth3\\trwWidth800" +
                "\\cellx1000\\intbl Second\\cell\\row}");

        var laterWidthTable =
            laterWidthModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            laterWidthTable.Alignment ==
                DocumentTableAlignment.Center &&
            laterWidthTable.PreferredWidth == null &&
            !laterWidthTable.AutoFit,
            "RTF fixed preferred width в последующей row должен отключать AutoFit, не меняя first-row projection.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\trqc\\trautofit1\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\trqr\\trautofit1\\trftsWidth3\\trwWidth600" +
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
            outerTable.Alignment ==
                DocumentTableAlignment.Center &&
            outerTable.PreferredWidth == null &&
            outerTable.AutoFit,
            "RTF parent table projection должен использовать только parent row definitions.");
        Assert(
            nestedTable.Alignment ==
                DocumentTableAlignment.Right &&
            nestedTable.PreferredWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 30
            } &&
            !nestedTable.AutoFit,
            "RTF nested table projection должен использовать собственные row definitions.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-table-alignment=\"center\"",
            "RTF projected table alignment HTML metadata");
        AssertContains(
            html,
            "data-preferred-width-kind=\"percent\"",
            "RTF projected table preferred width kind HTML metadata");
        AssertContains(
            html,
            "data-preferred-width-value=\"50\"",
            "RTF projected table preferred width value HTML metadata");

        var autoHtml =
            DocumentHtmlRenderer.Render(
                autoModel,
                CreateRenderTheme());

        AssertContains(
            autoHtml,
            "data-autofit=\"true\"",
            "RTF projected table AutoFit HTML metadata");
        AssertContains(
            autoHtml,
            "data-table-alignment=\"right\"",
            "RTF projected right alignment HTML metadata");
        AssertContains(
            autoHtml,
            "data-preferred-width-kind=\"auto\"",
            "RTF projected Auto preferred width HTML metadata");

        foreach (var control in new[]
                 {
                     "trql1",
                     "trqc0",
                     "trqr1"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как parameterized row-justification flag.");
        }
    }
}
