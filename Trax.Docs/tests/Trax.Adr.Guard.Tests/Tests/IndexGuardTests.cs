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

    private static async Task<IReadOnlyList<Adr>> LoadAsync(TempAdrRepo repo) =>
        await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false);

    /// <summary>Two ADRs and an index that agrees with both.</summary>
    private static TempAdrRepo TwoAdrs()
    {
        var repo = new TempAdrRepo();
        repo.Adr(First.FileName, Sample.Adr(spec: First));
        repo.Adr(Second.FileName, Sample.Adr(spec: Second));
        repo.Index(Sample.Index(central: false, First, Second));
        return repo;
    }

    private static async Task<GuardResult> ByAreaAsync(TempAdrRepo repo) =>
        await IndexGuards
            .TagTableAsync(
                (await LoadAsync(repo).ConfigureAwait(false)),
                repo.Options(),
                IndexGuards.ByAreaHeading,
                "areas"
            )
            .ConfigureAwait(false);

    #region Index presence

    [Test]
    public async Task Exists_WhenTheIndexIsThere_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        IndexGuards
            .Exists((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task Exists_WhenTheCorpusHasAdrsButNoIndex_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Delete("docs/adr/README.md");

        IndexGuards
            .Exists((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("but no index");
    }

    #endregion

    #region Tag table, both directions

    [Test]
    public async Task TagTable_AgreeingWithFrontmatter_Passes()
    {
        using var repo = TwoAdrs();

        (await ByAreaAsync(repo).ConfigureAwait(false)).Passed.Should().BeTrue();
    }

    [Test]
    public async Task TagTable_MissingAnAdr_Fails_FrontmatterToIndex()
    {
        using var repo = TwoAdrs();
        repo.Index(Sample.Index(central: false, First));

        (await ByAreaAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .Contain(o =>
                o.Contains("0002-migrations-are-hand-written-sql.md")
                && o.Contains("does not list it")
            );
    }

    [Test]
    public async Task TagTable_ListingAnAdrThatDoesNotExist_Fails_IndexToFrontmatter()
    {
        using var repo = TwoAdrs();
        repo.Delete($"docs/adr/{Second.FileName}");

        (await ByAreaAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .Contain(o => o.Contains("which does not exist"));
    }

    [Test]
    public async Task TagTable_ListingAnAdrUnderATagItDoesNotDeclare_Fails()
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

        (await ByAreaAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .Contain(o => o.Contains("does not declare areas 'testing'"));
    }

    [Test]
    public async Task TagTable_WithNoSuchSection_Fails()
    {
        using var repo = TwoAdrs();
        repo.Index(
            Sample
                .Index(central: false, First, Second)
                .Replace("## By area", "## Areas", StringComparison.Ordinal)
        );

        (await ByAreaAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '## By area' section");
    }

    [Test]
    public async Task TagTable_ByRepo_IsCheckedForACentralCorpus()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        var options = repo.Options(requireReposKey: true);
        var drifted = new AdrSpec(First.Number, First.Slug, First.Title, First.Areas, ["api"]);
        repo.Index(Sample.Index(central: true, drifted));

        (
            await IndexGuards
                .TagTableAsync(
                    (await AdrCorpus.DiscoverAsync(options).ConfigureAwait(false)),
                    options,
                    IndexGuards.ByRepoHeading,
                    "repos"
                )
                .ConfigureAwait(false)
        )
            .Offenders.Should()
            .NotBeEmpty("the ADR declares 'effect' but the index files it under 'api'");
    }

    #endregion

    #region Full list

    [Test]
    public async Task FullList_AgreeingWithTheCorpus_Passes()
    {
        using var repo = TwoAdrs();

        (
            await IndexGuards
                .FullListAsync((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
                .ConfigureAwait(false)
        )
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public async Task FullList_MissingAnAdr_Fails()
    {
        using var repo = TwoAdrs();
        repo.Index(Sample.Index(central: false, First));

        (
            await IndexGuards
                .FullListAsync((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
                .ConfigureAwait(false)
        )
            .Offenders.Should()
            .Contain(o => o.Contains(Second.FileName) && o.Contains("has no row"));
    }

    [Test]
    public async Task FullList_WithAStaleTitle_Fails()
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

        (
            await IndexGuards
                .FullListAsync((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
                .ConfigureAwait(false)
        )
            .Offenders.Should()
            .Contain(o => o.Contains("says 'An older title'"));
    }

    [Test]
    public async Task FullList_ListingAnAdrThatDoesNotExist_Fails()
    {
        using var repo = TwoAdrs();
        repo.Delete($"docs/adr/{Second.FileName}");

        (
            await IndexGuards
                .FullListAsync((await LoadAsync(repo).ConfigureAwait(false)), repo.Options())
                .ConfigureAwait(false)
        )
            .Offenders.Should()
            .Contain(o => o.Contains(Second.FileName) && o.Contains("does not exist"));
    }

    #endregion
}
