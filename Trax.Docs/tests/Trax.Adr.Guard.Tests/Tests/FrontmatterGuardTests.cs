namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class FrontmatterGuardTests
{
    private static IReadOnlyList<Adr> Load(TempAdrRepo repo, GuardOptions? options = null) =>
        AdrCorpus.Discover(options ?? repo.Options());

    #region authors

    [Test]
    public void Authors_WellFormed_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards.Authors(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void Authors_Missing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: null));

        FrontmatterGuards
            .Authors(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'authors' is missing");
    }

    [Test]
    public void Authors_Empty_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "[]"));

        FrontmatterGuards
            .Authors(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("empty");
    }

    [Test]
    public void Authors_Scalar_Fails_BecauseTheShapeIsPartOfTheContract()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "Theauxm"));

        FrontmatterGuards
            .Authors(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must be a list");
    }

    [Test]
    public void Authors_WithAtSign_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(authors: "[@Theauxm]"));

        FrontmatterGuards
            .Authors(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("must not carry '@'");
    }

    #endregion

    #region areas

    [Test]
    public void Areas_WithinTheVocabulary_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards.Areas(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void Areas_OutsideTheVocabulary_Fails_AndNamesTheKnownSet()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(areas: "[invented]"));

        var offender = FrontmatterGuards
            .Areas(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Subject;

        offender.Should().Contain("unknown area(s) invented");
        offender.Should().Contain("known: migrations, testing");
    }

    [Test]
    public void Areas_Empty_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(areas: "[]"));

        FrontmatterGuards
            .Areas(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("empty");
    }

    #endregion

    #region repos

    [Test]
    public void Repos_DeclaredInACentralAdr_Passes()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards.Repos(Load(repo, options), options).Passed.Should().BeTrue();
    }

    [Test]
    public void Repos_MissingFromACentralAdr_Fails()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: false));
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards
            .Repos(Load(repo, options), options)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'repos' is missing");
    }

    [Test]
    public void Repos_PresentInARepoLocalAdr_Fails_BecauseThePathAlreadySaysIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: true));

        FrontmatterGuards
            .Repos(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("forbidden in a repo-local ADR");
    }

    [Test]
    public void Repos_OutsideTheVocabulary_Fails()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(central: true, repos: "[nonsuch]"));
        var options = repo.Options(requireReposKey: true);

        FrontmatterGuards
            .Repos(Load(repo, options), options)
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
    public void Status_EachRecognisedShape_Passes(string status)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(status: status));

        FrontmatterGuards.Status(Load(repo)).Passed.Should().BeTrue();
    }

    [TestCase("Accepted")]
    [TestCase("active")]
    [TestCase("superseded-by-2")]
    [TestCase("superseded")]
    public void Status_UnrecognisedShape_Fails(string status)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(status: status));

        FrontmatterGuards
            .Status(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("is not one of");
    }

    [Test]
    public void NoDateKey_WhenDeclared_Fails_BecauseGitAlreadyRecordsIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(extraFrontmatter: "date: 2026-09-11"));

        FrontmatterGuards
            .NoDateKey(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'date' is not an accepted key");
    }

    [Test]
    public void FileNames_WellFormed_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        FrontmatterGuards.FileNames(Load(repo)).Passed.Should().BeTrue();
    }

    [TestCase("1-too-few-digits.md")]
    [TestCase("0001_underscores.md")]
    [TestCase("0001-NotKebab.md")]
    [TestCase("no-number.md")]
    public void FileNames_Malformed_Fails(string fileName)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(fileName, Sample.Adr());

        FrontmatterGuards
            .FileNames(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("NNNN-kebab-case-slug.md");
    }

    [Test]
    public void FileNames_DuplicateNumberInOneDirectory_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr("0001-a-second-decision.md", Sample.Adr());

        FrontmatterGuards
            .FileNames(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("number 0001 is already used");
    }

    #endregion
}
