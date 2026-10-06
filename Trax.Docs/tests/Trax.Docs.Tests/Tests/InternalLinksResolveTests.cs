using System.Globalization;
using System.Text;

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
/// <c>sync-docs.sh</c> does not publish are not link targets.</item>
/// <item><c>#anchor</c> matches a heading id on the target page, computed the way rehype-slug
/// (github-slugger) computes it.</item>
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

    private static readonly Regex AtxHeading = new(
        @"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.Compiled
    );

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
        var published = RepoRoot
            .MarkdownFiles()
            .Select(f => RepoRoot.Relative(f).Replace('\\', '/'))
            .Where(RepoRoot.IsPublished)
            .ToHashSet(StringComparer.Ordinal);

        var headingIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> IdsOf(string page)
        {
            if (!headingIds.TryGetValue(page, out var ids))
                headingIds[page] = ids = HeadingIds(File.ReadAllText(RepoRoot.Combine(page)));
            return ids;
        }

        var offenders = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            var isPublished = published.Contains(rel);

            foreach (var (line, target) in Links(File.ReadAllText(file)))
            {
                var problem = Check(rel, isPublished, target, published, IdsOf);
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
        Slug(PlainText(heading)).Should().Be(expected);

    [Test]
    public void HeadingIds_DeduplicateLikeGithubSlugger() =>
        HeadingIds("# Usage\n\n## Usage\n\n```\n# Usage\n```\n\n### Usage\n")
            .Should()
            .BeEquivalentTo(["usage", "usage-1", "usage-2"]);

    private static string? Check(
        string page,
        bool isPublished,
        string target,
        HashSet<string> published,
        Func<string, HashSet<string>> idsOf
    )
    {
        if (target.StartsWith('#'))
        {
            // A same-page anchor on an unpublished file (an ADR, a README) is rendered by
            // GitHub, not the site; only the site's pages are checked.
            if (!isPublished)
                return null;
            return idsOf(page).Contains(Uri.UnescapeDataString(target[1..]))
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

        string? file;
        if (path is "/docs" or "/docs/")
            file = "index.md";
        else if (path.StartsWith("/docs/", StringComparison.Ordinal))
            file = path["/docs/".Length..].TrimEnd('/') + ".md";
        else
            return null; // a site route outside the docs, such as /blog

        if (!published.Contains(file))
            return "no published page at this path";

        if (anchor is not null && !idsOf(file).Contains(Uri.UnescapeDataString(anchor)))
            return "no such heading on the target page";

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

    /// <summary>
    /// The ids rehype-slug gives a page's headings: github-slugger over each heading's text,
    /// numbered <c>-1</c>, <c>-2</c> on repeats.
    /// </summary>
    private static HashSet<string> HeadingIds(string content)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var start = 0;

        // Front matter is not part of the rendered page.
        if (lines.Length > 0 && lines[0] == "---")
        {
            var end = Array.IndexOf(lines, "---", 1);
            if (end > 0)
                start = end + 1;
        }

        for (var i = start; i < lines.Length; i++)
        {
            if (Fence.IsMatch(lines[i]))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            var m = AtxHeading.Match(lines[i]);
            if (!m.Success)
                continue;

            var slug = Slug(PlainText(m.Groups["text"].Value));
            var unique = slug;
            while (occurrences.ContainsKey(unique))
            {
                occurrences[slug]++;
                unique = $"{slug}-{occurrences[slug]}";
            }
            occurrences[unique] = 0;
            ids.Add(unique);
        }
        return ids;
    }

    /// <summary>A heading's rendered text: link and code markup dropped, their text kept.</summary>
    private static string PlainText(string heading)
    {
        var text = Regex.Replace(heading, @"!\[[^\]]*\]\([^)]*\)", "");
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"`([^`]*)`", "$1");
        text = Regex.Replace(text, @"\\(\p{P}|\p{S})", "$1");
        return text;
    }

    /// <summary>
    /// github-slugger: lowercase, drop everything but letters, marks, digits, connector
    /// punctuation, spaces and hyphens, then turn each space into a hyphen.
    /// </summary>
    private static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            var keep =
                c is ' ' or '-'
                || char.GetUnicodeCategory(c)
                    is UnicodeCategory.UppercaseLetter
                        or UnicodeCategory.LowercaseLetter
                        or UnicodeCategory.TitlecaseLetter
                        or UnicodeCategory.ModifierLetter
                        or UnicodeCategory.OtherLetter
                        or UnicodeCategory.NonSpacingMark
                        or UnicodeCategory.SpacingCombiningMark
                        or UnicodeCategory.EnclosingMark
                        or UnicodeCategory.DecimalDigitNumber
                        or UnicodeCategory.LetterNumber
                        or UnicodeCategory.OtherNumber
                        or UnicodeCategory.ConnectorPunctuation;
            if (keep)
                sb.Append(c == ' ' ? '-' : c);
        }
        return sb.ToString();
    }
}
