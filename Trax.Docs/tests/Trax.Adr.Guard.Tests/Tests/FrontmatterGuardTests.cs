namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class FrontmatterGuardTests
{
    private static async Task<IReadOnlyList<Adr>> LoadAsync(
        TempAdrRepo repo,
        GuardOptions? options = null
    ) => await AdrCorpus.DiscoverAsync(options ?? repo.Options()).ConfigureAwait(false);

    #region authors

    [Test]
    public async Task Authors_WellFormed_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards
            .Authors((await LoadAsync(repo).ConfigureAwait(false)))
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task Authors_Missing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: null));

        FrontmatterGuards
            .Authors((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'authors' is missing");
    }

    [Test]
    public async Task Authors_Empty_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "[]"));

        FrontmatterGuards
            .Authors((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("empty");
    }

    [Test]
    public async Task Authors_Scalar_Fails_BecauseTheShapeIsPartOfTheContract()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "Theauxm"));

        FrontmatterGuards
            .Authors((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must be a list");
    }

    [Test]
    public async Task Authors_WithAtSign_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "[@Theauxm]"));

        FrontmatterGuards
            .Authors((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must not carry '@'");
    }

    #endregion

    #region areas

    [Test]
    public async Task Areas_WithinTheVocabulary_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards
            .Areas((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task Areas_OutsideTheVocabulary_Fails_AndNamesTheKnownSet()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(areas: "[invented]"));

        var offender = FrontmatterGuards
            .Areas((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Subject;

        offender.Should().Contain("unknown area(s) invented");
        offender.Should().Contain("known: migrations, testing");
    }

    [Test]
    public async Task Areas_Empty_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(areas: "[]"));

        FrontmatterGuards
            .Areas((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("empty");
    }

    #endregion

    #region repos

    [Test]
    public async Task Repos_DeclaredInACentralAdr_Passes()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards
            .Repos((await LoadAsync(repo, options).ConfigureAwait(false)), options)
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task Repos_MissingFromACentralAdr_Fails()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: false));
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards
            .Repos((await LoadAsync(repo, options).ConfigureAwait(false)), options)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'repos' is missing");
    }

    [Test]
    public async Task Repos_PresentInARepoLocalAdr_Fails_BecauseThePathAlreadySaysIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: true));

        FrontmatterGuards
            .Repos((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("forbidden in a repo-local ADR");
    }

    [Test]
    public async Task Repos_OutsideTheVocabulary_Fails()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: true, repos: "[nonsuch]"));
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards
            .Repos((await LoadAsync(repo, options).ConfigureAwait(false)), options)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("unknown repo(s) nonsuch");
    }

    #endregion

    #region status, date, file names

    [TestCase("proposed")]
    [TestCase("accepted")]
    [TestCase("deprecated")]
    [TestCase("superseded-by-0002")]
    public async Task Status_EachRecognisedShape_Passes(string status)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(status: status));

        FrontmatterGuards
            .Status((await LoadAsync(repo).ConfigureAwait(false)))
            .Passed.Should()
            .BeTrue();
    }

    [TestCase("Accepted")]
    [TestCase("active")]
    [TestCase("superseded-by-2")]
    [TestCase("superseded")]
    public async Task Status_UnrecognisedShape_Fails(string status)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(status: status));

        FrontmatterGuards
            .Status((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("is not one of");
    }

    [Test]
    public async Task NoDateKey_WhenDeclared_Fails_BecauseGitAlreadyRecordsIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(extraFrontmatter: "date: 2026-09-11"));

        FrontmatterGuards
            .NoDateKey((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'date' is not an accepted key");
    }

    [Test]
    public async Task FileNames_WellFormed_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards
            .FileNames((await LoadAsync(repo).ConfigureAwait(false)))
            .Passed.Should()
            .BeTrue();
    }

    [TestCase("1-too-few-digits.md")]
    [TestCase("0001_underscores.md")]
    [TestCase("0001-NotKebab.md")]
    [TestCase("no-number.md")]
    public async Task FileNames_Malformed_Fails(string fileName)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(fileName, Sample.Adr());

        FrontmatterGuards
            .FileNames((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("NNNN-kebab-case-slug.md");
    }

    [Test]
    public async Task FileNames_DuplicateNumberInOneDirectory_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr("0001-a-second-decision.md", Sample.Adr());

        FrontmatterGuards
            .FileNames((await LoadAsync(repo).ConfigureAwait(false)))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("number 0001 is already used");
    }

    #endregion
}
