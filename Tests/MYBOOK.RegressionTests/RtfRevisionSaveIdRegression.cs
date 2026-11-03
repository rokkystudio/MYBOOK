using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF Revision Save IDs: ordered rsidtbl, rsidroot,
    /// character/paragraph/table/section save-session IDs, signed 32-bit
    /// bit patterns, reset/group semantics и HTML data-* export.
    /// </summary>
    private static void TestRtfRevisionSaveIds(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-rsid.rtf");

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
                {\*\rsidtbl\rsid1\rsid2\rsid3\rsid4\rsid5\rsid6\rsid-1;}
                \rsidroot1
                {\insrsid2 Inserted}
                {\delrsid3 Deleted}
                {\charrsid4\b Formatted\b0}
                \insrsid2 MergeA\insrsid3 MergeB\plain Plain
                {\insrsid2 Grouped}AfterGroup
                \pard\pararsid4 ParagraphOne\par
                \pard ParagraphTwo\par
                \trowd\tblrsid5\cellx1000\intbl RowOne\cell\row
                \trowd\cellx1000\intbl RowTwo\cell\row
                \pard\sectrsid6 SectionOne\par\sect
                \sectrsid-1 SectionTwo\par}
                """);

        var info =
            model.RevisionSaveInfo ??
            throw new InvalidOperationException(
                "RTF rsid metadata должна создавать DocumentRevisionSaveInfo.");

        Assert(
            info.SaveIds.SequenceEqual(
                new uint[]
                {
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    0xFFFFFFFF
                }) &&
            info.RootSaveId == 1 &&
            info.SectionSaveIds.SequenceEqual(
                new uint?[]
                {
                    6,
                    0xFFFFFFFF
                }),
            "RTF rsidtbl/rsidroot/sectrsid должны сохранять ordered opaque 32-bit значения.");

        var paragraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .ToArray();

        DocumentInline FindInline(string text)
        {
            return model.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Concat(
                    model.Blocks
                        .OfType<DocumentTable>()
                        .SelectMany(table => table.Rows)
                        .SelectMany(row => row.Cells)
                        .SelectMany(cell => cell.Inlines))
                .Single(inline =>
                    inline.Text.Contains(
                        text,
                        StringComparison.Ordinal));
        }

        Assert(
            FindInline("Inserted").InsertionRevisionSaveId == 2 &&
            FindInline("Inserted").DeletionRevisionSaveId == null &&
            FindInline("Inserted").CharacterRevisionSaveId == null &&
            FindInline("Deleted").DeletionRevisionSaveId == 3 &&
            FindInline("Formatted").CharacterRevisionSaveId == 4,
            "RTF insrsid/delrsid/charrsid должны сохраняться независимо на inline.");

        var mergeParagraph =
            paragraphs.Single(paragraph =>
                string.Concat(
                        paragraph.Inlines.Select(inline => inline.Text))
                    .Contains(
                        "MergeA",
                        StringComparison.Ordinal));
        var mergeA =
            mergeParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "MergeA",
                    StringComparison.Ordinal));
        var mergeB =
            mergeParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "MergeB",
                    StringComparison.Ordinal));
        var plain =
            mergeParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "Plain",
                    StringComparison.Ordinal));
        var grouped =
            mergeParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "Grouped",
                    StringComparison.Ordinal));
        var afterGroup =
            mergeParagraph.Inlines.Single(inline =>
                inline.Text.Contains(
                    "AfterGroup",
                    StringComparison.Ordinal));

        Assert(
            mergeA.InsertionRevisionSaveId == 2 &&
            mergeB.InsertionRevisionSaveId == 3 &&
            !ReferenceEquals(
                mergeA,
                mergeB) &&
            plain.InsertionRevisionSaveId == null &&
            grouped.InsertionRevisionSaveId == 2 &&
            afterGroup.InsertionRevisionSaveId == null,
            "RTF RSID должен участвовать в run merge, group scope и plain reset.");

        var paragraphOne =
            paragraphs.Single(paragraph =>
                string.Concat(
                        paragraph.Inlines.Select(inline => inline.Text))
                    .Contains(
                        "ParagraphOne",
                        StringComparison.Ordinal));
        var paragraphTwo =
            paragraphs.Single(paragraph =>
                string.Concat(
                        paragraph.Inlines.Select(inline => inline.Text))
                    .Contains(
                        "ParagraphTwo",
                        StringComparison.Ordinal));

        Assert(
            paragraphOne.ParagraphRevisionSaveId == 4 &&
            paragraphTwo.ParagraphRevisionSaveId == null,
            "RTF pararsid должен сохраняться на paragraph и сбрасываться pard.");

        var table =
            model.Blocks
                .OfType<DocumentTable>()
                .Single();
        Assert(
            table.Rows.Count == 2 &&
            table.Rows[0].TableRevisionSaveId == 5 &&
            table.Rows[1].TableRevisionSaveId == null,
            "RTF tblrsid должен сохраняться на row-backed table metadata и сбрасываться trowd.");

        var sectionBreak =
            model.Blocks
                .OfType<DocumentFlowBreak>()
                .Single(flowBreak =>
                    flowBreak.Kind ==
                    DocumentFlowBreakKind.Section);

        Assert(
            sectionBreak.SectionRevisionSaveId == 6,
            "RTF sectrsid должен сохраняться на terminating section break.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-rsid-root=\"00000001\"",
            "RTF rsidroot HTML metadata");
        AssertContains(
            html,
            "data-rsid-table=\"00000001,00000002,00000003,00000004,00000005,00000006,FFFFFFFF\"",
            "RTF rsid table HTML metadata");
        AssertContains(
            html,
            "data-section-rsids=\"00000006,FFFFFFFF\"",
            "RTF per-section rsid HTML metadata");
        AssertContains(
            html,
            "data-insertion-rsid=\"00000002\"",
            "RTF insrsid HTML metadata");
        AssertContains(
            html,
            "data-deletion-rsid=\"00000003\"",
            "RTF delrsid HTML metadata");
        AssertContains(
            html,
            "data-character-rsid=\"00000004\"",
            "RTF charrsid HTML metadata");
        AssertContains(
            html,
            "data-paragraph-rsid=\"00000004\"",
            "RTF pararsid HTML metadata");
        AssertContains(
            html,
            "data-table-rsid=\"00000005\"",
            "RTF tblrsid HTML metadata");
        AssertContains(
            html,
            "data-section-rsid=\"00000006\"",
            "RTF sectrsid HTML metadata");

        var modernTable =
            Read(
                @"{\rtf1\ansi{\*\rsidtbl\rsid7\rsid8}\rsidroot7 Modern\par}");

        Assert(
            modernTable.RevisionSaveInfo is
            {
                RootSaveId: 7
            } &&
            modernTable.RevisionSaveInfo.SaveIds.SequenceEqual(
                new uint[]
                {
                    7,
                    8
                }),
            "RTF rsidtbl должен принимать современную форму без terminal semicolon.");

        foreach (var control in new[]
                 {
                     "rsidroot",
                     "insrsid",
                     "delrsid",
                     "charrsid",
                     "pararsid",
                     "tblrsid",
                     "sectrsid"
                 })
        {
            Assert(
                RejectsInvalid(
                    "{\\rtf1\\ansi\\" +
                    control +
                    " X\\par}"),
                "RTF " +
                control +
                " без numeric parameter должен завершаться structural error.");
        }

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl\rsid}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl Text\rsid1}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl\rsid1;\rsid2}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl\rsid1}{\*\rsidtbl\rsid2}\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi\rsid1 X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\rsidtbl\rsid1\rsid2}\rsidroot1\rsidroot2 X\par}"),
            "RTF malformed/duplicate RSID metadata должен завершаться structural error.");
    }
}
