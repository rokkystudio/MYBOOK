using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет независимые RTF table-direction и row-cell-direction controls
    /// внутри table-style catalog: source presence, last-value precedence,
    /// parameterless grammar, nested groups и отсутствие влияния на body tables.
    /// </summary>
    private static void TestRtfTableStyleDirection(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-direction.rtf");

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
                "{\\*\\ts100\\tsrowd\\taprtl\\rtlrow\\ltrrow Table RTL Row LTR;}" +
                "{\\*\\ts101\\tsrowd\\ltrrow\\rtlrow Row RTL;}" +
                "{\\*\\ts102\\tsrowd\\rtlrow\\ltrrow Row LTR;}" +
                "{\\*\\ts103\\tsrowd\\taprtl Table RTL only;}" +
                "{\\*\\ts104\\tsrowd No direction;}" +
                "{\\*\\ts105\\tsrowd\\taprtl\\taprtl\\rtlrow\\rtlrow Repeated;}" +
                "{\\*\\ts106\\tsrowd{\\*\\keycode\\shift\\ctrl t}" +
                "\\ltrrow Nested keycode;}" +
                "{\\*\\ts100\\tsrowd\\tscfirstrow\\rtlrow Conditional RTL;}" +
                "}" +
                "\\trowd\\rtlrow\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 8,
            "RTF stylesheet должен сохранять восемь ordered table-style entries.");

        Assert(
            model.TableStyles[0].RowFormatting is
            {
                TableRightToLeft: true,
                RightToLeft: false
            },
            "RTF taprtl должен сохранять направление таблицы отдельно от итогового ltrrow.");

        Assert(
            model.TableStyles[1].RowFormatting is
            {
                TableRightToLeft: null,
                RightToLeft: true
            },
            "RTF ltrrow/rtlrow должен сохранять последний row-cell direction.");

        Assert(
            model.TableStyles[2].RowFormatting is
            {
                TableRightToLeft: null,
                RightToLeft: false
            },
            "RTF explicit ltrrow должен отличаться от отсутствующего row-cell direction.");

        Assert(
            model.TableStyles[3].RowFormatting is
            {
                TableRightToLeft: true,
                RightToLeft: null
            },
            "RTF taprtl без rtlrow/ltrrow не должен задавать row-cell direction.");

        Assert(
            model.TableStyles[4].RowFormatting == null,
            "RTF table style без direction и других row properties должен сохранять null RowFormatting.");

        Assert(
            model.TableStyles[5].RowFormatting is
            {
                TableRightToLeft: true,
                RightToLeft: true
            },
            "RTF повторные parameterless direction flags должны сохранять одно source state.");

        Assert(
            model.TableStyles[6].RowFormatting is
            {
                TableRightToLeft: null,
                RightToLeft: false
            },
            "RTF nested keycode group не должен менять table-style direction.");

        Assert(
            model.TableStyles[7].Handle == 100 &&
            model.TableStyles[7].Conditions ==
                DocumentTableStyleCondition.FirstRow &&
            model.TableStyles[7].RowFormatting is
            {
                TableRightToLeft: null,
                RightToLeft: true
            } &&
            model.TableStyles[0].RowFormatting is
            {
                TableRightToLeft: true,
                RightToLeft: false
            },
            "RTF conditional entry с тем же tsN должна иметь независимые direction values.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            !bodyTable.RightToLeft &&
            bodyTable.Rows.Single().RightToLeft == true,
            "RTF stylesheet table direction не должен менять LTR body table с explicit rtlrow.");

        var bodyRtlModel =
            Read(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\*\\ts110\\tsrowd\\ltrrow Style LTR;}}" +
                "\\trowd\\taprtl\\ltrrow\\cellx1000\\intbl Body\\cell\\row}");

        var bodyRtlTable =
            bodyRtlModel.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyRtlTable.RightToLeft &&
            bodyRtlTable.Rows.Single().RightToLeft == false &&
            bodyRtlModel.TableStyles.Single().RowFormatting is
            {
                TableRightToLeft: null,
                RightToLeft: false
            },
            "RTF body taprtl/ltrrow должен сохраняться независимо от style-row ltrrow.");

        foreach (var control in new[]
                 {
                     "taprtl0",
                     "taprtl1",
                     "rtlrow0",
                     "rtlrow1",
                     "ltrrow0",
                     "ltrrow1"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi" +
                    "{\\stylesheet{\\*\\ts111\\tsrowd\\" +
                    control +
                    " Invalid;}}" +
                    "Body\\par}"),
                $"RTF table-style {control} должен отклоняться как parameterized flag.");
        }

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi" +
                "{\\stylesheet\\taprtl{\\*\\ts112\\tsrowd Valid;}}" +
                "Body\\par}"),
            "RTF table-style taprtl вне определения tsN должен завершаться structural error.");
    }
}
