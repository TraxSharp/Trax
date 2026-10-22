using Trax.Skills.Publish;

namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every page under <c>migration-guides/</c> is linked from the <c>trax-upgrade</c> skill, as a
/// full <c>https://traxsharp.net/docs/migration-guides/&lt;slug&gt;</c> URL.
///
/// <para>The skill is how an agent bumping Trax in someone else's repository finds the guides
/// between two versions. It lists them by hand, with the release each applies from, so a guide
/// added without a line there is one the agent never reads, and the upgrade it describes is
/// skipped without anyone noticing. The section index, <c>migration-guides.md</c>, sits beside
/// the folder rather than in it, so it is not a guide here.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md"
)]
[TestFixture]
public class SkillsCoverMigrationGuidesTests
{
    private const string Adr =
        "Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md";

    private const string Skill = "skills/trax-upgrade/SKILL.md";

    private const string Prefix = "https://traxsharp.net/docs/migration-guides/";

    [Test]
    public void EveryMigrationGuide_IsLinkedFromTheUpgradeSkill()
    {
        var guides = Guides().ToList();
        guides
            .Should()
            .NotBeEmpty("the guard reads migration-guides/; finding none means it checks nothing");

        var linked = Linked(File.ReadAllText(RepoRoot.Combine(Skill.Split('/'))));
        var missing = guides.Where(g => !linked.Contains(g)).ToList();

        missing
            .Should()
            .BeEmpty(
                $"{Skill} is how an agent upgrading an app finds the guides between two versions, "
                    + $"so it links every guide as {Prefix}<slug>, with the release it applies "
                    + "from (or the condition, for a database procedure). See "
                    + Adr
                    + ". Not linked:\n  "
                    + string.Join("\n  ", missing.Select(g => $"migration-guides/{g}.md"))
            );
    }

    [TestCase(
        "[Guide](https://traxsharp.net/docs/migration-guides/hotchocolate-16)",
        "hotchocolate-16"
    )]
    [TestCase(
        "[Guide](https://traxsharp.net/docs/migration-guides/install-pg-trgm#1-check)",
        "install-pg-trgm"
    )]
    public void Linked_ReadsTheSlug(string markdown, string slug) =>
        Linked(markdown).Should().Contain(slug);

    [TestCase("[Guide](/docs/migration-guides/hotchocolate-16)")]
    [TestCase("`https://traxsharp.net/docs/migration-guides/hotchocolate-16`")]
    [TestCase("[Upgrading](https://traxsharp.net/docs/migration-guides)")]
    public void Linked_IgnoresWhatIsNotAFullLinkToAGuide(string markdown) =>
        Linked(markdown).Should().BeEmpty();

    /// <summary>The slug of every page under <c>migration-guides/</c>, in ordinal order.</summary>
    private static IEnumerable<string> Guides()
    {
        var folder = RepoRoot.Combine("migration-guides");
        return Directory.Exists(folder)
            ? Directory
                .EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Order(StringComparer.Ordinal)
            : [];
    }

    /// <summary>The guide slugs <paramref name="markdown"/> links to, anchors dropped.</summary>
    private static HashSet<string> Linked(string markdown) =>
        SkillRules
            .Links(markdown)
            .Where(t => t.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(t => t[Prefix.Length..].Split('#')[0])
            .Where(slug => slug.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}
