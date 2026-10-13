namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every <c>redirect_from</c> entry is an old <c>/docs/</c> path that no page is served at and
/// no other page claims.
///
/// <para>A page that moves keeps its old URL working by claiming it, and traxsharp.net turns
/// each claim into a permanent redirect. A claim on a path a live page is served at would hide
/// that page, and two claims on one path send the reader to whichever the build read last.
/// Both look fine in the diff that makes them.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class RedirectsTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    [Test]
    public void Redirects_AreOldDocsPaths_ClaimedOnce()
    {
        var site = DocsSite.Load();
        var problems = Problems(site);

        problems
            .Should()
            .BeEmpty(
                "a `redirect_from` entry is the old `/docs/<path>` a moved page was served at: it "
                    + "starts with /docs/, carries no anchor or query, names no page that still "
                    + "exists (the redirect would hide it), and is claimed by one page only. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Test]
    public void Problems_FindEachBadClaim()
    {
        var site = DocsSite.Of(
            ["index.md", "scheduler/dead-letters.md", "how-to/resolve-a-dead-letter.md"],
            [
                ("how-to/resolve-a-dead-letter.md", "/docs/scheduler/dead-letters-and-cleanup"),
                ("how-to/resolve-a-dead-letter.md", "/docs/scheduler/dead-letters"),
                ("how-to/resolve-a-dead-letter.md", "/scheduler/old"),
                ("how-to/resolve-a-dead-letter.md", "/docs/x#part"),
                ("index.md", "/docs/scheduler/dead-letters-and-cleanup/"),
            ]
        );

        Problems(site)
            .Should()
            .BeEquivalentTo([
                "how-to/resolve-a-dead-letter.md: /docs/scheduler/dead-letters is a live page (scheduler/dead-letters.md)",
                "how-to/resolve-a-dead-letter.md: /scheduler/old does not start with /docs/",
                "how-to/resolve-a-dead-letter.md: /docs/x#part carries an anchor or a query",
                "/docs/scheduler/dead-letters-and-cleanup is claimed more than once, by how-to/resolve-a-dead-letter.md, index.md",
            ]);
    }

    [Test]
    public void ARedirect_ResolvesToThePageClaimingIt()
    {
        var site = DocsSite.Of(
            ["contributing/writing-docs.md"],
            [("contributing/writing-docs.md", "/docs/reference/contributing-docs")]
        );

        site.Resolve("/docs/reference/contributing-docs")
            .Should()
            .Be("contributing/writing-docs.md");
        site.Resolve("/docs/reference/contributing-docs/")
            .Should()
            .Be("contributing/writing-docs.md");
        site.Resolve("/docs/contributing/writing-docs").Should().Be("contributing/writing-docs.md");
        site.Resolve("/docs/reference/nothing").Should().BeNull();
    }

    [Test]
    public void RedirectFrom_IsReadAsEitherListForm()
    {
        FrontMatter
            .Of("---\nredirect_from:\n  - /docs/a\n  - \"/docs/b\"\ntitle: X\n---\n")
            .List("redirect_from")
            .Should()
            .Equal("/docs/a", "/docs/b");
        FrontMatter
            .Of("---\nredirect_from: [/docs/a, '/docs/b']\n---\n")
            .List("redirect_from")
            .Should()
            .Equal("/docs/a", "/docs/b");
        FrontMatter
            .Of("---\nredirect_from: /docs/a\n---\n")
            .List("redirect_from")
            .Should()
            .Equal("/docs/a");
    }

    private static List<string> Problems(DocsSite site)
    {
        var problems = new List<string>();

        foreach (var (page, path) in site.RedirectClaims)
        {
            if (!path.StartsWith("/docs/", StringComparison.Ordinal))
                problems.Add($"{page}: {path} does not start with /docs/");
            else if (path.Contains('#') || path.Contains('?'))
                problems.Add($"{page}: {path} carries an anchor or a query");
            else if (DocsSite.FileFor(path) is { } file && site.Pages.Contains(file))
                problems.Add($"{page}: {path} is a live page ({file})");
        }

        problems.AddRange(
            site.RedirectClaims.GroupBy(c => c.Path.TrimEnd('/'), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g =>
                    $"{g.Key} is claimed more than once, by "
                    + string.Join(
                        ", ",
                        g.Select(c => c.Page).Distinct().Order(StringComparer.Ordinal)
                    )
                )
        );

        return problems;
    }
}
