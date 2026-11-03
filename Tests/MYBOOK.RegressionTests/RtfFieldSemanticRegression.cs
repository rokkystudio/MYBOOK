using System.IO;
using System.Text;
using Book.Documents;
using Book.Formats.Rtf;
using Book.Rendering;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет безопасную RTF field semantics. Cached fldrslt остаётся
    /// отображаемым текстом, а поддерживаемые field types получают kind,
    /// нормализованную instruction и target без вычисления значения.
    /// Неизвестные/исполняемые поля остаются plain saved result.
    /// </summary>
    private static void TestRtfFieldSemantics(string directory)
    {
        var path =
            Path.Combine(
                directory,
                "rtf-field-semantics.rtf");

        File.WriteAllText(
            path,
            """
            {\rtf1\ansi
            {\*\bkmkstart bookmark}
            {\field{\*\fldinst PAGE \* MERGEFORMAT}{\fldrslt 12}}\par
            {\field{\*\fldinst NUMPAGES}{\fldrslt 30}}\par
            {\field{\*\fldinst DATE \@ "yyyy-MM-dd"}{\fldrslt 2026-11-03}}\par
            {\field{\*\fldinst TIME \@ "HH:mm"}{\fldrslt 09:41}}\par
            {\field{\*\fldinst REF bookmark \h}{\fldrslt Section title}}\par
            {\field{\*\fldinst PAGEREF "bookmark" \h}{\fldrslt 7}}\par
            {\field{\*\fldinst NOTEREF noteMark \h}{\fldrslt 3}}\par
            {\field{\*\fldinst HYPERLINK "https://example.com/rtf-field"}{\fldrslt External field}}\par
            {\field{\*\fldinst HYPERLINK \l "bookmark"}{\fldrslt Internal field}}\par
            {\field{\*\fldinst HYPERLINK "file:///C:/secret.txt"}{\fldrslt Unsafe field}}\par
            {\field{\*\fldinst MACROBUTTON RunMacro Run}{\fldrslt Run}}\par
            {\field{\*\fldinst REF}{\fldrslt Missing target}}\par
            {\field{\*\fldinst PAGE}{\fldrslt \b 4\b0 2}}\par
            }
            """,
            Encoding.Latin1);

        var model =
            RtfDocumentReader.Read(path);
        var inlines =
            model.Blocks
                .OfType<DocumentParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .ToArray();

        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Page &&
                inline.Text == "12" &&
                inline.FieldInstruction?.StartsWith(
                    "PAGE",
                    StringComparison.OrdinalIgnoreCase) == true &&
                inline.FieldInstruction.Contains(
                    @"\*",
                    StringComparison.Ordinal)),
            "RTF PAGE должен сохранять cached result и normalized field instruction.");

        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.NumPages &&
                inline.Text == "30"),
            "RTF NUMPAGES должен сохранять semantic field kind.");

        var date =
            inlines.Single(inline =>
                inline.FieldKind == DocumentFieldKind.Date);
        var time =
            inlines.Single(inline =>
                inline.FieldKind == DocumentFieldKind.Time);

        Assert(
            date.Text == "2026-11-03" &&
            date.FieldInstruction?.Contains(
                @"\@",
                StringComparison.Ordinal) == true &&
            time.Text == "09:41" &&
            time.FieldInstruction?.Contains(
                @"\@",
                StringComparison.Ordinal) == true,
            "RTF DATE/TIME должны сохранять cached results и format switch в instruction.");

        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.Ref &&
                inline.FieldTarget == "bookmark" &&
                inline.Text == "Section title"),
            "RTF REF должен сохранять bare bookmark target.");

        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.PageRef &&
                inline.FieldTarget == "bookmark" &&
                inline.Text == "7"),
            "RTF PAGEREF должен сохранять quoted bookmark target.");

        Assert(
            inlines.Any(inline =>
                inline.FieldKind == DocumentFieldKind.NoteRef &&
                inline.FieldTarget == "noteMark" &&
                inline.Text == "3"),
            "RTF NOTEREF должен сохранять note target.");

        var external =
            inlines.Single(inline =>
                inline.Text == "External field");
        var internalLink =
            inlines.Single(inline =>
                inline.Text == "Internal field");
        var unsafeLink =
            inlines.Single(inline =>
                inline.Text == "Unsafe field");

        Assert(
            external.FieldKind == DocumentFieldKind.Hyperlink &&
            external.FieldTarget == "https://example.com/rtf-field" &&
            external.LinkHref == "https://example.com/rtf-field",
            "RTF external HYPERLINK должен сохранять field metadata и safe href.");

        Assert(
            internalLink.FieldKind == DocumentFieldKind.Hyperlink &&
            internalLink.FieldTarget == "#bookmark" &&
            internalLink.LinkHref == "#bookmark",
            "RTF local HYPERLINK должен сохранять bookmark target.");

        Assert(
            unsafeLink.FieldKind == DocumentFieldKind.Hyperlink &&
            unsafeLink.FieldTarget == null &&
            unsafeLink.LinkHref == null,
            "RTF unsafe HYPERLINK должен сохранять field kind/result без unsafe target.");

        Assert(
            inlines.Any(inline =>
                inline.Text == "Run" &&
                inline.FieldKind == null &&
                inline.FieldInstruction == null &&
                inline.FieldTarget == null) &&
            inlines.Any(inline =>
                inline.Text == "Missing target" &&
                inline.FieldKind == null),
            "RTF executable/unknown и malformed REF fields должны оставаться plain saved result.");

        var splitPage =
            inlines.Where(inline =>
                    inline.FieldKind == DocumentFieldKind.Page &&
                    inline.Text is "4" or "2")
                .ToArray();

        Assert(
            splitPage.Length == 2 &&
            splitPage.Any(inline =>
                inline.Text == "4" &&
                inline.Bold) &&
            splitPage.Any(inline =>
                inline.Text == "2" &&
                !inline.Bold) &&
            splitPage.All(inline =>
                inline.FieldInstruction?.StartsWith(
                    "PAGE",
                    StringComparison.OrdinalIgnoreCase) == true),
            "RTF fldrslt formatting groups должны сохранять field metadata на каждом result inline.");

        var html =
            DocumentHtmlRenderer.Render(
                model,
                CreateRenderTheme());

        AssertContains(
            html,
            "data-field-kind=\"page\"",
            "RTF PAGE field HTML metadata");
        AssertContains(
            html,
            "data-field-kind=\"numpages\"",
            "RTF NUMPAGES field HTML metadata");
        AssertContains(
            html,
            "data-field-kind=\"ref\"",
            "RTF REF field HTML metadata");
        AssertContains(
            html,
            "data-field-target=\"bookmark\"",
            "RTF REF target HTML metadata");
        AssertContains(
            html,
            "data-field-kind=\"hyperlink\"",
            "RTF HYPERLINK field HTML metadata");

        Assert(
            !html.Contains(
                "href=\"file:",
                StringComparison.OrdinalIgnoreCase),
            "RTF unsafe HYPERLINK не должен попадать в HTML href.");
    }
}
