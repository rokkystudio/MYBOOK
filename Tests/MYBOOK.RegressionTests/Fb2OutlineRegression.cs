using System.IO;
using System.Text;
using Book.Documents;
using Book.Rendering;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет FB2 оглавление по body/section title: дерево вложенности,
    /// составные и многострочные заголовки, явные/source section anchors,
    /// детерминированные synthetic anchors, коллизии исходных id, заголовки
    /// без текста, секции без title, независимые body и исключение notes/poem.
    /// Проверяет, что TargetAnchorId соответствует реально отрендеренному HTML.
    /// </summary>
    private static void TestFb2Outlines(string directory)
    {
        var path = Path.Combine(
            directory,
            "fb2-outline-hierarchy.fb2");

        File.WriteAllText(
            path,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0"
                         xmlns:xlink="http://www.w3.org/1999/xlink">
              <description>
                <title-info>
                  <book-title>FB2 Outline Hierarchy</book-title>
                </title-info>
              </description>
              <body id="main-body">
                <title><p>Preface</p></title>
                <section id="part-one">
                  <title><p>Part <strong>One</strong></p></title>
                  <p>First body paragraph.</p>
                  <section>
                    <title>
                      <p>Chapter without ID</p>
                      <p>Second line</p>
                    </title>
                    <p>Text after heading.</p>
                    <section id="deep-section">
                      <title id="deep-title">
                        <p>Deep heading</p>
                      </title>
                      <p>Deep section body.</p>
                    </section>
                  </section>
                  <section>
                    <title><p>Sibling chapter</p></title>
                    <poem>
                      <title><p>Poem title</p></title>
                      <stanza><v>Poem verse</v></stanza>
                    </poem>
                  </section>
                </section>
                <section>
                  <p>Untitled section introduction.</p>
                  <section id="promoted">
                    <title><p>Promoted child section</p></title>
                    <p>Nested inside untitled section.</p>
                  </section>
                </section>
                <section>
                  <title id="solo-title"><p>Standalone section</p></title>
                  <p>Paragraph after explicit title.</p>
                </section>
                <section>
                  <title><p>   </p></title>
                  <p>Section with empty heading.</p>
                </section>
                <p id="fb2-heading-1">Reserved source ID after generated titles.</p>
              </body>
              <body id="appendix-body">
                <section id="appendix">
                  <title><p>Appendix heading</p></title>
                  <p>Appendix content.</p>
                </section>
              </body>
              <body name="notes">
                <section id="note-one">
                  <title><p>Note heading</p></title>
                  <p>Note body.</p>
                </section>
              </body>
            </FictionBook>
            """,
            new UTF8Encoding(false));

        var model = SupportedFormatRegistry.ReadDocument(
            path);

        AssertEqual(
            2,
            model.Outlines.Count,
            "FB2 оглавление должно содержать заглавный body и отдельный appendix body.");

        var preface = model.Outlines[0];

        AssertEqual(
            "Preface",
            preface.Title,
            "FB2 body title должен стать корневым элементом оглавления.");

        AssertEqual(
            "main-body",
            preface.TargetAnchorId!,
            "FB2 body title должен использовать исходный id body.");

        AssertEqual(
            3,
            preface.Children.Count,
            "FB2 должен сохранять обычные sections и продвигать title вложенной секции без собственного родительского title.");

        var part = preface.Children[0];

        AssertEqual(
            "Part One",
            part.Title,
            "FB2 составной title должен содержать текст выделенного strong.");

        AssertEqual(
            "part-one",
            part.TargetAnchorId!,
            "FB2 section title должен наследовать явный id секции.");

        AssertEqual(
            2,
            part.Children.Count,
            "FB2 part должен содержать две вложенные главы.");

        var first = part.Children[0];

        AssertEqual(
            "Chapter without ID Second line",
            first.Title,
            "FB2 многострочный title должен объединять все title/p.");

        AssertEqual(
            "fb2-heading-2",
            first.TargetAnchorId!,
            "FB2 генерируемый id должен обходить зарезервированный id другого элемента.");

        AssertEqual(
            1,
            first.Children.Count,
            "FB2 глубокая секция должна быть дочерним элементом своей главы.");

        AssertEqual(
            "Deep heading",
            first.Children[0].Title,
            "FB2 deep title должен участвовать в иерархии.");

        AssertEqual(
            "deep-title",
            first.Children[0].TargetAnchorId!,
            "FB2 title@id должен иметь приоритет над section@id.");

        AssertEqual(
            "Sibling chapter",
            part.Children[1].Title,
            "FB2 следующая секция должна стать sibling, а не дочерним элементом.");

        AssertEqual(
            "fb2-heading-3",
            part.Children[1].TargetAnchorId!,
            "FB2 следующий title без id получает отдельный детерминированный id.");

        AssertEqual(
            0,
            part.Children[1].Children.Count,
            "FB2 poem title внутри главы не должен попадать в оглавление.");

        AssertEqual(
            "Promoted child section",
            preface.Children[1].Title,
            "FB2 title вложенной секции без заголовка родителя должен быть доступен в оглавлении.");

        AssertEqual(
            "promoted",
            preface.Children[1].TargetAnchorId!,
            "FB2 promoted section должен использовать свой source id.");

        AssertEqual(
            "Standalone section",
            preface.Children[2].Title,
            "FB2 section title с собственным id должен быть независимым пунктом.");

        AssertEqual(
            "solo-title",
            preface.Children[2].TargetAnchorId!,
            "FB2 explicit title id должен сохраняться.");

        AssertEqual(
            "Appendix heading",
            model.Outlines[1].Title,
            "FB2 следующий body должен формировать отдельную ветвь.");

        AssertEqual(
            "appendix",
            model.Outlines[1].TargetAnchorId!,
            "FB2 следующий body должен использовать свой section anchor.");

        Assert(
            !model.Outlines
                .SelectMany(item => item.Children)
                .Any(item =>
                    item.Title == "Note heading" ||
                    item.Title == "Poem title"),
            "FB2 notes и poem title не должны превращаться в chapter outlines.");

        var headings = model.Blocks
            .OfType<DocumentParagraph>()
            .Where(paragraph =>
                paragraph.HeadingLevel > 0)
            .ToArray();

        Assert(
            headings.Any(paragraph =>
                paragraph.AnchorId == "fb2-heading-2") &&
            headings.Any(paragraph =>
                paragraph.AnchorId == "fb2-heading-3") &&
            headings.All(paragraph =>
                paragraph.AnchorId != "fb2-heading-1"),
            "FB2 generated anchors должны принадлежать заголовкам без source id.");

        var allAnchors = model.Blocks
            .OfType<DocumentParagraph>()
            .Select(paragraph => paragraph.AnchorId)
            .Where(anchor => anchor != null)
            .ToArray();

        AssertEqual(
            allAnchors.Length,
            allAnchors.Distinct(StringComparer.Ordinal).Count(),
            "FB2 source и synthetic anchors должны быть уникальными.");

        Assert(
            model.Blocks
                .OfType<DocumentParagraph>()
                .Any(paragraph =>
                    paragraph.IsNote &&
                    paragraph.Inlines.Any(inline =>
                        inline.Text.Contains(
                            "Note body.",
                            StringComparison.Ordinal))),
            "FB2 body name=notes должен продолжать сохраняться в блоках.");

        var html = DocumentHtmlRenderer.Render(
            model,
            CreateRenderTheme());

        foreach (var target in new[]
                 {
                     "main-body",
                     "part-one",
                     "fb2-heading-2",
                     "deep-title",
                     "fb2-heading-3",
                     "promoted",
                     "solo-title",
                     "appendix"
                 })
        {
            AssertContains(
                html,
                $"id=\"anchor-{target}\"",
                $"FB2 outline anchor {target} должен присутствовать в HTML.");
        }

        var reopened = SupportedFormatRegistry.ReadDocument(
            path);

        AssertEqual(
            first.TargetAnchorId!,
            reopened.Outlines[0].Children[0].Children[0].TargetAnchorId!,
            "FB2 generated heading anchors должны повторяться при чтении одного документа.");

        var noBodyTitlePath = Path.Combine(
            directory,
            "fb2-outline-no-body-title.fb2");

        File.WriteAllText(
            noBodyTitlePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
              <body>
                <section>
                  <title><p>First</p></title>
                  <section><title><p>Nested</p></title></section>
                </section>
                <section><title><p>Second</p></title></section>
              </body>
            </FictionBook>
            """,
            new UTF8Encoding(false));

        var withoutBodyTitle =
            SupportedFormatRegistry.ReadDocument(
                noBodyTitlePath);

        AssertEqual(
            2,
            withoutBodyTitle.Outlines.Count,
            "FB2 body без собственного title должен содержать top-level sections.");

        AssertEqual(
            "fb2-heading-1",
            withoutBodyTitle.Outlines[0].TargetAnchorId!,
            "FB2 первый title без ID должен получить первый synthetic anchor.");

        AssertEqual(
            "fb2-heading-2",
            withoutBodyTitle.Outlines[0].Children[0].TargetAnchorId!,
            "FB2 вложенный title без ID должен получить следующий anchor.");

        AssertEqual(
            "fb2-heading-3",
            withoutBodyTitle.Outlines[1].TargetAnchorId!,
            "FB2 sibling title без ID должен получить следующий anchor.");
    }
}
