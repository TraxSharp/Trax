namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class LifecycleGuardTests
{
    private static readonly AdrSpec First = Sample.DefaultSpec;

    private static readonly AdrSpec Second = new(
        "0002",
        "tests-synchronise-on-a-signal",
        "Tests synchronise on a signal",
        ["testing"],
        ["effect"]
    );

    private static IReadOnlyList<Adr> Load(TempAdrRepo repo) => AdrCorpus.Discover(repo.Options());

    #region Status section

    [Test]
    public void StatusSection_AgreeingWithFrontmatter_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        LifecycleGuards.StatusSection(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void StatusSection_Missing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            First.FileName,
            Sample.Adr().Replace("## Status\n\n**Accepted.**\n\n", string.Empty)
        );

        LifecycleGuards
            .StatusSection(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '## Status' section");
    }

    [Test]
    public void StatusSection_DisagreeingWithFrontmatter_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(First.FileName, Sample.Adr(status: "proposed", statusSection: "**Accepted.**"));

        LifecycleGuards
            .StatusSection(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must open with '**Proposed.**'");
    }

    [Test]
    public void StatusSection_WithProseBeforeTheStatus_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(First.FileName, Sample.Adr(statusSection: "After some thought, **Accepted.**"));

        LifecycleGuards.StatusSection(Load(repo)).Offenders.Should().ContainSingle();
    }

    [Test]
    public void StatusSection_WithTrailingExplanation_Passes()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            First.FileName,
            Sample.Adr(
                statusSection: "**Accepted.** Recorded after the fact, so no moment weighed it."
            )
        );

        LifecycleGuards.StatusSection(Load(repo)).Passed.Should().BeTrue();
    }

    #endregion

    #region Supersession

    private static TempAdrRepo Superseded(
        string? oldStatusSection = null,
        string? newStatusSection = null,
        string oldStatus = "superseded-by-0002"
    )
    {
        var repo = new TempAdrRepo();
        repo.Adr(
            First.FileName,
            Sample.Adr(
                spec: First,
                status: oldStatus,
                statusSection: oldStatusSection
                    ?? $"**Superseded by** {Second.Link}. The rule moved."
            )
        );
        repo.Adr(
            Second.FileName,
            Sample.Adr(
                spec: Second,
                statusSection: newStatusSection
                    ?? $"**Accepted.** Supersedes {First.Link}, which was too broad."
            )
        );
        repo.Index(Sample.Index(central: false, First, Second));
        return repo;
    }

    [Test]
    public void Supersessions_WrittenFromBothSides_Passes()
    {
        using var repo = Superseded();

        var result = LifecycleGuards.Supersessions(Load(repo));

        result.Offenders.Should().BeEmpty();
        result.Passed.Should().BeTrue();
    }

    [Test]
    public void Supersessions_WithNoForwardLink_Fails()
    {
        using var repo = Superseded(oldStatusSection: "**Superseded by** a later decision.");

        LifecycleGuards
            .Supersessions(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must link forward");
    }

    [Test]
    public void Supersessions_WhereTheSupersederSaysNothing_Fails()
    {
        using var repo = Superseded(newStatusSection: "**Accepted.**");

        LifecycleGuards
            .Supersessions(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not say so");
    }

    [Test]
    public void Supersessions_NamingAnAdrThatDoesNotExist_Fails()
    {
        using var repo = Superseded(oldStatus: "superseded-by-0009");

        LifecycleGuards
            .Supersessions(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no ADR with that number exists");
    }

    [Test]
    public void Supersessions_WhenThereAreNone_InspectsTheCorpusRatherThanNothing()
    {
        using var repo = TempAdrRepo.Valid();

        var result = LifecycleGuards.Supersessions(Load(repo));

        result.Offenders.Should().BeEmpty();
        result
            .Passed.Should()
            .BeTrue("a corpus with no supersessions has nothing to check, which is not a failure");
    }

    #endregion

    #region Changelog

    [Test]
    public void Changelog_WithOneEntry_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        LifecycleGuards.Changelog(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void Changelog_NewestFirst_Passes()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            First.FileName,
            Sample.Adr(
                changelog: "- **2026-09-11**: Narrowed the scope.\n- **2026-08-25**: Recorded."
            )
        );

        LifecycleGuards.Changelog(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void Changelog_OldestFirst_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            First.FileName,
            Sample.Adr(
                changelog: "- **2026-08-25**: Recorded.\n- **2026-09-11**: Narrowed the scope."
            )
        );

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("not newest first");
    }

    [Test]
    public void Changelog_Missing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(First.FileName, Sample.Adr().Split("## Changelog")[0]);

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '## Changelog' section");
    }

    [Test]
    public void Changelog_Empty_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(First.FileName, Sample.Adr(changelog: string.Empty));

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("has no entries");
    }

    [TestCase("- 2026-09-11: Recorded.")]
    [TestCase("- **2026-09-11** Recorded.")]
    [TestCase("- **11-09-2026**: Recorded.")]
    [TestCase("- **2026-09-11**:")]
    public void Changelog_MalformedEntry_Fails(string entry)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(First.FileName, Sample.Adr(changelog: entry));

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("malformed changelog");
    }

    #endregion
}
