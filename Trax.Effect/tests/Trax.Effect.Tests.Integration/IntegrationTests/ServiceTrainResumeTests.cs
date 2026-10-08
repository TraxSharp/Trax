using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Tests.Integration.Fakes.Trains;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A service train whose row names a run to resume (<c>resume_from</c>, and <c>resume_at</c> when
/// an operator named a step) reads that run's lineage from <c>trax.checkpoint</c> and skips to its
/// latest checkpoint, on each data provider. A lineage of resumes is followed back to the nearest
/// checkpoint, and a resume that can no longer be trusted runs the chain from the top, saying why.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture(CheckpointStoreKind.Postgres)]
[TestFixture(CheckpointStoreKind.Sqlite)]
[TestFixture(CheckpointStoreKind.InMemory)]
[NonParallelizable]
[Property("adr", Adr)]
public class ServiceTrainResumeTests(CheckpointStoreKind store)
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private static readonly string[] EveryStep =
    [
        nameof(PlanResearch),
        nameof(SearchPapers),
        nameof(FetchFullTexts),
        nameof(ScoreFindings),
        nameof(Summarize),
    ];

    private readonly ScriptedResearchDecider _decider = new();
    private CheckpointHost _host = null!;
    private readonly List<long> _runs = [];

    [OneTimeSetUp]
    public void CreateHost() =>
        _host = CheckpointHost.Create(
            store,
            _decider,
            services => services.AddScopedTraxRoute<IResearchTrain, ResearchTrain>()
        );

    [OneTimeTearDown]
    public async Task DisposeHost() => await _host.DisposeAsync();

    [SetUp]
    public void Reset()
    {
        CheckpointProbe.Reset();
        _decider.Reset();
        _runs.Clear();
        _host.Logs.Clear();
    }

    [TearDown]
    public async Task DeleteRuns()
    {
        CheckpointProbe.Reset();
        await _host.Delete([.. _runs]);
    }

    [Test]
    public async Task A_run_naming_a_failed_run_skips_to_its_latest_checkpoint()
    {
        var failed = await Crash(nameof(Summarize));

        var resumed = await Run(resumeFrom: failed.Id);

        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        CheckpointProbe
            .Ran.Should()
            .Equal(
                [nameof(Summarize)],
                $"every step before the latest checkpoint is skipped ({Adr})"
            );
        _decider.Asked.Should().Be(0, "a skipped routing step asks no decider");
        resumed.ResumeFrom.Should().Be(failed.Id);
        (await _host.Checkpoints(resumed.Id)).Should().BeEmpty("a completed run keeps none");
    }

    [Test]
    public async Task A_resume_at_a_named_step_skips_to_it()
    {
        var failed = await Crash(nameof(Summarize));

        var resumed = await Run(resumeFrom: failed.Id, resumeAt: "ScoreFindings#0");

        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        CheckpointProbe.Ran.Should().Equal([nameof(ScoreFindings), nameof(Summarize)]);
    }

    [Test]
    public async Task A_lineage_of_two_resumes_picks_the_nearest_checkpoint()
    {
        // The first run reaches only the first checkpoint; the run resuming it writes the second
        // and fails after it; the run resuming that one starts after the second.
        var first = await Crash(nameof(ScoreFindings));
        (await _host.Checkpoints(first.Id)).Should().ContainSingle();

        CheckpointProbe.Reset();
        CheckpointProbe.FailIn = nameof(Summarize);
        var second = await Run(resumeFrom: first.Id);
        second.TrainState.Should().Be(TrainState.Failed);
        CheckpointProbe.Ran.Should().Equal([nameof(ScoreFindings), nameof(Summarize)]);
        (await _host.Checkpoints(second.Id))
            .Select(c => c.NodeId)
            .Should()
            .Equal(
                ["Checkpoint<Scored>#0"],
                "a resumed run writes only the checkpoints after its point"
            );

        CheckpointProbe.Reset();
        var third = await Run(resumeFrom: second.Id);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        CheckpointProbe
            .Ran.Should()
            .Equal([nameof(Summarize)], $"the second run's checkpoint is the nearest ({Adr})");
    }

    [Test]
    public async Task A_second_failure_before_any_new_checkpoint_resumes_from_the_same_one()
    {
        var first = await Crash(nameof(ScoreFindings));

        CheckpointProbe.Reset();
        CheckpointProbe.FailIn = nameof(ScoreFindings);
        var second = await Run(resumeFrom: first.Id);
        second.TrainState.Should().Be(TrainState.Failed);
        (await _host.Checkpoints(second.Id)).Should().BeEmpty();

        CheckpointProbe.Reset();
        var third = await Run(resumeFrom: second.Id);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        CheckpointProbe
            .Ran.Should()
            .Equal(
                [nameof(ScoreFindings), nameof(Summarize)],
                $"the first run's checkpoint is followed back through resume_from ({Adr})"
            );
    }

    [Test]
    public async Task A_resume_whose_checkpoint_no_longer_matches_runs_from_the_top_with_a_warning()
    {
        var failed = await Crash(nameof(Summarize));
        await _host.Tamper(failed.Id, row => row.ChainHash = new string('0', 64));

        var resumed = await Run(resumeFrom: failed.Id);

        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        CheckpointProbe.Ran.Should().Equal(EveryStep, $"a full rerun stays possible ({Adr})");
        _host
            .Logs.Should()
            .Contain(l =>
                l.Level == LogLevel.Warning
                && l.Message.Contains($"cannot resume run ({failed.Id}) and runs from the top")
            );
    }

    [Test]
    public async Task A_resume_of_a_run_with_no_checkpoint_runs_from_the_top_with_a_warning()
    {
        var failed = await Crash(nameof(FetchFullTexts));
        (await _host.Checkpoints(failed.Id)).Should().BeEmpty();

        var resumed = await Run(resumeFrom: failed.Id);

        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        CheckpointProbe.Ran.Should().Equal(EveryStep);
        _host
            .Logs.Should()
            .Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("runs from the top"));
    }

    private async Task<Models.Metadata.Metadata> Crash(string junction)
    {
        CheckpointProbe.FailIn = junction;
        var failed = await Run();
        failed.TrainState.Should().Be(TrainState.Failed);
        CheckpointProbe.Reset();
        _decider.Asked = 0;
        return failed;
    }

    private async Task<Models.Metadata.Metadata> Run(
        long? resumeFrom = null,
        string? resumeAt = null
    )
    {
        var (run, _) = await _host.Run<IResearchTrain>("graphs", resumeFrom, resumeAt);
        _runs.Add(run.Id);
        return run;
    }
}
