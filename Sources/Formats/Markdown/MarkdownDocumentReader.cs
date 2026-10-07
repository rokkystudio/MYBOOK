using System.IO;
using Markdig;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Markdown;

/// <summary>
/// Читает Markdown и помещает результат преобразования Markdig
/// в нейтральную блочную модель MYBOOK.
/// </summary>
internal static class MarkdownDocumentReader
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    /// <summary>
    /// Преобразует Markdown в доверенный внутренний HTML-блок.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = new DocumentBlock[]
            {
                new DocumentHtmlBlock
                {
                    Html = Markdig.Markdown.ToHtml(File.ReadAllText(path), Pipeline)
                }
            }
        };
    }
}
