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

    #region Crediting a central ADR

    /// <summary>
    /// A repo's shared guards enforce decisions recorded in the central corpus, which is not
    /// present in that repo. Nothing local can name them, so without this the census would
    /// force a false "not ADR-enforcing" opt-out on every one of them.
    /// </summary>
    [Test]
    public void Census_GuardCitingACentralAdr_IsClassified()
    {
        const string source = """
            namespace Some.Tests.Meta;

            /// <summary>
            /// No inline Version on a cross-repo reference.
            ///
            /// <para>Enforces <c>Trax.Docs/adr/0002-cross-repo-dependencies-are-exact-pinned.md</c>.</para>
            /// </summary>
            public class CrossRepoPackageReferenceTests { }
            """;

        using var repo = WithGuardSource("CrossRepoPackageReferenceTests.cs", source);

        Run(repo).Passed.Should().BeTrue();
    }

    [Test]
    public void Census_GuardCitingALocalAdr_IsAlsoClassified()
    {
        const string source = """
            namespace Some.Tests.Meta;

            /// <summary>
            /// Migrations are numbered.
            ///
            /// <para>Enforces <c>docs/adr/0001-schema-changes-are-hand-written-sql.md</c>.</para>
            /// </summary>
            public class MigrationsIntegrityTests { }
            """;

        using var repo = WithGuardSource("MigrationsIntegrityTests.cs", source);

        Run(repo).Passed.Should().BeTrue();
    }

    [Test]
    public void Census_GuardMentioningAdrsWithoutCitingOne_IsStillUnclassified()
    {
        const string source = """
            namespace Some.Tests.Meta;

            /// <summary>
            /// Something about adr conventions in general, naming no file.
            /// </summary>
            public class VagueTests { }
            """;

        using var repo = WithGuardSource("VagueTests.cs", source);

        Run(repo)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'VagueTests' is named by no ADR");
    }

    #endregion

    #region Reading the file correctly

    /// <summary>
    /// Only the first class in a file was censused, so a second guard beside it was never
    /// asked whether it was credited to anything.
    /// </summary>
    [Test]
    public void Census_SecondClassInTheSameFile_IsAlsoAsked()
    {
        const string source = """
            namespace Some.Tests.Meta;

            /// <summary>
            /// First.
            ///
            /// <para>Not ADR-enforcing: it pins a file naming convention nobody weighed.</para>
            /// </summary>
            public class AlphaTests { }

            /// <summary>Second, and deliberately unclassified.</summary>
            public class BetaTests { }
            """;

        using var repo = WithGuardSource("TwoGuards.cs", source);

        Run(repo)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'BetaTests' is named by no ADR");
    }

    /// <summary>
    /// The marker was searched for across the whole file, so a fixture holding a template
    /// of a guard class was credited with the template's placeholder text as its reason.
    /// </summary>
    [Test]
    public void Census_MarkerInsideARawStringLiteral_IsNotALiveOptOut()
    {
        var quote = new string('"', 3);
        var source =
            "namespace Some.Tests.Meta;\n\n"
            + "public class GammaTests\n{\n"
            + $"    private const string Template = {quote}\n"
            + "        /// Not ADR-enforcing: a placeholder inside a fixture, not a declaration.\n"
            + "        public class SomethingTests { }\n"
            + $"        {quote};\n"
            + "}\n";

        using var repo = WithGuardSource("Fixture.cs", source);

        var offenders = Run(repo).Offenders;

        offenders
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'GammaTests' is named by no ADR");
        offenders
            .Should()
            .NotContain(
                o => o.Contains("SomethingTests"),
                "a class inside a literal is data, not a declaration"
            );
    }

    [Test]
    public void Census_OptOutBelongingToAnotherClass_DoesNotCreditThisOne()
    {
        const string source = """
            namespace Some.Tests.Meta;

            /// <summary>
            /// First.
            ///
            /// <para>Not ADR-enforcing: it pins a file naming convention nobody weighed.</para>
            /// </summary>
            public class AlphaTests { }

            public class DeltaTests { }
            """;

        using var repo = WithGuardSource("Neighbours.cs", source);

        Run(repo)
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'DeltaTests' is named by no ADR");
    }

    #endregion

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
