using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет исходные tblind/tblindtype внутри RTF table-style catalog:
    /// nullable value/unit, dxa/pct conversion, auto/nil units, repeats,
    /// signed Int32 bounds, conditional entries, nested-group isolation,
    /// numeric grammar и независимость от body table indentation.
    /// </summary>
    private static void TestRtfTableStyleIndent(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-indent.rtf");

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
                "{\\*\\ts260\\tsrowd\\tblind720\\tblindtype1 Dxa;}" +
                "{\\*\\ts261\\tsrowd\\tblind1250\\tblindtype3 Pct;}" +
                "{\\*\\ts262\\tsrowd\\tblind-200\\tblindtype2 Nil;}" +
                "{\\*\\ts263\\tsrowd\\tblind400\\tblindtype0 Auto;}" +
                "{\\*\\ts264\\tsrowd\\tblind-120 Value-only;}" +
                "{\\*\\ts265\\tsrowd\\tblindtype1 Unit-only;}" +
                "{\\*\\ts266\\tsrowd Plain;}" +
                "{\\*\\ts260\\tsrowd\\tscfirstrow" +
                "\\tblind-300\\tblindtype1 Conditional;}" +
                "{\\*\\ts267\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\tblind90\\tblindtype1 Nested;}" +
                "}" +
                "\\trowd\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 9,
            "RTF stylesheet должен сохранять девять ordered table-style entries.");

        Assert(
            model.TableStyles[0].RowFormatting?.Indent is
            {
                Value: 720,
                Unit: 1,
                Points: 36,
                Percent: null
            },
            "RTF tblindtype1 должен преобразовать explicit twips в points.");

        Assert(
            model.TableStyles[1].RowFormatting?.Indent is
            {
                Value: 1250,
                Unit: 3,
                Points: null,
                Percent: 25
            },
            "RTF tblindtype3 должен преобразовать percent fiftieths в проценты.");

        Assert(
            model.TableStyles[2].RowFormatting?.Indent is
            {
                Value: -200,
                Unit: 2,
                Points: null,
                Percent: null
            } &&
            model.TableStyles[3].RowFormatting?.Indent is
            {
                Value: 400,
                Unit: 0,
                Points: null,
                Percent: null
            },
            "RTF table-style nil/auto units должны сохранять source value без inferred layout.");

        Assert(
            model.TableStyles[4].RowFormatting?.Indent is
            {
                Value: -120,
                Unit: null,
                Points: null,
                Percent: null
            } &&
            model.TableStyles[5].RowFormatting?.Indent is
            {
                Value: null,
                Unit: 1,
                Points: null,
                Percent: null
            },
            "RTF value-only и unit-only declarations не должны создавать default values.");

        Assert(
            model.TableStyles[6].RowFormatting == null,
            "RTF table-style без row properties должен сохранять null RowFormatting.");

        Assert(
            model.TableStyles[7].Handle == 260 &&
            model.TableStyles[7].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[7].RowFormatting?.Indent is
            {
                Value: -300,
                Unit: 1,
                Points: -15
            } &&
            model.TableStyles[0].RowFormatting?.Indent?.Points == 36,
            "RTF conditional table style должен сохранять independent table indent.");

        Assert(
            model.TableStyles[8].RowFormatting?.Indent is
            {
                Value: 90,
                Unit: 1,
                Points: 4.5
            },
            "RTF nested keycode не должен перезаписывать style indentation.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.Indent == null &&
            bodyTable.Rows.Single().TableIndent == null,
            "RTF table-style indentation не должен применяться к body таблице.");

        var repeat =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts268\\tsrowd" +
                "\\tblind20\\tblind1250" +
                "\\tblindtype0\\tblindtype3 Repeated;}" +
                "{\\*\\ts269\\tsrowd" +
                "\\tblindtype3\\tblindtype1\\tblind0 Zero;}" +
                "{\\*\\ts270\\tsrowd\\tblind60\\tblindtype9 Unknown;}" +
                "{\\*\\ts271\\tsrowd\\tblind-60\\tblindtype-1 Negative unit;}" +
                "}" +
                "Text\\par}");

        Assert(
            repeat.TableStyles[0].RowFormatting?.Indent is
            {
                Value: 1250,
                Unit: 3,
                Points: null,
                Percent: 25
            } &&
            repeat.TableStyles[1].RowFormatting?.Indent is
            {
                Value: 0,
                Unit: 1,
                Points: 0,
                Percent: null
            },
            "RTF repeated tblind/tblindtype должны сохранять последние source values.");

        Assert(
            repeat.TableStyles[2].RowFormatting?.Indent is
            {
                Value: 60,
                Unit: 9,
                Points: null,
                Percent: null
            } &&
            repeat.TableStyles[3].RowFormatting?.Indent is
            {
                Value: -60,
                Unit: -1,
                Points: null,
                Percent: null
            },
            "RTF unknown tblindtype должен оставаться source metadata без effective defaults.");

        var boundaries =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts272\\tsrowd\\tblind-2147483648\\tblindtype1 Min;}" +
                "{\\*\\ts273\\tsrowd\\tblind2147483647\\tblindtype3 Max;}" +
                "}" +
                "Text\\par}");

        Assert(
            boundaries.TableStyles[0].RowFormatting?.Indent is
            {
                Value: int.MinValue,
                Unit: 1
            } &&
            boundaries.TableStyles[0].RowFormatting?.Indent?.Points ==
                int.MinValue / 20.0 &&
            boundaries.TableStyles[1].RowFormatting?.Indent is
            {
                Value: int.MaxValue,
                Unit: 3
            } &&
            boundaries.TableStyles[1].RowFormatting?.Indent?.Percent ==
                int.MaxValue / 50.0,
            "RTF table-style indent должен сохранять полный signed Int32 диапазон.");

        var bodyExplicit =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts274\\tsrowd\\tblind720\\tblindtype1 Style;}" +
                "}" +
                "\\trowd\\tblind-100\\tblindtype1" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            bodyExplicit.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Indent is
                {
                    SourceValue: -100,
                    SourceUnit: 1,
                    Points: -5
                } &&
            bodyExplicit.TableStyles.Single().RowFormatting?.Indent is
            {
                Value: 720,
                Unit: 1,
                Points: 36
            },
            "RTF body tblind и table-style tblind должны храниться независимо.");

        foreach (var invalid in new[]
                 {
                     "tblind",
                     "tblindtype",
                     "tblind-",
                     "tblind2147483648",
                     "tblind-2147483649",
                     "tblindtype2147483648"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts275\\tsrowd\\" +
                    invalid +
                    " Invalid;}}" +
                    "Text\\par}"),
                $"RTF table-style {invalid} должен отклонять отсутствующий или invalid numeric parameter.");
        }
    }
}
