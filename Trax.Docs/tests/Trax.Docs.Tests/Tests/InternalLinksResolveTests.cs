namespace Trax.Docs.Tests.Tests;

[TestFixture]
public class InternalLinksResolveTests
{
    // Matches markdown links to a `/docs/...` path: [text](/docs/path) or [text](/docs/path#anchor).
    private static readonly Regex DocsLink = new(
        @"\]\(\<?(/docs/(?<path>[^)\s#""]+))(?<anchor>#[^)""\s]+)?\>?\)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pre-existing broken links flagged as tech debt. Each entry is `<source>:<line> -> /docs/<target>`.
    /// New broken links must NOT be added; fix or remove them. To clear an entry, fix the link in source
    /// (point at a real page, or remove the link) and delete the entry here. Format must match exactly
    /// the offender format used by the test below.
    /// </summary>
    private static readonly HashSet<string> KnownBrokenLinks = new(StringComparer.Ordinal)
    {
        // api-graphql-client.md SDK Reference block points at /docs/sdk-reference/graphql-client/*
        // pages that have not been written yet (the SDK reference for the GraphQL client is still
        // tracked under sdk-reference/graphql-api/). Until those pages exist, exempt them here so
        // CI doesn't block on tech debt that pre-dates this lint.
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/add-trax-graphql-client",
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/builder",
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/validate-assemblies",
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/i-graphql-client-request",
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/graphql-resource-request",
        "api-graphql-client.md:187 -> /docs/sdk-reference/graphql-client/response-strictness",
        // The scheduler SDK reference lives at sdk-reference/scheduler-api/ now; this link was not
        // updated when the page was reorganized.
        "cross-cutting/e2e-testing.md:225 -> /docs/sdk-reference/scheduler/scheduler-configuration-builder",
    };

    [Test]
    public void Every_InternalDocsLink_ResolvesTo_ExistingFile()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            if (rel.EndsWith("InternalLinksResolveTests.cs", StringComparison.Ordinal))
                continue;

            var content = File.ReadAllText(file);
            var lines = content.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in DocsLink.Matches(lines[i]))
                {
                    var path = m.Groups["path"].Value;
                    if (ResolvesToMarkdown(path))
                        continue;

                    var offender = $"{rel}:{i + 1} -> /docs/{path}";
                    if (KnownBrokenLinks.Contains(offender))
                        continue;

                    offenders.Add(offender);
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Every `/docs/path/to/file` link in Trax.Docs markdown must resolve to a real markdown "
                    + "file in the repo. `/docs/foo/bar` maps to `foo/bar.md` (or `foo/bar/index.md`) "
                    + "from the repo root. Broken links surface as 404s on traxsharp.net once Vercel "
                    + "rebuilds the site. If a link is intentionally broken (planned page, tech debt), "
                    + "add it to KnownBrokenLinks with a justification. New offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static bool ResolvesToMarkdown(string docsPath)
    {
        var normalized = docsPath.TrimEnd('/');

        var candidate = Path.Combine(RepoRoot.Path, normalized + ".md");
        if (File.Exists(candidate))
            return true;

        candidate = Path.Combine(RepoRoot.Path, normalized, "index.md");
        if (File.Exists(candidate))
            return true;

        return false;
    }
}
