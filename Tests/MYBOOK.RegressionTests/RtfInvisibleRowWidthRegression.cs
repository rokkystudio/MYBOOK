using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF invisible cells перед/после строки: trftsWidthB/
    /// trwWidthB и trftsWidthA/trwWidthA с null/auto/percent/twips units,
    /// trowd reset, HTML metadata и malformed width pairs.
    /// </summary>
    private static void TestRtfInvisibleRowWidths(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-invisible-row-widths.rtf");

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
                """
                {\rtf1\ansi
                \trowd\trftsWidthB3\trwWidthB720\trftsWidthA2\trwWidthA2500\cellx1000\intbl A\cell\row
                \trowd\trftsWidthB1\trwWidthB999\trftsWidthA0\trwWidthA999\cellx1000\intbl B\cell\row
                \trowd\cellx1000\intbl C\cell\row}
                """);

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();

        Assert(
            table.Rows.Count == 3,
            "RTF invisible-width fixture должен содержать три строки.");

        var first =
            table.Rows[0];
        var second =
            table.Rows[1];
        var third =
            table.Rows[2];

        Assert(
            first.InvisibleCellBeforeWidth is
            {
                Kind: DocumentPreferredWidthKind.Points,
                Value: 36
            } &&
            first.InvisibleCellAfterWidth is
            {
                Kind: DocumentPreferredWidthKind.Percent,
                Value: 50
            },
            "RTF trwWidthB/A должен сохранять twips и 1/50-percent units.");

        Assert(
            second.InvisibleCellBeforeWidth is
            {
                Kind: DocumentPreferredWidthKind.Auto,
                Value: null
            } &&
            second.InvisibleCellAfterWidth == null,
            "RTF invisible row width unit 1 должен сохранять Auto, unit 0 — отсутствие invisible cell.");

        Assert(
            third.InvisibleCellBeforeWidth == null &&
            third.InvisibleCellAfterWidth == null,
            "RTF trowd должен сбрасывать invisible before/after row widths.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-row-invisible-before-width-kind=\"points\"",
            "RTF invisible-before width kind HTML metadata");
        AssertContains(
            html,
            "data-row-invisible-before-width-value=\"36\"",
            "RTF invisible-before width value HTML metadata");
        AssertContains(
            html,
            "data-row-invisible-after-width-kind=\"percent\"",
            "RTF invisible-after width kind HTML metadata");
        AssertContains(
            html,
            "data-row-invisible-after-width-value=\"50\"",
            "RTF invisible-after width value HTML metadata");
        AssertContains(
            html,
            "data-row-invisible-before-width-kind=\"auto\"",
            "RTF invisible-before Auto HTML metadata");

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trwWidthB720\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthB3\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthB3\trwWidthB-1\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthB4\trwWidthB720\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthA\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trwWidthA\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthA2\trwWidthA-50\cellx1000\intbl X\cell\row}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\trowd\trftsWidthA9\trwWidthA50\cellx1000\intbl X\cell\row}"),
            "RTF malformed invisible row width pair должен завершаться structural error.");
    }
}
