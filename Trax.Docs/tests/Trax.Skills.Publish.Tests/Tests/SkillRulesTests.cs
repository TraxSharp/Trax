namespace Trax.Skills.Publish.Tests.Tests;

[TestFixture]
public class SkillRulesTests
{
    private static IReadOnlyList<string> Problems(TempSkills skills) =>
        [.. SkillRules.Check(skills.Skills).Select(p => p.ToString())];

    [Test]
    public void AWellFormedSkill_HasNoProblems()
    {
        using var skills = new TempSkills()
            .Skill("trax-write-a-train", body: "See [the layout](layout.md#top).\n")
            .File_("trax-write-a-train/layout.md");

        Problems(skills).Should().BeEmpty();
    }

    [Test]
    public void ASkillWithoutTheInternalMarker_IsRefused()
    {
        // Without it, `npx skills add TraxSharp/Trax` serves the skill from main: the CLI's
        // recursive fallback finds Trax.Docs/skills/ (skills 1.7.4, 2026-10-20).
        using var skills = new TempSkills().Skill("trax", metadata: "");

        Problems(skills).Should().ContainSingle().Which.Should().Contain("internal: true");
    }

    [Test]
    public void AStampedVersionInTheSource_IsRefused()
    {
        using var skills = new TempSkills().Skill(
            "trax",
            metadata: "metadata:\n  internal: true\n  trax-version: \"1.0.0\"\n"
        );

        Problems(skills).Should().ContainSingle().Which.Should().Contain("the release writes it");
    }

    [Test]
    public void ANameThatIsNotTheFolder_IsRefused()
    {
        using var skills = new TempSkills().Skill("trax", name: "trax-core");

        Problems(skills)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("but the folder is 'trax'");
    }

    [TestCase("Trax")]
    [TestCase("trax_core")]
    [TestCase("trax--core")]
    public void ANameOutsideTheSpec_IsRefused(string folder)
    {
        using var skills = new TempSkills().Skill(folder);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("lowercase words");
    }

    [Test]
    public void AMissingOrOverlongDescription_IsRefused()
    {
        using var skills = new TempSkills()
            .Skill("a", description: "\"\"")
            .Skill("b", description: new string('x', SkillRules.MaxDescriptionLength + 1))
            .Skill("c", description: "Use for <train> work.");

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().StartWith("a: ").And.Contain("no 'description'"),
                b => b.Should().StartWith("b: ").And.Contain("the limit is"),
                c => c.Should().StartWith("c: ").And.Contain("'<' or '>'")
            );
    }

    [Test]
    public void AnUnknownKey_IsRefused()
    {
        using var skills = new TempSkills().Skill(
            "trax",
            metadata: "allowed-tools: Bash\nmetadata:\n  internal: true\n  owner: me\n"
        );

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().Contain("key 'allowed-tools'"),
                b => b.Should().Contain("metadata key 'owner'")
            );
    }

    [Test]
    public void AScriptInASkill_IsRefused()
    {
        using var skills = new TempSkills().Skill("trax").File_("trax/scripts/setup.sh");

        Problems(skills).Should().ContainSingle().Which.Should().Contain("never a script");
    }

    [Test]
    public void AFileOutsideAnySkill_IsRefused()
    {
        using var skills = new TempSkills().Skill("trax").File_("notes.md");

        Problems(skills).Should().ContainSingle().Which.Should().StartWith("notes.md: ");
    }

    [Test]
    public void ALinkThatIsMissingOrLeavesTheSkill_IsRefused()
    {
        using var skills = new TempSkills()
            .Skill(
                "trax",
                body: "[a](missing.md) [b](../other/SKILL.md) [c](https://traxsharp.net/docs) [d](#x)\n"
            )
            .Skill("other");

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().Contain("'missing.md' names a file that does not exist"),
                b => b.Should().Contain("leaves the skill's folder")
            );
    }

    [Test]
    public void ALinkInsideAFence_IsNotALink()
    {
        using var skills = new TempSkills().Skill("trax", body: "```md\n[a](missing.md)\n```\n");

        Problems(skills).Should().BeEmpty();
    }

    [Test]
    public void BrokenFrontmatter_IsReportedAndStopsTheOtherRules()
    {
        using var skills = new TempSkills().Skill("trax", metadata: "  stray: indent\n");

        Problems(skills).Should().ContainSingle().Which.Should().Contain("frontmatter:");
    }
}
