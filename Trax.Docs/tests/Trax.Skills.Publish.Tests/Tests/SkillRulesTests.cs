namespace Trax.Skills.Publish.Tests.Tests;

[TestFixture]
public class SkillRulesTests
{
    private static IReadOnlyList<string> Problems(TempSkills skills) =>
        [.. SkillRules.Check(skills.Skills).Select(p => p.ToString())];

    [Test]
    public async Task AWellFormedSkill_HasNoProblems()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync("trax-write-a-train", body: "See [the layout](layout.md#top).\n")
            .ConfigureAwait(false);
        await skills.FileAsync("trax-write-a-train/layout.md").ConfigureAwait(false);

        Problems(skills).Should().BeEmpty();
    }

    [Test]
    public async Task ASkillWithoutTheInternalMarker_IsRefused()
    {
        // Without it, `npx skills add TraxSharp/Trax` serves the skill from main: the CLI's
        // recursive fallback finds Trax.Docs/skills/ (skills 1.7.4, 2026-10-20).
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax", metadata: "").ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("internal: true");
    }

    [Test]
    public async Task AStampedVersionInTheSource_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync(
                "trax",
                metadata: "metadata:\n  internal: true\n  trax-version: \"1.0.0\"\n"
            )
            .ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("the release writes it");
    }

    [Test]
    public async Task ANameThatIsNotTheFolder_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax", name: "trax-core").ConfigureAwait(false);

        Problems(skills)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("but the folder is 'trax'");
    }

    [TestCase("Trax")]
    [TestCase("trax_core")]
    [TestCase("trax--core")]
    public async Task ANameOutsideTheSpec_IsRefused(string folder)
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync(folder).ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("lowercase words");
    }

    [Test]
    public async Task AMissingOrOverlongDescription_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("a", description: "\"\"").ConfigureAwait(false);
        await skills
            .SkillAsync("b", description: new string('x', SkillRules.MaxDescriptionLength + 1))
            .ConfigureAwait(false);
        await skills.SkillAsync("c", description: "Use for <train> work.").ConfigureAwait(false);

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().StartWith("a: ").And.Contain("no 'description'"),
                b => b.Should().StartWith("b: ").And.Contain("the limit is"),
                c => c.Should().StartWith("c: ").And.Contain("'<' or '>'")
            );
    }

    [Test]
    public async Task AnUnknownKey_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync(
                "trax",
                metadata: "allowed-tools: Bash\nmetadata:\n  internal: true\n  owner: me\n"
            )
            .ConfigureAwait(false);

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().Contain("key 'allowed-tools'"),
                b => b.Should().Contain("metadata key 'owner'")
            );
    }

    [Test]
    public async Task AScriptInASkill_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        await skills.FileAsync("trax/scripts/setup.sh").ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("never a script");
    }

    [Test]
    public async Task ALinkedFileInASkill_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        skills.Link("trax/extra.md", (await skills.OutsideAsync().ConfigureAwait(false)).File);

        Problems(skills)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                "trax: 'extra.md' is a symbolic link. A published skill ships its own files, never what a link points at"
            );
    }

    [Test]
    public async Task ALinkedFolderInASkill_IsRefused_AndNotEntered()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        var outside = (await skills.OutsideAsync().ConfigureAwait(false)).Folder;
        await File.WriteAllTextAsync(Path.Combine(outside, "run.sh"), "echo").ConfigureAwait(false);
        skills.Link("trax/more", outside, folder: true);

        Problems(skills)
            .Should()
            .ContainSingle("the folder is reported once and never entered, so run.sh is not")
            .Which.Should()
            .Contain("'more' is a symbolic link");
    }

    [Test]
    public async Task ALinkedSkillFolder_IsRefused()
    {
        using var source = await TempSkills.CreateAsync().ConfigureAwait(false);
        await source.SkillAsync("trax").ConfigureAwait(false);
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        skills.Link("trax", Path.Combine(source.Skills, "trax"), folder: true);

        Problems(skills)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("trax: the skill's folder is a symbolic link");
    }

    [Test]
    public async Task AFileOutsideAnySkill_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax").ConfigureAwait(false);
        await skills.FileAsync("notes.md").ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().StartWith("notes.md: ");
    }

    [Test]
    public async Task ALinkThatIsMissingOrLeavesTheSkill_IsRefused()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync(
                "trax",
                body: "[a](missing.md) [b](../other/SKILL.md) [c](https://traxsharp.net/docs) [d](#x)\n"
            )
            .ConfigureAwait(false);
        await skills.SkillAsync("other").ConfigureAwait(false);

        Problems(skills)
            .Should()
            .SatisfyRespectively(
                a => a.Should().Contain("'missing.md' names a file that does not exist"),
                b => b.Should().Contain("leaves the skill's folder")
            );
    }

    [Test]
    public async Task ALinkInsideAFence_IsNotALink()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills
            .SkillAsync("trax", body: "```md\n[a](missing.md)\n```\n")
            .ConfigureAwait(false);

        Problems(skills).Should().BeEmpty();
    }

    [Test]
    public async Task BrokenFrontmatter_IsReportedAndStopsTheOtherRules()
    {
        using var skills = await TempSkills.CreateAsync().ConfigureAwait(false);
        await skills.SkillAsync("trax", metadata: "  stray: indent\n").ConfigureAwait(false);

        Problems(skills).Should().ContainSingle().Which.Should().Contain("frontmatter:");
    }
}
