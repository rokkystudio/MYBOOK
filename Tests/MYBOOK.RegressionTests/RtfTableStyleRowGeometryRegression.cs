using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет source geometry RTF table-style row: signed trleft/trgaph/trrh,
    /// twips-to-points projection, auto/minimum/exact height, missing controls,
    /// repeated values, nested style groups и независимость body row state.
    /// </summary>
    private static void TestRtfTableStyleRowGeometry(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-geometry.rtf");

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
                "{\\*\\ts90\\tsrowd\\trleft-120\\trgaph90\\trrh-480 Exact;}" +
                "{\\*\\ts91\\tsrowd\\trleft0\\trgaph0\\trrh0 Auto;}" +
                "{\\*\\ts92\\tsrowd\\trleft120\\trgaph20\\trrh400 Minimum;}" +
                "{\\*\\ts93\\tsrowd Plain;}" +
                "{\\*\\ts94\\tsrowd\\trleft40\\trleft-60" +
                "\\trgaph10\\trgaph20\\trrh360\\trrh-720 Last;}" +
                "{\\*\\ts95\\tsrowd" +
                "{\\*\\keycode\\shift\\ctrl t}" +
                "\\trleft-40 Nested;}" +
                "}" +
                "\\trowd\\trleft80\\trgaph60\\trrh-120" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 6,
            "RTF stylesheet должен сохранять шесть ordered table-style entries.");

        var exact =
            model.TableStyles[0].RowFormatting;

        Assert(
            exact is
            {
                LeftOffsetTwips: -120,
                LeftOffsetPoints: -6,
                CellGapTwips: 90,
                CellGapPoints: 4.5,
                HeightTwips: -480,
                HeightPoints: 24,
                HeightExact: true
            },
            "RTF negative trleft/trrh должны сохранять signed source и exact height.");

        var automatic =
            model.TableStyles[1].RowFormatting;

        Assert(
            automatic is
            {
                LeftOffsetTwips: 0,
                LeftOffsetPoints: 0,
                CellGapTwips: 0,
                CellGapPoints: 0,
                HeightTwips: 0,
                HeightPoints: 0,
                HeightExact: false
            },
            "RTF explicit zero geometry должна отличаться от отсутствующих controls.");

        var minimum =
            model.TableStyles[2].RowFormatting;

        Assert(
            minimum is
            {
                LeftOffsetTwips: 120,
                LeftOffsetPoints: 6,
                CellGapTwips: 20,
                CellGapPoints: 1,
                HeightTwips: 400,
                HeightPoints: 20,
                HeightExact: false
            },
            "RTF positive trrh должен сохранять minimum height.");

        Assert(
            model.TableStyles[3].RowFormatting == null,
            "RTF отсутствие geometry и других row controls должно сохранять null RowFormatting.");

        var repeated =
            model.TableStyles[4].RowFormatting;

        Assert(
            repeated is
            {
                LeftOffsetTwips: -60,
                LeftOffsetPoints: -3,
                CellGapTwips: 20,
                CellGapPoints: 1,
                HeightTwips: -720,
                HeightPoints: 36,
                HeightExact: true
            },
            "RTF repeated trleft/trgaph/trrh должны использовать последние source values.");

        var nested =
            model.TableStyles[5].RowFormatting;

        Assert(
            nested is
            {
                LeftOffsetTwips: -40,
                LeftOffsetPoints: -2,
                CellGapTwips: null,
                CellGapPoints: null,
                HeightTwips: null,
                HeightPoints: null,
                HeightExact: null
            },
            "RTF controls nested keycode group не должны становиться table-style row geometry.");

        var body =
            model.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            body.LeftOffsetTwips == 80 &&
            body.CellGapTwips == 60 &&
            body.HeightTwips == -120 &&
            body.HeightExact,
            "RTF body row geometry должна определяться собственными controls, не stylesheet.");

        var isolated =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts99\\tsrowd\\trrh-2147483648 Maximum magnitude;}" +
                "}" +
                "Body\\par}");

        var extreme =
            isolated.TableStyles.Single().RowFormatting;

        Assert(
            extreme is
            {
                HeightTwips: int.MinValue,
                HeightExact: true
            } &&
            extreme.HeightPoints ==
                Math.Abs((double)int.MinValue) / 20.0,
            "RTF signed 32-bit trrh должен сохраняться без overflow и диапазонных эвристик.");

        foreach (var control in new[]
                 {
                     "trleft",
                     "trgaph",
                     "trrh"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts96\\tsrowd\\" +
                    control +
                    " Missing;}}" +
                    "Body\\par}"),
                $"RTF table-style {control} требует numeric parameter.");
        }

        foreach (var control in new[]
                 {
                     "trleft2147483648",
                     "trgaph-2147483649",
                     "trrh2147483648",
                     "trrh-"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts97\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF {control} должен отклоняться при malformed signed Int32 parameter.");
        }
    }
}
