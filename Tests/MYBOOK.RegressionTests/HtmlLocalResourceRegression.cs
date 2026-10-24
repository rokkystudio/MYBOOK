using System.IO;
using System.Runtime.CompilerServices;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static class HtmlLocalResourceRegression
{
    [ModuleInitializer]
    public static void Run()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "MYBOOK-HtmlMapping-" +
                Guid.NewGuid().ToString("N"));
        var otherRoot =
            root + "-other";

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(otherRoot);

        try
        {
            var firstPath =
                Path.Combine(
                    root,
                    "book page #1.html");
            var secondPath =
                Path.Combine(
                    root,
                    "second.htm");
            var otherPath =
                Path.Combine(
                    otherRoot,
                    "book.html");

            File.WriteAllText(
                firstPath,
                "<img src=\"assets/cover.png\">");
            File.WriteAllText(
                secondPath,
                "<p>second</p>");
            File.WriteAllText(
                otherPath,
                "<p>other</p>");

            var first =
                HtmlLocalResourceService.CreateMapping(
                    firstPath);
            var second =
                HtmlLocalResourceService.CreateMapping(
                    secondPath);
            var other =
                HtmlLocalResourceService.CreateMapping(
                    otherPath);

            Assert(
                string.Equals(
                    first.RootDirectory,
                    Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(root)),
                    StringComparison.OrdinalIgnoreCase),
                "HTML mapping root должен быть каталогом документа.");
            Assert(
                first.DocumentUri.Scheme ==
                Uri.UriSchemeHttps,
                "HTML mapping должен использовать HTTPS origin.");
            Assert(
                first.HostName.EndsWith(
                    ".invalid",
                    StringComparison.Ordinal),
                "HTML mapping должен использовать reserved .invalid host.");
            Assert(
                first.DocumentUri.AbsoluteUri.Contains(
                    "book%20page%20%231.html",
                    StringComparison.Ordinal),
                "HTML document filename должен быть URL-encoded.");
            Assert(
                string.Equals(
                    first.HostName,
                    second.HostName,
                    StringComparison.Ordinal),
                "HTML documents одного каталога должны иметь один local origin.");
            Assert(
                !string.Equals(
                    first.HostName,
                    other.HostName,
                    StringComparison.Ordinal),
                "HTML documents разных каталогов должны быть изолированы разными origins.");

            var invalidPath =
                Path.Combine(
                    root,
                    "not-html.txt");
            File.WriteAllText(
                invalidPath,
                "text");

            var rejected = false;
            try
            {
                HtmlLocalResourceService.CreateMapping(
                    invalidPath);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(
                rejected,
                "HTML mapping должен отклонять non-HTML files.");

            Console.WriteLine(
                "PASS HTML local resource mapping");
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
            Directory.Delete(
                otherRoot,
                recursive: true);
        }
    }

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
