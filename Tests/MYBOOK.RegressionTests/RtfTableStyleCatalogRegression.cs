using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет neutral RTF table-style catalog из stylesheet: ordered entries,
    /// signed Int16 handles, names, based-on/next references, tsrowd,
    /// conditional entries с одинаковым handle, nested keycode isolation,
    /// Unicode style names и structural validation без применения formatting.
    /// </summary>
    private static void TestRtfTableStyleCatalog(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-table-style-catalog.rtf");

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
                "{\\rtf1\\ansi\\uc1" +
                "{\\stylesheet" +
                "{\\s0 Normal;}" +
                "{\\*\\cs10 Default Paragraph Font;}" +
                "{\\*\\ts11\\tsrowd\\sbasedon222\\snext11 Normal Table;}" +
                "{\\*\\ts16\\tsrowd\\sbasedon11\\snext16 Table List 8;}" +
                "{\\*\\ts16\\tsrowd\\tscfirstrow Table List 8;}" +
                "{\\*\\ts16\\tsrowd\\tscbandhorzodd Table List 8;}" +
                "{\\*\\ts21\\tsrowd{\\*\\keycode\\shift\\ctrl t}" +
                "\\sbasedon16\\snext21 Caf\\u233? Table;}" +
                "}" +
                "\\trowd\\ts16\\cellx1000\\intbl Body\\cell\\row}");

        Assert(
            model.TableStyles.Count == 5,
            "RTF stylesheet должен сохранять только пять table-style entries.");

        var normalTable =
            model.TableStyles[0];

        Assert(
            normalTable.Handle == 11 &&
            normalTable.Name == "Normal Table" &&
            normalTable.BasedOnHandle == 222 &&
            normalTable.NextHandle == 11 &&
            normalTable.RowDefinition &&
            normalTable.Conditions == DocumentTableStyleCondition.None,
            "RTF base table style должен сохранять handle/name/based-on/next/tsrowd.");

        var baseEntry =
            model.TableStyles[1];

        Assert(
            baseEntry.Handle == 16 &&
            baseEntry.Name == "Table List 8" &&
            baseEntry.BasedOnHandle == 11 &&
            baseEntry.NextHandle == 16 &&
            baseEntry.RowDefinition &&
            baseEntry.Conditions == DocumentTableStyleCondition.None,
            "RTF table style base entry должен сохраняться отдельно от conditional entries.");

        var firstRowEntry =
            model.TableStyles[2];

        Assert(
            firstRowEntry.Handle == 16 &&
            firstRowEntry.Name == "Table List 8" &&
            firstRowEntry.BasedOnHandle == null &&
            firstRowEntry.NextHandle == null &&
            firstRowEntry.RowDefinition &&
            firstRowEntry.Conditions == DocumentTableStyleCondition.FirstRow,
            "RTF tscfirstrow entry должна сохранять source-only references как null.");

        var oddBandEntry =
            model.TableStyles[3];

        Assert(
            oddBandEntry.Handle == 16 &&
            oddBandEntry.Conditions == DocumentTableStyleCondition.OddRowBand,
            "RTF repeated table-style handle должен сохранять отдельную odd-row-band entry.");

        var unicodeEntry =
            model.TableStyles[4];

        Assert(
            unicodeEntry.Handle == 21 &&
            unicodeEntry.Name == "Café Table" &&
            unicodeEntry.BasedOnHandle == 16 &&
            unicodeEntry.NextHandle == 21 &&
            unicodeEntry.RowDefinition &&
            unicodeEntry.Conditions == DocumentTableStyleCondition.None,
            "RTF table-style catalog должен декодировать Unicode name и игнорировать nested keycode text.");

        var bodyTable =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            bodyTable.StyleHandle == 16 &&
            bodyTable.Rows.Single().TableStyleHandle == 16,
            "RTF stylesheet catalog не должен изменять body tsN projection/source state.");

        Assert(
            model.Blocks.Count == 1,
            "RTF stylesheet definitions не должны создавать body paragraphs или tables.");

        Assert(
            RejectsInvalid(
                "{\\rtf1\\ansi" +
                "{\\stylesheet{\\ts11\\tsrowd Missing star;}}" +
                "Body\\par}"),
            "RTF table style без обязательного \\* prefix должен завершаться structural error.");

        foreach (var source in new[]
                 {
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts\\tsrowd Missing;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts32768\\tsrowd High;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts-32769\\tsrowd Low;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd\\sbasedon Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd\\snext32768 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd\\tscfirstrow1 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd\\ts12 Bad;}}Body\\par}",
                     "{\\rtf1\\ansi{\\stylesheet{\\*\\ts11\\tsrowd Missing semicolon}}Body\\par}"
                 })
        {
            Assert(
                RejectsInvalid(source),
                "RTF malformed table-style catalog entry должна завершаться structural error.");
        }
    }
}
