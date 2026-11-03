using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF \softlheightN как точный nonrequired line-height marker.
    /// Значение сохраняется без трактовки единиц и без CSS line-height,
    /// остаётся в исходной позиции, проходит через table/field content
    /// и экспортируется только как HTML metadata.
    /// </summary>
    private static void TestRtfSoftLineHeight(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-soft-line-height.rtf");

        DocumentModel Read(string source)
        {
            File.WriteAllText(
                path,
                source,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        bool Rejects(string source)
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
                @"{\rtf1\ansi\b A\softlheight240\softline B\softlheight-120 C\b0\par}");

        var paragraph =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Single();
        var heights =
            paragraph.Inlines
                .Where(inline => inline.SoftLineHeight != null)
                .ToArray();

        Assert(
            heights.Length == 2 &&
            heights[0].Text.Length == 0 &&
            heights[0].SoftLineHeight == 240 &&
            heights[0].Bold &&
            heights[1].Text.Length == 0 &&
            heights[1].SoftLineHeight == -120 &&
            heights[1].Bold,
            "RTF softlheightN должен сохранять signed raw values в точных marker inlines.");

        Assert(
            paragraph.Inlines.Any(inline =>
                inline.SoftLineBreak &&
                inline.Text == Environment.NewLine &&
                inline.Bold),
            "RTF softlheight marker не должен поглощать соседний softline.");

        Assert(
            FlattenText(model).Contains(
                "A" + Environment.NewLine + "BC",
                StringComparison.Ordinal),
            "RTF softlheight marker не должен добавлять видимый текст.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "<span class=\"soft-line-height\" data-soft-line-height=\"240\"></span>",
            "RTF positive softlheight HTML metadata");
        AssertContains(
            html,
            "<span class=\"soft-line-height\" data-soft-line-height=\"-120\"></span>",
            "RTF signed softlheight HTML metadata");

        var table =
            Read(
                @"{\rtf1\ansi\trowd\cellx1200\intbl A\softlheight333 B\cell\row}");

        var cell =
            table.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows.Single()
                .Cells.Single();

        Assert(
            cell.Inlines.Any(inline =>
                inline.SoftLineHeight == 333 &&
                inline.Text.Length == 0),
            "RTF softlheight внутри table cell должен сохраняться в ordered cell inlines.");

        var field =
            Read(
                @"{\rtf1\ansi{\field{\*\fldinst PAGE}{\fldrslt 1\softlheight444 2}}\par}");

        var fieldMarker =
            field.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(item => item.Inlines)
                .Single(inline => inline.SoftLineHeight == 444);

        Assert(
            fieldMarker.FieldKind == DocumentFieldKind.Page &&
            fieldMarker.FieldInstruction == "PAGE",
            "RTF softlheight внутри fldrslt должен сохранять field metadata.");

        Assert(
            Rejects(@"{\rtf1\ansi A\softlheight B\par}") &&
            Rejects(@"{\rtf1\ansi A\softlheight2147483648 B\par}"),
            "RTF softlheight без Int32 parameter или с overflow должен завершаться structural error.");
    }
}
