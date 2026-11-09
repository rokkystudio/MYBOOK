using System.IO;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет разделение пользовательского Books corpus и binary regression
    /// fixtures, наличие отдельной папки Ветхого Завета и сохранение полного
    /// издания Библии в корне книжной библиотеки.
    /// </summary>
    private static void TestBookLibraryLayout(string repositoryRoot)
    {
        var booksDirectory =
            Path.Combine(
                repositoryRoot,
                "Books");
        var oldTestamentDirectory =
            Path.Combine(
                booksDirectory,
                "Ветхий Завет");
        var fixturesDirectory =
            Path.Combine(
                repositoryRoot,
                "Tests",
                "Fixtures");

        Assert(
            Directory.Exists(oldTestamentDirectory),
            "Books должен содержать отдельный каталог Ветхий Завет.");

        Assert(
            File.Exists(
                Path.Combine(
                    oldTestamentDirectory,
                    "Завет Ветхий. Первая книга Моисеева. БЫТИЕ - royallib.ru.fb2")) &&
            File.Exists(
                Path.Combine(
                    oldTestamentDirectory,
                    "Kniga_Sudey_Izrailevyih.fb2")),
            "Книги Ветхого Завета должны находиться в собственном каталоге.");

        Assert(
            File.Exists(
                Path.Combine(
                    booksDirectory,
                    "bibliya.fb2")),
            "Полное издание Библии должно оставаться в корне Books.");

        Assert(
            !Directory
                .EnumerateFiles(
                    booksDirectory,
                    "Завет Ветхий.*",
                    SearchOption.TopDirectoryOnly)
                .Any(),
            "Отдельные книги Ветхого Завета не должны лежать в корне Books.");

        foreach (var fixtureName in new[]
                 {
                     "Regression_EPUB2.epub",
                     "Regression_EPUB3.epub",
                     "Regression_Word97_Binary.doc"
                 })
        {
            Assert(
                File.Exists(
                    Path.Combine(
                        fixturesDirectory,
                        fixtureName)),
                $"Tests/Fixtures должен содержать {fixtureName}.");

            Assert(
                !Directory
                    .EnumerateFiles(
                        booksDirectory,
                        fixtureName,
                        SearchOption.AllDirectories)
                    .Any(),
                $"Books corpus не должен содержать {fixtureName}.");
        }
    }
}
