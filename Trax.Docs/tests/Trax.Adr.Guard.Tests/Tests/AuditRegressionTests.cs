namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// Defects an independent audit found in the first cut of this guard. Each one passed the
/// suite that shipped with it, which is why they are pinned together here rather than
/// folded into the fixtures that missed them.
/// </summary>
[TestFixture]
public class AuditRegressionTests
{
    private static IReadOnlyList<Adr> Load(TempAdrRepo repo, GuardOptions? options = null) =>
        AdrCorpus.Discover(options ?? repo.Options());

    /// <summary>
    /// A quoted format template satisfied the exemplars check while the document's real
    /// Exemplars section said nothing at all. The most likely ADR to do this is one about
    /// the ADR format.
    /// </summary>
    [Test]
    public void FencedTemplate_DoesNotSatisfy_TheExemplarsCheck()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "Quoting the template:\n\n"
                    + "```md\n## Exemplars\n\n**Unenforced:** "
                    + Sample.GoodReason
                    + "\n```\n\nAnd nothing of our own."
            )
        );

        ExemplarGuards
            .SectionPresent(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("declares neither", "the fenced example is not this document's answer");
    }

    [Test]
    public void FencedClassName_IsNotReadAsAnEnforcementClaim()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "```md\n- `SomeImaginaryTests` pins it.\n```\n\n**Unenforced:** "
                    + Sample.GoodReason
            )
        );

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Offenders.Should().BeEmpty();
        ExemplarGuards.SectionPresent(Load(repo)).Passed.Should().BeTrue();
    }

    /// <summary>
    /// The count was fabricated with Math.Max(inspected, 1), so a check that inspected
    /// nothing reported 1 and passed. That is the exact vacuous pass the Inspected field
    /// exists to catch, and it applied to the two exemplar checks specifically.
    /// </summary>
    [Test]
    public void ACheckWithNothingToInspect_ReportsZero_NotAFabricatedOne()
    {
        using var repo = TempAdrRepo.Valid();

        var resolve = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        resolve.Inspected.Should().Be(0, "the baseline corpus names no local guard");
        resolve.NothingToCheck.Should().BeTrue();
        resolve
            .Passed.Should()
            .BeTrue("having nothing to check is legitimate here, but it is reported as such");
    }

    [Test]
    public void ACheckThatShouldHaveInspectedSomething_FailsWhenItInspectsNothing()
    {
        var result = new GuardResult("x", [], 0, "rule");

        result.Passed.Should().BeFalse();
        result.InspectedNothing.Should().BeTrue();
        result.NothingToCheck.Should().BeFalse("this check made no allowance for an empty scan");
    }

    /// <summary>
    /// The full table's Repos and Areas columns were parsed but never compared, so a row
    /// could name any repo and any area and stay green while the two tag tables above it
    /// were checked in both directions.
    /// </summary>
    [Test]
    public void FullList_TagColumnsAreChecked_NotJustTheTitle()
    {
        using var repo = TempAdrRepo.Valid();
        // Only the full-list row is edited. The By-area table still agrees with the
        // frontmatter, so a failure can only come from the column that was not checked.
        repo.Index(
            Sample
                .Index(central: false, Sample.DefaultSpec)
                .Replace(
                    "| Tests are deterministic | testing |",
                    "| Tests are deterministic | migrations |"
                )
        );

        IndexGuards
            .FullList(Load(repo), repo.Options())
            .Offenders.Should()
            .Contain(o => o.Contains("omits areas testing"));
    }

    /// <summary>
    /// The census scanned the whole Exemplars section while the exemplar checks stripped
    /// the "enforced elsewhere" paragraph first. Moving a real local class name into that
    /// paragraph therefore credited it to the census while hiding it from both resolution
    /// and cite-back.
    /// </summary>
    [Test]
    public void Census_DoesNotCreditAClassHiddenInTheElsewhereParagraph()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "**Enforced elsewhere:** `MigrationsIntegrityTests` allegedly lives in "
                    + "another repository, which is not true, and this sentence is long enough to pass."
            )
        );
        repo.Write(
            "tests/Some.Tests.Meta/MigrationsIntegrityTests.cs",
            "namespace Some.Tests.Meta;\n\npublic class MigrationsIntegrityTests { }\n"
        );

        CensusGuards
            .EveryGuardIsClassified(Load(repo), repo.Options(censusRoot: "tests/Some.Tests.Meta"))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("is named by no ADR and does not opt out");
    }

    /// <summary>
    /// The "enforced elsewhere" paragraph was stripped from the raw section, before the fences
    /// were. A fenced example quoting the marker therefore deleted the prose that followed it,
    /// taking a real claim with it: resolution and cite-back both went quiet rather than red.
    /// </summary>
    [Test]
    public void FencedElsewhereExample_DoesNotDeleteTheClaimBelowIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "The third state is written like this:\n\n"
                    + "```md\n**Enforced elsewhere:** `SomeOtherRepoTests` in every repo.\n```\n\n"
                    + "- `RealLocalTests` pins it here."
            )
        );

        var resolve = ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options());

        resolve.NothingToCheck.Should().BeFalse("the section makes one claim, in prose");
        resolve
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("RealLocalTests", "a claim under a fenced example is still a claim");
    }

    #region Cite-back demanded two places and checked one

    private const string GuardPath = "tests/Some.Tests.Meta/MigrationsIntegrityTests.cs";

    private static TempAdrRepo WithClaimedGuard(string source)
    {
        var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "- `MigrationsIntegrityTests` pins the numbering.")
        );
        repo.Write(GuardPath, source);
        return repo;
    }

    private static string Guard(string docLine, string codeLine) =>
        $$"""
            namespace Some.Tests.Meta;

            /// <summary>Numbering. {{docLine}}</summary>
            public class MigrationsIntegrityTests
            {
                public void Check() => Assert.Fail("{{codeLine}}");
            }
            """;

    private const string Adr0001 = "0001-tests-are-deterministic.md";

    [Test]
    public void CiteBack_InDocstringAndFailureMessage_Passes()
    {
        using var repo = WithClaimedGuard(Guard($"See {Adr0001}.", $"See {Adr0001}."));

        ExemplarGuards.NamedGuardsCiteBack(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    /// <summary>
    /// The weak form: a single stray mention anywhere in the file satisfied the check,
    /// while the failure message told the reader it had to be in two specific places.
    /// </summary>
    [Test]
    public void CiteBack_InDocstringOnly_Fails()
    {
        using var repo = WithClaimedGuard(Guard($"See {Adr0001}.", "no citation here"));

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not name it in a failure message");
    }

    /// <summary>
    /// The failure message alone is now enough. The docstring half of the old two-place rule
    /// was replaced by the attribute, which is what establishes the link; the message is what
    /// the person who trips the guard reads, so it is the half that still has to be there.
    /// </summary>
    [Test]
    public void CiteBack_InAFailureMessageOnly_Passes()
    {
        using var repo = WithClaimedGuard(Guard("no citation here", $"See {Adr0001}."));

        ExemplarGuards.NamedGuardsCiteBack(Load(repo), repo.Options()).Passed.Should().BeTrue();
    }

    [Test]
    public void CiteBack_Absent_Fails()
    {
        using var repo = WithClaimedGuard(Guard("nothing", "nothing"));

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not name it in a failure message");
    }

    #endregion

    #region Reading C# source, not mentions of it

    /// <summary>
    /// An ADR could claim a guard that does not exist, because class resolution ran a regex
    /// over raw file text: a commented-out declaration counted as the real thing.
    /// </summary>
    [Test]
    public void CommentedOutClass_DoesNotSatisfyAnEnforcementClaim()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "- `PhantomGuardTests` pins this.")
        );
        repo.Write(
            "tests/Some.Tests.Meta/Real.cs",
            "namespace Some.Tests.Meta;\n\n// public class PhantomGuardTests { }\npublic class RealTests { }\n"
        );

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("PhantomGuardTests");
    }

    [Test]
    public void ClassInsideARawStringLiteral_DoesNotSatisfyAnEnforcementClaim()
    {
        var quote = new string('"', 3);
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(exemplars: "- `TemplatedTests` pins this.")
        );
        repo.Write(
            "tests/Some.Tests.Meta/Fixture.cs",
            "namespace Some.Tests.Meta;\n\npublic class RealTests\n{\n"
                + $"    const string T = {quote}\n        public class TemplatedTests {{ }}\n        {quote};\n}}\n"
        );

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("TemplatedTests");
    }

    /// <summary>
    /// Cite-back demanded the docstring and the failure message but accepted any line that
    /// was not a docstring, so a plain comment satisfied it and a guard gutted to zero
    /// assertions kept its ADR's enforcement claim intact.
    /// </summary>
    [Test]
    public void CiteBack_InAnOrdinaryComment_DoesNotCountAsAFailureMessage()
    {
        using var repo = WithClaimedGuard(
            $$"""
            namespace Some.Tests.Meta;

            /// <summary>Numbering. See {{Adr0001}}.</summary>
            public class MigrationsIntegrityTests
            {
                // See {{Adr0001}} for why this exists.
                public void Check() { }
            }
            """
        );

        ExemplarGuards
            .NamedGuardsCiteBack(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not name it in a failure message");
    }

    #endregion

    /// <summary>A dotted name is prose. The suffix rule must not be what decides it.</summary>
    [Test]
    public void DottedClassName_IsProse_EvenWithTheTestsSuffix()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "- `Trax.Core.HygieneGuardsTests` lives upstream.\n\n**Unenforced:** "
                    + Sample.GoodReason
            )
        );

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Offenders.Should().BeEmpty();
        ExemplarGuards.SectionPresent(Load(repo)).Passed.Should().BeTrue();
    }
}
