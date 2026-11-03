using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет однозначные Unicode special-character controls RTF:
    /// em/en spaces, directional marks и zero-width joiner/non-joiner.
    /// Символы сохраняют character formatting, hyperlink semantics,
    /// table-cell content и проходят в HTML без замены эвристическими
    /// пробелами или удалением format-control code points.
    /// </summary>
    private static void TestRtfUnicodeSpecialCharacters(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-unicode-special-characters.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        var model =
            Read(
                @"{\rtf1\ansi\b A\emspace B\enspace C\ltrmark D\rtlmark E\zwj F\zwnj G\b0\par}");

        var paragraph =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Single();

        Assert(
            paragraph.Inlines.Count == 1 &&
            paragraph.Inlines[0].Bold &&
            paragraph.Inlines[0].Text ==
                "A\u2003B\u2002C\u200ED\u200FE\u200DF\u200CG",
            "RTF Unicode special-character controls должны давать точные code points и сохранять style.");

        var text =
            FlattenText(model);

        Assert(
            text.Contains("\u2003", StringComparison.Ordinal) &&
            text.Contains("\u2002", StringComparison.Ordinal) &&
            text.Contains("\u200E", StringComparison.Ordinal) &&
            text.Contains("\u200F", StringComparison.Ordinal) &&
            text.Contains("\u200D", StringComparison.Ordinal) &&
            text.Contains("\u200C", StringComparison.Ordinal),
            "RTF special Unicode format controls не должны удаляться из text projection.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        Assert(
            html.Contains("\u2003", StringComparison.Ordinal) &&
            html.Contains("\u2002", StringComparison.Ordinal) &&
            html.Contains("\u200E", StringComparison.Ordinal) &&
            html.Contains("\u200F", StringComparison.Ordinal) &&
            html.Contains("\u200D", StringComparison.Ordinal) &&
            html.Contains("\u200C", StringComparison.Ordinal),
            "HTML renderer должен сохранять Unicode special characters без ASCII fallback.");

        var link =
            Read(
                @"{\rtf1\ansi{\field{\*\fldinst HYPERLINK ""https://example.com/rtf-special""}{\fldrslt A\emspace B\zwj C\zwnj D}}}");

        var linkedInline =
            link.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(item => item.Inlines)
                .Single(inline =>
                    inline.LinkHref == "https://example.com/rtf-special");

        Assert(
            linkedInline.Text == "A\u2003B\u200DC\u200CD",
            "RTF special characters внутри HYPERLINK result должны сохранять LinkHref и точный текст.");

        var table =
            Read(
                @"{\rtf1\ansi\trowd\cellx1200\intbl A\rtlmark B\ltrmark C\cell\row}");

        var cell =
            table.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows.Single()
                .Cells.Single();

        Assert(
            string.Concat(cell.Inlines.Select(inline => inline.Text)) ==
                "A\u200FB\u200EC",
            "RTF directional marks внутри table cell должны сохраняться в cell inlines.");

        var paired =
            Read(
                @"{\rtf1\ansi \emdash\endash\lquote X\rquote\ldblquote Y\rdblquote\bullet\emspace\enspace\par}");

        Assert(
            FlattenText(paired).Contains(
                "\u2014\u2013\u2018X\u2019\u201CY\u201D\u2022\u2003\u2002",
                StringComparison.Ordinal),
            "RTF типографские special controls должны иметь стабильное Unicode отображение.");
    }
}
