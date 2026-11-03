using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF old-properties destinations: ordered raw control words,
    /// character/paragraph/table/section scope, reset semantics, отсутствие
    /// влияния previous properties на current formatting и HTML metadata.
    /// </summary>
    private static void TestRtfOldProperties(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-old-properties.rtf");

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

        static void AssertControls(
            DocumentPreviousProperties? previousProperties,
            DocumentPreviousPropertiesKind kind,
            params (string Word, int? Parameter)[] expected)
        {
            Assert(
                previousProperties != null &&
                previousProperties.Kind == kind &&
                previousProperties.Controls.Count == expected.Length,
                "RTF old-properties должен сохранять kind и точное количество ordered controls.");

            for (var index = 0;
                 index < expected.Length;
                 index++)
            {
                var actual =
                    previousProperties!.Controls[index];
                var item =
                    expected[index];

                Assert(
                    actual.Word == item.Word &&
                    actual.Parameter == item.Parameter,
                    "RTF old-properties control #" +
                    index +
                    " должен сохранять word и optional signed parameter.");
            }
        }

        var model =
            Read(
                """
                {\rtf1\ansi
                {\b\i{\*\oldcprops\b0\i0\expnd-4\crauth1\crdate123;}Changed} Outside\par
                \pard\li720{\*\oldpprops\li0\fi0\prauth0\prdate456\pararsid-1;}ParagraphOne\par
                \pard ParagraphTwo\par
                \trowd\trleft240{\*\oldtprops\trleft0\trrh-200\trauth1\trdate789\tblrsid17;}\cellx2000\intbl RowOne\cell\row
                \trowd\cellx2000\intbl RowTwo\cell\row
                \pard{\*\oldsprops\sbknone\sectrsid33;}SectionOne\par\sect
                {\*\oldsprops\sbkpage\sectrsid-1;}SectionTwo\par}
                """);

        var bodyParagraph =
            model.Blocks
                .OfType<DocumentParagraph>()
                .First(paragraph =>
                    paragraph.Inlines.Any(inline =>
                        inline.Text.Contains(
                            "Changed",
                            StringComparison.Ordinal)));
        var changed =
            bodyParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "Changed",
                    StringComparison.Ordinal));
        var outside =
            bodyParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "Outside",
                    StringComparison.Ordinal));

        Assert(
            changed.Bold &&
            changed.Italic &&
            changed.Revisions.Count == 0 &&
            outside.PreviousProperties == null,
            "RTF oldcprops не должен применять previous controls к current character formatting и не должен выходить за group scope.");

        AssertControls(
            changed.PreviousProperties,
            DocumentPreviousPropertiesKind.Character,
            ("b", 0),
            ("i", 0),
            ("expnd", -4),
            ("crauth", 1),
            ("crdate", 123));

        var paragraphOne =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline =>
                        inline.Text.Contains(
                            "ParagraphOne",
                            StringComparison.Ordinal)));
        var paragraphTwo =
            model.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline =>
                        inline.Text.Contains(
                            "ParagraphTwo",
                            StringComparison.Ordinal)));

        Assert(
            paragraphOne.LeftIndentPoints == 36 &&
            paragraphTwo.PreviousProperties == null,
            "RTF oldpprops не должен менять current indent, а pard должен сбрасывать previous paragraph snapshot.");

        AssertControls(
            paragraphOne.PreviousProperties,
            DocumentPreviousPropertiesKind.Paragraph,
            ("li", 0),
            ("fi", 0),
            ("prauth", 0),
            ("prdate", 456),
            ("pararsid", -1));

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();
        var rowOne =
            table.Rows[0];
        var rowTwo =
            table.Rows[1];

        Assert(
            rowOne.LeftOffsetPoints == 12 &&
            rowTwo.PreviousProperties == null,
            "RTF oldtprops не должен менять current row offset, а trowd должен сбрасывать previous table snapshot.");

        AssertControls(
            rowOne.PreviousProperties,
            DocumentPreviousPropertiesKind.Table,
            ("trleft", 0),
            ("trrh", -200),
            ("trauth", 1),
            ("trdate", 789),
            ("tblrsid", 17));

        var sectionBreak =
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Single(flowBreak =>
                    flowBreak.Kind ==
                    DocumentFlowBreakKind.Section);

        AssertControls(
            sectionBreak.PreviousSectionProperties,
            DocumentPreviousPropertiesKind.Section,
            ("sbknone", null),
            ("sectrsid", 33));

        AssertControls(
            model.FinalSectionPreviousProperties,
            DocumentPreviousPropertiesKind.Section,
            ("sbkpage", null),
            ("sectrsid", -1));

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-previous-character-properties=\"\\b0 \\i0 \\expnd-4 \\crauth1 \\crdate123\"",
            "RTF oldcprops HTML metadata");
        AssertContains(
            html,
            "data-previous-paragraph-properties=\"\\li0 \\fi0 \\prauth0 \\prdate456 \\pararsid-1\"",
            "RTF oldpprops HTML metadata");
        AssertContains(
            html,
            "data-previous-table-properties=\"\\trleft0 \\trrh-200 \\trauth1 \\trdate789 \\tblrsid17\"",
            "RTF oldtprops HTML metadata");
        AssertContains(
            html,
            "data-previous-section-properties=\"\\sbknone \\sectrsid33\"",
            "RTF oldsprops section-break HTML metadata");
        AssertContains(
            html,
            "data-final-section-previous-properties=\"\\sbkpage \\sectrsid-1\"",
            "RTF final oldsprops HTML metadata");

        var resetModel =
            Read(
                """
                {\rtf1\ansi
                {\*\oldcprops\b0;}\plain Character\par
                \pard{\*\oldpprops\li0;}\pard Paragraph\par
                {\*\oldtprops\trleft0;}\trowd\cellx1000\intbl Row\cell\row
                {\*\oldsprops\sbkpage;}\sectd Section\par}
                """);

        var resetCharacter =
            resetModel.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Single(inline =>
                    inline.Text.Contains(
                        "Character",
                        StringComparison.Ordinal));
        var resetParagraph =
            resetModel.Blocks
                .OfType<DocumentParagraph>()
                .Single(paragraph =>
                    paragraph.Inlines.Any(inline =>
                        inline.Text.Contains(
                            "Paragraph",
                            StringComparison.Ordinal)));
        var resetRow =
            resetModel.Blocks
                .OfType<DocumentTable>()
                .Single()
                .Rows
                .Single();

        Assert(
            resetCharacter.PreviousProperties == null &&
            resetParagraph.PreviousProperties == null &&
            resetRow.PreviousProperties == null &&
            resetModel.FinalSectionPreviousProperties == null,
            "RTF plain/pard/trowd/sectd должны сбрасывать previous-properties metadata соответствующего scope.");

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\oldcprops\b0;}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops\b0}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops;}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops\b0 text;}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops{\b0};}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops\b0;\i0}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops\b0\~;}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\oldcprops\b0;}{\*\oldcprops\i0;}X\par}"),
            "RTF malformed old-properties destination должен завершаться structural error.");
    }
}
