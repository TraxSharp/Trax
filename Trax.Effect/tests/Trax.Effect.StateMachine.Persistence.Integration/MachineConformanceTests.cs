using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Testing;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The three drift checks a machine with a generated frontend twin needs, folded into one call
/// (<see cref="MachineConformance"/>). Each covers something the others cannot: the IR check catches a
/// machine edit that skipped regeneration, the corpus check catches a build that embedded a different golden
/// than the one committed, and the self-check catches an engine that no longer reproduces it. These tests
/// drive each check to both verdicts over real files on disk, plus the aggregate, because a check that
/// silently returns "no problems" for a missing or stale artifact is worse than no check.
/// </summary>
public class MachineConformanceTests
{
    private const string PassCorpus = """
        {"cases":[{"given":{"machine":"conformance-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"{\"machine\":\"conformance-pass\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"}},{"given":{"machine":"conformance-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Push"},"expect":{"outcome":"rejected","reason":"no-transition"}}]}
        """;

    private const string DriftCorpus = """
        {"cases":[{"given":{"machine":"conformance-drift","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"WRONG"}}]}
        """;

    /// <summary>Declarative (so it can export an IR) and ships a corpus its own engine reproduces.</summary>
    private sealed class ConformingMachine : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "conformance-pass");

        public override string? Corpus => PassCorpus;
    }

    /// <summary>Embeds a corpus the engine does NOT reproduce, so the self-check leg fires.</summary>
    private sealed class DriftingMachine : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "conformance-drift");

        public override string? Corpus => DriftCorpus;
    }

    private string _dir = null!;
    private string _ir = null!;
    private string _corpus = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-conformance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _ir = Path.Combine(_dir, "machine.ir.json");
        _corpus = Path.Combine(_dir, "differential.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Commit both artifacts exactly as the machine would generate them.</summary>
    private void CommitArtifacts(IMachine machine)
    {
        File.WriteAllText(_ir, machine.ExportIr() + "\n");
        File.WriteAllText(_corpus, machine.Corpus!);
    }

    #region Check

    [Test]
    public void Check_returns_no_problems_when_the_ir_the_corpus_and_the_engine_all_agree()
    {
        var machine = new ConformingMachine();
        CommitArtifacts(machine);

        MachineConformance.Check(machine, _ir, _corpus).Should().BeEmpty();
    }

    [Test]
    public void Check_reports_all_three_kinds_of_drift_at_once()
    {
        var machine = new DriftingMachine();
        File.WriteAllText(_ir, "{\"stale\":true}");
        File.WriteAllText(_corpus, PassCorpus); // committed golden is not the embedded one

        var problems = MachineConformance.Check(machine, _ir, _corpus);

        problems.Should().HaveCount(3);
        problems.Should().ContainSingle(p => p.StartsWith("IR: "));
        problems.Should().ContainSingle(p => p.StartsWith("corpus: "));
        problems.Should().ContainSingle(p => p.StartsWith("self-check: "));
    }

    [Test]
    public void Check_surfaces_a_self_check_diff_when_only_the_engine_disagrees()
    {
        // Both artifacts are exactly what the machine ships, so the only thing left to catch is the engine
        // failing to reproduce the corpus it embeds.
        var machine = new DriftingMachine();
        CommitArtifacts(machine);

        MachineConformance
            .Check(machine, _ir, _corpus)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("self-check: the embedded corpus no longer replays:");
    }

    #endregion

    #region CheckIr

    [Test]
    public void CheckIr_reports_a_committed_ir_that_was_never_written()
    {
        var missing = Path.Combine(_dir, "absent.ir.json");

        MachineConformance
            .CheckIr(new ConformingMachine(), missing)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(missing);
    }

    [Test]
    public void CheckIr_reports_a_committed_ir_that_no_longer_matches_the_machine()
    {
        File.WriteAllText(_ir, "{\"machine\":\"something-else\"}");

        MachineConformance
            .CheckIr(new ConformingMachine(), _ir)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("machine.ir.json")
            .And.Contain("ExportIr()");
    }

    [Test]
    public void CheckIr_accepts_the_committed_ir_with_or_without_a_trailing_newline()
    {
        var machine = new ConformingMachine();

        File.WriteAllText(_ir, machine.ExportIr());
        MachineConformance.CheckIr(machine, _ir).Should().BeEmpty();

        File.WriteAllText(_ir, machine.ExportIr() + "\n");
        MachineConformance.CheckIr(machine, _ir).Should().BeEmpty();
    }

    #endregion

    #region CheckCorpus

    [Test]
    public void CheckCorpus_is_empty_when_the_machine_embeds_none_and_none_is_committed()
    {
        MachineConformance
            .CheckCorpus(new DeclarativeTurnstileMachine(), _corpus)
            .Should()
            .BeEmpty();
    }

    [Test]
    public void CheckCorpus_reports_a_committed_corpus_for_a_machine_that_embeds_none()
    {
        File.WriteAllText(_corpus, PassCorpus);

        MachineConformance
            .CheckCorpus(new DeclarativeTurnstileMachine(), _corpus)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ships no embedded corpus");
    }

    [Test]
    public void CheckCorpus_reports_a_missing_file_for_a_machine_that_embeds_one()
    {
        MachineConformance
            .CheckCorpus(new ConformingMachine(), _corpus)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("none is committed");
    }

    [Test]
    public void CheckCorpus_reports_a_committed_corpus_that_differs_from_the_embedded_one()
    {
        File.WriteAllText(_corpus, DriftCorpus);

        MachineConformance
            .CheckCorpus(new ConformingMachine(), _corpus)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("built against a different golden");
    }

    [Test]
    public void CheckCorpus_accepts_the_committed_corpus_with_a_trailing_newline()
    {
        var machine = new ConformingMachine();
        File.WriteAllText(_corpus, machine.Corpus + "\n");

        MachineConformance.CheckCorpus(machine, _corpus).Should().BeEmpty();
    }

    #endregion

    #region WriteIr

    [Test]
    public void WriteIr_writes_the_export_with_a_trailing_newline()
    {
        var machine = new ConformingMachine();

        MachineConformance.WriteIr(machine, _ir);

        File.ReadAllText(_ir).Should().Be(machine.ExportIr() + "\n");
    }

    [Test]
    public void WriteIr_overwrites_a_stale_ir_so_CheckIr_passes_again()
    {
        var machine = new ConformingMachine();
        File.WriteAllText(_ir, "{\"stale\":true}");
        MachineConformance.CheckIr(machine, _ir).Should().NotBeEmpty();

        MachineConformance.WriteIr(machine, _ir);

        MachineConformance.CheckIr(machine, _ir).Should().BeEmpty();
    }

    #endregion
}
