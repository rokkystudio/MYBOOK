using System.IO;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Text;

/// <summary>
/// Читает обычный текстовый файл в нейтральную модель MYBOOK.
/// </summary>
internal static class TextDocumentReader
{
    /// <summary>
    /// Загружает весь TXT-файл как один preformatted-блок.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = new DocumentBlock[]
            {
                new DocumentParagraph
                {
                    Preformatted = true,
                    Inlines = new[]
                    {
                        new DocumentInline
                        {
                            Text = File.ReadAllText(path)
                        }
                    }
                }
            }
        };
    }
}
