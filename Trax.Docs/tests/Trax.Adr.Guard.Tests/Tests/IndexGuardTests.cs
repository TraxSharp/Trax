namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class IndexGuardTests
{
    private static readonly AdrSpec First = Sample.DefaultSpec;

    private static readonly AdrSpec Second = new(
        "0002",
        "migrations-are-hand-written-sql",
        "Migrations are hand-written SQL",
        ["migrations"],
        ["effect"]
    );

    private static IReadOnlyList<Adr> Load(TempAdrRepo repo) => AdrCorpus.Discover(repo.Options());

    /// <summary>Two ADRs and an index that agrees with both.</summary>
    private static TempAdrRepo TwoAdrs()
    {
        var repo = new TempAdrRepo();
        repo.Adr(First.FileName, Sample.Adr(spec: First));
        repo.Adr(Second.FileName, Sample.Adr(spec: Second));
        repo.Index(Sample.Index(central: false, First, Second));
        return repo;
    }

    private static GuardResult ByArea(TempAdrRepo repo) =>
        IndexGuards.TagTable(Load(repo), repo.Options(), IndexGuards.ByAreaHeading, "areas");

    #region Index presence

    [Test]
    public void Exists_WhenTheIndexIsThere_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        IndexGuards.Exists(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void Exists_WhenTheCorpusHasAdrsButNoIndex_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Delete("docs/adr/README.md");

        IndexGuards
            .Exists(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("but no index");
    }

    #endregion

    #region Tag table, both directions

    [Test]
    public void TagTable_AgreeingWithFrontmatter_Passes()
    {
        using var repo = TwoAdrs();

        ByArea(repo).Passed.Should().BeTrue();
    }

    [Test]
    public void TagTable_MissingAnAdr_Fails_FrontmatterToIndex()
    {
        using var repo = TwoAdrs();
        repo.Index(Sample.Index(central: false, First));

        ByArea(repo)
            .Offenders.Should()
            .Contain(o =>
                o.Contains("0002-migrations-are-hand-written-sql.md")
                && o.Contains("does not list it")
            );
    }

    [Test]
    public void TagTable_ListingAnAdrThatDoesNotExist_Fails_IndexToFrontmatter()
    {
        using var repo = TwoAdrs();
        repo.Delete($"docs/adr/{Second.FileName}");

        ByArea(repo).Offenders.Should().Contain(o => o.Contains("which does not exist"));
    }

    [Test]
    public void TagTable_ListingAnAdrUnderATagItDoesNotDeclare_Fails()
    {
        using var repo = TwoAdrs();
        // Move 0002 under 'testing', which it does not declare.
        var drifted = new AdrSpec(
            Second.Number,
            Second.Slug,
            Second.Title,
            ["testing"],
            Second.Repos
        );
        repo.Index(Sample.Index(central: false, First, drifted));

        ByArea(repo)
            .Offenders.Should()
            .Contain(o => o.Contains("does not declare areas 'testing'"));
    }

    [Test]
    public void TagTable_WithNoSuchSection_Fails()
    {
        using var repo = TwoAdrs();
        repo.Index(Sample.Index(central: false, First, Second).Replace("## By area", "## Areas"));

        ByArea(repo)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '## By area' section");
    }

    [Test]
    public void TagTable_ByRepo_IsCheckedForACentralCorpus()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        var options = repo.Options(requireReposKey: true);
        var drifted = new AdrSpec(First.Number, First.Slug, First.Title, First.Areas, ["api"]);
        repo.Index(Sample.Index(central: true, drifted));

        IndexGuards
            .TagTable(AdrCorpus.Discover(options), options, IndexGuards.ByRepoHeading, "repos")
            .Offenders.Should()
            .NotBeEmpty("the ADR declares 'effect' but the index files it under 'api'");
    }

    #endregion

    #region Full list

    [Test]
    public void FullList_AgreeingWithTheCorpus_Passes()
    {
        using var repo = TwoAdrs();

        IndexGuards.FullList(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void FullList_MissingAnAdr_Fails()
    {
        using var repo = TwoAdrs();
        repo.Index(Sample.Index(central: false, First));

        IndexGuards
            .FullList(Load(repo), repo.Options())
            .Offenders.Should()
            .Contain(o => o.Contains(Second.FileName) && o.Contains("has no row"));
    }

    [Test]
    public void FullList_WithAStaleTitle_Fails()
    {
        using var repo = TwoAdrs();
        var renamed = new AdrSpec(
            Second.Number,
            Second.Slug,
            "An older title",
            Second.Areas,
            Second.Repos
        );
        repo.Index(Sample.Index(central: false, First, renamed));

        IndexGuards
            .FullList(Load(repo), repo.Options())
            .Offenders.Should()
            .Contain(o => o.Contains("says 'An older title'"));
    }

    [Test]
    public void FullList_ListingAnAdrThatDoesNotExist_Fails()
    {
        using var repo = TwoAdrs();
        repo.Delete($"docs/adr/{Second.FileName}");

        IndexGuards
            .FullList(Load(repo), repo.Options())
            .Offenders.Should()
            .Contain(o => o.Contains(Second.FileName) && o.Contains("does not exist"));
    }

    #endregion
}
