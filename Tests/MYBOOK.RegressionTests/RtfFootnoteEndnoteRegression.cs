using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет automatic RTF footnotes/endnotes: \chftn anchors,
    /// \footnote/\ftnalt destinations, независимые start counters,
    /// note-tail blocks, cached formatting и строгий отказ от профилей,
    /// которые требуют custom marks, restart или non-Arabic numbering.
    /// </summary>
    private static void TestRtfFootnotesAndEndnotes(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-footnotes-endnotes.rtf");

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

        bool RejectsUnsupported(string source)
        {
            try
            {
                Read(source);
                return false;
            }
            catch (NotSupportedException)
            {
                return true;
            }
        }

        var model =
            Read(
                """
                {\rtf1\ansi\ftnstart3\aftnstart5
                Main \b\chftn{\footnote \pard\plain \chftn Foot note text\par}\b0
                 End \chftn{\footnote\ftnalt \pard\plain \chftn End note text\par}
                 Again \chftn{\footnote \pard\plain \chftn Second footnote\par}\par}
                """);

        var main =
            model.Blocks
                .OfType<DocumentParagraph>()
                .First(paragraph => !paragraph.IsNote);

        var references =
            main.Inlines
                .Where(inline =>
                    inline.LinkHref?.StartsWith(
                        "#rtf-",
                        StringComparison.Ordinal) == true)
                .ToArray();

        Assert(
            references.Length == 3 &&
            references[0].Text == "3" &&
            references[0].LinkHref == "#rtf-footnote-1" &&
            references[0].Superscript &&
            references[0].Bold &&
            references[1].Text == "5" &&
            references[1].LinkHref == "#rtf-endnote-2" &&
            references[1].Superscript &&
            references[2].Text == "4" &&
            references[2].LinkHref == "#rtf-footnote-3" &&
            references[2].Superscript,
            "RTF automatic note references должны сохранять отдельные footnote/endnote counters, anchors и captured formatting.");

        var noteAnchors =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Where(paragraph =>
                    paragraph.IsNote &&
                    paragraph.AnchorId != null)
                .ToArray();

        Assert(
            noteAnchors.Length == 3 &&
            noteAnchors[0].AnchorId == "rtf-footnote-1" &&
            noteAnchors[1].AnchorId == "rtf-endnote-2" &&
            noteAnchors[2].AnchorId == "rtf-footnote-3",
            "RTF note destinations должны добавляться в хвост модели с уникальными anchors.");

        var noteParagraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Where(paragraph =>
                    paragraph.IsNote &&
                    paragraph.AnchorId == null &&
                    paragraph.Inlines.Count > 0)
                .ToArray();

        Assert(
            noteParagraphs.Length == 3 &&
            noteParagraphs[0].Inlines[0].Text == "3" &&
            noteParagraphs[0].Inlines[0].Superscript &&
            string.Concat(noteParagraphs[0].Inlines.Select(inline => inline.Text))
                .Contains(
                    "Foot note text",
                    StringComparison.Ordinal) &&
            noteParagraphs[1].Inlines[0].Text == "5" &&
            noteParagraphs[1].Inlines[0].Superscript &&
            string.Concat(noteParagraphs[1].Inlines.Select(inline => inline.Text))
                .Contains(
                    "End note text",
                    StringComparison.Ordinal) &&
            noteParagraphs[2].Inlines[0].Text == "4" &&
            string.Concat(noteParagraphs[2].Inlines.Select(inline => inline.Text))
                .Contains(
                    "Second footnote",
                    StringComparison.Ordinal),
            "RTF note bodies должны использовать тот же automatic marker и сохранять note content.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "href=\"#anchor-rtf-footnote-1\"",
            "RTF footnote reference HTML");
        AssertContains(
            html,
            "href=\"#anchor-rtf-endnote-2\"",
            "RTF endnote reference HTML");
        AssertContains(
            html,
            "id=\"anchor-rtf-footnote-1\"",
            "RTF footnote anchor HTML");
        AssertContains(
            html,
            "id=\"anchor-rtf-endnote-2\"",
            "RTF endnote anchor HTML");

        var fieldNote =
            Read(
                """
                {\rtf1\ansi Ref\chftn{\footnote
                \pard\plain\chftn
                {\field{\*\fldinst PAGE}{\fldrslt 7}} body\par}
                tail\par}
                """);

        var fieldInline =
            fieldNote.Blocks
                .OfType<DocumentParagraph>()
                .Where(paragraph => paragraph.IsNote)
                .SelectMany(paragraph => paragraph.Inlines)
                .Single(inline =>
                    inline.FieldKind == DocumentFieldKind.Page);

        Assert(
            fieldInline.Text == "7" &&
            fieldInline.FieldInstruction == "PAGE",
            "RTF field semantics внутри footnote destination должны сохраняться.");

        Assert(
            RejectsInvalid(@"{\rtf1\ansi A\chftn B\par}") &&
            RejectsInvalid(@"{\rtf1\ansi A\chftn\chftn{\footnote X\par}}") &&
            RejectsInvalid(@"{\rtf1\ansi A\ftnalt\par}") &&
            RejectsInvalid(@"{\rtf1\ansi A\chftn{\footnote X\ftnalt Y\par}}"),
            "RTF malformed automatic note structure должен завершаться structural error.");

        Assert(
            RejectsUnsupported(@"{\rtf1\ansi A{\footnote Custom\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi\ftnrestart A\chftn{\footnote \chftn X\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi\ftnrstpg A\chftn{\footnote \chftn X\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi\aftnrestart A\chftn{\footnote\ftnalt \chftn X\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi\ftnnalc A\chftn{\footnote \chftn X\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi\aftnnruc A\chftn{\footnote\ftnalt \chftn X\par}}") &&
            RejectsUnsupported(
                @"{\rtf1\ansi A\chftn{\footnote\trowd\cellx1000\intbl Cell\cell\row}}"),
            "RTF custom/restart/non-Arabic/table note profiles должны оставаться explicit unsupported.");
    }
}
