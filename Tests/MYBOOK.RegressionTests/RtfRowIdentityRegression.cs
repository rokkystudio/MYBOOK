using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF row identity/banding controls: signed irow/irowband,
    /// header-band value -1, lastrow flag grammar, last-value precedence,
    /// trowd reset, nested rows, vertical merge reconstruction и HTML metadata.
    /// </summary>
    private static void TestRtfRowIdentity(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-row-identity.rtf");

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
                "\\trowd\\irow0\\irowband-1" +
                "\\cellx1000\\intbl Header\\cell\\row" +
                "\\trowd\\irow7\\irowband6\\lastrow" +
                "\\cellx1000\\intbl Last\\cell\\row" +
                "\\trowd\\cellx1000\\intbl Reset\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 3,
            "RTF row-identity fixture должна содержать три строки.");

        var headerRow =
            table.Rows[0];

        Assert(
            headerRow.RowIndex == 0 &&
            headerRow.BandIndex == -1 &&
            headerRow.IsLastRow == null,
            "RTF irow/irowband должны сохранять row index и header band -1.");

        var lastRow =
            table.Rows[1];

        Assert(
            lastRow.RowIndex == 7 &&
            lastRow.BandIndex == 6 &&
            lastRow.IsLastRow == true,
            "RTF lastrow должен сохраняться как explicit parameterless marker.");

        var resetRow =
            table.Rows[2];

        Assert(
            resetRow.RowIndex == null &&
            resetRow.BandIndex == null &&
            resetRow.IsLastRow == null,
            "RTF trowd должен сбрасывать row identity/banding state.");

        var precedenceModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd" +
                "\\irow1\\irow-4" +
                "\\irowband2\\irowband-3" +
                "\\lastrow\\lastrow" +
                "\\cellx1000\\intbl P\\cell\\row}");

        var precedenceRow =
            precedenceModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            precedenceRow.RowIndex == -4 &&
            precedenceRow.BandIndex == -3 &&
            precedenceRow.IsLastRow == true,
            "RTF repeated row identity controls должны сохранять последнее numeric значение и flag presence.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\irow3\\irowband-1\\lastrow" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd\\irow4\\irowband0" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];

        Assert(
            verticalOriginRow.RowIndex == 3 &&
            verticalOriginRow.BandIndex == -1 &&
            verticalOriginRow.IsLastRow == true &&
            verticalOriginRow.Cells.Single().RowSpan == 2,
            "RTF vertical merge reconstruction должен сохранять origin row identity metadata.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\irow10\\irowband9\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\irow-2\\irowband-1\\lastrow" +
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
            outerRow.RowIndex == 10 &&
            outerRow.BandIndex == 9 &&
            outerRow.IsLastRow == null,
            "RTF nested row identity state не должен протекать в parent row.");
        Assert(
            nestedRow.RowIndex == -2 &&
            nestedRow.BandIndex == -1 &&
            nestedRow.IsLastRow == true,
            "RTF nested row должен сохранять собственные identity/banding controls.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-index=\"0\"",
            "RTF irow HTML metadata");
        AssertContains(
            html,
            "data-row-band-index=\"-1\"",
            "RTF irowband header HTML metadata");
        AssertContains(
            html,
            "data-row-index=\"7\"",
            "RTF signed row index HTML metadata");
        AssertContains(
            html,
            "data-row-last=\"true\"",
            "RTF lastrow HTML metadata");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd\\irow" +
                "\\cellx1000\\intbl X\\cell\\row}") &&
            RejectsInvalid(
                "{\\rtf1\\ansi\\trowd\\irowband" +
                "\\cellx1000\\intbl X\\cell\\row}"),
            "RTF irow/irowband без numeric parameter должны завершаться structural error.");

        foreach (var control in new[]
                 {
                     "lastrow0",
                     "lastrow1",
                     "lastrow-1"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как parameterized lastrow flag.");
        }
    }
}
