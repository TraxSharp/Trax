namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// Composition of the checks, and the reporting contract.
///
/// <para>
/// Enforces <c>adr/0001-architectural-rules-are-executable-guards.md</c>: a guard that
/// cannot fail reads as coverage while enforcing nothing, so every check must report what
/// it inspected and inspecting zero is a failure rather than a pass.
/// </para>
/// </summary>
[TestFixture]
public class GuardRunnerTests
{
    #region Discovery

    /// <summary>
    /// The regression this whole design exists to prevent. nwyc's equivalent fixture
    /// passed nine of its thirteen assertions over an empty set until a discovery check
    /// was added: a renamed or emptied directory turns every "no violations" result
    /// vacuous at once, and nothing says so.
    /// </summary>
    [Test]
    public void Run_EmptyCorpus_FailsDiscovery_RatherThanPassingEveryCheckOverNothing()
    {
        using var repo = new TempAdrRepo();

        var results = GuardRunner.Run(repo.Options());

        results.Should().ContainSingle();
        results[0].Name.Should().Be("corpus/discovery");
        results[0].Passed.Should().BeFalse();
        results[0].Offenders.Should().ContainSingle().Which.Should().Contain("no ADRs found");
    }

    [Test]
    public void Run_MissingDirectory_FailsDiscovery()
    {
        using var repo = new TempAdrRepo();
        repo.Write("README.md", "# A repo with no ADR directory at all\n");

        GuardRunner.Run(repo.Options()).Should().ContainSingle().Which.Passed.Should().BeFalse();
    }

    /// <summary>
    /// The checks a repo-local corpus runs, as a contract. "More than ten" would still pass
    /// with eight of them deleted. The census is absent because it only runs when a root is
    /// passed, and is pinned separately below.
    /// </summary>
    private static readonly string[] RepoLocalRoster =
    [
        "corpus/discovery",
        "frontmatter/parseable",
        "frontmatter/authors",
        "frontmatter/areas",
        "frontmatter/repos",
        "frontmatter/status",
        "frontmatter/keys",
        "frontmatter/file-names",
        "lifecycle/status-section",
        "lifecycle/supersessions",
        "lifecycle/changelog",
        "exemplars/section",
        "exemplars/guards-resolve",
        "exemplars/guards-cite-back",
        "index/exists",
        "index/areas-table",
        "index/full-list",
        "hygiene/no-em-dashes",
        "hygiene/title",
    ];

    /// <summary>
    /// Every check must account for what it looked at: it inspected something, or it said it
    /// had nothing to inspect. The three that legitimately inspect nothing over this corpus
    /// are named, because AllowsEmpty switches off the vacuous-pass protection and a fourth
    /// check quietly claiming it is exactly what this test is here to catch.
    /// </summary>
    [Test]
    public void Run_ValidCorpus_EveryCheckEitherInspectsSomethingOrSaysItHadNothingToCheck()
    {
        using var repo = TempAdrRepo.Valid();

        var results = GuardRunner.Run(repo.Options());

        results.Select(r => r.Name).Should().BeEquivalentTo(RepoLocalRoster);

        results
            .Where(r => r.InspectedNothing)
            .Should()
            .BeEmpty(
                "a check that inspected nothing has not passed, it has failed to look. A check "
                    + "that legitimately has nothing to check says so with AllowsEmpty instead. "
                    + "See adr/0001-architectural-rules-are-executable-guards.md."
            );

        results
            .Where(r => r.NothingToCheck)
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(
                [
                    "lifecycle/supersessions",
                    "exemplars/guards-resolve",
                    "exemplars/guards-cite-back",
                ],
                "the baseline corpus supersedes nothing and names no local guard, so those "
                    + "three have nothing to look at. Any other check reaching zero here has "
                    + "gone vacuous, and its AllowsEmpty would hide it."
            );
    }

    /// <summary>
    /// The census is the one check no roster above can pin, because it is added only when a
    /// root is passed. Left unpinned, switching it off is a quieter run rather than a failure.
    /// </summary>
    [Test]
    public void Run_WithACensusRoot_AddsTheCensusToTheRoster_AndRunsIt()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            "tests/Some.Tests.Meta/MigrationsIntegrityTests.cs",
            """
            namespace Some.Tests.Meta;

            /// <summary>
            /// Migration files are numbered sequentially.
            ///
            /// <para>Not ADR-enforcing: it pins a file naming convention nobody weighed an
            /// alternative for.</para>
            /// </summary>
            public class MigrationsIntegrityTests { }
            """
        );

        var results = GuardRunner.Run(repo.Options(censusRoot: "tests/Some.Tests.Meta"));
        var failures = results.Where(r => !r.Passed).ToList();

        results
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo([.. RepoLocalRoster, "census/classified"]);
        results.Single(r => r.Name == "census/classified").Inspected.Should().Be(1);
        failures.Should().BeEmpty(string.Join("\n", failures.SelectMany(f => f.Offenders)));
    }

    #endregion

    #region End to end

    [Test]
    public void Run_ValidRepoLocalCorpus_AllChecksPass()
    {
        using var repo = TempAdrRepo.Valid();

        var failures = GuardRunner.Run(repo.Options()).Where(r => !r.Passed).ToList();

        failures.Should().BeEmpty(string.Join("\n", failures.SelectMany(f => f.Offenders)));
    }

    [Test]
    public void Run_ValidCentralCorpus_AllChecksPass()
    {
        using var repo = TempAdrRepo.Valid(central: true);
        var options = repo.Options(requireReposKey: true);

        var failures = GuardRunner.Run(options).Where(r => !r.Passed).ToList();

        failures.Should().BeEmpty(string.Join("\n", failures.SelectMany(f => f.Offenders)));
    }

    [Test]
    public void Run_CentralCorpus_ChecksTheByRepoTable_WhichALocalOneDoesNot()
    {
        using var repo = TempAdrRepo.Valid(central: true);

        var central = GuardRunner.Run(repo.Options(requireReposKey: true)).Select(r => r.Name);
        var local = GuardRunner.Run(repo.Options()).Select(r => r.Name);

        central.Should().Contain("index/repos-table");
        local.Should().NotContain("index/repos-table");
    }

    #endregion

    #region Reporting

    [Test]
    public void Report_WhenEverythingPasses_ReturnsZero()
    {
        using var repo = TempAdrRepo.Valid();
        var output = new StringWriter();

        var exitCode = GuardRunner.Report(GuardRunner.Run(repo.Options()), output);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("ADR checks passed");
    }

    [Test]
    public void Report_WhenSomethingFails_ReturnsOne_AndPrintsTheRuleAndTheOffender()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(areas: "[invented]"));
        var output = new StringWriter();

        var exitCode = GuardRunner.Report(GuardRunner.Run(repo.Options()), output);
        var text = output.ToString();

        exitCode.Should().Be(1);
        text.Should().Contain("FAIL  frontmatter/areas");
        text.Should()
            .Contain("closed vocabulary", "the report explains the rule, not just the violation");
        text.Should().Contain("unknown area(s) invented", "and names the offender");
    }

    #endregion
}
