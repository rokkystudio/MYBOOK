using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет older table-formatting properties внутри RTF table styles:
    /// row widths/padding/shading/borders, unit-only source declarations,
    /// raw cell-shading aliases, border routing и malformed grammar без
    /// применения catalog formatting к body.
    /// </summary>
    private static void TestRtfTableStyleLegacyFormatting(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-legacy-formatting.rtf");

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
                "{\\*\\ts60\\tsrowd" +
                "\\trftsWidth3\\trwWidth1440" +
                "\\trftsWidthB3" +
                "\\trftsWidthA2\\trwWidthA2500" +
                "\\trpaddl108\\trpaddr108" +
                "\\trpaddfl3\\trpaddfr3" +
                "\\trpaddft3\\trpaddfb0" +
                "\\trshdng2500\\trcfpat1\\trcbpat2\\trbgcross" +
                "\\trbrdrt\\brdrs\\brdrw20\\brdrcf1\\brsp10\\brdrsh" +
                "\\trbrdrl\\brdrnil" +
                "\\trbrdrb" +
                "\\trbrdrr\\brdrdb\\brdrw40" +
                "\\trbrdrh\\brdrdot" +
                "\\trbrdrv\\brdrhair" +
                "\\clshdngraw5000\\clcfpatraw2\\clcbpatraw1\\rawclbgdkfdiag" +
                " Legacy Table;}" +
                "}" +
                "\\trowd\\ts60\\cellx1000\\intbl Body\\cell\\row}");

        var style =
            model.TableStyles.Single();
        var row =
            style.RowFormatting ??
            throw new InvalidOperationException(
                "RTF older row-style formatting metadata отсутствует.");

        Assert(
            row.PreferredWidth is
            {
                Unit: 3,
                Value: 1440,
                Resolved:
                {
                    Kind: DocumentPreferredWidthKind.Points,
                    Value: 72
                }
            },
            "RTF table-style row preferred width должен сохранять raw pair и resolved points.");

        Assert(
            row.InvisibleCellBeforeWidth is
            {
                Unit: 3,
                Value: null,
                Resolved: null
            },
            "RTF trftsWidthB3 без trwWidthB должен сохраняться как unit-only source declaration.");

        Assert(
            row.InvisibleCellAfterWidth is
            {
                Unit: 2,
                Value: 2500,
                Resolved:
                {
                    Kind: DocumentPreferredWidthKind.Percent,
                    Value: 50
                }
            },
            "RTF table-style invisible-after width должен сохранять raw percent pair и resolved value.");

        var padding =
            row.Padding ??
            throw new InvalidOperationException(
                "RTF older row-style padding metadata отсутствует.");

        Assert(
            padding.Left is
            {
                Value: 108,
                Unit: 3,
                Points: 5.4
            } &&
            padding.Right is
            {
                Value: 108,
                Unit: 3,
                Points: 5.4
            },
            "RTF table-style row left/right padding должен сохранять twips и points.");

        Assert(
            padding.Top is
            {
                Value: null,
                Unit: 3,
                Points: null
            } &&
            padding.Bottom is
            {
                Value: null,
                Unit: 0,
                Points: null
            },
            "RTF unit-only trpaddft/trpaddfb declarations должны сохраняться без synthetic value.");

        var rowShading =
            row.Shading ??
            throw new InvalidOperationException(
                "RTF row-style shading metadata отсутствует.");

        Assert(
            rowShading.ForegroundColorIndex == 1 &&
            rowShading.BackgroundColorIndex == 2 &&
            rowShading.PercentageHundredths == 2500 &&
            rowShading.Pattern == 0x0018 &&
            rowShading.Resolved is
            {
                ForegroundColor: "#FF0000",
                BackgroundColor: "#0000FF",
                Pattern: 0x0018,
                FillPercentage: 25
            },
            "RTF older row shading должна сохранять source и resolved neutral values.");

        Assert(
            row.BorderTop is
            {
                None: false,
                Type: 0x01,
                WidthTwips: 20,
                ColorIndex: 1,
                SpacingTwips: 10,
                Shadow: true,
                Resolved:
                {
                    WidthPoints: 1,
                    Type: 0x01,
                    Color: "#FF0000",
                    SpacingPoints: 0.5,
                    Shadow: true
                }
            },
            "RTF trbrdrt должен сохранять source и resolved row border.");

        Assert(
            row.BorderLeft is
            {
                None: true,
                Resolved: null
            } &&
            row.BorderBottom is
            {
                None: false,
                Type: 0,
                Resolved: null
            } &&
            row.BorderRight is
            {
                Type: 0x03,
                WidthTwips: 40
            } &&
            row.BorderHorizontalInside?.Type == 0x06 &&
            row.BorderVerticalInside?.Type == 0x05,
            "RTF older row borders должны различать explicit none, selector-only и line styles.");

        var rawCellShading =
            style.CellFormatting
                ?.RawCellShading ??
            throw new InvalidOperationException(
                "RTF raw cell-style shading metadata отсутствует.");

        Assert(
            rawCellShading.ForegroundColorIndex == 2 &&
            rawCellShading.BackgroundColorIndex == 1 &&
            rawCellShading.PercentageHundredths == 5000 &&
            rawCellShading.Pattern == 0x0010 &&
            rawCellShading.Resolved is
            {
                ForegroundColor: "#0000FF",
                BackgroundColor: "#FF0000",
                Pattern: 0x0010,
                FillPercentage: 50
            },
            "RTF rawcl*/cl*raw aliases должны сохранять higher-priority source shading отдельно от ts shading.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.StyleHandle == 60 &&
            bodyTable.Rows.Single().TableStyleHandle == 60 &&
            bodyTable.Rows.Single().BorderTop == null &&
            bodyTable.Rows.Single().Shading == null,
            "RTF table-style older formatting не должно применяться к body table без style cascade.");

        var repeated =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts61\\tsrowd" +
                "\\trftsWidth1\\trftsWidth3\\trwWidth720" +
                "\\trpaddl20\\trpaddl40\\trpaddfl3" +
                "\\trshdng1000\\trshdng7500\\trbghoriz\\trbgvert" +
                "\\clshdngraw1000\\clshdngraw6000\\rawclbghoriz\\rawclbgdcross" +
                " Repeated;}" +
                "}" +
                "Body\\par}");

        var repeatedStyle =
            repeated.TableStyles.Single();

        Assert(
            repeatedStyle.RowFormatting?.PreferredWidth is
            {
                Unit: 3,
                Value: 720
            } &&
            repeatedStyle.RowFormatting?.Padding?.Left is
            {
                Value: 40,
                Unit: 3,
                Points: 2
            } &&
            repeatedStyle.RowFormatting?.Shading is
            {
                PercentageHundredths: 7500,
                Pattern: 0x0015
            } &&
            repeatedStyle.CellFormatting?.RawCellShading is
            {
                PercentageHundredths: 6000,
                Pattern: 0x0019
            },
            "RTF repeated older/raw table-style controls должны использовать последнее source value.");

        var borderIsolation =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet" +
                "{\\*\\ts62\\tsrowd" +
                "\\trbrdrt\\brdrs\\brdrw20" +
                "\\clbrdrt\\brdrdb\\brdrw40 Isolation;}" +
                "}" +
                "Body\\par}");

        Assert(
            borderIsolation.TableStyles
                .Single()
                .RowFormatting
                ?.BorderTop is
                {
                    Type: 0x01,
                    WidthTwips: 20
                },
            "RTF clbrdr* selector внутри style должен завершать routing generic brdr* к row trbrdr* selector.");

        var rowPatternCases =
            new (string Control, ushort Pattern)[]
            {
                ("trbgdkhor", 0x000E),
                ("trbgdkvert", 0x000F),
                ("trbgdkfdiag", 0x0010),
                ("trbgdkbdiag", 0x0011),
                ("trbgdkcross", 0x0012),
                ("trbgdkdcross", 0x0013),
                ("trbghoriz", 0x0014),
                ("trbgvert", 0x0015),
                ("trbgfdiag", 0x0016),
                ("trbgbdiag", 0x0017),
                ("trbgcross", 0x0018),
                ("trbgdcross", 0x0019)
            };

        foreach (var (control, expectedPattern) in rowPatternCases)
        {
            var patternModel =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet" +
                    "{\\*\\ts70\\tsrowd\\" +
                    control +
                    " Pattern;}" +
                    "}" +
                    "Body\\par}");

            Assert(
                patternModel.TableStyles
                    .Single()
                    .RowFormatting
                    ?.Shading
                    ?.Pattern == expectedPattern,
                $"RTF {control} должен сохранять exact older row-style shading pattern.");
        }

        var rawPatternCases =
            new (string Control, ushort Pattern)[]
            {
                ("rawclbgdkhor", 0x000E),
                ("rawclbgdkvert", 0x000F),
                ("rawclbgdkfdiag", 0x0010),
                ("rawclbgdkbdiag", 0x0011),
                ("rawclbgdkcross", 0x0012),
                ("rawclbgdkdcross", 0x0013),
                ("rawclbghoriz", 0x0014),
                ("rawclbgvert", 0x0015),
                ("rawclbgfdiag", 0x0016),
                ("rawclbgbdiag", 0x0017),
                ("rawclbgcross", 0x0018),
                ("rawclbgdcross", 0x0019)
            };

        foreach (var (control, expectedPattern) in rawPatternCases)
        {
            var patternModel =
                Read(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet" +
                    "{\\*\\ts71\\tsrowd\\" +
                    control +
                    " Pattern;}" +
                    "}" +
                    "Body\\par}");

            Assert(
                patternModel.TableStyles
                    .Single()
                    .CellFormatting
                    ?.RawCellShading
                    ?.Pattern == expectedPattern,
                $"RTF {control} должен сохранять exact raw cell-style shading pattern.");
        }

        foreach (var source in new[]
                 {
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trftsWidth Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trftsWidth4 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trwWidth720 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trpaddl Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trpaddfl1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trshdng Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trpat-1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trbgcross1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trbrdrt1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\trbrdrt\\brdrs1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\clshdngraw Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\rawclbgcross1 Bad;}}Body\\par}"
                 })
        {
            Assert(
                RejectsInvalid(source),
                "RTF malformed older/raw table-style formatting должна завершаться structural error.");
        }
    }
}
