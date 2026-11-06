using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF table-style handle tsN: signed Int16 grammar, first-row
    /// table projection, independent row source values, trowd reset,
    /// stylesheet isolation, vertical merge, nested tables и HTML metadata.
    /// </summary>
    private static void TestRtfTableStyleHandle(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-handle.rtf");

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
                "{\\stylesheet" +
                "{\\*\\ts18\\tsrowd Normal Table;}" +
                "{\\*\\ts27\\tsrowd Custom Table;}}" +
                "\\trowd\\ts18" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\ts27" +
                "\\cellx1000\\intbl B\\cell\\row" +
                "\\trowd" +
                "\\cellx1000\\intbl C\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.StyleHandle == 18,
            "RTF DocumentTable.StyleHandle должен проецироваться из первой row definition.");

        Assert(
            table.Rows.Count == 3 &&
            table.Rows[0].TableStyleHandle == 18 &&
            table.Rows[1].TableStyleHandle == 27 &&
            table.Rows[2].TableStyleHandle == null,
            "RTF tsN должен сохранять независимый row source handle и сбрасываться через trowd.");

        var stylesheetOnlyModel =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts31\\tsrowd Style Only;}}" +
                "\\trowd" +
                "\\cellx1000\\intbl No style\\cell\\row}");

        var stylesheetOnlyTable =
            stylesheetOnlyModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            stylesheetOnlyTable.StyleHandle == null &&
            stylesheetOnlyTable.Rows.Single().TableStyleHandle == null,
            "RTF tsN внутри stylesheet не должен протекать в body row state.");

        var repeatedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\ts5\\ts-7\\ts32767" +
                "\\cellx1000\\intbl Last\\cell\\row}");

        var repeatedRow =
            repeatedModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            repeatedRow.TableStyleHandle == 32767,
            "RTF repeated tsN должен сохранять последнее style handle.");

        var boundaryModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\ts-32768" +
                "\\cellx1000\\intbl Min\\cell\\row" +
                "\\trowd\\ts32767" +
                "\\cellx1000\\intbl Max\\cell\\row}");

        var boundaryTable =
            boundaryModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            boundaryTable.StyleHandle == -32768 &&
            boundaryTable.Rows[0].TableStyleHandle == -32768 &&
            boundaryTable.Rows[1].TableStyleHandle == 32767,
            "RTF tsN должен принимать обе signed Int16 boundary values.");

        var verticalMerge =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\ts9" +
                "\\clvmgf\\cellx1000\\intbl Top\\cell\\row" +
                "\\trowd\\ts10" +
                "\\clvmrg\\cellx1000\\intbl Bottom\\cell\\row}");

        var verticalTable =
            verticalMerge.Blocks
                .OfType<DocumentTable>()
                .Single();
        var verticalOriginRow =
            verticalTable.Rows[0];

        Assert(
            verticalOriginRow.TableStyleHandle == 9 &&
            verticalOriginRow.Cells.Single().RowSpan == 2,
            "RTF vertical merge reconstruction должен сохранять origin row table-style handle.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\ts12\\cellx4000" +
                "\\intbl\\itap1 Outer before\\par" +
                "\\intbl\\itap2 Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\ts21" +
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
            outerTable.StyleHandle == 12 &&
            outerTable.Rows.Single().TableStyleHandle == 12,
            "RTF parent table должен сохранять собственный table-style handle.");
        Assert(
            nestedTable.StyleHandle == 21 &&
            nestedTable.Rows.Single().TableStyleHandle == 21,
            "RTF nested table должен сохранять независимый table-style handle.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-table-style-handle=\"18\"",
            "RTF table style handle HTML metadata");
        AssertContains(
            html,
            "data-row-table-style-handle=\"18\"",
            "RTF first row table style handle HTML metadata");
        AssertContains(
            html,
            "data-row-table-style-handle=\"27\"",
            "RTF second row table style handle HTML metadata");

        foreach (var control in new[]
                 {
                     "ts",
                     "ts32768",
                     "ts-32769"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\trowd\\" +
                    control +
                    "\\cellx1000\\intbl X\\cell\\row}"),
                $"RTF {control} должен отклоняться как invalid signed 16-bit table-style handle.");
        }
    }
}
