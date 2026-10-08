using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Utils;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Which scheduled runs resume from a checkpoint, on Postgres and SQLite: a manifest's retry and a
/// dead-letter requeue resume after the failed run's latest checkpoint whenever the train's chain
/// allows it, and rerun from the top otherwise; <c>requeueExecution</c> always reruns from the
/// top; the operator's resume queues one, and only one queued resume of a run exists at a time.
/// A resumed run skips every step before the checkpoint, including the write it already
/// committed, replays the decisions after it, and restores the same state on a remote worker.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property("adr", Adr)]
public class CheckpointResumeTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly string[] Before =
    [
        nameof(PlanResearch),
        nameof(WriteResearchNote),
        nameof(SearchDeep),
        nameof(FetchFullTexts),
    ];

    private readonly ResearchDecider _decider = new();
    private ResearchCluster _cluster = null!;

    [OneTimeSetUp]
    public async Task CreateCluster() => _cluster = await ResearchCluster.Create(store, _decider);

    [OneTimeTearDown]
    public async Task DisposeCluster() => await _cluster.DisposeAsync();

    [SetUp]
    public async Task Reset()
    {
        ResearchProbe.Reset();
        _decider.Reset();
        await _cluster.Reset();
    }

    [TearDown]
    public void ResetProbe() => ResearchProbe.Reset();

    [Test]
    public async Task A_manifest_retry_after_a_crash_in_Summarize_does_not_rerun_FetchFullTexts()
    {
        var manifest = await _cluster.Manifest("crash");
        var failed = await Crash(manifest, nameof(SummarizeLong));

        var retry = await _cluster.Cycle(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ResumeFrom.Should().Be(failed.Id, $"the retry resumes the run it retries ({Adr})");
        retry.ResumeAt.Should().BeNull("it resumes after the latest checkpoint");
        var entry = await _cluster.With(d =>
            d.WorkQueues.AsNoTracking().SingleAsync(q => q.MetadataId == retry.Id)
        );
        entry.ResumeFrom.Should().Be(failed.Id);
        ResearchProbe.RanFor("crash").Should().Equal([nameof(SummarizeLong)]);

        // The junction events of each run: the retry has none of the steps before the checkpoint.
        var original = await _cluster.JunctionRuns(failed.Id);
        var skipped = original.Where(j => Before.Contains(j.Name)).Select(j => j.NodeId).ToList();
        skipped
            .Should()
            .HaveCount(
                Before.Length,
                "the failed run ran every step before it; its rows: "
                    + string.Join(", ", original.Select(j => $"{j.Name}@{j.NodeId}"))
            );
        var resumed = await _cluster.JunctionRuns(retry.Id);
        resumed
            .Select(j => j.NodeId)
            .Should()
            .NotIntersectWith(skipped, $"the resumed run skipped them in place ({Adr})")
            .And.Contain(original.Single(j => j.Name == nameof(SummarizeLong)).NodeId);
        resumed.Should().NotContain(j => j.Name == nameof(FetchFullTexts));
    }

    [Test]
    public async Task A_write_before_the_checkpoint_exists_once_after_a_crash_and_a_resume()
    {
        var manifest = await _cluster.Manifest("note");
        await Crash(manifest, nameof(SummarizeLong));

        (await _cluster.Cycle(manifest)).TrainState.Should().Be(TrainState.Completed);

        (await Notes("note"))
            .Should()
            .Be(1, $"the write committed before the checkpoint is not repeated ({Adr})");
    }

    [Test]
    public async Task A_second_failure_after_a_resume_resumes_from_the_same_checkpoint()
    {
        var manifest = await _cluster.Manifest("twice");
        var first = await Crash(manifest, nameof(SummarizeLong));

        // The first retry resumes and fails again, after the checkpoint, writing none of its own.
        ResearchProbe.FailIn = nameof(SummarizeLong);
        var second = await _cluster.Cycle(manifest);
        second.TrainState.Should().Be(TrainState.Failed);
        second.ResumeFrom.Should().Be(first.Id);
        (await _cluster.Checkpoints(second.Id)).Should().BeEmpty();

        ResearchProbe.Reset();
        var third = await _cluster.Cycle(manifest);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        third.ResumeFrom.Should().Be(second.Id);
        ResearchProbe
            .RanFor("twice")
            .Should()
            .Equal(
                [nameof(SummarizeLong)],
                $"the first run's checkpoint is followed back through the resumed run ({Adr})"
            );
    }

    [Test]
    public async Task Decisions_after_the_checkpoint_replay_on_resume()
    {
        var manifest = await _cluster.Manifest("decide", replayDecisionsOnRetry: true);
        var failed = await Crash(manifest, nameof(SummarizeLong));
        _decider.Asked.Should().Be(2, "the failed run asked both questions");

        // A decider asked now would choose the other tracks.
        _decider.Depth = ResearchDepth.Quick;
        _decider.Style = SummaryStyle.Short;
        var retry = await _cluster.Cycle(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ResumeFrom.Should().Be(failed.Id);
        retry.ReplayDecisionsOf.Should().Be(failed.Id);
        ResearchProbe
            .RanFor("decide")
            .Should()
            .Equal(
                [nameof(SummarizeLong)],
                $"the question after the checkpoint took the recorded track ({Adr})"
            );
        _decider
            .Asked.Should()
            .Be(2, "the question before the checkpoint was skipped and the one after replayed");
    }

    [Test]
    public async Task A_dead_letter_requeue_resumes_and_requeueExecution_reruns_from_the_top()
    {
        var manifest = await _cluster.Manifest("dead", maxRetries: 0);
        var failed = await Crash(manifest, nameof(SummarizeLong));

        // Dead-lettered on the next cycle, then requeued by an operator.
        await _cluster.Host.RunManifestManager();
        long requeued;
        using (var scope = _cluster.Host.Services.CreateScope())
        {
            var deadLetter = await _cluster.With(d =>
                d.DeadLetters.AsNoTracking().SingleAsync(x => x.ManifestId == manifest.Id)
            );
            var result = await scope
                .ServiceProvider.GetRequiredService<ITraxScheduler>()
                .RequeueDeadLetterAsync(deadLetter.Id);
            result.Success.Should().BeTrue(result.Message);
            requeued = result.WorkQueueId!.Value;
        }

        var entry = await _cluster.Entry(requeued);
        entry.ResumeFrom.Should().Be(failed.Id, $"a dead letter's requeue resumes ({Adr})");
        var resumed = await _cluster.DispatchAndRun(entry);
        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        ResearchProbe.RanFor("dead").Should().Equal([nameof(SummarizeLong)]);

        ResearchProbe.Reset();
        var again = await _cluster.Operation(o =>
            o.RequeueExecutionAsync(failed.Id, CancellationToken.None)
        );
        again.Success.Should().BeTrue(again.Message);
        var rerunEntry = await _cluster.Entry(again.Id!.Value);
        rerunEntry.ResumeFrom.Should().BeNull("requeueExecution keeps its meaning: run it again");
        var rerun = await _cluster.DispatchAndRun(rerunEntry);

        rerun.TrainState.Should().Be(TrainState.Completed, rerun.FailureReason);
        ResearchProbe
            .RanFor("dead")
            .Should()
            .StartWith(Before, $"a requeue reruns the chain from the top ({Adr})");
    }

    [Test]
    public async Task A_run_without_a_checkpoint_retries_from_the_top()
    {
        var manifest = await _cluster.Manifest("early");
        var failed = await Crash(manifest, nameof(FetchFullTexts));
        (await _cluster.Checkpoints(failed.Id)).Should().BeEmpty();

        var retry = await _cluster.Cycle(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ResumeFrom.Should().BeNull("nothing was checkpointed");
        ResearchProbe.RanFor("early").Should().StartWith(Before);
    }

    [TestCase("chain")]
    [TestCase("state")]
    public async Task An_old_checkpoint_after_a_state_or_chain_change_is_refused_and_the_retry_reruns_from_the_top(
        string changed
    )
    {
        var manifest = await _cluster.Manifest("deploy-" + changed);
        var failed = await Crash(manifest, nameof(SummarizeLong));

        // As a deploy that changed the chain, or the state's shape, leaves the stored row.
        await _cluster.With(async d =>
        {
            foreach (
                var row in await d.Checkpoints.Where(c => c.MetadataId == failed.Id).ToListAsync()
            )
                if (changed == "chain")
                    row.ChainHash = new string('0', 64);
                else
                    row.StateFingerprint = "an-older-shape";
            await d.SaveChanges(CancellationToken.None);
            return 0;
        });

        var retry = await _cluster.Cycle(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ResumeFrom.Should().BeNull($"the stale checkpoint is refused ({Adr})");
        ResearchProbe.RanFor("deploy-" + changed).Should().StartWith(Before);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task An_operator_resume_racing_a_manifest_retry_queues_exactly_one(bool retryFirst)
    {
        var manifest = await _cluster.Manifest("race-" + retryFirst);
        var failed = await Crash(manifest, nameof(SummarizeLong));

        OperationResult operatorResume;
        using (var scope = _cluster.Host.Services.CreateScope())
        {
            var operations = (OperationsService)
                scope.ServiceProvider.GetRequiredService<IOperationsService>();

            // The retry is queued after the operator's checks and before its insert, or after it.
            if (retryFirst)
                operations.BeforeResumeEnqueue = async _ =>
                    await _cluster.Host.RunManifestManager();
            operatorResume = await operations
                .ResumeExecutionAsync(failed.Id, null, CancellationToken.None)
                .WaitAsync(Bound);
        }
        if (!retryFirst)
            await _cluster.Host.RunManifestManager();

        var queuedResumes = await _cluster.With(d =>
            d.WorkQueues.AsNoTracking()
                .Where(q => q.ResumeFrom == failed.Id && q.Status == WorkQueueStatus.Queued)
                .ToListAsync()
        );
        queuedResumes.Should().ContainSingle($"one queued resume per run ({Adr})");
        var retry = await _cluster.QueuedEntry(manifest.Id);

        if (retryFirst)
        {
            operatorResume.Success.Should().BeFalse();
            operatorResume.Message.Should().Contain("already queued");
            retry.ResumeFrom.Should().Be(failed.Id);
        }
        else
        {
            operatorResume.Success.Should().BeTrue(operatorResume.Message);
            queuedResumes.Single().Id.Should().Be(operatorResume.Id);
            retry.ResumeFrom.Should().BeNull("the retry lost the race and reruns from the top");
        }
    }

    [Test]
    public async Task An_operator_resume_queues_a_resume_at_the_named_step_through_the_mediator()
    {
        var manifest = await _cluster.Manifest("operator");
        var failed = await Crash(manifest, nameof(SummarizeLong));

        // The question after the checkpoint, named by its node as the run graph shows it, rather
        // than left to the latest checkpoint.
        var step = (await _cluster.JunctionRuns(failed.Id))
            .Single(j =>
                j.Kind == JunctionRunKind.Choice && j.QuestionKey == QuestionKey.For<SummaryStyle>()
            )
            .NodeId!;
        var result = await _cluster.Operation(o =>
            o.ResumeExecutionAsync(failed.Id, step, CancellationToken.None)
        );

        result.Success.Should().BeTrue(result.Message);
        var entry = await _cluster.Entry(result.Id!.Value);
        entry.ResumeFrom.Should().Be(failed.Id);
        entry.ResumeAt.Should().Be(step);
        entry.ManifestId.Should().BeNull("it is queued through the mediator, as a requeue is");
        var resumed = await _cluster.DispatchAndRun(entry);
        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        resumed.ResumeAt.Should().Be(step);
        ResearchProbe.RanFor("operator").Should().Equal([nameof(SummarizeLong)]);
        resumed
            .ReplayDecisionsOf.Should()
            .Be(
                failed.Id,
                "a resume replays the decisions of the run it resumes, as a requeue does"
            );
        _decider.Asked.Should().Be(2, "the style question replayed and the depth one was skipped");
    }

    [Test]
    public async Task An_operator_resume_is_refused_with_a_reason_for_each_case()
    {
        var manifest = await _cluster.Manifest("refusals");
        var early = await Crash(manifest, nameof(FetchFullTexts));
        var late = await Crash(manifest, nameof(SummarizeLong));
        var completed = await _cluster.Cycle(manifest);
        completed.TrainState.Should().Be(TrainState.Completed);

        (await Resume(9_999_999)).Should().Be("Execution 9999999 not found.");
        (await Resume(completed.Id))
            .Should()
            .Contain("is Completed; only a failed or cancelled run can be resumed");
        (await Resume(early.Id))
            .Should()
            .Contain("checkpoint", "the check's own reason is given verbatim");
        (await Resume(late.Id, "FetchFullTexts#0"))
            .Should()
            .Contain("No checkpoint", "the step comes before the checkpoint");

        await _cluster.With(d =>
            d.Metadatas.Where(m => m.Id == late.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.InvokingMachine, "Research.Machine"))
        );
        (await Resume(late.Id))
            .Should()
            .Contain("state machine 'Research.Machine'")
            .And.Contain("cannot be resumed");
    }

    [Test]
    public async Task A_second_operator_resume_while_one_is_queued_is_refused()
    {
        var manifest = await _cluster.Manifest("again");
        var failed = await Crash(manifest, nameof(SummarizeLong));

        var first = await _cluster.Operation(o =>
            o.ResumeExecutionAsync(failed.Id, null, CancellationToken.None)
        );
        first.Success.Should().BeTrue(first.Message);

        (await Resume(failed.Id)).Should().Contain("already queued");
    }

    [Test]
    public async Task A_resume_on_a_remote_worker_restores_the_checkpoint()
    {
        await using var remote = _cluster.Worker(
            _decider,
            s => s.AddTraxJobRunner(o => o.AllowUnsignedRequests())
        );
        var manifest = await _cluster.Manifest("remote");
        var failed = await Crash(manifest, nameof(SummarizeLong));

        await _cluster.Host.RunManifestManager();
        var entry = await _cluster.QueuedEntry(manifest.Id);
        entry.ResumeFrom.Should().Be(failed.Id);
        var runId = await _cluster.Dispatch(entry);

        // The request the HTTP submitter sends, across the wire, run on the worker.
        var input = _cluster.Host.HeldInput(runId)!;
        var sent = new RemoteJobRequest(
            runId,
            JsonSerializer.Serialize(
                input,
                input.GetType(),
                TraxJsonSerializationOptions.ManifestProperties
            ),
            input.GetType().FullName
        );
        var received = JsonSerializer.Deserialize<RemoteJobRequest>(
            JsonSerializer.Serialize(sent)
        )!;
        using (var scope = remote.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<ITraxRequestHandler>()
                .ExecuteJobAsync(received)
                .WaitAsync(Bound);

        var resumed = await _cluster.Host.Run(runId);
        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        resumed.ResumeFrom.Should().Be(failed.Id);
        resumed.Output.Should().Contain("long deep summary of remote, 12 pages");
        ResearchProbe
            .RanFor("remote")
            .Should()
            .Equal([nameof(SummarizeLong)], $"the worker restored the stored state ({Adr})");
    }

    [Test]
    public async Task A_resumed_run_cancelled_through_its_cancel_flag_on_another_host_is_recorded_cancelled()
    {
        // The dashboard's cancel and the API's are this call; on another host, it reaches the run
        // only through the database.
        var other = _cluster.Cluster.Host(
            machines: false,
            scheduler: true,
            configure: s => s.AddSingleton<IDecider>(_decider)
        );
        var manifest = await _cluster.Manifest("cancel");
        var failed = await Crash(manifest, nameof(SummarizeLong));
        var runId = await DispatchRetry(manifest, failed);

        // Held while it asks the style question, between the checkpoint and SummarizeLong.
        _decider.HoldNextAsk();
        var job = Task.Run(() => _cluster.Host.RunJob(runId));
        (await _decider.Asking.WaitAsync(Bound))
            .Should()
            .BeTrue($"the resumed run asks the question after the checkpoint ({Adr})");

        using (var scope = other.Services.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .CancelExecutionsAsync([runId], CancellationToken.None)
            )
                .Count.Should()
                .Be(1);
        _decider.Release();
        await job.WaitAsync(Bound);

        var run = await _cluster.Host.Run(runId);
        run.TrainState.Should()
            .Be(
                TrainState.Cancelled,
                $"a resumed run reads its cancel flag before its next junction, as any run does ({Adr})"
            );
        run.CancellationRequested.Should().BeTrue();
        run.ResumeFrom.Should().Be(failed.Id);
        ResearchProbe
            .RanFor("cancel")
            .Should()
            .BeEmpty(
                "the cancel stopped it before SummarizeLong, and it skipped every step before"
            );
    }

    [Test]
    public async Task A_resumed_run_past_its_job_timeout_is_recorded_cancelled()
    {
        var manifest = await _cluster.Manifest("timeout", timeoutSeconds: 60);
        var failed = await Crash(manifest, nameof(SummarizeLong));
        var runId = await DispatchRetry(manifest, failed);

        _decider.HoldNextAsk();
        var job = Task.Run(() => _cluster.Host.RunJob(runId));
        (await _decider.Asking.WaitAsync(Bound))
            .Should()
            .BeTrue($"the resumed run asks the question after the checkpoint ({Adr})");

        // The resumed run has been going for longer than its manifest allows.
        await _cluster.Host.Age(runId, TimeSpan.FromMinutes(5));
        await _cluster.Host.RunManifestManager();
        (await _cluster.Host.Run(runId))
            .CancellationRequested.Should()
            .BeTrue("the manifest manager flags a run past its timeout");
        _decider.Release();
        await job.WaitAsync(Bound);

        var run = await _cluster.Host.Run(runId);
        run.TrainState.Should()
            .Be(
                TrainState.Cancelled,
                $"the timeout reaches a resumed run as it reaches any run ({Adr})"
            );
        run.ResumeFrom.Should().Be(failed.Id);
        ResearchProbe.RanFor("timeout").Should().BeEmpty();
    }

    [Test]
    public async Task A_resumed_run_reports_progress_only_for_the_steps_it_runs()
    {
        var manifest = await _cluster.Manifest("progress");
        var failed = await Crash(manifest, nameof(SummarizeLong));
        var runId = await DispatchRetry(manifest, failed);

        // Held in the first junction it runs, whichever that is: had it run a step before the
        // checkpoint, that step would be the one held and reported.
        ResearchProbe.HoldIn = ResearchProbe.AnyJunction;
        var job = Task.Run(() => _cluster.Host.RunJob(runId));
        (await ResearchProbe.Held.WaitAsync(Bound)).Should().BeTrue("the resumed run starts");

        var running = await _cluster.Host.Run(runId);
        running
            .CurrentlyRunningJunction.Should()
            .Be(
                nameof(SummarizeLong),
                $"the first junction a resumed run reports is the first after its checkpoint ({Adr})"
            );
        running.JunctionStartedAt.Should().NotBeNull();
        ResearchProbe.RanFor("progress").Should().Equal([nameof(SummarizeLong)]);

        ResearchProbe.Release();
        await job.WaitAsync(Bound);

        var run = await _cluster.Host.Run(runId);
        run.TrainState.Should().Be(TrainState.Completed, run.FailureReason);
        run.CurrentlyRunningJunction.Should().BeNull("every junction it ran has ended");
        ResearchProbe.RanFor("progress").Should().Equal([nameof(SummarizeLong)]);
    }

    [Test]
    public async Task A_resumed_retry_waits_out_its_backoff()
    {
        await using var delayed = await ResearchCluster.Create(
            store,
            _decider,
            s => s.DefaultRetryDelay(TimeSpan.FromMinutes(10)).MaxRetryDelay(TimeSpan.FromHours(1))
        );
        var manifest = await delayed.Manifest("backoff");
        ResearchProbe.FailIn = nameof(SummarizeLong);
        var failed = await delayed.Cycle(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        ResearchProbe.Reset();

        await delayed.Host.RunManifestManager();
        var retry = await delayed.QueuedEntry(manifest.Id);
        retry.ResumeFrom.Should().Be(failed.Id, $"the retry resumes ({Adr})");
        retry
            .ScheduledAt.Should()
            .BeAfter(
                DateTime.UtcNow.AddMinutes(9),
                $"a retry that resumes waits out the same backoff as one that reruns ({Adr})"
            );

        using (var scope = delayed.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);
        (await delayed.Entry(retry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "the dispatcher leaves it until its backoff is over");
        ResearchProbe.RanFor("backoff").Should().BeEmpty();

        // The backoff over.
        await delayed.With(d =>
            d.WorkQueues.Where(q => q.Id == retry.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(q => q.ScheduledAt, DateTime.UtcNow.AddMinutes(-1))
                )
        );
        var resumed = await delayed.DispatchAndRun(await delayed.Entry(retry.Id));

        resumed.TrainState.Should().Be(TrainState.Completed, resumed.FailureReason);
        resumed.ResumeFrom.Should().Be(failed.Id);
        ResearchProbe.RanFor("backoff").Should().Equal([nameof(SummarizeLong)]);
    }

    /// <summary>
    /// Queues <paramref name="manifest"/>'s retry, checks it resumes <paramref name="failed"/>, and
    /// dispatches it without running it.
    /// </summary>
    private async Task<long> DispatchRetry(
        Effect.Models.Manifest.Manifest manifest,
        Metadata failed
    )
    {
        await _cluster.Host.RunManifestManager();
        var entry = await _cluster.QueuedEntry(manifest.Id);
        entry.ResumeFrom.Should().Be(failed.Id, $"the retry resumes the failed run ({Adr})");
        return await _cluster.Dispatch(entry);
    }

    /// <summary>Runs <paramref name="manifest"/> once with a crash in <paramref name="junction"/>.</summary>
    private async Task<Metadata> Crash(Effect.Models.Manifest.Manifest manifest, string junction)
    {
        ResearchProbe.FailIn = junction;
        var failed = await _cluster.Cycle(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        ResearchProbe.Reset();
        return failed;
    }

    private async Task<string?> Resume(long runId, string? from = null) =>
        (
            await _cluster.Operation(o =>
                o.ResumeExecutionAsync(runId, from, CancellationToken.None)
            )
        ).Message;

    private Task<int> Notes(string topic) =>
        _cluster.With(d =>
            d.ManifestGroups.AsNoTracking()
                .CountAsync(g => g.Name.StartsWith(ResearchProbe.NotePrefix + topic + "-"))
        );
}
