using System.IO;
using System.Windows;
using System.Windows.Documents;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Rtf;

/// <summary>
/// Читает RTF средствами встроенного WPF parser и преобразует результат
/// в нейтральную модель MYBOOK без зависимости от внешнего офисного ПО.
/// </summary>
internal static class RtfDocumentReader
{
    /// <summary>
    /// Загружает RTF-файл и преобразует поддерживаемые блоки и inline-стили.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        var flow = new FlowDocument();

        using (var stream = File.OpenRead(path))
        {
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            range.Load(stream, DataFormats.Rtf);
        }

        var blocks = new List<DocumentBlock>();

        foreach (var block in flow.Blocks)
        {
            AppendBlock(blocks, block);
        }

        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = blocks
        };
    }

    private static void AppendBlock(List<DocumentBlock> result, Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                result.Add(new DocumentParagraph
                {
                    Inlines = ReadInlines(paragraph.Inlines)
                });
                break;

            case Section section:
                foreach (var child in section.Blocks)
                {
                    AppendBlock(result, child);
                }
                break;

            case List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var child in item.Blocks)
                    {
                        AppendBlock(result, child);
                    }
                }
                break;

            default:
            {
                var range = new TextRange(block.ContentStart, block.ContentEnd);
                var text = range.Text.TrimEnd();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(new DocumentParagraph
                    {
                        Inlines = new[]
                        {
                            new DocumentInline
                            {
                                Text = text
                            }
                        }
                    });
                }
                break;
            }
        }
    }

    private static IReadOnlyList<DocumentInline> ReadInlines(InlineCollection inlines)
    {
        var result = new List<DocumentInline>();

        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    result.Add(ToInline(run.Text, run));
                    break;

                case LineBreak:
                    result.Add(new DocumentInline { Text = Environment.NewLine });
                    break;

                case Span span:
                {
                    var nested = ReadInlines(span.Inlines);
                    foreach (var item in nested)
                    {
                        result.Add(new DocumentInline
                        {
                            Text = item.Text,
                            Bold = item.Bold || span.FontWeight >= FontWeights.Bold,
                            Italic = item.Italic || span.FontStyle == FontStyles.Italic,
                            Underline = item.Underline ||
                                        span.TextDecorations?.Contains(TextDecorations.Underline[0]) == true,
                            Superscript = item.Superscript,
                            Code = item.Code
                        });
                    }
                    break;
                }

                default:
                {
                    var range = new TextRange(inline.ContentStart, inline.ContentEnd);
                    if (!string.IsNullOrEmpty(range.Text))
                    {
                        result.Add(new DocumentInline
                        {
                            Text = range.Text
                        });
                    }
                    break;
                }
            }
        }

        return result;
    }

    private static DocumentInline ToInline(string text, Inline element)
    {
        return new DocumentInline
        {
            Text = text,
            Bold = element.FontWeight >= FontWeights.Bold,
            Italic = element.FontStyle == FontStyles.Italic,
            Underline = element.TextDecorations?.Contains(TextDecorations.Underline[0]) == true
        };
    }
}
