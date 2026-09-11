namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class HygieneGuardTests
{
    private static IReadOnlyList<Adr> Load(TempAdrRepo repo) => AdrCorpus.Discover(repo.Options());

    [Test]
    public void NoEmDashes_CleanCorpus_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        HygieneGuards.NoEmDashes(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void NoEmDashes_InAnAdr_Fails_WithFileAndLine()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(statusSection: "**Accepted.** The rule holds — everywhere.")
        );

        HygieneGuards
            .NoEmDashes(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("docs/adr/0001-tests-are-deterministic.md:");
    }

    [Test]
    public void NoEmDashes_InTheIndex_Fails_BecauseTheIndexIsPartOfTheCorpus()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Index(
            Sample.Index(central: false, Sample.DefaultSpec) + "\nA trailing note — with a dash.\n"
        );

        HygieneGuards
            .NoEmDashes(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("README.md:");
    }

    [Test]
    public void NoEmDashes_DoesNotFlagAHyphenOrEnDash()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(statusSection: "**Accepted.** Use --locked-mode, and see pages 3–5.")
        );

        HygieneGuards.NoEmDashes(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void TitlePresent_WhenTheHeadingIsThere_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        HygieneGuards.TitlePresent(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void TitlePresent_WhenTheHeadingIsMissing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr().Replace($"# {Sample.DefaultSpec.Title}", $"## {Sample.DefaultSpec.Title}")
        );

        HygieneGuards
            .TitlePresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '# ' title heading");
    }
}
