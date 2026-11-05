using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF table autoformat/look flags: first-row table projection,
    /// независимый row source state, trowd reset, vertical merge, nested tables,
    /// HTML metadata и запрет numeric parameters для flag controls.
    /// </summary>
    private static void TestRtfTableLook(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-look.rtf");

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
                "\\tbllkborder\\tbllkshading\\tbllkbestfit" +
                "\\tbllkhdrrows\\tbllkhdrcols\\tbllknorowband" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd" +
                "\\tbllkfont\\tbllkcolor\\tbllklastrow" +
                "\\tbllklastcol\\tbllknocolband" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd\\cellx1000\\intbl C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Look is
            {
                Border: true,
                Shading: true,
                Font: null,
                Color: null,
                BestFit: true,
                HeaderRows: true,
                LastRow: null,
                HeaderColumns: true,
                LastColumn: null,
                NoRowBand: true,
                NoColumnBand: null
            },
            "RTF DocumentTable.Look должен проецироваться из первой row definition.");

        Assert(
            table.Rows.Count == 3 &&
            table.Rows[0].LookBorder == true &&
            table.Rows[0].LookShading == true &&
            table.Rows[0].LookBestFit == true &&
            table.Rows[0].LookHeaderRows == true &&
            table.Rows[0].LookHeaderColumns == true &&
            table.Rows[0].LookNoRowBand == true &&
            table.Rows[0].LookFont == null &&
            table.Rows[1].LookFont == true &&
            table.Rows[1].LookColor == true &&
            table.Rows[1].LookLastRow == true &&
            table.Rows[1].LookLastColumn == true &&
            table.Rows[1].LookNoColumnBand == true &&
            table.Rows[1].LookBorder == null,
            "RTF rows должны сохранять собственные tbllk* source flags независимо от table projection.");

        var resetRow =
            table.Rows[2];

        Assert(
            resetRow.LookBorder == null &&
            resetRow.LookShading == null &&
            resetRow.LookFont == null &&
            resetRow.LookColor == null &&
            resetRow.LookBestFit == null &&
            resetRow.LookHeaderRows == null &&
            resetRow.LookLastRow == null &&
            resetRow.LookHeaderColumns == null &&
            resetRow.LookLastColumn == null &&
            resetRow.LookNoRowBand == null &&
            resetRow.LookNoColumnBand == null,
            "RTF trowd должен сбрасывать table-look source state.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tbllkborder\\tbllkhdrrows" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd\\tbllkfont" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            verticalTable.Look is
            {
                Border: true,
                HeaderRows: true,
                Font: null
            },
            "RTF vertical-merge table projection должен использовать first-row look flags.");
        Assert(
            verticalTable.Rows[0].LookBorder == true &&
            verticalTable.Rows[0].LookHeaderRows == true &&
            verticalTable.Rows[0].Cells.Single().RowSpan == 2,
            "RTF vertical merge reconstruction должен сохранять origin row look metadata.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\tbllkborder\\tbllkshading\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd" +
                "\\tbllkfont\\tbllkcolor\\tbllknocolband" +
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
            outerTable.Look is
            {
                Border: true,
                Shading: true,
                Font: null,
                Color: null,
                NoColumnBand: null
            },
            "RTF parent table look должен использовать только parent row definitions.");
        Assert(
            nestedTable.Look is
            {
                Border: null,
                Shading: null,
                Font: true,
                Color: true,
                NoColumnBand: true
            },
            "RTF nested table должен иметь независимый table-look projection.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        var tableStart =
            html.IndexOf(
                "<table class=\"flow-table\"",
                StringComparison.Ordinal);
        var tableEnd =
            html.IndexOf(
                '>',
                tableStart);
        var tableTag =
            html[
                tableStart..(tableEnd + 1)];

        AssertContains(
            tableTag,
            "data-table-look-border=\"true\"",
            "RTF table-level border look HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-shading=\"true\"",
            "RTF table-level shading look HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-best-fit=\"true\"",
            "RTF table-level best-fit HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-header-rows=\"true\"",
            "RTF table-level header-rows HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-header-columns=\"true\"",
            "RTF table-level header-columns HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-no-row-band=\"true\"",
            "RTF table-level no-row-band HTML metadata");

        Assert(
            !tableTag.Contains(
                "data-table-look-font=",
                StringComparison.Ordinal) &&
            !tableTag.Contains(
                "data-table-look-color=",
                StringComparison.Ordinal) &&
            !tableTag.Contains(
                "data-table-look-last-row=",
                StringComparison.Ordinal) &&
            !tableTag.Contains(
                "data-table-look-last-column=",
                StringComparison.Ordinal) &&
            !tableTag.Contains(
                "data-table-look-no-column-band=",
                StringComparison.Ordinal),
            "RTF table-level look HTML metadata не должен включать flags только из последующих rows.");

        AssertContains(
            html,
            "data-table-look-font=\"true\"",
            "RTF row-level font look HTML metadata");
        AssertContains(
            html,
            "data-table-look-last-row=\"true\"",
            "RTF row-level last-row look HTML metadata");
        AssertContains(
            html,
            "data-table-look-no-column-band=\"true\"",
            "RTF row-level no-column-band HTML metadata");

        foreach (var control in new[]
                 {
                     "tbllkborder0",
                     "tbllkshading1",
                     "tbllkfont0",
                     "tbllkcolor1",
                     "tbllkbestfit0",
                     "tbllkhdrrows1",
                     "tbllklastrow0",
                     "tbllkhdrcols1",
                     "tbllklastcol0",
                     "tbllknorowband1",
                     "tbllknocolband0"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как parameterized table-look flag.");
        }
    }
}
