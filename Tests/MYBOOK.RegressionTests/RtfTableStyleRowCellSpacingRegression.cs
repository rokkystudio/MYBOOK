using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет source spacing четырёх сторон RTF table-style row:
    /// value/unit, Null/Twips semantics, incomplete declarations,
    /// repeated controls, conditional entries, nested groups
    /// и независимое форматирование body table rows.
    /// </summary>
    private static void TestRtfTableStyleRowCellSpacing(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-cell-spacing.rtf");

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
                "{\\*\\ts121\\tsrowd" +
                "\\trspdt40\\trspdft3" +
                "\\trspdr-20\\trspdfr3" +
                "\\trspdb120\\trspdfb0" +
                "\\trspdl-10\\trspdfl0 Four sides;}" +
                "{\\*\\ts122\\tsrowd" +
                "\\trspdft3\\trspdfr0\\trspdb80\\trspdl200 Incomplete;}" +
                "{\\*\\ts123\\tsrowd Plain;}" +
                "{\\*\\ts124\\tsrowd" +
                "\\trspdl40\\trspdl100\\trspdfl0\\trspdfl3 Repeated;}" +
                "{\\*\\ts121\\tsrowd\\tscfirstrow" +
                "\\trspdt120\\trspdft3 Conditional;}" +
                "{\\*\\ts125\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\trspdb-40\\trspdfb3 Nested keycode;}" +
                "}" +
                "\\trowd\\trspdt80\\trspdft3" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 6,
            "RTF stylesheet должен сохранять шесть ordered table-style entries.");

        var full =
            model.TableStyles[0].RowFormatting?.CellSpacing;

        Assert(
            full is
            {
                Top:
                {
                    Value: 40,
                    Unit: 3,
                    Points: 2
                },
                Right:
                {
                    Value: -20,
                    Unit: 3,
                    Points: -1
                },
                Bottom:
                {
                    Value: 120,
                    Unit: 0,
                    Points: null
                },
                Left:
                {
                    Value: -10,
                    Unit: 0,
                    Points: null
                }
            },
            "RTF trspd* сохраняет independent source spacing; unit 3 переводит twips в points, unit 0 оставляет Null.");

        var incomplete =
            model.TableStyles[1].RowFormatting?.CellSpacing;

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
            "RTF table-style trspd* должен сохранять value-only и unit-only declarations без вычисления отсутствующих данных.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF table-style entry без row properties должен оставлять null RowFormatting.");

        var repeated =
            model.TableStyles[3].RowFormatting?.CellSpacing;

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
            "RTF repeated trspdl/trspdfl должен сохранять последнее source value и unit.");

        Assert(
            model.TableStyles[4].Handle == 121 &&
            model.TableStyles[4].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[4].RowFormatting?.CellSpacing is
            {
                Top:
                {
                    Value: 120,
                    Unit: 3,
                    Points: 6
                },
                Left: null
            } &&
            model.TableStyles[0].RowFormatting?.CellSpacing?.Top?.Value == 40,
            "RTF conditional entry с тем же style handle должен сохранять независимое spacing.");

        Assert(
            model.TableStyles[5].RowFormatting?.CellSpacing is
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
            "RTF nested keycode не должен добавлять table-style spacing.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.Rows.Single().CellSpacingTop is
            {
                Value: 80,
                Unit: 3
            } &&
            bodyTable.Rows.Single().CellSpacingTopPoints == 4 &&
            bodyTable.Rows.Single().CellSpacingLeft == null,
            "RTF body row должен использовать только собственные spacing controls.");

        var extremes =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts126\\tsrowd" +
                "\\trspdl-2147483648\\trspdfl3 Extremes;}}" +
                "Body\\par}");

        Assert(
            extremes.TableStyles.Single().RowFormatting?.CellSpacing?.Left is
            {
                Value: int.MinValue,
                Unit: 3
            } &&
            extremes.TableStyles.Single().RowFormatting?.CellSpacing?.Left?.Points ==
                int.MinValue / 20.0,
            "RTF table-style spacing должен сохранять полный signed Int32 диапазон.");

        foreach (var control in new[]
                 {
                     "trspdt",
                     "trspdr",
                     "trspdb",
                     "trspdl",
                     "trspdft",
                     "trspdfr",
                     "trspdfb",
                     "trspdfl",
                     "trspdft1",
                     "trspdfr2",
                     "trspdfb4",
                     "trspdfl-1",
                     "trspdl2147483648"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts127\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF table-style {control} должен отклоняться при invalid numeric parameter или unit.");
        }
    }
}
