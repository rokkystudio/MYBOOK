using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF Unicode alternative \upr/\ud: выбор второго destination,
    /// scoped Unicode skips/character formatting, nested alternatives,
    /// Word bookmark forms, raw binary внутри ANSI alternative
    /// и строгий отказ для malformed structures.
    /// </summary>
    private static void TestRtfUnicodeAlternatives(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-unicode-alternatives.rtf");

        DocumentModel Read(string rtf)
        {
            File.WriteAllText(
                path,
                rtf,
                Encoding.Latin1);

            return RtfDocumentReader.Read(path);
        }

        bool Rejects(string rtf)
        {
            try
            {
                Read(rtf);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        var plain = Read(
            @"{\rtf1\ansi Before {\upr{ANSI only}{\*\ud Unicode \u1046?}} after\par}");

        var plainText =
            FlattenText(plain);

        Assert(
            plainText.Contains("Before Unicode Ж after", StringComparison.Ordinal) &&
            !plainText.Contains("ANSI only", StringComparison.Ordinal),
            "RTF upr должен выводить Unicode destination вместо ANSI fallback.");

        var format = Read(
            @"{\rtf1\ansi Start {\upr{\b ANSI}{\*\ud\b Unicode}} tail\par}");

        var paragraph =
            format.Blocks.OfType<DocumentParagraph>().Single();

        Assert(
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains("Unicode", StringComparison.Ordinal) &&
                inline.Bold) &&
            paragraph.Inlines.Any(inline =>
                inline.Text.Contains("tail", StringComparison.Ordinal) &&
                !inline.Bold) &&
            !FlattenText(format).Contains("ANSI", StringComparison.Ordinal),
            "RTF upr должен сохранять character formatting Unicode branch и восстанавливать внешний state.");

        var nested = Read(
            @"{\rtf1\ansi\uc1 pre {\upr{A}{\*\ud\uc0\u1046}} {\upr{B}{\*\ud{\upr{C}{\*\ud \u1047?}}}} end \u1048?\par}");

        var nestedText =
            FlattenText(nested);

        Assert(
            nestedText.Contains("pre Ж З end И", StringComparison.Ordinal) &&
            !nestedText.Contains("A", StringComparison.Ordinal) &&
            !nestedText.Contains("B", StringComparison.Ordinal) &&
            !nestedText.Contains("C", StringComparison.Ordinal),
            "RTF nested upr должен выбрать Unicode без ANSI текста и восстановить внешнее uc1.");

        var escaped = Read(
            @"{\rtf1\ansi{\upr{fallback \{brace\} \'41}{\*\ud Selected \{value\}}}\par}");

        Assert(
            FlattenText(escaped).Contains("Selected {value}", StringComparison.Ordinal) &&
            !FlattenText(escaped).Contains("fallback", StringComparison.Ordinal),
            "RTF upr ANSI lexer должен учитывать escaped braces и hex bytes.");

        var ignoredAnsiSideEffects = Read(
            @"{\rtf1\ansi Before{\upr{\par{\*\bkmkstart Shadow} fallback}{\*\ud Unicode}} after\par}");

        Assert(
            ignoredAnsiSideEffects.Blocks.OfType<DocumentParagraph>().Count() == 1 &&
            FlattenText(ignoredAnsiSideEffects).Contains("BeforeUnicode after", StringComparison.Ordinal) &&
            !ignoredAnsiSideEffects.Blocks.OfType<DocumentParagraph>()
                .Any(item => item.AnchorId == "Shadow"),
            "RTF upr не должен применять paragraph и bookmark effects ANSI branch.");

        var ignoredBareUd = Read(
            @"{\rtf1\ansi Before{\*\ud Ignored} after\par}");

        Assert(
            !FlattenText(ignoredBareUd).Contains("Ignored", StringComparison.Ordinal),
            "RTF ud без upr должен оставаться ignorable destination.");

        var rawBinaryAnsi =
            "{\\rtf1\\ansi{\\upr{\\bin3 " +
            "}{}" +
            "}{\\*\\ud Selected}}\\par}";

        Assert(
            FlattenText(Read(rawBinaryAnsi)).Contains("Selected", StringComparison.Ordinal),
            "RTF upr ANSI lexer должен игнорировать фигурные скобки внутри binN raw bytes.");

        var bookmark = Read(
            @"{\rtf1\ansi{\upr{\*\bkmkstart LabGValue}{\*\ud{\*\bkmkstart Lab\u915GValue}}} Text\par}");

        Assert(
            bookmark.Blocks.OfType<DocumentParagraph>().Any(
                item => string.Equals(
                    item.AnchorId,
                    "LabΓValue",
                    StringComparison.Ordinal)) &&
            !FlattenText(bookmark).Contains("LabGValue", StringComparison.Ordinal),
            "RTF upr bookmark Unicode destination должен выбирать Unicode имя.");

        var link = Read(
            @"{\rtf1\ansi{\field{\*\fldinst HYPERLINK ""https://example.com/rtf-upr""}{\fldrslt{\upr{ANSI}{\*\ud\u1046?}}}}}");

        Assert(
            link.Blocks.OfType<DocumentParagraph>()
                .SelectMany(item => item.Inlines)
                .Any(inline =>
                    inline.Text.Contains("Ж", StringComparison.Ordinal) &&
                    inline.LinkHref == "https://example.com/rtf-upr"),
            "RTF upr внутри hyperlink field result должен сохранять LinkHref.");

        Assert(
            Rejects(@"{\rtf1\ansi{\upr}}") &&
            Rejects(@"{\rtf1\ansi{\upr{ANSI}}}") &&
            Rejects(@"{\rtf1\ansi{\upr{ANSI}{Unicode}}}") &&
            Rejects(@"{\rtf1\ansi{\upr{ANSI}{\*\ud1 Unicode}}}") &&
            Rejects(@"{\rtf1\ansi{\upr{ANSI}{\*\ud Unicode}trailing}}") &&
            Rejects(@"{\rtf1\ansi{\upr{ANSI}{\*\ud Unicode}") &&
            Rejects(@"{\rtf1\ansi{\upr{bad \'ZZ}{\*\ud Unicode}}}") &&
            Rejects(@"{\rtf1\ansi{\upr{\bin-1 ABC}{\*\ud Unicode}}}") &&
            Rejects(@"{\rtf1\ansi{\upr{\bin4 ABC}{\*\ud Unicode}}}"),
            "RTF malformed upr должен давать structural error вместо ANSI fallback или повреждённого текста.");
    }
}
