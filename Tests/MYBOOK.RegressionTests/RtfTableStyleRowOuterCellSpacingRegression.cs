using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет source outer spacing крайних сторон RTF table-style row:
    /// value/unit, Null/Twips semantics, incomplete declarations,
    /// repeated controls, conditional entries, nested groups
    /// и независимые inner/outer/body row spacing properties.
    /// </summary>
    private static void TestRtfTableStyleRowOuterCellSpacing(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-outer-cell-spacing.rtf");

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
                "{\\*\\ts201\\tsrowd" +
                "\\trspdt40\\trspdft3" +
                "\\trspot120\\trspoft3" +
                "\\trspor-40\\trspofr3" +
                "\\trspob60\\trspofb0" +
                "\\trspol0\\trspofl3 Four outer sides;}" +
                "{\\*\\ts202\\tsrowd" +
                "\\trspoft3\\trspofr0\\trspob80\\trspol200 Incomplete;}" +
                "{\\*\\ts203\\tsrowd Plain;}" +
                "{\\*\\ts204\\tsrowd" +
                "\\trspol20\\trspol100\\trspofl0\\trspofl3 Repeated;}" +
                "{\\*\\ts201\\tsrowd\\tscfirstrow" +
                "\\trspot80\\trspoft3 Conditional;}" +
                "{\\*\\ts205\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\trspob-40\\trspofb3 Nested keycode;}" +
                "}" +
                "\\trowd\\trspot90\\trspoft3" +
                "\\trspdt20\\trspdft3" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 6,
            "RTF stylesheet должен сохранять шесть ordered table-style entries.");

        var firstRow =
            model.TableStyles[0].RowFormatting;
        var full =
            firstRow?.OuterCellSpacing;

        Assert(
            full is
            {
                Top:
                {
                    Value: 120,
                    Unit: 3,
                    Points: 6
                },
                Right:
                {
                    Value: -40,
                    Unit: 3,
                    Points: -2
                },
                Bottom:
                {
                    Value: 60,
                    Unit: 0,
                    Points: null
                },
                Left:
                {
                    Value: 0,
                    Unit: 3,
                    Points: 0
                }
            },
            "RTF trspo* должен сохранять independent outer spacing, unit 0 Null и unit 3 Twips.");

        Assert(
            firstRow?.CellSpacing is
            {
                Top:
                {
                    Value: 40,
                    Unit: 3,
                    Points: 2
                },
                Right: null,
                Bottom: null,
                Left: null
            },
            "RTF trspo* не должен перезаписывать внутренние trspd* values.");

        var incomplete =
            model.TableStyles[1].RowFormatting?.OuterCellSpacing;

        Assert(
            incomplete is
            {
                Top:
                {
                    Value: null,
                    Unit: 3,
                    Points: null
                },
                Right:
                {
                    Value: null,
                    Unit: 0,
                    Points: null
                },
                Bottom:
                {
                    Value: 80,
                    Unit: null,
                    Points: null
                },
                Left:
                {
                    Value: 200,
                    Unit: null,
                    Points: null
                }
            },
            "RTF outer spacing сохраняет unit-only и value-only declarations без inferred defaults.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF table style без row properties должен сохранять null RowFormatting.");

        var repeated =
            model.TableStyles[3].RowFormatting?.OuterCellSpacing;

        Assert(
            repeated is
            {
                Top: null,
                Right: null,
                Bottom: null,
                Left:
                {
                    Value: 100,
                    Unit: 3,
                    Points: 5
                }
            },
            "RTF repeated trspol/trspofl сохраняет последние source value и unit.");

        Assert(
            model.TableStyles[4].Handle == 201 &&
            model.TableStyles[4].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[4].RowFormatting?.OuterCellSpacing is
            {
                Top:
                {
                    Value: 80,
                    Unit: 3,
                    Points: 4
                },
                Left: null
            } &&
            model.TableStyles[0].RowFormatting?.OuterCellSpacing?.Top?.Value == 120,
            "RTF conditional entry с тем же handle должен сохранять собственный outer spacing.");

        Assert(
            model.TableStyles[5].RowFormatting?.OuterCellSpacing is
            {
                Top: null,
                Right: null,
                Bottom:
                {
                    Value: -40,
                    Unit: 3,
                    Points: -2
                },
                Left: null
            },
            "RTF nested keycode не должен изменять outer spacing table-style entry.");

        var bodyRow =
            model.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            bodyRow.OuterCellSpacingTop is
            {
                Value: 90,
                Unit: 3
            } &&
            bodyRow.CellSpacingTop is
            {
                Value: 20,
                Unit: 3
            } &&
            bodyRow.OuterCellSpacingLeft == null,
            "RTF body row spacing должен определяться собственными controls, не stylesheet.");

        var extremes =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts206\\tsrowd" +
                "\\trspol-2147483648\\trspofl3 Extreme;}}" +
                "Body\\par}");

        Assert(
            extremes.TableStyles.Single().RowFormatting?.OuterCellSpacing?.Left is
            {
                Value: int.MinValue,
                Unit: 3
            } &&
            extremes.TableStyles.Single().RowFormatting?.OuterCellSpacing?.Left?.Points ==
                int.MinValue / 20.0,
            "RTF outer spacing должен сохранять полный signed Int32 диапазон.");

        foreach (var control in new[]
                 {
                     "trspot",
                     "trspor",
                     "trspob",
                     "trspol",
                     "trspoft",
                     "trspofr",
                     "trspofb",
                     "trspofl",
                     "trspoft1",
                     "trspofr2",
                     "trspofb4",
                     "trspofl-1",
                     "trspot2147483648"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts207\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF table-style {control} должен отклонять invalid numeric parameter или unit.");
        }
    }
}
