using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет source outer padding крайних ячеек RTF table-style row:
    /// independent value/unit properties, Null/Twips semantics, incomplete
    /// declarations, repeated controls, conditional entries, nested groups,
    /// signed Int32 и отсутствие влияния на body row formatting.
    /// </summary>
    private static void TestRtfTableStyleRowOuterPadding(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-outer-padding.rtf");

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
                "{\\*\\ts211\\tsrowd" +
                "\\trpaddt40\\trpaddft3" +
                "\\trpadot120\\trpadoft3" +
                "\\trpador-40\\trpadofr3" +
                "\\trpadob60\\trpadofb0" +
                "\\trpadol0\\trpadofl3 Four outer sides;}" +
                "{\\*\\ts212\\tsrowd" +
                "\\trpadoft3\\trpadofr0\\trpadob80\\trpadol200 Incomplete;}" +
                "{\\*\\ts213\\tsrowd Plain;}" +
                "{\\*\\ts214\\tsrowd" +
                "\\trpadol20\\trpadol100\\trpadofl0\\trpadofl3 Repeated;}" +
                "{\\*\\ts211\\tsrowd\\tscfirstrow" +
                "\\trpadot80\\trpadoft3 Conditional;}" +
                "{\\*\\ts215\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\trpadob-40\\trpadofb3 Nested keycode;}" +
                "}" +
                "\\trowd\\trpadot90\\trpadoft3" +
                "\\trpaddt20\\trpaddft3" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 6,
            "RTF stylesheet должен сохранять шесть ordered table-style entries.");

        var firstRow =
            model.TableStyles[0].RowFormatting;
        var full =
            firstRow?.OuterPadding;

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
            "RTF trpado* должен сохранять source outer padding: unit 0 fallback и unit 3 Twips.");

        Assert(
            firstRow?.Padding is
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
            } &&
            firstRow?.OuterCellSpacing == null,
            "RTF trpado* не должен перезаписывать default padding или outer spacing.");

        var incomplete =
            model.TableStyles[1].RowFormatting?.OuterPadding;

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
            "RTF trpado* должен сохранять unit-only и value-only declarations без inferred defaults.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF style без row properties должен сохранять null RowFormatting.");

        var repeated =
            model.TableStyles[3].RowFormatting?.OuterPadding;

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
            "RTF repeated trpadol/trpadofl должен сохранять последние source value и unit.");

        Assert(
            model.TableStyles[4].Handle == 211 &&
            model.TableStyles[4].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[4].RowFormatting?.OuterPadding is
            {
                Top:
                {
                    Value: 80,
                    Unit: 3,
                    Points: 4
                },
                Left: null
            } &&
            model.TableStyles[0].RowFormatting?.OuterPadding?.Top?.Value == 120,
            "RTF conditional style entry с тем же handle должен иметь независимый outer padding.");

        Assert(
            model.TableStyles[5].RowFormatting?.OuterPadding is
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
            "RTF nested keycode не должен менять outer padding table-style entry.");

        var bodyRow =
            model.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            bodyRow.OuterPaddingTop is
            {
                Value: 90,
                Unit: 3
            } &&
            bodyRow.PaddingTop is
            {
                Value: 20,
                Unit: 3
            } &&
            bodyRow.OuterPaddingLeft == null,
            "RTF body row должен сохранять собственный outer/default padding независимо от stylesheet.");

        var extremes =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts216\\tsrowd" +
                "\\trpadol-2147483648\\trpadofl3 Extreme;}}" +
                "Body\\par}");

        Assert(
            extremes.TableStyles.Single().RowFormatting?.OuterPadding?.Left is
            {
                Value: int.MinValue,
                Unit: 3
            } &&
            extremes.TableStyles.Single().RowFormatting?.OuterPadding?.Left?.Points ==
                int.MinValue / 20.0,
            "RTF outer padding должен сохранять полный signed Int32 диапазон.");

        foreach (var control in new[]
                 {
                     "trpadot",
                     "trpador",
                     "trpadob",
                     "trpadol",
                     "trpadoft",
                     "trpadofr",
                     "trpadofb",
                     "trpadofl",
                     "trpadoft1",
                     "trpadofr2",
                     "trpadofb4",
                     "trpadofl-1",
                     "trpadot2147483648"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts217\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF table-style {control} должен отклонять invalid numeric parameter или unit.");
        }
    }
}
