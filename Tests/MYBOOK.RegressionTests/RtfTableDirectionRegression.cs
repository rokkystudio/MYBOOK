using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF table direction через taprtl отдельно от row cell
    /// precedence через rtlrow/ltrrow, включая reset, nested tables,
    /// multi-row consistency, HTML direction и invalid parameters.
    /// </summary>
    private static void TestRtfTableDirection(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-direction.rtf");

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
                "\\trowd\\taprtl\\ltrrow\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\taprtl\\rtlrow\\cellx1000\\intbl B\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.RightToLeft,
            "RTF taprtl должен задавать right-to-left table direction.");
        Assert(
            table.Rows.Count == 2 &&
            table.Rows[0].RightToLeft == false &&
            table.Rows[1].RightToLeft == true,
            "RTF taprtl не должен подменять rtlrow/ltrrow cell precedence.");

        var oppositeModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\rtlrow\\cellx1000\\intbl C\\cell\\row}");

        var oppositeTable =
            oppositeModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            !oppositeTable.RightToLeft &&
            oppositeTable.Rows.Single().RightToLeft == true,
            "RTF rtlrow без taprtl должен оставлять table direction left-to-right.");

        var resetModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\taprtl\\cellx1000\\intbl RTL\\cell\\row" +
                "\\pard Between\\par" +
                "\\trowd\\cellx1000\\intbl LTR\\cell\\row}");

        var resetTables =
            resetModel.Blocks
                .OfType<DocumentTable>()
                .ToArray();

        Assert(
            resetTables.Length == 2 &&
            resetTables[0].RightToLeft &&
            !resetTables[1].RightToLeft,
            "RTF table direction не должен протекать после завершения таблицы.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\taprtl\\ltrrow" +
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
            !outerTable.RightToLeft,
            "RTF nested taprtl не должен протекать в parent table.");
        Assert(
            nestedTable.RightToLeft &&
            nestedTable.Rows.Single().RightToLeft == false,
            "RTF nested taprtl должен сохранять table direction отдельно от ltrrow.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-table-rtl=\"true\"",
            "RTF taprtl HTML metadata");
        AssertContains(
            html,
            "dir=\"rtl\"",
            "RTF taprtl HTML direction");
        AssertContains(
            html,
            "data-row-direction=\"ltr\"",
            "RTF ltrrow HTML metadata внутри RTL table");
        AssertContains(
            html,
            "data-row-direction=\"rtl\"",
            "RTF rtlrow HTML metadata внутри RTL table");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi" +
                "\\trowd\\taprtl\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\cellx1000\\intbl B\\cell\\row}"),
            "RTF строки одной table с несовместимым taprtl должны завершаться structural error.");

        foreach (var control in new[]
                 {
                     "taprtl1",
                     "rtlrow1",
                     "ltrrow0"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как parameterized direction flag.");
        }
    }
}
