namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every <c>https://traxsharp.net/docs/...</c> link in shipped code lands on a published page,
/// and its anchor on a heading of that page.
///
/// <para>The code links to its docs from XML doc comments (<c>&lt;seealso href&gt;</c>), from an
/// analyzer's <c>helpLinkUri</c>, and from the comments of the samples and templates a reader
/// copies. Those links reach a reader through IntelliSense and a build warning, long after the
/// page they name was renamed, and nothing on the docs side knew they existed. A page that moves
/// claims its old path in <c>redirect_from</c>, which this accepts, as the site does.</para>
///
/// <para>Scanned: every <c>*.cs</c> under a <c>Trax.*/src/</c> folder (the packages, the analyzer
/// included), and under <c>Trax.Samples/samples/</c> and <c>Trax.Samples/templates/</c>; and every
/// <c>*.ts</c> and <c>*.tsx</c> of the operations console's app, whose help links and empty states
/// send an operator to the docs (each term's glossary entry among them).</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class CodeLinksToDocsResolveTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    // A trailing period or slash is the sentence's or the URL's, not the page's.
    private static readonly Regex DocsLink = new(
        @"https://traxsharp\.net(?<path>/docs(?:/[A-Za-z0-9_\-]+)*)/?(?:#(?<anchor>[A-Za-z0-9_\-]+))?",
        RegexOptions.Compiled
    );

    [Test]
    public void Every_DocsLink_InCode_Resolves()
    {
        var site = DocsSite.Load();
        var links = CodeFiles().SelectMany(f => Links(f.Path, File.ReadAllText(f.Full))).ToList();

        links
            .Should()
            .NotBeEmpty(
                "the guard reads the docs links in the code; finding none means it checks nothing"
            );

        var offenders = links
            .Select(l => (l.Where, l.Url, Problem: Problem(site, l.Path, l.Anchor)))
            .Where(x => x.Problem is not null)
            .Select(x => $"{x.Where}  {x.Url}  ({x.Problem})")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a traxsharp.net/docs link in code reaches a reader through IntelliSense, a build "
                    + "warning or a scaffolded project, so it must land on a published page, and an "
                    + "anchor on a heading of it. Fix the link, or, when the page moved, have the "
                    + "page claim the old path in `redirect_from`. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void The_ConsolesGlossaryLinks_AreAmongThoseChecked()
    {
        // The console links each Trax term to its own glossary entry (lib/vocabulary.ts). A move
        // of that file out of the scanned folder would leave those links unchecked.
        var glossaryLinks = CodeFiles()
            .Where(f =>
                f.Path.EndsWith("ClientApp/src/lib/vocabulary.ts", StringComparison.Ordinal)
            )
            .SelectMany(f => Links(f.Path, File.ReadAllText(f.Full)))
            .Where(l => l.Path == "/docs/reference/glossary" && l.Anchor is not null)
            .ToList();

        glossaryLinks
            .Should()
            .NotBeEmpty(
                "the console's term help links each term's glossary heading, and this guard must read them"
            );
    }

    [Test]
    public void Links_AreReadWithoutTheSentenceAroundThem()
    {
        const string source = """
            /// See https://traxsharp.net/docs/sdk-reference/configuration/use-broadcaster.
            /// <seealso href="https://traxsharp.net/docs/effect/metadata#what-gets-persisted">x</seealso>
            // https://traxsharp.net/docs and https://traxsharp.net/docs/scheduler/ and https://traxsharp.net/blog
            """;

        Links("x.cs", source)
            .Select(l => (l.Where, l.Path, l.Anchor))
            .Should()
            .Equal(
                ("x.cs:1", "/docs/sdk-reference/configuration/use-broadcaster", null),
                ("x.cs:2", "/docs/effect/metadata", "what-gets-persisted"),
                ("x.cs:3", "/docs", null),
                ("x.cs:3", "/docs/scheduler", null)
            );
    }

    private static string? Problem(DocsSite site, string path, string? anchor)
    {
        var page = site.Resolve(path);
        if (page is null)
            return "no published page at this path, and no page claims it in redirect_from";
        if (anchor is not null && !site.HeadingIds(page).Contains(anchor))
            return $"no heading '#{anchor}' on {page}";
        return null;
    }

    private static IEnumerable<(string Where, string Url, string Path, string? Anchor)> Links(
        string file,
        string source
    )
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            foreach (Match m in DocsLink.Matches(lines[i]))
                yield return (
                    $"{file}:{i + 1}",
                    m.Value,
                    m.Groups["path"].Value,
                    m.Groups["anchor"].Success ? m.Groups["anchor"].Value : null
                );
    }

    /// <summary>The shipped code a docs link can sit in, repository-relative.</summary>
    private static IEnumerable<(string Path, string Full)> CodeFiles()
    {
        var roots = Directory
            .EnumerateDirectories(RepoRoot.Monorepo, "Trax.*")
            .Select(folder => Path.Combine(folder, "src"))
            .Append(Path.Combine(RepoRoot.Monorepo, "Trax.Samples", "samples"))
            .Append(Path.Combine(RepoRoot.Monorepo, "Trax.Samples", "templates"));

        var consoleApp = Path.Combine(
            RepoRoot.Monorepo,
            "Trax.Api",
            "src",
            "Trax.Api.Dashboard",
            "ClientApp",
            "src"
        );

        return roots
            .SelectMany(root => RepoRoot.FilesUnder(root, "*.cs"))
            .Concat(RepoRoot.FilesUnder(consoleApp, "*.ts"))
            .Concat(RepoRoot.FilesUnder(consoleApp, "*.tsx"))
            .Select(f => (Path.GetRelativePath(RepoRoot.Monorepo, f).Replace('\\', '/'), f))
            .OrderBy(f => f.Item1, StringComparer.Ordinal);
    }
}
