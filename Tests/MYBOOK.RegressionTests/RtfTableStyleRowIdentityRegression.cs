using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет nullable RTF table-style row identity и banding: signed
    /// irow/irowband, header band -1, parameterless lastrow, explicit zero,
    /// last-value precedence, conditional styles, nested destinations,
    /// independent body rows и строгие numeric/flag parameters.
    /// </summary>
    private static void TestRtfTableStyleRowIdentity(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-identity.rtf");

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
                "{\\*\\ts310\\tsrowd\\irow0\\irowband-1\\lastrow Header last;}" +
                "{\\*\\ts311\\tsrowd\\irow-2147483648" +
                "\\irowband2147483647 Bounds;}" +
                "{\\*\\ts312\\tsrowd Plain;}" +
                "{\\*\\ts313\\tsrowd\\irow1\\irow-4" +
                "\\irowband2\\irowband-3\\lastrow\\lastrow Repeated;}" +
                "{\\*\\ts310\\tsrowd\\tscfirstrow" +
                "\\irow9\\irowband0 Conditional;}" +
                "{\\*\\ts314\\tsrowd" +
                "{\\*\\keycode\\irow90\\irowband91\\lastrow\\ts999}" +
                "\\irow3 Nested;}" +
                "{\\*\\ts315\\tsrowd\\lastrow Flag only;}" +
                "{\\*\\ts316\\tsrowd\\irow0 Index only;}" +
                "{\\*\\ts317\\tsrowd\\irowband0 Band only;}" +
                "{\\*\\ts318\\tsrowd\\trleft100\\irowband5 Mixed;}" +
                "}" +
                "\\trowd\\irow55\\irowband-2\\lastrow" +
                "\\cellx1000\\intbl A\\cell\\row" +
                "\\trowd\\cellx1000\\intbl B\\cell\\row}");

        Assert(
            model.TableStyles.Count == 10,
            "RTF stylesheet должен сохранять десять ordered table-style entries.");

        Assert(
            model.TableStyles[0].RowFormatting is
            {
                RowIndex: 0,
                BandIndex: -1,
                IsLastRow: true,
                Look: null
            },
            "RTF style irow0/irowband-1/lastrow должны сохранять source identity без table look.");

        Assert(
            model.TableStyles[1].RowFormatting is
            {
                RowIndex: int.MinValue,
                BandIndex: int.MaxValue,
                IsLastRow: null
            },
            "RTF style irow/irowband должны сохранять signed Int32 boundaries.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF style без row controls должен сохранять null RowFormatting.");

        Assert(
            model.TableStyles[3].RowFormatting is
            {
                RowIndex: -4,
                BandIndex: -3,
                IsLastRow: true
            },
            "RTF repeated identity controls должны сохранять последнее numeric значение и flag presence.");

        Assert(
            model.TableStyles[4].Handle == 310 &&
            model.TableStyles[4].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[4].RowFormatting is
            {
                RowIndex: 9,
                BandIndex: 0,
                IsLastRow: null
            } &&
            model.TableStyles[0].RowFormatting is
            {
                RowIndex: 0,
                BandIndex: -1,
                IsLastRow: true
            },
            "RTF conditional entry с общим tsN должна иметь независимые row identity values.");

        Assert(
            model.TableStyles[5].RowFormatting is
            {
                RowIndex: 3,
                BandIndex: null,
                IsLastRow: null
            } &&
            model.TableStyles[5].Handle == 314,
            "RTF nested keycode с row controls и tsN не должен менять parent style.");

        Assert(
            model.TableStyles[6].RowFormatting is
            {
                RowIndex: null,
                BandIndex: null,
                IsLastRow: true
            },
            "RTF lastrow-only style должна сохранять explicit true без row index и band.");

        Assert(
            model.TableStyles[7].RowFormatting is
            {
                RowIndex: 0,
                BandIndex: null,
                IsLastRow: null
            },
            "RTF explicit irow0 должна отличаться от отсутствующего row index.");

        Assert(
            model.TableStyles[8].RowFormatting is
            {
                RowIndex: null,
                BandIndex: 0,
                IsLastRow: null
            },
            "RTF explicit irowband0 должна отличаться от отсутствующего band index.");

        Assert(
            model.TableStyles[9].RowFormatting is
            {
                RowIndex: null,
                BandIndex: 5,
                IsLastRow: null,
                LeftOffsetTwips: 100,
                LeftOffsetPoints: 5
            },
            "RTF row identity должна сочетаться с независимой row geometry.");

        var body =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            body.Rows.Count == 2 &&
            body.Rows[0].RowIndex == 55 &&
            body.Rows[0].BandIndex == -2 &&
            body.Rows[0].IsLastRow == true &&
            body.Rows[1].RowIndex == null &&
            body.Rows[1].BandIndex == null &&
            body.Rows[1].IsLastRow == null,
            "RTF body rows должны сохранять собственные row identity и trowd reset.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-index=\"55\"",
            "RTF body row irow HTML metadata");
        AssertContains(
            html,
            "data-row-band-index=\"-2\"",
            "RTF body row irowband HTML metadata");
        AssertContains(
            html,
            "data-row-last=\"true\"",
            "RTF body row lastrow HTML metadata");

        Assert(
            !html.Contains(
                "data-row-index=\"9\"",
                StringComparison.Ordinal) &&
            !html.Contains(
                "data-row-index=\"-4\"",
                StringComparison.Ordinal),
            "RTF style identity не должна становиться HTML metadata body rows.");

        var plainBody =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts319\\tsrowd" +
                "\\irow2147483647\\irowband-2147483648\\lastrow Style;}}" +
                "\\trowd\\cellx1000\\intbl Plain\\cell\\row}");

        Assert(
            plainBody.TableStyles.Single().RowFormatting is
            {
                RowIndex: int.MaxValue,
                BandIndex: int.MinValue,
                IsLastRow: true
            } &&
            plainBody.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single() is
            {
                RowIndex: null,
                BandIndex: null,
                IsLastRow: null
            },
            "RTF stylesheet row identity не должна задавать defaults body table.");

        foreach (var word in new[]
                 {
                     "irow",
                     "irowband"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts320\\tsrowd\\" +
                    word +
                    " Missing;}}" +
                    "Text\\par}"),
                $"RTF table-style {word} без numeric parameter должен завершаться structural error.");
        }

        foreach (var parameter in new[]
                 {
                     "0",
                     "1",
                     "-1",
                     "2147483647",
                     "-2147483648"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts321\\tsrowd\\lastrow" +
                    parameter +
                    " Invalid;}}" +
                    "Text\\par}"),
                $"RTF table-style lastrow{parameter} должен отклонять numeric parameter.");
        }
    }
}
