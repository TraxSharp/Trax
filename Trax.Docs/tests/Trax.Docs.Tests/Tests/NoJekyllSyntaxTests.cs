namespace Trax.Docs.Tests.Tests;

/// <summary>
/// No leftover Jekyll template syntax in the documentation.
///
/// <para>The site is rendered by Next.js, not Jekyll. A surviving {{ site.baseurl }} or {% link %}
/// renders as literal text on traxsharp.net.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public partial class NoJekyllSyntaxTests
{
    private static readonly (string Description, Regex Pattern)[] BannedPatterns =
    [
        ("{{ site.baseurl }} (Jekyll baseurl template)", SiteBaseUrl),
        ("{% link ... %} (Jekyll link tag)", LinkTag),
        ("{% include ... %} (Jekyll include tag)", IncludeTag),
        ("{: .classname } (kramdown IAL)", KramdownAttributes),
    ];

    [GeneratedRegex(
        @"\{\{\s*site\.baseurl\s*\}\}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex SiteBaseUrl { get; }

    [GeneratedRegex(@"\{%\s*link\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkTag { get; }

    [GeneratedRegex(@"\{%\s*include\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IncludeTag { get; }

    [GeneratedRegex(
        @"\{:\s*\.[A-Za-z0-9_-]+\s*\}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex KramdownAttributes { get; }

    [Test]
    public void DocsMarkdown_ContainsNo_JekyllOrKramdownSyntax()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            // Self-reference exclusion: the lint test source mentions these patterns in strings.
            if (rel.EndsWith("NoJekyllSyntaxTests.cs", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllText(file)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (desc, pattern) in BannedPatterns)
                {
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add($"{rel}:{i + 1}  -> {desc}: {lines[i].Trim()}");
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Documentation Link Format requires plain Markdown — the docs are rendered "
                    + "by Trax.Website (Next.js/React), not Jekyll. Use direct `/docs/path/to/file` "
                    + "links instead of `{{ site.baseurl }}{% link %}` or `{: .classname }` IALs. "
                    + "See Trax.Docs/adr/0008-documentation-conventions-are-linted.md. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
