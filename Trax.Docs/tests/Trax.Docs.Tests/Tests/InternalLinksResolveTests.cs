namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every internal link resolves to a page that exists, and every anchor to a heading on it.
///
/// <para>The failure mode this exists for is invisible in a diff: a link to a page nobody wrote
/// reads exactly like a working one, and ships as a 404.</para>
///
/// <para>Three rules, all modelled on how traxsharp.net builds the docs:</para>
/// <list type="bullet">
/// <item>A link on a published page is site-absolute (<c>/docs/...</c>), a same-page anchor
/// (<c>#...</c>), or carries a scheme. The site renders every other href as an external link
/// that the browser resolves against the page URL, so <c>delayed-jobs.md</c> on
/// <c>/docs/scheduler</c> lands on the raw markdown at <c>/docs/delayed-jobs.md</c> or a 404.</item>
/// <item><c>/docs/x</c> resolves to <c>x.md</c> and nothing else (bare <c>/docs</c> is
/// <c>index.md</c>). The site's slug is the file path minus <c>.md</c>; a nested
/// <c>x/index.md</c> is served at <c>/docs/x/index</c>, not <c>/docs/x</c>. Files that
/// <c>sync-docs.sh</c> does not publish are not link targets. A path a page claims in its
/// <c>redirect_from</c> front matter resolves to that page, because the site redirects it; new
/// links should still name the page itself.</item>
/// <item><c>#anchor</c> matches a heading id on the target page, computed the way rehype-slug
/// (github-slugger) computes it. Through a redirect, it is checked against the page the
/// redirect lands on.</item>
/// </list>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class InternalLinksResolveTests
{
    // An inline markdown link or image target: [text](target) or [text](<target> "title").
    private static readonly Regex Link = new(
        @"\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+""[^""]*"")?\s*\)",
        RegexOptions.Compiled
    );

    private static readonly Regex Scheme = new(
        @"^[a-zA-Z][a-zA-Z0-9+.\-]*:",
        RegexOptions.Compiled
    );

    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);

    private static readonly Regex InlineCode = new(@"`[^`]*`", RegexOptions.Compiled);

    /// <summary>
    /// Pre-existing broken links, keyed by <c>page -> target</c> (an offender line without its
    /// line number and reason), so editing a page does not move its key. Each entry carries a
    /// comment giving the reason. New
    /// broken links must NOT be added; fix the link instead. An entry that no longer matches a
    /// broken link fails the build, so a fixed link takes its entry with it.
    /// </summary>
    private static readonly HashSet<string> KnownBrokenLinks = new(StringComparer.Ordinal) { };

    [Test]
    public void Every_InternalLink_ResolvesTo_ExistingPageAndHeading()
    {
        var site = DocsSite.Load();

        var offenders = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            var isPublished = site.Pages.Contains(rel);

            foreach (var (line, target) in Links(File.ReadAllText(file)))
            {
                var problem = Check(rel, isPublished, target, site);
                if (problem is null)
                    continue;

                var key = $"{rel} -> {target}";
                if (KnownBrokenLinks.Contains(key))
                {
                    allowed.Add(key);
                    continue;
                }

                offenders.Add($"{rel}:{line} -> {target} ({problem})");
            }
        }

        var stale = KnownBrokenLinks.Except(allowed).OrderBy(k => k, StringComparer.Ordinal);

        offenders
            .Should()
            .BeEmpty(
                "every link on a published Trax.Docs page must be `/docs/...`, `#anchor`, or carry "
                    + "a scheme; `/docs/foo/bar` must resolve to `foo/bar.md` from the repo root; "
                    + "and an `#anchor` must match a heading id on the target page (github-slugger, "
                    + "as rehype-slug renders it). Relative links resolve against the page URL on "
                    + "traxsharp.net and land on raw markdown or a 404. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. New offenders:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                "a KnownBrokenLinks entry that matches no broken link is stale: the link was "
                    + "fixed or removed, so delete the entry. Stale entries:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [TestCase("Getting Started", "getting-started")]
    [TestCase("`AddTraxGraphQLClient<TClient>()`", "addtraxgraphqlclienttclient")]
    [TestCase("What's new in 1.2?", "whats-new-in-12")]
    [TestCase("connection_init payloads", "connection_init-payloads")]
    [TestCase("See [the guide](/docs/x) now", "see-the-guide-now")]
    [TestCase("**Bold** and *em*", "bold-and-em")]
    [TestCase("A -- B", "a----b")]
    public void Slug_Matches_GithubSlugger(string heading, string expected) =>
        Headings.Slug(Headings.PlainText(heading)).Should().Be(expected);

    [Test]
    public void HeadingIds_DeduplicateLikeGithubSlugger() =>
        Headings
            .Ids("# Usage\n\n## Usage\n\n```\n# Usage\n```\n\n### Usage\n")
            .Should()
            .BeEquivalentTo(["usage", "usage-1", "usage-2"]);

    private static string? Check(string page, bool isPublished, string target, DocsSite site)
    {
        if (target.StartsWith('#'))
        {
            // A same-page anchor on an unpublished file (an ADR, a README) is rendered by
            // GitHub, not the site; only the site's pages are checked.
            if (!isPublished)
                return null;
            return site.HeadingIds(page).Contains(Uri.UnescapeDataString(target[1..]))
                ? null
                : "no such heading on this page";
        }

        if (Scheme.IsMatch(target) || target.StartsWith("//", StringComparison.Ordinal))
            return null;

        if (!target.StartsWith('/'))
            return isPublished ? "relative link; write it as /docs/..." : null;

        var hash = target.IndexOf('#');
        var path = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? null : target[(hash + 1)..];

        if (DocsSite.FileFor(path) is null)
            return null; // a site route outside the docs, such as /blog

        var file = site.Resolve(path);
        if (file is null)
            return "no published page at this path, and no page claims it in redirect_from";

        if (anchor is not null && !site.HeadingIds(file).Contains(Uri.UnescapeDataString(anchor)))
            return file == DocsSite.FileFor(path)
                ? "no such heading on the target page"
                : $"no such heading on {file}, where this path redirects";

        return null;
    }

    /// <summary>The links on a page with their line numbers, skipping code.</summary>
    private static IEnumerable<(int Line, string Target)> Links(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence.IsMatch(lines[i]))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            foreach (Match m in Link.Matches(InlineCode.Replace(lines[i], "")))
                yield return (i + 1, m.Groups["target"].Value);
        }
    }
}
