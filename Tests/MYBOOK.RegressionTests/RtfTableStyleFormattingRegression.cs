using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет neutral RTF table-style cell formatting: raw no-effect width,
    /// Auto/Twips padding, vertical alignment, nowrap, shading/patterns,
    /// source-preserving borders, band sizes, last-value precedence
    /// и structural validation без применения style cascade к body.
    /// </summary>
    private static void TestRtfTableStyleFormatting(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-formatting.rtf");

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
                "{\\colortbl;" +
                "\\red255\\green0\\blue0;" +
                "\\red0\\green0\\blue255;}" +
                "{\\stylesheet" +
                "{\\*\\ts20\\tsrowd" +
                "\\tscellwidth123\\tscellwidthfts9" +
                "\\tscellpaddt40\\tscellpaddft3" +
                "\\tscellpaddl-20\\tscellpaddfl0" +
                "\\tscellpaddr60" +
                "\\tscellpaddb80\\tscellpaddfb3" +
                "\\tsvertalc\\tsnowrap" +
                "\\tscellcfpat1\\tscellcbpat2\\tscellpct2500\\tsbgdkcross" +
                "\\tsbrdrt\\brdrdashd\\brdrw20\\brdrcf1\\brsp10\\brdrsh\\brdrframe" +
                "\\tsbrdrl\\brdrnil" +
                "\\tsbrdrb" +
                "\\tsbrdrr\\brdrdb\\brdrw40" +
                "\\tsbrdrh\\brdrdot" +
                "\\tsbrdrv\\brdrhair" +
                "\\tsbrdrdgl\\brdrnone" +
                "\\tsbrdrdgr\\brdrtriple" +
                "\\tscbandsh2\\tscbandsv3 Styled;}" +
                "}" +
                "\\trowd\\ts20\\cellx1000\\intbl Body\\cell\\row}");

        var style =
            model.TableStyles.Single();
        var formatting =
            style.CellFormatting ??
            throw new InvalidOperationException(
                "RTF table-style formatting metadata отсутствует.");

        Assert(
            formatting.EmittedWidth == 123 &&
            formatting.EmittedWidthUnit == 9,
            "RTF tscellwidth/tscellwidthfts должны сохраняться raw без layout interpretation.");

        var padding =
            formatting.Padding ??
            throw new InvalidOperationException(
                "RTF table-style padding metadata отсутствует.");

        Assert(
            padding.Top is
            {
                Value: 40,
                Unit: 3,
                Points: 2
            },
            "RTF tscellpaddt/tscellpaddft3 должны сохранять Twips и points.");
        Assert(
            padding.Left is
            {
                Value: -20,
                Unit: 0,
                Points: null
            },
            "RTF table-style padding unit 0 должен сохранять Auto без points.");
        Assert(
            padding.Right is
            {
                Value: 60,
                Unit: null,
                Points: null
            },
            "RTF table-style padding без unit должен сохранять missing unit.");
        Assert(
            padding.Bottom is
            {
                Value: 80,
                Unit: 3,
                Points: 4
            },
            "RTF bottom table-style padding должен переводить unit 3 из twips в points.");

        Assert(
            formatting.VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Center &&
            formatting.NoWrap,
            "RTF tsvertalc/tsnowrap должны сохранять table-style cell flags.");

        var shading =
            formatting.Shading ??
            throw new InvalidOperationException(
                "RTF table-style shading metadata отсутствует.");

        Assert(
            shading.ForegroundColorIndex == 1 &&
            shading.BackgroundColorIndex == 2 &&
            shading.PercentageHundredths == 2500 &&
            shading.Pattern == 0x0012,
            "RTF table-style shading должна сохранять exact source indexes/percentage/pattern.");
        Assert(
            shading.Resolved is
            {
                ForegroundColor: "#FF0000",
                BackgroundColor: "#0000FF",
                Pattern: 0x0012,
                FillPercentage: 25
            },
            "RTF table-style shading должна предоставлять resolved neutral colors/fill.");

        var top =
            formatting.BorderTop ??
            throw new InvalidOperationException(
                "RTF top table-style border metadata отсутствует.");

        Assert(
            !top.None &&
            top.Type == 0x08 &&
            top.WidthTwips == 20 &&
            top.ColorIndex == 1 &&
            top.SpacingTwips == 10 &&
            top.Shadow &&
            top.Frame,
            "RTF tsbrdrt должен сохранять exact border source properties.");
        Assert(
            top.Resolved is
            {
                WidthPoints: 1,
                Type: 0x08,
                Color: "#FF0000",
                SpacingPoints: 0.5,
                Shadow: true,
                Frame: true
            },
            "RTF table-style border должен предоставлять resolved neutral border.");

        Assert(
            formatting.BorderLeft is
            {
                None: true,
                Resolved: null
            },
            "RTF tsbrdrl/brdrnil должен отличаться от отсутствующего border selector.");

        Assert(
            formatting.BorderBottom is
            {
                None: false,
                Type: 0,
                Resolved: null
            },
            "RTF selector-only tsbrdrb должен сохраняться отдельно от missing border.");

        Assert(
            formatting.BorderRight is
            {
                None: false,
                Type: 0x03,
                WidthTwips: 40
            } &&
            formatting.BorderHorizontalInside?.Type == 0x06 &&
            formatting.BorderVerticalInside?.Type == 0x05 &&
            formatting.BorderDiagonalDown is
            {
                None: true,
                Resolved: null
            } &&
            formatting.BorderDiagonalUp?.Type == 0x0A,
            "RTF table-style border selectors должны сохранять side/inside/diagonal source state.");

        Assert(
            formatting.RowBandSize == 2 &&
            formatting.ColumnBandSize == 3,
            "RTF tscbandsh/tscbandsv должны сохранять raw band sizes.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.StyleHandle == 20 &&
            bodyTable.Rows.Single().TableStyleHandle == 20,
            "RTF table-style formatting catalog не должен применять formatting к body table.");

        var repeated =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts31\\tsrowd" +
                "\\tscellwidth1\\tscellwidth-2" +
                "\\tsvertalt\\tsvertalb" +
                "\\tscellpct1000\\tscellpct7500" +
                "\\tsbghoriz\\tsbgvert" +
                "\\tscbandsh1\\tscbandsh-4" +
                "\\tscbandsv2\\tscbandsv5 Repeated;}" +
                "}" +
                "Body\\par}");

        var repeatedFormatting =
            repeated.TableStyles.Single().CellFormatting ??
            throw new InvalidOperationException(
                "RTF repeated table-style formatting metadata отсутствует.");

        Assert(
            repeatedFormatting.EmittedWidth == -2 &&
            repeatedFormatting.VerticalAlignment ==
                DocumentTableCellVerticalAlignment.Bottom &&
            repeatedFormatting.Shading?.PercentageHundredths == 7500 &&
            repeatedFormatting.Shading?.Pattern == 0x0015 &&
            repeatedFormatting.RowBandSize == -4 &&
            repeatedFormatting.ColumnBandSize == 5,
            "RTF repeated table-style formatting controls должны использовать последнее source value.");

        var oldBorderModel =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts32\\tsrowd" +
                "\\tsbrdrt\\brdrs\\brdrw20" +
                "\\trbrdrt\\brdrdb\\brdrw40 Border isolation;}" +
                "}" +
                "Body\\par}");

        var isolatedBorder =
            oldBorderModel.TableStyles
                .Single()
                .CellFormatting
                ?.BorderTop;

        Assert(
            isolatedBorder is
            {
                Type: 0x01,
                WidthTwips: 20
            },
            "RTF old trbrdr selector должен завершать routing generic brdr controls к tsbrdr selector.");

        var patternCases =
            new (string Control, ushort Pattern)[]
            {
                ("tsbgdkhor", 0x000E),
                ("tsbgdkvert", 0x000F),
                ("tsbgdkfdiag", 0x0010),
                ("tsbgdkbdiag", 0x0011),
                ("tsbgdkcross", 0x0012),
                ("tsbgdkdcross", 0x0013),
                ("tsbghoriz", 0x0014),
                ("tsbgvert", 0x0015),
                ("tsbgfdiag", 0x0016),
                ("tsbgbdiag", 0x0017),
                ("tsbgcross", 0x0018),
                ("tsbgdcross", 0x0019)
            };

        foreach (var (control, expectedPattern) in patternCases)
        {
            var patternModel =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet" +
                    "{\\*\\ts40\\tsrowd\\" +
                    control +
                    " Pattern;}" +
                    "}" +
                    "Body\\par}");

            Assert(
                patternModel.TableStyles
                    .Single()
                    .CellFormatting
                    ?.Shading
                    ?.Pattern == expectedPattern,
                $"RTF {control} должен сохранять точный table-style shading pattern.");
        }

        foreach (var source in new[]
                 {
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellwidth Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellpaddt Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellpaddft3 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellpaddt10\\tscellpaddft1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsvertalt1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsnowrap0 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellcfpat Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscellpct Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbgcross1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbrdrt1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbrdrt\\brdrs1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbrdrt\\brdrw Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbrdrt\\brdrnil0 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tsbrdrt\\brdrsh0 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscbandsh Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts50\\tsrowd\\tscbandsv Bad;}}Body\\par}"
                 })
        {
            Assert(
                RejectsInvalid(source),
                "RTF malformed table-style cell formatting должна завершаться structural error.");
        }
    }
}
