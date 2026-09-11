namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every concept page with code carries its SDK reference block.
///
/// <para>The block is how a reader gets from an example to the method-level documentation for the
/// calls it makes.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[TestFixture]
public class SdkReferenceBlockTests
{
    private static readonly Regex CodeBlock = new(
        @"^```",
        RegexOptions.Compiled | RegexOptions.Multiline
    );
    private static readonly Regex SdkReferenceHeading = new(
        @"^##\s+SDK Reference\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    /// <summary>
    /// Folders that are reference material and don't require an SDK Reference block.
    /// sdk-reference/ pages are themselves the SDK reference; index pages are tables of contents.
    /// </summary>
    private static readonly HashSet<string> ExemptPathPrefixes = new(StringComparer.Ordinal)
    {
        "sdk-reference/",
        "migration-guides/",
        // Neither of these is published to traxsharp.net (sync-docs.sh skips adr/, and
        // .claude/ is agent tooling), so neither has SDK pages to link to. An ADR's code
        // is illustrative, and the format spec's code is the ADR template itself.
        "adr/",
        ".claude/",
    };

    private static readonly HashSet<string> ExemptFiles = new(StringComparer.Ordinal)
    {
        "README.md",
        "index.md",
        "sdk-reference.md",
        "reference.md",
        "samples.md",
        // Top-level overview pages are tables of contents; their code blocks are quick-start
        // tasters. The detailed pages they link to carry the SDK Reference blocks.
        "getting-started.md",
        "core.md",
        "effect.md",
        "mediator.md",
        "scheduler.md",
        "dashboard.md",
        "api.md",
        "cross-cutting.md",
    };

    /// <summary>
    /// Pages that currently lack an SDK Reference block but pre-date this lint. Each entry must
    /// describe why the page is exempt OR is queued as tech debt. New pages MUST include the block.
    /// </summary>
    private static readonly HashSet<string> KnownExceptions = new(StringComparer.Ordinal)
    {
        // Reference / supplementary pages whose code blocks are CLI commands, shell snippets,
        // or release-tooling examples, not SDK calls. They have no SDK methods to link to.
        "reference/benchmarks.md",
        "reference/cli.md",
        "reference/comparison.md",
        "reference/migration.md",
        "reference/semantic-release.md",
        // Only code block is a `gh attestation verify` CLI command, no SDK calls.
        "supply-chain-security.md",
        // Convention pages whose code is directory trees, SQL, csproj fragments or test code
        // rather than SDK calls. There is no SDK method for a folder layout to link to.
        "reference/project-layout.md",
        "reference/test-conventions.md",
        "reference/writing-migrations.md",
        "reference/contributing-docs.md",
        // Tech debt: tracked for follow-up. These ARE concept pages with SDK code but the
        // SDK reference block was missed when authored. Fix by adding the block.
        "core/ide-extensions.md",
        "cross-cutting/di-registration.md",
        "effect/architecture.md",
    };

    [Test]
    public void Every_ConceptPage_WithCodeBlocks_Has_SdkReferenceSection()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');

            if (IsExempt(rel))
                continue;

            var content = File.ReadAllText(file);

            // Count fenced code blocks (pairs of ``` lines). Need an even number > 0.
            var fenceCount = CodeBlock.Matches(content).Count;
            if (fenceCount < 2)
                continue; // no fenced code blocks

            // Require an "## SDK Reference" heading somewhere in the page.
            if (!SdkReferenceHeading.IsMatch(content))
                offenders.Add(rel);
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > SDK Reference Blocks: every concept/guide page in Trax.Docs (outside "
                    + "sdk-reference/) that contains fenced code blocks must end with a "
                    + "`## SDK Reference` section listing the SDK methods used. The block links readers "
                    + "from the concept page to the canonical SDK reference. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. "
                    + "Pages missing the block:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static bool IsExempt(string relPath)
    {
        var fileName = Path.GetFileName(relPath);
        if (ExemptFiles.Contains(fileName))
            return true;

        if (KnownExceptions.Contains(relPath))
            return true;

        foreach (var prefix in ExemptPathPrefixes)
        {
            if (relPath.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
