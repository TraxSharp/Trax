namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// The census asks the reverse question: not "does this ADR have a guard" but "does this
/// guard have a decision". A guard nobody linked to one pins something, and no reader can
/// tell whether that something was chosen or is an accident somebody froze.
/// </summary>
[TestFixture]
public class CensusGuardTests
{
    private const string CensusRoot = "tests/Some.Tests.Meta";

    private static GuardResult Run(TempAdrRepo repo) =>
        CensusGuards.EveryGuardIsClassified(
            AdrCorpus.Discover(repo.Options()),
            repo.Options(censusRoot: CensusRoot)
        );

    private static TempAdrRepo WithGuardSource(
        string fileName,
        string source,
        string? exemplars = null
    )
    {
        var repo = TempAdrRepo.Valid();
        if (exemplars is not null)
            repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: exemplars));
        repo.Write($"{CensusRoot}/{fileName}", source);
        return repo;
    }

    private const string PlainGuard = """
        namespace Some.Tests.Meta;

        [TestFixture]
        public class MigrationsIntegrityTests
        {
            [Test]
            public void Migrations_are_numbered() => Assert.Pass();
        }
        """;

    private static string GuardOptingOut(string reason) =>
        $$"""
            namespace Some.Tests.Meta;

            /// <summary>
            /// Migration files are numbered sequentially.
            ///
            /// <para>Not ADR-enforcing: {{reason}}</para>
            /// </summary>
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered() => Assert.Pass();
            }
            """;

    [Test]
    public void Census_WhenDisabled_RunsNothing()
    {
        using var repo = TempAdrRepo.Valid();

        var result = CensusGuards.EveryGuardIsClassified(
            AdrCorpus.Discover(repo.Options()),
            repo.Options()
        );

        result.Inspected.Should().Be(0);
        result.Offenders.Should().BeEmpty();
    }

    [Test]
    public void Census_GuardNamedByAnAdr_IsClassified()
    {
        using var repo = WithGuardSource(
            "MigrationsIntegrityTests.cs",
            PlainGuard,
            exemplars: "- `MigrationsIntegrityTests` pins the numbering."
        );

        Run(repo).Passed.Should().BeTrue();
    }

    [Test]
    public void Census_GuardNamedByNoAdrAndNotOptingOut_Fails()
    {
        using var repo = WithGuardSource("MigrationsIntegrityTests.cs", PlainGuard);

        Run(repo)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("is named by no ADR and does not opt out");
    }

    [Test]
    public void Census_GuardOptingOutWithARealReason_IsClassified()
    {
        using var repo = WithGuardSource(
            "MigrationsIntegrityTests.cs",
            GuardOptingOut("it pins a file naming convention nobody weighed an alternative for.")
        );

        Run(repo).Passed.Should().BeTrue();
    }

    [Test]
    public void Census_GuardOptingOutWithADeferral_Fails()
    {
        using var repo = WithGuardSource(
            "MigrationsIntegrityTests.cs",
            GuardOptingOut("we have not recorded the decision yet, but somebody really should.")
        );

        Run(repo).Offenders.Should().ContainSingle().Which.Should().Contain("reads as a deferral");
    }

    [Test]
    public void Census_GuardBothNamedAndOptingOut_Fails_BecauseItIsAContradiction()
    {
        using var repo = WithGuardSource(
            "MigrationsIntegrityTests.cs",
            GuardOptingOut("it pins a file naming convention nobody weighed an alternative for."),
            exemplars: "- `MigrationsIntegrityTests` pins the numbering."
        );

        Run(repo).Offenders.Should().ContainSingle().Which.Should().Contain("AND declares");
    }

    [Test]
    public void Census_HelperWithoutATestsSuffix_IsNotCounted()
    {
        using var repo = WithGuardSource(
            "RepoRoot.cs",
            "namespace Some.Tests.Meta;\n\ninternal static class RepoRoot { }\n"
        );

        var result = Run(repo);

        result.Offenders.Should().BeEmpty("helpers are not guards");
        result.Inspected.Should().Be(0);
    }

    [Test]
    public void Census_MissingRoot_Fails_RatherThanPassingOverNothing()
    {
        using var repo = TempAdrRepo.Valid();

        CensusGuards
            .EveryGuardIsClassified(
                AdrCorpus.Discover(repo.Options()),
                repo.Options(censusRoot: "tests/DoesNotExist")
            )
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not exist");
    }
}
