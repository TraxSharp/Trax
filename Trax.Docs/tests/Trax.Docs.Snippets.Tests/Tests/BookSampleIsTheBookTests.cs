using System.Text.RegularExpressions;

namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// <c>Trax.Samples/book/Shopfront</c> is the project the book builds, file for file: each file the
/// book's last page leaves the reader with (<see cref="BookProject"/>) is in the sample with the same
/// text, and every other file in the sample is one a command in the book generates or one the
/// repository needs, named in <see cref="NotPrintedByTheBook"/>.
///
/// <para>The sample's own suite runs the book's tests against it, so this is what makes those runs
/// say something about the book. A page edited without the sample, or the sample without the page,
/// fails here, naming the file and the page that prints it.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class BookSampleIsTheBookTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private static string Sample =>
        Path.Combine(RepoRoot.Monorepo, "Trax.Samples", "book", "Shopfront");

    /// <summary>
    /// The files in the sample the book does not print, each with why it is there. Paths are
    /// relative to the sample, with forward slashes; <c>*</c> matches within a folder and <c>**</c>
    /// across folders.
    /// </summary>
    private static readonly (string Pattern, string Why)[] NotPrintedByTheBook =
    [
        ("Directory.Build.props", "repository only: stops Trax.Samples' props applying"),
        ("Directory.Build.targets", "repository only: builds against Trax's source"),
        ("README.md", "repository only: what the folder is"),
        ("**/packages.lock.json", "repository only: every project restores locked"),
        ("Shopfront.slnx", "dotnet new sln and dotnet sln add, chapters 2 and 11"),
        (
            "dotnet-tools.json",
            "dotnet new tool-manifest and dotnet tool install, chapters 12 and 18"
        ),
        ("Shopfront.Hub/Properties/launchSettings.json", "dotnet new web, chapter 1"),
        ("machines/checkout/*", "trax machine generate, chapters 12 and 13"),
        ("Shopfront.Core/Data/Migrations/*", "dotnet ef migrations add, chapter 18"),
        ("web/package-lock.json", "npm install, chapter 15"),
        ("web/src/trax/**", "the hub's --trax-export-client and trax client, chapter 15"),
    ];

    [Test]
    public void EveryFileTheBookPrints_IsInTheSample_AsPrinted()
    {
        var offenders = new List<string>();
        foreach (
            var (file, fence) in BookProject.Files().OrderBy(f => f.Key, StringComparer.Ordinal)
        )
        {
            var path = Path.Combine(Sample, file);
            if (!File.Exists(path))
                offenders.Add($"{file}  is missing (printed by {fence.Location})");
            else if (Normalized(File.ReadAllText(path)) != Normalized(fence.Code))
                offenders.Add($"{file}  differs from {fence.Location}");
        }

        offenders
            .Should()
            .BeEmpty(
                "Trax.Samples/book/Shopfront holds each file the book prints exactly as its last "
                    + "page leaves it. Copy the fence into the sample, or the sample's file into the "
                    + "fence, so the two say the same thing. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void EveryFileInTheSample_IsPrintedByTheBook_OrGeneratedByIt()
    {
        var printed = BookProject.Files().Keys.ToHashSet(StringComparer.Ordinal);
        var allowed = NotPrintedByTheBook.Select(p => Glob(p.Pattern)).ToList();

        var offenders = SampleFiles()
            .Where(f => !printed.Contains(f) && !allowed.Any(a => a.IsMatch(f)))
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "every file in Trax.Samples/book/Shopfront is one the book prints, one a command "
                    + "in the book generates, or one the repository needs. Print it in the book, "
                    + "delete it, or name it in NotPrintedByTheBook with the reason. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void EveryFileNotPrintedByTheBook_IsStillThere()
    {
        var files = SampleFiles();
        var unused = NotPrintedByTheBook
            .Where(p => !files.Any(Glob(p.Pattern).IsMatch))
            .Select(p => p.Pattern)
            .ToList();

        unused
            .Should()
            .BeEmpty(
                "an entry in NotPrintedByTheBook that matches nothing in the sample lets a file "
                    + "back in unchecked. Remove it. Unused:\n  "
                    + string.Join("\n  ", unused)
            );
    }

    private static List<string> SampleFiles()
    {
        var s = Path.DirectorySeparatorChar;
        string[] skipped = [$"{s}bin{s}", $"{s}obj{s}", $"{s}node_modules{s}", $"{s}dist{s}"];
        return Directory
            .EnumerateFiles(Sample, "*", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(x => f.Contains(x, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(Sample, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string Normalized(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static Regex Glob(string pattern) =>
        new(
            "^"
                + Regex
                    .Escape(pattern)
                    .Replace(@"\*\*/", "(?:.*/)?")
                    .Replace(@"\*\*", ".*")
                    .Replace(@"\*", "[^/]*")
                + "$"
        );
}
