namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class CliTests
{
    private static string[] Minimal(string repo) => ["--repo", repo, "--known-areas", "testing"];

    [Test]
    public void Parse_MinimalArguments_UsesTheTraxDefaults()
    {
        using var repo = TempAdrRepo.Valid();

        var parsed = Cli.Parse(Minimal(repo.Root));

        parsed.Error.Should().BeNull();
        parsed.Options!.AdrRoot.Should().Be("docs/adr");
        parsed.Options.TestRoots.Should().Equal("tests");
        parsed.Options.RequireReposKey.Should().BeFalse();
        parsed.Options.CensusRoot.Should().BeNull();
    }

    [Test]
    public void Parse_AllArguments_AreRead()
    {
        using var repo = TempAdrRepo.Valid();

        var parsed = Cli.Parse([
            "--repo",
            repo.Root,
            "--adr-root",
            "adr",
            "--test-roots",
            "tests, samples",
            "--known-areas",
            "testing,migrations",
            "--known-repos",
            "effect,api",
            "--require-repos-key",
            "--census-root",
            "tests/Some.Tests.Meta",
        ]);

        parsed.Error.Should().BeNull();
        parsed.Options!.AdrRoot.Should().Be("adr");
        parsed.Options.TestRoots.Should().Equal("tests", "samples");
        parsed.Options.KnownAreas.Should().BeEquivalentTo(["testing", "migrations"]);
        parsed.Options.KnownRepos.Should().BeEquivalentTo(["effect", "api"]);
        parsed.Options.RequireReposKey.Should().BeTrue();
        parsed.Options.CensusRoot.Should().Be("tests/Some.Tests.Meta");
    }

    /// <summary>
    /// An empty path combined to the repo root, so a census meant to be switched off censused
    /// the whole tree instead. The composite action never passes one, which is why nothing
    /// caught it.
    /// </summary>
    [TestCase("")]
    [TestCase("/")]
    public void Parse_EmptyCensusRoot_DisablesTheCensus_RatherThanWideningIt(string value)
    {
        using var repo = TempAdrRepo.Valid();

        var parsed = Cli.Parse([.. Minimal(repo.Root), "--census-root", value]);

        parsed.Error.Should().BeNull();
        parsed.Options!.CensusRoot.Should().BeNull();
    }

    [Test]
    public void Parse_WithoutKnownAreas_IsRejected_BecauseTheVocabularyIsClosedOnPurpose()
    {
        using var repo = TempAdrRepo.Valid();

        Cli.Parse(["--repo", repo.Root]).Error.Should().Contain("--known-areas is required");
    }

    [Test]
    public void Parse_RequiringReposWithoutAVocabulary_IsRejected()
    {
        using var repo = TempAdrRepo.Valid();

        Cli.Parse([.. Minimal(repo.Root), "--require-repos-key"])
            .Error.Should()
            .Contain("needs --known-repos");
    }

    [Test]
    public void Parse_MissingRepository_IsRejected()
    {
        Cli.Parse(["--repo", "/no/such/place", "--known-areas", "testing"])
            .Error.Should()
            .Contain("does not exist");
    }

    [Test]
    public void Parse_UnknownOption_IsRejected()
    {
        Cli.Parse(["--nonsense", "x"]).Error.Should().Contain("Unknown option");
    }

    [Test]
    public void Parse_FlagWithNoValue_IsRejected()
    {
        Cli.Parse(["--known-areas"]).Error.Should().Contain("needs a value");
    }
}
