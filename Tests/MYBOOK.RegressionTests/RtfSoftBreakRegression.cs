using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF nonrequired breaks: \softline сохраняется отдельным
    /// inline break с текущим character formatting, \softpage/\softcol
    /// создают soft Page/Column flow breaks, а required break controls
    /// остаются отличимыми. HTML экспортирует soft-break metadata.
    /// </summary>
    private static void TestRtfSoftBreaks(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-soft-breaks.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        bool RejectsTableBreak(string control)
        {
            try
            {
                Read(
                    @"{\rtf1\ansi\trowd\cellx1200\intbl Cell" +
                    control +
                    @"Tail\cell\row}");
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }

        var model =
            Read(
                @"{\rtf1\ansi\b A\softline B\softpage C\softcol D\page E\column F\b0\par}");

        var paragraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .ToArray();
        var breaks =
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .ToArray();

        Assert(
            paragraphs.Length == 5,
            "RTF soft/required page and column breaks должны разделять пять text fragments.");

        var first =
            paragraphs[0];

        Assert(
            first.Inlines.Count == 3 &&
            first.Inlines[0].Text == "A" &&
            !first.Inlines[0].SoftLineBreak &&
            first.Inlines[1].Text == Environment.NewLine &&
            first.Inlines[1].SoftLineBreak &&
            first.Inlines[1].Bold &&
            first.Inlines[2].Text == "B" &&
            !first.Inlines[2].SoftLineBreak &&
            first.Inlines[2].Bold,
            "RTF softline должен сохраняться отдельным formatted inline и не сливаться с текстом.");

        Assert(
            FlattenText(model).Contains(
                "A" + Environment.NewLine + "B",
                StringComparison.Ordinal),
            "RTF softline должен оставаться line break в plain-text projection.");

        Assert(
            breaks.Length == 4 &&
            breaks[0] is
            {
                Kind: DocumentFlowBreakKind.Page,
                IsSoft: true
            } &&
            breaks[1] is
            {
                Kind: DocumentFlowBreakKind.Column,
                IsSoft: true
            } &&
            breaks[2] is
            {
                Kind: DocumentFlowBreakKind.Page,
                IsSoft: false
            } &&
            breaks[3] is
            {
                Kind: DocumentFlowBreakKind.Column,
                IsSoft: false
            },
            "RTF softpage/softcol должны отличаться от required page/column в neutral model.");

        Assert(
            paragraphs[1].Inlines.Single().Text == "C" &&
            paragraphs[1].Inlines.Single().Bold &&
            paragraphs[2].Inlines.Single().Text == "D" &&
            paragraphs[2].Inlines.Single().Bold &&
            paragraphs[3].Inlines.Single().Text == "E" &&
            paragraphs[3].Inlines.Single().Bold &&
            paragraphs[4].Inlines.Single().Text == "F" &&
            paragraphs[4].Inlines.Single().Bold,
            "RTF soft/required flow breaks должны сохранять текущий character formatting.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "<br class=\"soft-line-break\" data-soft-break=\"true\">",
            "RTF softline HTML metadata");
        AssertContains(
            html,
            "flow-break-page flow-break-soft\" data-soft-break=\"true\"",
            "RTF softpage HTML metadata");
        AssertContains(
            html,
            "flow-break-column flow-break-soft\" data-soft-break=\"true\"",
            "RTF softcol HTML metadata");

        Assert(
            html.Contains(
                "flow-break-page\">",
                StringComparison.Ordinal) &&
            html.Contains(
                "flow-break-column\">",
                StringComparison.Ordinal),
            "RTF required page/column HTML не должны получать soft-break marker.");

        var tableLine =
            Read(
                @"{\rtf1\ansi\trowd\cellx1200\intbl A\softline B\cell\row}");

        var cell =
            tableLine.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows.Single()
                .Cells.Single();

        Assert(
            cell.Inlines.Any(inline =>
                inline.SoftLineBreak &&
                inline.Text == Environment.NewLine),
            "RTF softline внутри table cell должен оставаться inline break.");

        Assert(
            RejectsTableBreak(@"\softpage ") &&
            RejectsTableBreak(@"\softcol "),
            "RTF softpage/softcol внутри table должны давать explicit unsupported до table pagination layout.");
    }
}
