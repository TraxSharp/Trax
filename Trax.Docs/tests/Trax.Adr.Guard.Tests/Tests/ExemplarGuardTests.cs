namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class ExemplarGuardTests
{
    private const string GuardPath = "tests/Some.Tests.Meta/Tests/MigrationsIntegrityTests.cs";

    private static IReadOnlyList<Adr> Load(TempAdrRepo repo) => AdrCorpus.Discover(repo.Options());

    /// <summary>A corpus whose single ADR names a guard class, with that class on disk.</summary>
    private static TempAdrRepo WithGuard(
        string guardSource,
        string exemplars = "- `MigrationsIntegrityTests` pins the numbering."
    )
    {
        var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: exemplars));
        repo.Write(GuardPath, guardSource);
        return repo;
    }

    private static string GuardCiting(string adrFileName) =>
        $$"""
            namespace Some.Tests.Meta;

            /// <summary>
            /// Migration files are numbered sequentially and shipped as embedded resources.
            /// </summary>
            /// <remarks>Enforces docs/adr/{{adrFileName}}.</remarks>
            [Property("adr", "docs/adr/{{adrFileName}}")]
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered()
                {
                    Assert.Pass("see docs/adr/{{adrFileName}}");
                }
            }
            """;

    private const string GuardCitingNothing = """
        namespace Some.Tests.Meta;

        [TestFixture]
        public class MigrationsIntegrityTests
        {
            [Test]
            public void Migrations_are_numbered() => Assert.Pass();
        }
        """;

    #region Section shape

    [Test]
    public void SectionPresent_WithAnUnenforcedAdmission_Passes()
    {
        using var repo = TempAdrRepo.Valid();

        ExemplarGuards.SectionPresent(Load(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void SectionPresent_Missing_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr().Replace("## Exemplars", "## Notes"));

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no '## Exemplars' section");
    }

    [Test]
    public void SectionPresent_NamingNoGuardAndNotOptingOut_Fails_BecauseSilenceReadsAsEnforcement()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: "- [a doc](/docs/effect)"));

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("declares neither");
    }

    [Test]
    public void SectionPresent_NamingGuardsAndAlsoOptingOut_Fails_BecauseItIsAContradiction()
    {
        using var repo = WithGuard(
            GuardCiting(Sample.DefaultSpec.FileName),
            exemplars: $"- `MigrationsIntegrityTests` pins it.\n\n**Unenforced:** {Sample.GoodReason}"
        );

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("AND claims enforcement");
    }

    [Test]
    public void SectionPresent_UnenforcedWithNoReason_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: "**Unenforced:**"));

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("needs a real reason");
    }

    [Test]
    public void SectionPresent_UnenforcedWithAShortReason_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: "**Unenforced:** it is hard."));

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("needs a real reason");
    }

    [TestCase("we have not written the guard yet, and it is a large piece of work to do properly")]
    [TestCase("a TODO: the census that would cover this is a separate and much larger change")]
    [TestCase("this will be enforced later, once the surrounding refactor has actually landed")]
    public void SectionPresent_UnenforcedWithADeferral_Fails_EvenWhenLongEnough(string reason)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: $"**Unenforced:** {reason}"));

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("reads as a deferral");
    }

    #endregion

    #region Enforced elsewhere

    /// <summary>
    /// The state a cross-repo decision needs. Its guards are real but live in the repos it
    /// binds, which this corpus cannot see, so neither a bare class name nor an
    /// "unenforced" admission would be true.
    /// </summary>
    [Test]
    public void SectionPresent_EnforcedElsewhere_Passes_WithoutNamingALocalClass()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                central: true,
                exemplars: "**Enforced elsewhere:** `NoIgnoreAttributeTests` in every repo's "
                    + "Tests.Meta project, and HygieneGuards.NoIgnoreAttribute shipped from Trax.Core."
            )
        );
        var options = repo.Options(requireReposKey: true);
        var adrs = AdrCorpus.Discover(options);

        ExemplarGuards.SectionPresent(adrs).Passed.Should().BeTrue();
        ExemplarGuards
            .NamedGuardsResolve(adrs, options)
            .Offenders.Should()
            .BeEmpty("a class named inside the elsewhere marker is not claimed as local");
    }

    [Test]
    public void SectionPresent_EnforcedElsewhereWithAThinReason_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "**Enforced elsewhere:** guards.")
        );

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("needs a real reason");
    }

    [Test]
    public void SectionPresent_EnforcedElsewhereAndUnenforced_Fails_BecauseItIsAContradiction()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: $"**Enforced elsewhere:** {Sample.GoodReason}\n\n**Unenforced:** {Sample.GoodReason}"
            )
        );

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("AND claims enforcement");
    }

    #endregion

    #region Named guards

    [Test]
    public void NamedGuardsResolve_WhenTheClassExists_Passes()
    {
        using var repo = WithGuard(GuardCiting(Sample.DefaultSpec.FileName));

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    /// <summary>
    /// The hole this replaced. Two classes shared a name, the claim resolved against whichever
    /// the scan reached first, and deleting the one doing the work left the ADR green.
    /// </summary>
    [Test]
    public void NamedGuardsResolve_WhenAClassSharesTheNameButDoesNotClaimTheAdr_Fails()
    {
        using var repo = WithGuard(GuardCitingNothing);

        var result = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        result.Passed.Should().BeFalse();
        result.Offenders[0].Should().Contain("does not claim this ADR back");
    }

    /// <summary>
    /// csharpier wraps a long attribute onto several lines, one argument per line. The guard
    /// read the attribute a line at a time, so a real back-citation in that shape failed as
    /// "does not claim this ADR back" and an ADR slug had to be shortened to fit on one line.
    /// </summary>
    [Test]
    public void NamedGuardsResolve_WhenTheAttributeIsWrappedAcrossLines_Passes()
    {
        using var repo = WithGuard(
            $$"""
            namespace Some.Tests.Meta;

            [Property(
                "adr",
                "docs/adr/{{Sample.DefaultSpec.FileName}}"
            )]
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered() => Assert.Pass();
            }
            """
        );

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Offenders.Should().BeEmpty();
    }

    [Test]
    public void NamedGuardsResolve_WhenAWrappedAttributeIsCommentedOut_Fails()
    {
        using var repo = WithGuard(
            $$"""
            namespace Some.Tests.Meta;

            // [Property(
            //     "adr",
            //     "docs/adr/{{Sample.DefaultSpec.FileName}}"
            // )]
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered() => Assert.Pass();
            }
            """
        );

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not claim this ADR back");
    }

    [Test]
    public void NamedGuardsResolve_WhenAWrappedAttributeIsSeparatedFromTheClass_Fails()
    {
        using var repo = WithGuard(
            $$"""
            namespace Some.Tests.Meta;

            [Property(
                "adr",
                "docs/adr/{{Sample.DefaultSpec.FileName}}"
            )]
            internal static class Unrelated { }

            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered() => Assert.Pass();
            }
            """
        );

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not claim this ADR back");
    }

    [Test]
    public void NamedGuardsResolve_WhenTwoClassesClaimTheSameAdr_Fails()
    {
        using var repo = WithGuard(GuardCiting(Sample.DefaultSpec.FileName));
        repo.Write(
            "tests/Other.Tests.Meta/Tests/MigrationsIntegrityTests.cs",
            GuardCiting(Sample.DefaultSpec.FileName)
        );

        var result = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        result.Passed.Should().BeFalse();
        result.Offenders[0].Should().Contain("2 classes claim it back");
    }

    [Test]
    public void NamedGuardsResolve_WhenTheClassIsGone_Fails()
    {
        using var repo = WithGuard(GuardCiting(Sample.DefaultSpec.FileName));
        repo.Delete(GuardPath);

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("which is not a class under");
    }

    [Test]
    public void NamedGuardsResolve_QualifiedNameInAnotherRepo_IsProse_NotAClaim()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "- `HygieneGuards.NoIgnoreAttribute` in Trax.Core ships the check.\n\n"
                    + $"**Unenforced:** {Sample.GoodReason}"
            )
        );

        var result = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        result
            .Offenders.Should()
            .BeEmpty("a dotted name lives in another repo and cannot be resolved here");
        ExemplarGuards
            .SectionPresent(Load(repo))
            .Passed.Should()
            .BeTrue("so it does not count as a guard claim either");
    }

    [Test]
    public void NamedGuardsCiteBack_WhenTheGuardNamesTheAdr_Passes()
    {
        using var repo = WithGuard(GuardCiting(Sample.DefaultSpec.FileName));

        ExemplarGuards.NamedGuardsCiteBack(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void NamedGuardsCiteBack_WhenTheGuardIsSilent_Fails()
    {
        using var repo = WithGuard(GuardCitingNothing);

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not name it in a failure message");
    }

    [Test]
    public void NamedGuardsCiteBack_WhenTheGuardCitesADifferentAdr_Fails()
    {
        using var repo = WithGuard(GuardCiting("0009-some-other-decision.md"));

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle();
    }

    private static readonly AdrSpec SecondSpec = new(
        "0002",
        "guards-name-their-decision",
        "Guards name their decision",
        ["testing"],
        ["effect"]
    );

    /// <summary>A corpus of two ADRs that both name <c>MigrationsIntegrityTests</c>.</summary>
    private static TempAdrRepo WithTwoAdrsNamingTheGuard(string guardSource)
    {
        var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "- `MigrationsIntegrityTests` pins the numbering.")
        );
        repo.Adr(
            SecondSpec.FileName,
            Sample.Adr(SecondSpec, exemplars: "- `MigrationsIntegrityTests` pins it too.")
        );
        repo.Write(GuardPath, guardSource);
        return repo;
    }

    private static string GuardCitingBoth(string attributes) =>
        $$"""
            namespace Some.Tests.Meta;

            /// <summary>Enforces docs/adr/{{Sample.DefaultSpec.FileName}} and docs/adr/{{SecondSpec.FileName}}.</summary>
            {{attributes}}
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered()
                {
                    Assert.Pass(
                        "see docs/adr/{{Sample.DefaultSpec.FileName}} and docs/adr/{{SecondSpec.FileName}}"
                    );
                }
            }
            """;

    /// <summary>
    /// A test class can hold up two decisions. The scan kept only the last attribute above a
    /// class, so the first decision's claim failed as "does not claim this ADR back" although
    /// the class carried it, and a class that already claimed two ADRs silently lost one.
    /// </summary>
    [Test]
    public void NamedGuardsResolve_WhenAClassClaimsTwoAdrsInTwoAttributes_BothResolve()
    {
        using var repo = WithTwoAdrsNamingTheGuard(
            GuardCitingBoth(
                $"""
                [Property("adr", "docs/adr/{Sample.DefaultSpec.FileName}")]
                [Property("adr", "docs/adr/{SecondSpec.FileName}")]
                """
            )
        );

        var result = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        result.Offenders.Should().BeEmpty();
        result.Inspected.Should().Be(2);
    }

    [Test]
    public void NamedGuardsResolve_WhenAClassClaimsTwoAdrsInOneAttributeList_BothResolve()
    {
        using var repo = WithTwoAdrsNamingTheGuard(
            GuardCitingBoth(
                $"""
                [Property("adr", "docs/adr/{Sample.DefaultSpec.FileName}"), Property("adr", "docs/adr/{SecondSpec.FileName}")]
                """
            )
        );

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Offenders.Should().BeEmpty();
    }

    /// <summary>
    /// Resolution pairs a claim with the class that carries the attribute. Cite-back looked the
    /// claim up by bare name instead, first file wins, so with two classes of one name it could
    /// read the wrong one: here the unattributed class cites the decision and the attributed one
    /// does not, and cite-back must still fail.
    /// </summary>
    [Test]
    public void NamedGuardsCiteBack_ReadsTheClassCarryingTheAttribute_NotAnotherOfTheSameName()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "- `MigrationsIntegrityTests` pins the numbering.")
        );
        repo.Write(
            "tests/A.Tests.Meta/Tests/MigrationsIntegrityTests.cs",
            GuardCiting(Sample.DefaultSpec.FileName)
                .Replace($"[Property(\"adr\", \"docs/adr/{Sample.DefaultSpec.FileName}\")]\n", "")
        );
        repo.Write(
            "tests/B.Tests.Meta/Tests/MigrationsIntegrityTests.cs",
            $$"""
            namespace B.Tests.Meta;

            [Property("adr", "docs/adr/{{Sample.DefaultSpec.FileName}}")]
            [TestFixture]
            public class MigrationsIntegrityTests
            {
                [Test]
                public void Migrations_are_numbered() => Assert.Pass();
            }
            """
        );

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("B.Tests.Meta");
    }

    #endregion
}
