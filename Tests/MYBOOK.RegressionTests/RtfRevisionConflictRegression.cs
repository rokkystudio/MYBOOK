using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет binary conflict entries в RTF revtbl: current author,
    /// byte-length previous author, byte-exact PreviousRevisionTime,
    /// mixed regular/conflict entries, Unicode/codepage decoding, author
    /// index resolution и inert HTML metadata.
    /// </summary>
    private static void TestRtfRevisionConflicts(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-revision-conflicts.rtf");

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
                {\rtf1\ansi\ansicpg1251\uc1
                {\*\revtbl
                {Alice;}
                {\u1041?\u1086?\u1073?\'00\'04\'c0\'ed\'ed\'e0\'00\'0a\'0d\'3b\'ff}
                {Carol;}}
                {\revised\revauth1 Conflict text}\par}
                """);

        Assert(
            model.RevisionAuthors.SequenceEqual(
                new[]
                {
                    "Alice",
                    "Боб",
                    "Carol"
                }),
            "RTF revtbl compatibility author list должен сохранять current author conflict entry в исходном индексе.");

        Assert(
            model.RevisionAuthorEntries.Count == 3 &&
            model.RevisionAuthorEntries[0] is
            {
                Author: "Alice",
                Conflict: null
            } &&
            model.RevisionAuthorEntries[2] is
            {
                Author: "Carol",
                Conflict: null
            },
            "RTF revtbl должен сохранять ordered regular/conflict entries без phantom authors.");

        var conflictEntry =
            model.RevisionAuthorEntries[1];
        var conflict =
            conflictEntry.Conflict ??
            throw new InvalidOperationException(
                "RTF binary revtbl entry должен создавать DocumentRevisionConflict.");

        Assert(
            conflictEntry.Author == "Боб" &&
            conflict.PreviousAuthor == "Анна" &&
            conflict.PreviousAuthorByteLength == 4 &&
            conflict.PreviousRevisionTimeBytes.SequenceEqual(
                new byte[]
                {
                    0x0A,
                    0x0D,
                    0x3B,
                    0xFF
                }),
            "RTF revtbl conflict должен сохранять Unicode current author, codepage previous author и четыре raw DTTM bytes.");

        var revisedInline =
            model.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .Single(inline =>
                    inline.Text.Contains(
                        "Conflict text",
                        StringComparison.Ordinal));
        var revision =
            revisedInline.Revisions.Single();

        Assert(
            revision.Kind ==
                DocumentRevisionKind.Insertion &&
            revision.AuthorIndex == 1 &&
            revision.Author == "Боб",
            "RTF revauth должен разрешать current author conflict entry по обычному revtbl index.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-revision-author-index=\"1\"",
            "RTF conflict revtbl HTML index");
        AssertContains(
            html,
            "data-revision-author=\"Боб\"",
            "RTF conflict revtbl current author HTML metadata");
        AssertContains(
            html,
            "data-revision-conflict=\"true\"",
            "RTF conflict revtbl HTML flag");
        AssertContains(
            html,
            "data-revision-previous-author=\"Анна\"",
            "RTF conflict revtbl previous author HTML metadata");
        AssertContains(
            html,
            "data-revision-previous-author-byte-length=\"4\"",
            "RTF conflict revtbl previous author byte length HTML metadata");
        AssertContains(
            html,
            "data-revision-previous-time-bytes=\"0A0D3BFF\"",
            "RTF conflict revtbl byte-exact time HTML metadata");

        var optionalTerminator =
            Read(
                "{\\rtf1\\ansi" +
                "{\\*\\revtbl" +
                "{A\\'00\\'03Joe\\'00\\'01\\'02\\'03\\'04;}" +
                "{B;}}" +
                "X\\par}");

        Assert(
            optionalTerminator.RevisionAuthors.SequenceEqual(
                new[]
                {
                    "A",
                    "B"
                }) &&
            optionalTerminator.RevisionAuthorEntries[0].Conflict is
            {
                PreviousAuthor: "Joe",
                PreviousAuthorByteLength: 3
            } &&
            optionalTerminator.RevisionAuthorEntries[0]
                .Conflict!
                .PreviousRevisionTimeBytes
                .SequenceEqual(
                    new byte[]
                    {
                        1,
                        2,
                        3,
                        4
                    }),
            "RTF conflict revtbl должен принимать optional semicolon после четырёх PreviousRevisionTime bytes.");

        Assert(
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A\'00}}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A\'00\'03Jo}}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A\'00\'01B\'01\'01\'02\'03\'04}}X\par}") &&
            RejectsInvalid(
                @"{\rtf1\ansi{\*\revtbl{A\'00\'01B\'00\'01\'02\'03}}X\par}"),
            "RTF truncated/malformed conflict revtbl payload должен завершаться structural error.");

        Assert(
            RejectsUnsupported(
                @"{\rtf1\ansi{\*\revtbl{A\'00\'01\u1041?\'00\'01\'02\'03\'04}}X\par}"),
            "RTF Unicode control внутри byte-counted previous-author conflict field должен оставаться explicit unsupported.");
    }
}
