using System.Globalization;
using System.Text.Json.Nodes;

namespace Trax.Skills.Publish.Tests.Tests;

[TestFixture]
public class StagerTests
{
    [Test]
    public async Task Stage_StampsTheVersion_AndDropsTheInternalMarker()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync("trax", body: "# Trax\n\nRead [more](more.md).\n")
            .ConfigureAwait(false);
        await skills.FileAsync("trax/more.md", "more").ConfigureAwait(false);

        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .BeEmpty();

        var staged = await File.ReadAllTextAsync(
                Path.Combine(skills.Out, "skills", "trax", "SKILL.md")
            )
            .ConfigureAwait(false);
        staged
            .Should()
            .Be(
                "---\nname: trax\ndescription: Use when writing a train in an app built on Trax.\n"
                    + "metadata:\n  trax-version: \"1.70.0\"\n---\n# Trax\n\nRead [more](more.md).\n"
            );
        (
            await File.ReadAllTextAsync(Path.Combine(skills.Out, "skills", "trax", "more.md"))
                .ConfigureAwait(false)
        )
            .Should()
            .Be("more");
        File.Exists(Path.Combine(skills.Out, "LICENSE")).Should().BeTrue();
    }

    [Test]
    public async Task Stage_WritesThePluginAtTheReleaseVersion_AndNoMarketplace()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        await skills.SkillAsync("trax-upgrade").ConfigureAwait(false);

        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .BeEmpty();

        var plugin = JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(skills.Out, ".claude-plugin", "plugin.json"))
                .ConfigureAwait(false)
        );
        Text(plugin, "name").Should().Be("trax");
        Text(plugin, "version").Should().Be("1.70.0");
        File.Exists(Path.Combine(skills.Out, ".claude-plugin", "marketplace.json"))
            .Should()
            .BeFalse("the marketplace lives on main and points at the branch");

        (await File.ReadAllTextAsync(Path.Combine(skills.Out, "README.md")).ConfigureAwait(false))
            .Should()
            .Contain("- `trax`\n- `trax-upgrade`")
            .And.Contain("tree/skills-v1.70.0")
            .And.Contain("/plugin install trax@traxsharp");
    }

    [Test]
    public void TheMarketplace_PointsTheTraxPluginAtTheSkillsBranch()
    {
        var market = Stager.MarketplaceManifest();

        Text(market, "name").Should().Be("traxsharp");
        var source = At(At(At(market, "plugins"), 0), "source");
        Text(source, "source").Should().Be("github");
        Text(source, "repo").Should().Be("TraxSharp/Trax");
        Text(source, "ref").Should().Be("skills");
    }

    [Test]
    public async Task Stage_RefusesASkillThatBreaksARule_AndWritesNothing()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax", metadata: "").ConfigureAwait(false);

        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle();
        Directory.Exists(skills.Out).Should().BeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Stage_RefusesALinkInASkill_AndStagesNothingItPointsAt(bool folder)
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        var (file, dir) = (await skills.OutsideAsync().ConfigureAwait(false));
        skills.Link(folder ? "trax/more" : "trax/extra.md", folder ? dir : file, folder);

        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("is a symbolic link");
        Directory.Exists(skills.Out).Should().BeFalse();
    }

    [TestCase("v1.70.0")]
    [TestCase("1.70")]
    [TestCase("main")]
    public async Task Stage_RefusesAVersionThatIsNotARelease(string version)
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);

        (
            await Stager
                .StageAsync(skills.Skills, version, skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("not a release version");
    }

    [Test]
    public async Task Stage_RefusesNoSkills_AndANonEmptyOutput()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("no skills");

        await skills.SkillAsync("trax").ConfigureAwait(false);
        Directory.CreateDirectory(skills.Out);
        await File.WriteAllTextAsync(Path.Combine(skills.Out, "stale"), "").ConfigureAwait(false);
        (
            await Stager
                .StageAsync(skills.Skills, "1.70.0", skills.License, skills.Out)
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("not empty");
    }

    private static JsonNode At(JsonNode? node, string key) =>
        node?[key] ?? throw new AssertionException($"The JSON has no '{key}'.");

    private static JsonNode At(JsonNode? node, int index) =>
        node?[index]
        ?? throw new AssertionException(
            string.Create(CultureInfo.InvariantCulture, $"The JSON has no item {index}.")
        );

    private static string Text(JsonNode? node, string key) => At(node, key).GetValue<string>();
}
