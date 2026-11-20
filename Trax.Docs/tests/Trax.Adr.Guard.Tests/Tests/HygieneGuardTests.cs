namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class HygieneGuardTests
{
    private static async Task<IReadOnlyList<Adr>> LoadAsync(TempAdrRepo repo) =>
        await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false);

    [Test]
    public async Task NoEmDashes_CleanCorpus_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        HygieneGuards
            .NoEmDashes((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task NoEmDashes_InAnAdr_Fails_WithFileAndLine()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(statusSection: "**Accepted.** The rule holds — everywhere.")
        );

        HygieneGuards
            .NoEmDashes((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("docs/adr/0001-tests-are-deterministic.md:");
    }

    [Test]
    public async Task NoEmDashes_InTheIndex_Fails_BecauseTheIndexIsPartOfTheCorpus()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Index(
            Sample.Index(central: false, Sample.DefaultSpec) + "\nA trailing note — with a dash.\n"
        );

        HygieneGuards
            .NoEmDashes((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("README.md:");
    }

    [Test]
    public async Task NoEmDashes_DoesNotFlagAHyphenOrEnDash()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(statusSection: "**Accepted.** Use --locked-mode, and see pages 3–5.")
        );

        HygieneGuards
            .NoEmDashes((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task TitlePresent_WhenTheHeadingIsThere_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        HygieneGuards
            .TitlePresent((await LoadAsync(repo).ConfigureAwait(false)))
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task TitlePresent_WhenTheHeadingIsMissing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample
                .Adr()
                .Replace(
                    $"# {Sample.DefaultSpec.Title}",
                    $"## {Sample.DefaultSpec.Title}",
                    StringComparison.Ordinal
                )
        );

        HygieneGuards
            .TitlePresent((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '# ' title heading");
    }
}
