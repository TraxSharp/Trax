using Trax.Skills.Publish;

namespace Trax.Docs.Tests.Tests;

/// <summary>
/// The agent skills under <c>skills/</c> are ready for a release to publish: each passes
/// <see cref="SkillRules"/>, and every docs page a skill sends an agent to exists.
///
/// <para>A skill is read by an agent in someone else's repository, so a link is only useful as
/// a full <c>https://traxsharp.net/docs/...</c> URL, and a link to a page that moved sends the
/// agent to a 404 at the moment it asked for the detail. The skill's own C# fences carry the
/// docs markers and are compiled by <c>CompiledSnippetsTests.cs</c> in Trax.Docs.Snippets.Tests,
/// like any page's.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md"
)]
[TestFixture]
public class SkillsAreWellFormedTests
{
    private const string Adr =
        "Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md";

    private const string Site = "https://traxsharp.net";

    private static string SkillsRoot => RepoRoot.Combine("skills");

    [Test]
    public void EverySkill_PassesTheReleaseRules()
    {
        var problems = SkillRules.Check(SkillsRoot).Select(p => p.ToString()).ToList();

        problems
            .Should()
            .BeEmpty(
                "a release stages these skills with the same rules and stops on the first broken "
                    + "one, so a skill that breaks them blocks the release. See "
                    + Adr
                    + ". Problems:\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Test]
    public void EveryDocsLink_InASkill_ResolvesToAPublishedPage()
    {
        var site = DocsSite.Load();
        var offenders = new List<string>();

        foreach (var file in SkillFiles())
        foreach (var target in SkillRules.Links(File.ReadAllText(file)))
        {
            var problem = Check(target, site);
            if (problem is not null)
                offenders.Add(
                    $"{RepoRoot.Relative(file).Replace('\\', '/')} -> {target} ({problem})"
                );
        }

        offenders
            .Should()
            .BeEmpty(
                "a skill is read in another repository, so a docs link is a full "
                    + $"{Site}/docs/... URL to a page this checkout publishes, with an anchor that "
                    + "matches a heading on it. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [TestCase("/docs/getting-started", "site-relative")]
    [TestCase("https://traxsharp.net/docs/no-such-page", "no published page")]
    [TestCase("https://traxsharp.net/docs/getting-started#no-such-heading", "no such heading")]
    [TestCase("http://traxsharp.net/docs/getting-started", "https")]
    public void Check_RefusesWhatAnAgentCannotFollow(string target, string problem) =>
        Check(target, DocsSite.Load()).Should().Contain(problem);

    [TestCase("https://traxsharp.net/docs/getting-started")]
    [TestCase("https://traxsharp.net/roadmap")]
    [TestCase("https://github.com/TraxSharp/Trax")]
    [TestCase("more.md")]
    [TestCase("#steps")]
    public void Check_AcceptsWhatAnAgentCanFollow(string target) =>
        Check(target, DocsSite.Load()).Should().BeNull();

    private static IEnumerable<string> SkillFiles() =>
        Directory.Exists(SkillsRoot)
            ? Directory
                .EnumerateFiles(SkillsRoot, "*.md", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
            : [];

    private static string? Check(string target, DocsSite site)
    {
        if (target.StartsWith('/'))
            return "site-relative; an agent outside traxsharp.net cannot follow it, write "
                + Site
                + target;
        if (target.StartsWith("http://traxsharp.net", StringComparison.Ordinal))
            return "use https";
        if (!target.StartsWith(Site + "/docs", StringComparison.Ordinal))
            return null;

        var path = target[Site.Length..];
        var hash = path.IndexOf('#');
        var anchor = hash < 0 ? null : path[(hash + 1)..];
        var file = site.Resolve(hash < 0 ? path : path[..hash]);
        if (file is null)
            return "no published page at this path";
        if (anchor is not null && !site.HeadingIds(file).Contains(Uri.UnescapeDataString(anchor)))
            return $"no such heading on {file}";
        return null;
    }
}
