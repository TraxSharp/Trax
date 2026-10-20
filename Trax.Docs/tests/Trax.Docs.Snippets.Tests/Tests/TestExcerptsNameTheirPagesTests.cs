using System.Text.RegularExpressions;

namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// A page that shows a library's test, and the test, name each other.
///
/// <para>A page backs a behaviour with an excerpt of the test that proves it
/// (<c>from=Trax.Core/tests/...</c>), and <see cref="SampleExcerptsMatchSourceTests"/> holds the
/// excerpt to the file. That does not tell whoever edits the test that a page shows it. So the
/// test file says so, in a comment near its top:</para>
///
/// <code>// Shown on https://traxsharp.net/docs/core/memory</code>
///
/// <para>Both directions are checked: a page's excerpt of a file under a library folder's
/// <c>tests/</c> needs that line in the file, and a line in such a file needs the page it names
/// to excerpt the file. Samples and templates are not covered: sample pages tour their sample,
/// and a template's comments ship into the reader's project.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class TestExcerptsNameTheirPagesTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private static readonly Regex ShownOn = new(
        @"^\s*//\s*Shown on https://traxsharp\.net(?<page>/docs(?:/[A-Za-z0-9_\-]+)*)/?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    [Test]
    public void Every_ExcerptOfALibraryTest_IsNamedByTheTest()
    {
        var offenders = Excerpted()
            .Where(e => !PagesNamedBy(e.File).Contains(e.Page))
            .Select(e =>
                $"{e.Location}: {e.File} has no `// Shown on https://traxsharp.net{e.Page}`"
            )
            .Distinct()
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a test a page shows says so, so whoever edits the test knows a page depends on "
                    + "it. Add the line near the top of the file. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void Every_ShownOnLine_NamesAPageThatShowsTheFile()
    {
        var shown = Excerpted().Select(e => (e.File, e.Page)).ToHashSet();

        var claims = LibraryTestFiles()
            .SelectMany(file => PagesNamedBy(file).Select(page => (File: file, Page: page)))
            .ToList();

        claims
            .Should()
            .NotBeEmpty(
                "the examples in Trax.Core's tests name their pages; finding none means this checks nothing"
            );

        var offenders = claims
            .Where(c => !shown.Contains(c))
            .Select(c => $"{c.File}: says it is shown on {c.Page}, which has no from= fence of it")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a `// Shown on` line is a promise the page keeps. Excerpt the file on that page, "
                    + "or fix or delete the line. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void ShownOn_IsReadOnlyFromACommentLine()
    {
        const string source = """
            // Shown on https://traxsharp.net/docs/core/memory
            //   Shown on https://traxsharp.net/docs/sdk-reference/trains-and-junctions/train/
            var url = "// Shown on https://traxsharp.net/docs/elsewhere";
            """;

        ShownOn
            .Matches(source)
            .Select(m => m.Groups["page"].Value)
            .Should()
            .Equal("/docs/core/memory", "/docs/sdk-reference/trains-and-junctions/train");
    }

    /// <summary>
    /// Each fence on a published page that excerpts a file under a library folder's <c>tests/</c>,
    /// with the page as its URL path.
    /// </summary>
    private static IEnumerable<(string Location, string File, string Page)> Excerpted() =>
        Fences
            .OnPublishedPages()
            .Where(f => f.From is not null && IsLibraryTest(f.From))
            .Select(f => (f.Location, f.From!, UrlPath(f.Page)));

    private static bool IsLibraryTest(string repoRelative)
    {
        var segments = repoRelative.Split('/');
        return segments.Length > 2
            && segments[0].StartsWith("Trax.", StringComparison.Ordinal)
            && segments[0] is not ("Trax.Samples" or "Trax.Docs")
            && segments[1] == "tests";
    }

    private static string UrlPath(string page)
    {
        var path = page.Replace('\\', '/');
        path = path.EndsWith(".md", StringComparison.Ordinal) ? path[..^3] : path;
        return path == "index" ? "/docs" : "/docs/" + path;
    }

    private static IReadOnlySet<string> PagesNamedBy(string repoRelative)
    {
        var file = Path.Combine(
            new[] { RepoRoot.Monorepo }.Concat(repoRelative.Split('/')).ToArray()
        );
        return File.Exists(file)
            ? ShownOn
                .Matches(File.ReadAllText(file))
                .Select(m => m.Groups["page"].Value)
                .ToHashSet()
            : new HashSet<string>();
    }

    /// <summary>The C# files under each library folder's <c>tests/</c>, repository-relative.</summary>
    private static IEnumerable<string> LibraryTestFiles() =>
        Directory
            .EnumerateDirectories(RepoRoot.Monorepo, "Trax.*")
            .Where(folder => Path.GetFileName(folder) is not ("Trax.Samples" or "Trax.Docs"))
            .Select(folder => Path.Combine(folder, "tests"))
            .Where(Directory.Exists)
            .SelectMany(tests =>
                Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            )
            .Where(file =>
                !file.Split(Path.DirectorySeparatorChar)
                    .Any(s => s is "bin" or "obj" or "node_modules")
            )
            .Select(file => Path.GetRelativePath(RepoRoot.Monorepo, file).Replace('\\', '/'));
}
