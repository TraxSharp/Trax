using System.Text.Json.Nodes;

namespace Trax.Skills.Publish.Tests.Tests;

[TestFixture]
public class StagerTests
{
    [Test]
    public void Stage_StampsTheVersion_AndDropsTheInternalMarker()
    {
        using var skills = new TempSkills()
            .Skill("trax", body: "# Trax\n\nRead [more](more.md).\n")
            .File_("trax/more.md", "more");

        Stager.Stage(skills.Skills, "1.70.0", skills.License, skills.Out).Should().BeEmpty();

        var staged = File.ReadAllText(Path.Combine(skills.Out, "skills", "trax", "SKILL.md"));
        staged
            .Should()
            .Be(
                "---\nname: trax\ndescription: Use when writing a train in an app built on Trax.\n"
                    + "metadata:\n  trax-version: \"1.70.0\"\n---\n# Trax\n\nRead [more](more.md).\n"
            );
        File.ReadAllText(Path.Combine(skills.Out, "skills", "trax", "more.md")).Should().Be("more");
        File.Exists(Path.Combine(skills.Out, "LICENSE")).Should().BeTrue();
    }

    [Test]
    public void Stage_WritesManifestsAtTheReleaseVersion()
    {
        using var skills = new TempSkills().Skill("trax").Skill("trax-upgrade");

        Stager.Stage(skills.Skills, "1.70.0", skills.License, skills.Out).Should().BeEmpty();

        var plugin = JsonNode.Parse(
            File.ReadAllText(Path.Combine(skills.Out, ".claude-plugin", "plugin.json"))
        )!;
        plugin["name"]!.GetValue<string>().Should().Be("trax");
        plugin["version"]!.GetValue<string>().Should().Be("1.70.0");

        var market = JsonNode.Parse(
            File.ReadAllText(Path.Combine(skills.Out, ".claude-plugin", "marketplace.json"))
        )!;
        market["name"]!.GetValue<string>().Should().Be("traxsharp");
        market["plugins"]![0]!["source"]!.GetValue<string>().Should().Be("./");
        market["plugins"]![0]!["version"]!.GetValue<string>().Should().Be("1.70.0");

        File.ReadAllText(Path.Combine(skills.Out, "README.md"))
            .Should()
            .Contain("- `trax`\n- `trax-upgrade`")
            .And.Contain("tree/v1.70.0/skills/");
    }

    [Test]
    public void Stage_RefusesASkillThatBreaksARule_AndWritesNothing()
    {
        using var skills = new TempSkills().Skill("trax", metadata: "");

        Stager.Stage(skills.Skills, "1.70.0", skills.License, skills.Out).Should().ContainSingle();
        Directory.Exists(skills.Out).Should().BeFalse();
    }

    [TestCase("v1.70.0")]
    [TestCase("1.70")]
    [TestCase("main")]
    public void Stage_RefusesAVersionThatIsNotARelease(string version)
    {
        using var skills = new TempSkills().Skill("trax");

        Stager
            .Stage(skills.Skills, version, skills.License, skills.Out)
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("not a release version");
    }

    [Test]
    public void Stage_RefusesNoSkills_AndANonEmptyOutput()
    {
        using var skills = new TempSkills();
        Stager
            .Stage(skills.Skills, "1.70.0", skills.License, skills.Out)
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("no skills");

        skills.Skill("trax");
        Directory.CreateDirectory(skills.Out);
        File.WriteAllText(Path.Combine(skills.Out, "stale"), "");
        Stager
            .Stage(skills.Skills, "1.70.0", skills.License, skills.Out)
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("not empty");
    }
}
