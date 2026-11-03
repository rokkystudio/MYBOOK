using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет required RTF line/page/column/section breaks:
    /// line остаётся внутри абзаца, page/column/section сохраняются
    /// отдельными flow-break блоками, formatting продолжается после break,
    /// HTML различает три block break kinds, а breaks внутри таблиц
    /// отклоняются до появления table-pagination layout.
    /// </summary>
    private static void TestRtfFlowBreaks(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-flow-breaks.rtf");

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
                    @"{\rtf1\ansi\trowd\cellx1000\intbl Cell" +
                    control +
                    @"\cell\row}");
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }

        var model =
            Read(
                @"{\rtf1\ansi\b First\line Second\page Third\column Fourth\sect Fifth\b0\par}");

        var paragraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .ToArray();
        var breaks =
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .ToArray();

        Assert(
            paragraphs.Length == 4 &&
            paragraphs[0].Inlines.Count == 1 &&
            paragraphs[0].Inlines[0].Text == "First" + Environment.NewLine + "Second" &&
            paragraphs[0].Inlines[0].Bold &&
            paragraphs[1].Inlines.Single().Text == "Third" &&
            paragraphs[1].Inlines.Single().Bold &&
            paragraphs[2].Inlines.Single().Text == "Fourth" &&
            paragraphs[2].Inlines.Single().Bold &&
            paragraphs[3].Inlines.Single().Text == "Fifth" &&
            paragraphs[3].Inlines.Single().Bold,
            "RTF line/page/column/section breaks должны сохранять текст и scoped formatting.");

        Assert(
            breaks.Select(item => item.Kind)
                .SequenceEqual(
                [
                    DocumentFlowBreakKind.Page,
                    DocumentFlowBreakKind.Column,
                    DocumentFlowBreakKind.Section
                ]),
            "RTF page/column/sect должны сохранять точные flow-break kinds и порядок.");

        var blockKinds =
            model.Blocks.Select(block => block switch
            {
                DocumentParagraph => "paragraph",
                DocumentFlowBreak flowBreak => flowBreak.Kind.ToString(),
                _ => block.GetType().Name
            }).ToArray();

        Assert(
            blockKinds.SequenceEqual(
            [
                "paragraph",
                "Page",
                "paragraph",
                "Column",
                "paragraph",
                "Section",
                "paragraph"
            ]),
            "RTF required breaks должны разделять flow в точных позициях.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "First<br>Second",
            "RTF line break HTML");
        AssertContains(
            html,
            "flow-break-page",
            "RTF page break HTML");
        AssertContains(
            html,
            "flow-break-column",
            "RTF column break HTML");
        AssertContains(
            html,
            "flow-break-section",
            "RTF section break HTML");

        var leading =
            Read(
                @"{\rtf1\ansi\page PageStart\column ColumnStart\sect SectionStart\par}");

        Assert(
            leading.Blocks.First() is DocumentFlowBreak
            {
                Kind: DocumentFlowBreakKind.Page
            } &&
            leading.Blocks.Count(block => block is DocumentFlowBreak
            {
                Kind: DocumentFlowBreakKind.Column
            }) == 1 &&
            leading.Blocks.Count(block => block is DocumentFlowBreak
            {
                Kind: DocumentFlowBreakKind.Section
            }) == 1 &&
            FlattenText(leading).Contains(
                "PageStart",
                StringComparison.Ordinal) &&
            FlattenText(leading).Contains(
                "ColumnStart",
                StringComparison.Ordinal) &&
            FlattenText(leading).Contains(
                "SectionStart",
                StringComparison.Ordinal),
            "RTF leading required breaks не должны создавать фиктивный текст.");

        Assert(
            RejectsTableBreak(@"\page ") &&
            RejectsTableBreak(@"\column ") &&
            RejectsTableBreak(@"\sect "),
            "RTF page/column/sect внутри table должны давать explicit unsupported.");
    }
}
