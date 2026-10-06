namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// Ways the scanners reported success over input they had misread. Each of these passed
/// before the single-pass rewrite, which is the failure mode with no symptom: the guard does
/// not break, it goes blind.
/// </summary>
[TestFixture]
public class ScannerHardeningTests
{
    private static IReadOnlyList<Adr> Load(TempAdrRepo repo) => AdrCorpus.Discover(repo.Options());

    #region C# scanning

    [Test]
    public void ClassInsideABlockComment_DoesNotSatisfyAnEnforcementClaim()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(exemplars: "- `PhantomTests` pins this."));
        repo.Write(
            "tests/Some.Tests.Meta/Real.cs",
            "namespace Some.Tests.Meta;\n\n/* public class PhantomTests { } */\npublic class RealTests { }\n"
        );

        ExemplarGuards
            .NamedGuardsResolve(Load(repo), repo.Options())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("PhantomTests");
    }

    /// <summary>
    /// A single-line raw string used to flip the scanner on and leave it on, hiding every class
    /// declared after it. An entirely unclassified guard then passed the census.
    /// </summary>
    [Test]
    public void SingleLineRawString_DoesNotHideLaterClassesFromTheCensus()
    {
        var quote = new string('"', 3);
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            "tests/Some.Tests.Meta/Fixture.cs",
            "namespace Some.Tests.Meta;\n\npublic class FirstTests\n{\n"
                + $"    const string T = {quote}class InnerTests {{}}{quote};\n}}\n\n"
                + "public class TotallyUnclassifiedTests { }\n"
        );

        var offenders = CensusGuards
            .EveryGuardIsClassified(Load(repo), repo.Options(censusRoot: "tests/Some.Tests.Meta"))
            .Offenders;

        offenders.Should().Contain(o => o.Contains("TotallyUnclassifiedTests"));
        offenders
            .Should()
            .NotContain(o => o.Contains("InnerTests"), "a class inside a literal is data");
    }

    [Test]
    public void MultiLineClassDeclaration_IsSeenByTheCensusAndByResolution()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            "tests/Some.Tests.Meta/Split.cs",
            "namespace Some.Tests.Meta;\n\npublic sealed class\n    SneakyTests\n{ }\n"
        );

        CensusGuards
            .EveryGuardIsClassified(Load(repo), repo.Options(censusRoot: "tests/Some.Tests.Meta"))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "SneakyTests",
                "the census and resolution must agree about what a declaration is"
            );
    }

    /// <summary>
    /// A docstring planted inside a raw string literal, as an attribute argument, sat on the
    /// line directly above the declaration. Declarations were read from the blanked source
    /// and the docstring from the raw file, so the planted marker opted the class out and the
    /// census reported it classified.
    /// </summary>
    [Test]
    public void OptOutMarkerInsideAnAttributeLiteral_DoesNotAnswerForTheClassBelowIt()
    {
        var quote = new string('"', 3);
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            "tests/Some.Tests.Meta/Planted.cs",
            "namespace Some.Tests.Meta;\n\n"
                + $"[System.ComponentModel.Description({quote}\n"
                + "/// <para>Not ADR-enforcing: it pins a naming convention nobody weighed an "
                + $"alternative for.</para>{quote})]\n"
                + "public class PlantedTests { }\n"
        );

        CensusGuards
            .EveryGuardIsClassified(Load(repo), repo.Options(censusRoot: "tests/Some.Tests.Meta"))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "'PlantedTests' is named by no ADR",
                "a marker inside a string literal is not an answer"
            );
    }

    #endregion

    #region Markdown scanning

    [Test]
    public void TildeFenceInsideABacktickBlock_DoesNotSwallowLaterSections()
    {
        var text =
            "# T\n\n## Status\n\n```md\n~~~\nnot a fence\n~~~\n```\n\n## Exemplars\n\nreal\n";

        Markdown.Section(text, "Exemplars").Should().Be("real");
        Markdown.Section(text, "Status").Should().StartWith("```md");
    }

    [Test]
    public void ClaimInAnIndentedCodeBlock_IsAnExampleNotAClaim()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(
                exemplars: "The format looks like this:\n\n    - `ShownAsAnExampleTests` holds it up.\n\n"
                    + $"**Unenforced:** {Sample.GoodReason}"
            )
        );

        ExemplarGuards.NamedGuardsResolve(Load(repo), repo.Options()).Offenders.Should().BeEmpty();
        ExemplarGuards.SectionPresent(Load(repo)).Passed.Should().BeTrue();
    }

    #endregion

    #region Corpus discovery and frontmatter

    [Test]
    public void AdrInASubdirectory_IsStillChecked()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            "docs/adr/archive/0002-totally-broken.md",
            "no frontmatter, no title, nothing\n"
        );

        FrontmatterGuards
            .Parseable(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("0002-totally-broken.md", "moving an ADR into a subfolder must not exempt it");
    }

    [Test]
    public void SelfSupersession_IsRejected()
    {
        using var repo = TempAdrRepo.Valid();
        var spec = Sample.DefaultSpec;
        repo.Adr(
            spec.FileName,
            Sample.Adr(
                status: "superseded-by-0001",
                statusSection: $"**Superseded by** {spec.Link}. Supersedes {spec.Link}."
            )
        );

        LifecycleGuards
            .Supersessions(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("superseded by itself");
    }

    [TestCase("Date: 2026-09-11", "'date' is not an accepted key")]
    [TestCase("deadline: soon", "unexpected key(s) deadline")]
    public void UnexpectedFrontmatterKey_IsRejected(string line, string expected)
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(extraFrontmatter: line));

        FrontmatterGuards
            .NoDateKey(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(expected);
    }

    [Test]
    public void ChangelogWithAStarBullet_IsChecked_NotSkipped()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(
            Sample.DefaultSpec.FileName,
            Sample.Adr(changelog: "* 2020-01-01 no bold, no colon")
        );

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("malformed");
    }

    [Test]
    public void ImpossibleChangelogDate_IsRejected()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(changelog: "- **9999-99-99**: Recorded."));

        LifecycleGuards
            .Changelog(Load(repo))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("not real dates");
    }

    [Test]
    public void LowercaseReadme_IsFoundAsTheIndex()
    {
        using var repo = new TempAdrRepo();
        var spec = Sample.DefaultSpec;
        repo.Adr(spec.FileName, Sample.Adr());
        repo.Write("docs/adr/readme.md", Sample.Index(central: false, spec));

        IndexGuards
            .Exists(AdrCorpus.Discover(repo.Options()), repo.Options())
            .Passed.Should()
            .BeTrue("casing must not decide this differently on macOS and on Linux CI");
    }

    #endregion
}
