using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF ytsN и conditional table-style tsc* paragraph controls:
    /// signed Int16 style handle, parameterless marker grammar, pard reset,
    /// cell-local combinations и HTML source metadata без применения stylesheet.
    /// </summary>
    private static void TestRtfAppliedTableStyle(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-applied-table-style.rtf");

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
                "{\\*\\ts18\\tsrowd First;}" +
                "{\\*\\ts27\\tsrowd Second;}}" +
                "\\trowd\\ts18\\cellx1000\\cellx2000" +
                "\\pard\\plain\\intbl" +
                "\\yts18\\tscfirstrow\\tscfirstcol" +
                "\\tscbandhorzodd\\tscbandvertodd\\tscnwcell A\\cell" +
                "\\pard\\plain\\intbl" +
                "\\yts18\\tscfirstrow\\tsclastcol" +
                "\\tscbandhorzodd\\tscbandverteven\\tscnecell B\\cell\\row" +
                "\\trowd\\ts27\\cellx1000\\cellx2000" +
                "\\pard\\plain\\intbl" +
                "\\yts27\\tsclastrow\\tscfirstcol" +
                "\\tscbandhorzeven\\tscbandvertodd\\tscswcell C\\cell" +
                "\\pard\\plain\\intbl" +
                "\\yts-7\\tsclastrow\\tsclastcol" +
                "\\tscbandhorzeven\\tscbandverteven\\tscsecell D\\cell\\row}");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        var paragraphs =
            table.Rows
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentParagraph>()
                .ToArray();

        Assert(
            paragraphs.Length == 4,
            "RTF yts/tsc fixture должна содержать четыре cell paragraphs.");

        Assert(
            paragraphs[0].AppliedTableStyleHandle == 18 &&
            paragraphs[0].TableStyleConditions ==
                (DocumentTableStyleCondition.FirstRow |
                 DocumentTableStyleCondition.FirstColumn |
                 DocumentTableStyleCondition.OddRowBand |
                 DocumentTableStyleCondition.OddColumnBand |
                 DocumentTableStyleCondition.NorthWestCell),
            "RTF first cell должен сохранять yts и first-row/first-column/odd-band/NW markers.");

        Assert(
            paragraphs[1].AppliedTableStyleHandle == 18 &&
            paragraphs[1].TableStyleConditions ==
                (DocumentTableStyleCondition.FirstRow |
                 DocumentTableStyleCondition.LastColumn |
                 DocumentTableStyleCondition.OddRowBand |
                 DocumentTableStyleCondition.EvenColumnBand |
                 DocumentTableStyleCondition.NorthEastCell),
            "RTF second cell должен сохранять first-row/last-column/NE conditional markers.");

        Assert(
            paragraphs[2].AppliedTableStyleHandle == 27 &&
            paragraphs[2].TableStyleConditions ==
                (DocumentTableStyleCondition.LastRow |
                 DocumentTableStyleCondition.FirstColumn |
                 DocumentTableStyleCondition.EvenRowBand |
                 DocumentTableStyleCondition.OddColumnBand |
                 DocumentTableStyleCondition.SouthWestCell),
            "RTF third cell должен сохранять last-row/first-column/SW conditional markers.");

        Assert(
            paragraphs[3].AppliedTableStyleHandle == -7 &&
            paragraphs[3].TableStyleConditions ==
                (DocumentTableStyleCondition.LastRow |
                 DocumentTableStyleCondition.LastColumn |
                 DocumentTableStyleCondition.EvenRowBand |
                 DocumentTableStyleCondition.EvenColumnBand |
                 DocumentTableStyleCondition.SouthEastCell),
            "RTF fourth cell должен сохранять signed yts и SE conditional markers.");

        var resetModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\yts32767\\tscfirstrow\\tscnwcell Styled\\par" +
                "\\pard Reset\\par}");

        var resetParagraphs =
            resetModel.Blocks
                .OfType<DocumentParagraph>()
                .ToArray();

        Assert(
            resetParagraphs.Length == 2 &&
            resetParagraphs[0].AppliedTableStyleHandle == 32767 &&
            resetParagraphs[0].TableStyleConditions ==
                (DocumentTableStyleCondition.FirstRow |
                 DocumentTableStyleCondition.NorthWestCell) &&
            resetParagraphs[1].AppliedTableStyleHandle == null &&
            resetParagraphs[1].TableStyleConditions ==
                DocumentTableStyleCondition.None,
            "RTF pard должен сбрасывать yts и все conditional table-style markers.");

        var repeatedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\yts1\\yts-32768\\tscfirstrow\\tscfirstrow Repeat\\par}");

        var repeatedParagraph =
            repeatedModel.Blocks
                .OfType<DocumentParagraph>()
                .Single();

        Assert(
            repeatedParagraph.AppliedTableStyleHandle == -32768 &&
            repeatedParagraph.TableStyleConditions ==
                DocumentTableStyleCondition.FirstRow,
            "RTF repeated yts использует последнее handle, repeated tsc marker остаётся одним source condition.");

        var nestedModel =
            Read(
                "{\\rtf1\\ansi" +
                "\\trowd\\cellx4000" +
                "\\pard\\plain\\intbl\\yts12\\tscfirstrow Outer before\\par" +
                "\\intbl\\itap2\\yts21\\tsclastrow Nested\\nestcell" +
                "{\\*\\nesttableprops\\trowd\\cellx1000\\nestrow}" +
                "\\pard\\plain\\intbl\\itap1\\yts12\\tscfirstrow Outer after\\cell\\row" +
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
        var outerParagraphs =
            outerTable.Rows
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentParagraph>()
                .ToArray();
        var nestedParagraph =
            nestedTable.Rows
                .SelectMany(row => row.Cells)
                .SelectMany(cell => cell.Blocks)
                .OfType<DocumentParagraph>()
                .Single();

        Assert(
            outerParagraphs.Length == 2 &&
            outerParagraphs.All(paragraph =>
                paragraph.AppliedTableStyleHandle == 12 &&
                paragraph.TableStyleConditions ==
                    DocumentTableStyleCondition.FirstRow),
            "RTF parent cell paragraphs должны сохранять собственный yts/tsc state вокруг nested table.");
        Assert(
            nestedParagraph.AppliedTableStyleHandle == 21 &&
            nestedParagraph.TableStyleConditions ==
                (DocumentTableStyleCondition.FirstRow |
                 DocumentTableStyleCondition.LastRow),
            "RTF nested cell paragraph без pard должен наследовать parent tsc marker и применять собственный yts/tsc state.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-applied-table-style-handle=\"18\"",
            "RTF yts positive HTML metadata");
        AssertContains(
            html,
            "data-applied-table-style-handle=\"-7\"",
            "RTF yts signed HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-first-row=\"true\"",
            "RTF tscfirstrow HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-last-row=\"true\"",
            "RTF tsclastrow HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-first-column=\"true\"",
            "RTF tscfirstcol HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-last-column=\"true\"",
            "RTF tsclastcol HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-odd-row-band=\"true\"",
            "RTF tscbandhorzodd HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-even-row-band=\"true\"",
            "RTF tscbandhorzeven HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-odd-column-band=\"true\"",
            "RTF tscbandvertodd HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-even-column-band=\"true\"",
            "RTF tscbandverteven HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-north-west-cell=\"true\"",
            "RTF tscnwcell HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-north-east-cell=\"true\"",
            "RTF tscnecell HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-south-west-cell=\"true\"",
            "RTF tscswcell HTML metadata");
        AssertContains(
            html,
            "data-table-style-condition-south-east-cell=\"true\"",
            "RTF tscsecell HTML metadata");

        foreach (var control in new[]
                 {
                     "yts",
                     "yts32768",
                     "yts-32769"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\" +
                    control +
                    " X\\par}"),
                $"RTF {control} должен отклоняться как invalid signed 16-bit applied table-style handle.");
        }

        foreach (var control in new[]
                 {
                     "tscfirstrow1",
                     "tsclastrow0",
                     "tscfirstcol1",
                     "tsclastcol0",
                     "tscbandhorzodd1",
                     "tscbandhorzeven0",
                     "tscbandvertodd1",
                     "tscbandverteven0",
                     "tscnwcell1",
                     "tscnecell0",
                     "tscswcell1",
                     "tscsecell0"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\" +
                    control +
                    " X\\par}"),
                $"RTF {control} должен отклоняться как parameterized conditional table-style flag.");
        }
    }
}
