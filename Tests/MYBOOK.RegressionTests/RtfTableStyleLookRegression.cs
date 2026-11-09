using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет 11 parameterless RTF tbllk* flags в table-style catalog:
    /// independent nullable source state, complete/partial flag sets, repeated
    /// controls, conditional entries, nested destinations, body isolation,
    /// HTML body look metadata и numeric-parameter grammar.
    /// </summary>
    private static void TestRtfTableStyleLook(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-look.rtf");

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
                "{\\*\\ts280\\tsrowd" +
                "\\tbllkborder\\tbllkshading\\tbllkfont" +
                "\\tbllkcolor\\tbllkbestfit\\tbllkhdrrows" +
                "\\tbllklastrow\\tbllkhdrcols\\tbllklastcol" +
                "\\tbllknorowband\\tbllknocolband Full look;}" +
                "{\\*\\ts281\\tsrowd\\tbllkborder\\tbllkbestfit Partial look;}" +
                "{\\*\\ts282\\tsrowd Plain;}" +
                "{\\*\\ts280\\tsrowd\\tscfirstrow" +
                "\\tbllkfont\\tbllklastrow Conditional;}" +
                "{\\*\\ts283\\tsrowd" +
                "{\\*\\keycode\\tbllkborder\\tbllkfont\\ts299}" +
                "\\tbllkcolor Nested keycode;}" +
                "{\\*\\ts284\\tsrowd" +
                "\\tbllkborder\\tbllkborder Repeated;}" +
                "{\\*\\ts285\\tsrowd\\trleft120\\tbllkshading Mixed;}" +
                "}" +
                "\\trowd\\tbllkcolor\\tbllkhdrrows" +
                "\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 7,
            "RTF stylesheet должен сохранять семь ordered table-style entries.");

        Assert(
            model.TableStyles[0].RowFormatting?.Look is
            {
                Border: true,
                Shading: true,
                Font: true,
                Color: true,
                BestFit: true,
                HeaderRows: true,
                LastRow: true,
                HeaderColumns: true,
                LastColumn: true,
                NoRowBand: true,
                NoColumnBand: true
            },
            "RTF table-style tbllk* должен сохранять все 11 parameterless flags.");

        Assert(
            model.TableStyles[1].RowFormatting?.Look is
            {
                Border: true,
                Shading: null,
                Font: null,
                Color: null,
                BestFit: true,
                HeaderRows: null,
                LastRow: null,
                HeaderColumns: null,
                LastColumn: null,
                NoRowBand: null,
                NoColumnBand: null
            },
            "RTF table-style partial look должен сохранять absent flags как null.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF table-style без row formatting должен сохранять null RowFormatting.");

        Assert(
            model.TableStyles[3].Handle == 280 &&
            model.TableStyles[3].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[3].RowFormatting?.Look is
            {
                Border: null,
                Font: true,
                LastRow: true,
                Color: null
            } &&
            model.TableStyles[0].RowFormatting?.Look?.Border == true,
            "RTF conditional entry с общим tsN должна хранить независимые table-look flags.");

        Assert(
            model.TableStyles[4].RowFormatting?.Look is
            {
                Border: null,
                Color: true
            },
            "RTF keycode nested group не должен менять outer table-style flags.");

        Assert(
            model.TableStyles[5].RowFormatting?.Look is
            {
                Border: true,
                Shading: null
            },
            "RTF repeated table-look flags должны сохранять source presence.");

        Assert(
            model.TableStyles[6].RowFormatting is
            {
                LeftOffsetTwips: 120,
                Look:
                {
                    Border: null,
                    Shading: true
                }
            },
            "RTF table-style look должен сочетаться с другими row source properties.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.Look is
            {
                Border: null,
                Shading: null,
                Font: null,
                Color: true,
                BestFit: null,
                HeaderRows: true,
                LastRow: null,
                HeaderColumns: null,
                LastColumn: null,
                NoRowBand: null,
                NoColumnBand: null
            } &&
            bodyTable.Rows.Single().LookBorder == null &&
            bodyTable.Rows.Single().LookColor == true,
            "RTF body table-look должен использовать только body controls.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        var tableStart =
            html.IndexOf(
                "<table class=\"flow-table\"",
                StringComparison.Ordinal);
        var tableEnd =
            html.IndexOf(
                '>',
                tableStart);
        var tableTag =
            html[tableStart..(tableEnd + 1)];

        AssertContains(
            tableTag,
            "data-table-look-color=\"true\"",
            "RTF body table look color HTML metadata");
        AssertContains(
            tableTag,
            "data-table-look-header-rows=\"true\"",
            "RTF body table look header rows HTML metadata");

        Assert(
            !tableTag.Contains(
                "data-table-look-border",
                StringComparison.Ordinal) &&
            !tableTag.Contains(
                "data-table-look-shading",
                StringComparison.Ordinal),
            "RTF stylesheet look не должен создавать body HTML table flags.");

        var words = new[]
        {
            "tbllkborder",
            "tbllkshading",
            "tbllkfont",
            "tbllkcolor",
            "tbllkbestfit",
            "tbllkhdrrows",
            "tbllklastrow",
            "tbllkhdrcols",
            "tbllklastcol",
            "tbllknorowband",
            "tbllknocolband"
        };

        foreach (var word in words)
        {
            foreach (var parameter in new[]
                     {
                         "0",
                         "1",
                         "-1",
                         "2147483647"
                     })
            {
                Assert(
                    RejectsInvalid(
                        "{\\rtf1\\ansi" +
                        "{\\stylesheet{\\*\\ts286\\tsrowd\\" +
                        word +
                        parameter +
                        " Invalid;}}" +
                        "Text\\par}"),
                    $"RTF table-style {word}{parameter} должен отклонять numeric parameter.");
            }
        }

        var emptyBody =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts287\\tsrowd" +
                "\\tbllkborder\\tbllknocolband Style only;}}" +
                "\\trowd\\cellx1000\\intbl Plain\\cell\\row}");

        Assert(
            emptyBody.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Look == null &&
            emptyBody.TableStyles.Single().RowFormatting?.Look is
            {
                Border: true,
                NoColumnBand: true
            },
            "RTF table style look должен оставаться source metadata при body table без look flags.");
    }
}
