using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет сохранение row alignment, AutoFit и pagination flags
    /// в RTF table-style catalog без применения свойств к body tables.
    /// </summary>
    private static void TestRtfTableStyleRowLayout(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-row-layout.rtf");

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
                "{\\*\\ts70\\tsrowd\\trql\\trqc\\trautofit0" +
                "\\trhdr\\trkeep\\trkeepfollow Centered;}" +
                "{\\*\\ts71\\tsrowd\\trqr\\trautofit1 Right;}" +
                "{\\*\\ts72\\tsrowd Plain;}" +
                "}" +
                "\\trowd\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 3,
            "RTF stylesheet должен сохранять три table-style entries.");

        var centered =
            model.TableStyles[0].RowFormatting;

        Assert(
            centered is
            {
                Alignment: DocumentTableAlignment.Center,
                AutoFit: false,
                RepeatAsHeader: true,
                PreventPageSplit: true,
                KeepWithNext: true
            },
            "RTF table style должен сохранять last alignment, explicit AutoFit0 и pagination flags.");

        var right =
            model.TableStyles[1].RowFormatting;

        Assert(
            right is
            {
                Alignment: DocumentTableAlignment.Right,
                AutoFit: true,
                RepeatAsHeader: false,
                PreventPageSplit: false,
                KeepWithNext: false
            },
            "RTF table style должен сохранять explicit AutoFit1 и независимые row flags.");

        Assert(
            model.TableStyles[2].RowFormatting == null,
            "RTF table style без row properties должен сохранять null RowFormatting.");

        var body =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            body.Rows.Single().Alignment == DocumentTableAlignment.Left &&
            body.Rows.Single().AutoFit == null &&
            !body.Rows.Single().RepeatAsHeader &&
            !body.Rows.Single().PreventPageSplit &&
            !body.Rows.Single().KeepWithNext,
            "RTF table-style row layout не должен применяться к body table.");

        foreach (var control in new[]
                 {
                     "trql1",
                     "trqc0",
                     "trqr1",
                     "trhdr0",
                     "trkeep1",
                     "trkeepfollow0",
                     "trautofit",
                     "trautofit-1",
                     "trautofit2"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi{\\stylesheet{\\*\\ts80\\tsrowd\\" +
                    control +
                    " Invalid;}}Body\\par}"),
                $"RTF table-style row control {control} должен отклоняться при invalid grammar.");
        }
    }
}
