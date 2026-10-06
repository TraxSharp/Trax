using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ChangeSignal;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest's retry, made by the ManifestManager or by a dead-letter requeue, replays the
/// decisions its failed run recorded instead of asking the decider again, and only when that is
/// sound: the source is the manifest's failed run, read from the database, a run of its train that
/// recorded its decisions, asked them itself, has not had them replayed into a failure already,
/// and was queued by the manifest with the input the retry is given. Anything else, the lookup
/// failing included, asks afresh rather than failing the retry. An operator can ask afresh
/// explicitly, and a manifest can opt out.
///
/// <para>Enforces <c>docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md")]
public class ManifestRetryReplaysDecisionsTests
{
    private const string Adr =
        "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md";

    private ServiceProvider _provider = null!;

    // A host built for one test, in place of the shared one. Disposed by that test.
    private IServiceProvider? _override;

    private IServiceProvider Provider => _override ?? _provider;
    private ScriptedDecider _decider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _decider = new ScriptedDecider();
        _provider = BuildProvider(_decider);
    }

    private static ServiceProvider BuildProvider(
        IDecider decider,
        Action<IServiceCollection>? configure = null,
        Action<SchedulerConfigurationBuilder>? configureScheduler = null,
        bool recordDecisions = true
    )
    {
        var services = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton(decider)
            .AddTrax(trax =>
                (
                    recordDecisions
                        ? trax.AddEffects(effects =>
                            effects
                                .SaveTrainParameters()
                                .UsePostgres(TestPostgres.ConnectionString)
                                .AddDecisionRecording()
                                .AddJson()
                        )
                        : trax.AddEffects(effects =>
                            effects
                                .SaveTrainParameters()
                                .UsePostgres(TestPostgres.ConnectionString)
                                .AddJson()
                        )
                )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    // No backoff, so a retry is dispatched on the cycle that queues it.
                    .AddScheduler(scheduler =>
                    {
                        scheduler.UseInMemoryWorkers().DefaultRetryDelay(TimeSpan.Zero);
                        configureScheduler?.Invoke(scheduler);
                        return scheduler;
                    })
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            );
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public async Task Clean()
    {
        DecisionProbe.Reset();
        Answer(ProbeLane.Slow, ProbeSize.Large);

        using var scope = Provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [TearDown]
    public void ResetProbe() => DecisionProbe.Reset();

    [Test]
    public async Task A_retry_takes_the_failed_runs_tracks_without_asking_the_decider()
    {
        var manifest = await CreateManifestAsync("retry");
        var before = _decider.Requests.Count;

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        (_decider.Requests.Count - before).Should().Be(2, "the first run asked both questions");

        // A decider asked now would send the retry down the other tracks.
        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .Be(failed.Id, $"the retry replays the run it retries. See {Adr}");
        (await EntryOf(retry.Id)).ReplayDecisionsOf.Should().Be(failed.Id);
        (_decider.Requests.Count - before)
            .Should()
            .Be(2, $"the retry asked nothing: two questions in all, not four. See {Adr}");
        DecisionProbe
            .TracksOf("retry")
            .Should()
            .Equal(["Slow", "Large", "Slow", "Large"], "the retry took the failed run's tracks");
        (await DecisionsOf(retry.Id))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(d => d.Replayed, "both answers came from the failed run");
    }

    [Test]
    public async Task A_retry_replays_once_and_the_retry_after_a_failed_replay_asks_afresh()
    {
        var manifest = await CreateManifestAsync("once", maxRetries: 3);
        var before = _decider.Requests.Count;

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var first = await CycleAsync(manifest);
        first.TrainState.Should().Be(TrainState.Failed);

        // The first retry replays the first run's answers, and fails with them.
        var second = await CycleAsync(manifest);
        second.TrainState.Should().Be(TrainState.Failed);
        second.ReplayDecisionsOf.Should().Be(first.Id);
        (_decider.Requests.Count - before).Should().Be(2, "the first retry asked nothing");

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var third = await CycleAsync(manifest);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        third
            .ReplayDecisionsOf.Should()
            .BeNull($"answers already replayed into a failure are not replayed again. See {Adr}");
        (_decider.Requests.Count - before)
            .Should()
            .Be(4, "the second retry asked both questions afresh");
        DecisionProbe
            .TracksOf("once")
            .Should()
            .Equal(["Slow", "Large", "Slow", "Large", "Fast", "Small"]);
    }

    [Test]
    public async Task A_dead_letter_requeue_after_a_failed_replay_asks_afresh()
    {
        // One retry: the first run fails, its replay fails, and the manifest is dead-lettered.
        var manifest = await CreateManifestAsync("trapped", maxRetries: 1);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var first = await CycleAsync(manifest);
        var replay = await CycleAsync(manifest);
        replay.ReplayDecisionsOf.Should().Be(first.Id);
        replay.TrainState.Should().Be(TrainState.Failed);

        await RunManifestManagerAsync();
        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var requeued = await RequeueDeadLetterAndRunAsync(manifest, askAfresh: false);

        requeued.TrainState.Should().Be(TrainState.Completed, requeued.FailureReason);
        requeued
            .ReplayDecisionsOf.Should()
            .BeNull($"the requeue does not repeat the answers that already failed. See {Adr}");
        DecisionProbe.TracksOf("trapped").TakeLast(2).Should().Equal("Fast", "Small");
    }

    [Test]
    public async Task A_manifest_retry_asks_afresh_once_a_requeue_replayed_its_failed_run_and_failed()
    {
        var manifest = await CreateManifestAsync("requeued", maxRetries: 3);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // An operator requeues the failed run itself. Its run replays the failed run's answers
        // and fails too. It belongs to no manifest, so the manifest never retries it directly.
        long requeueEntry;
        using (var scope = Provider.CreateScope())
        {
            var result = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .RequeueExecutionAsync(failed.Id, CancellationToken.None);
            result.Success.Should().BeTrue(result.Message);
            requeueEntry = result.Id!.Value;
        }
        var requeued = await DispatchEntryAsync(requeueEntry);
        requeued.TrainState.Should().Be(TrainState.Failed);
        requeued.ReplayDecisionsOf.Should().Be(failed.Id);
        requeued
            .ManifestId.Should()
            .BeNull("a requeue through the operations service is no manifest's run");

        await AssertRetryAsksAfreshAsync(manifest, "requeued");
    }

    [Test]
    public async Task An_occurrence_after_an_acknowledged_dead_letter_asks_afresh()
    {
        var manifest = await CreateManifestAsync("acknowledged", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // Dead-lettered, then acknowledged: the failure no longer counts, so the next run is an
        // ordinary occurrence, not a retry of it.
        await RunManifestManagerAsync();
        using (var scope = Provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
            var deadLetterId = await WithData(data =>
                data.DeadLetters.AsNoTracking()
                    .Where(d => d.ManifestId == manifest.Id)
                    .Select(d => d.Id)
                    .SingleAsync()
            );
            (await scheduler.AcknowledgeDeadLetterAsync(deadLetterId, "handled"))
                .Success.Should()
                .BeTrue();
        }

        await AssertRetryAsksAfreshAsync(manifest, "acknowledged");
    }

    [Test]
    public async Task A_dependent_retried_before_its_parent_succeeds_again_replays()
    {
        var parent = await CreateManifestAsync("parent");
        var dependent = await CreateManifestAsync("dependent", dependsOn: parent);
        (await CycleAsync(parent)).TrainState.Should().Be(TrainState.Completed);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(dependent);
        failed.TrainState.Should().Be(TrainState.Failed);

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var retry = await CycleAsync(dependent);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ReplayDecisionsOf.Should().Be(failed.Id, "no new parent success: this is a retry");
    }

    [Test]
    public async Task A_dependent_fired_by_a_new_parent_success_asks_afresh()
    {
        var parent = await CreateManifestAsync("parent-again");
        var dependent = await CreateManifestAsync("fired", dependsOn: parent);
        (await CycleAsync(parent)).TrainState.Should().Be(TrainState.Completed);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(dependent);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The parent succeeds again: the dependent's next run is fired by that success.
        DecisionProbe.FailAt = ProbeFailure.None;
        using (var scope = Provider.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<ITraxScheduler>()
                .TriggerAsync(parent.ExternalId);
        (await DispatchQueuedAsync(parent)).TrainState.Should().Be(TrainState.Completed);

        await AssertRetryAsksAfreshAsync(dependent, "fired");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_dead_letter_requeue_asks_afresh_while_something_already_replays_the_failed_run(
        bool queuedEntry
    )
    {
        var manifest = await CreateManifestAsync("in-flight", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        await RunManifestManagerAsync();

        // An operator's requeue of the failed run is queued, or already running.
        await WithData(async data =>
        {
            if (queuedEntry)
                data.WorkQueues.Add(
                    WorkQueue.Create(
                        new CreateWorkQueue
                        {
                            TrainName = manifest.Name,
                            Input = manifest.Properties,
                            InputTypeName = manifest.PropertyTypeName,
                            ReplayDecisionsOf = failed.Id,
                            ScheduledAt = DateTime.UtcNow.AddHours(1),
                        }
                    )
                );
            else
            {
                var running = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = manifest.Name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                        ReplayDecisionsOf = failed.Id,
                    }
                );
                running.TrainState = TrainState.InProgress;
                data.Metadatas.Add(running);
            }
            await data.SaveChanges(CancellationToken.None);
            return true;
        });

        var deadLetterId = await WithData(data =>
            data.DeadLetters.AsNoTracking()
                .Where(d => d.ManifestId == manifest.Id)
                .Select(d => d.Id)
                .SingleAsync()
        );
        using (var scope = Provider.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ITraxScheduler>()
                    .RequeueDeadLetterAsync(deadLetterId)
            )
                .Success.Should()
                .BeTrue();

        (await QueuedEntryOf(manifest))
            .ReplayDecisionsOf.Should()
            .BeNull($"the failed run's answers are already being replayed once. See {Adr}");
    }

    [Test]
    public async Task A_retry_after_cleanup_swept_the_failed_replay_asks_afresh_on_a_host_without_cleanup()
    {
        await using var swept = BuildProvider(
            _decider,
            configureScheduler: scheduler =>
                scheduler.AddMetadataCleanup(c =>
                    c.AddTrainType<IDecisionProbeTrain>(TimeSpan.FromMinutes(5))
                )
        );
        _override = swept;
        Manifest manifest;
        try
        {
            manifest = await CreateManifestAsync("swept", maxRetries: 3);

            DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
            var first = await CycleAsync(manifest);
            var replay = await CycleAsync(manifest);
            replay.ReplayDecisionsOf.Should().Be(first.Id);

            // Both runs are older than the retention. The sweep deletes the replay together with
            // the run it replayed, never the replay alone, which would leave that run looking as
            // though nothing had replayed it.
            await WithData(async data =>
            {
                await data
                    .Metadatas.Where(m => m.Id == first.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-11))
                    );
                return await data
                    .Metadatas.Where(m => m.Id == replay.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-10))
                    );
            });
            using (var scope = Provider.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<IMetadataCleanupTrain>()
                    .Run(new MetadataCleanupRequest());
            (await WithData(data => data.Metadatas.AnyAsync(m => m.Id == replay.Id)))
                .Should()
                .BeFalse();
            (await WithData(data => data.Metadatas.AnyAsync(m => m.Id == first.Id)))
                .Should()
                .BeFalse($"a replay is deleted only with the run it replays. See {Adr}");
        }
        finally
        {
            _override = null;
        }

        // The retry is queued by a host that runs no cleanup and knows no retention.
        await AssertRetryAsksAfreshAsync(manifest, "swept");
    }

    [Test]
    public async Task A_linked_retry_whose_source_is_deleted_before_it_runs_asks_afresh()
    {
        var manifest = await CreateManifestAsync("vanished");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The retry is queued with its link, then the run it names is deleted outside Trax.
        await RunManifestManagerAsync();
        (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().Be(failed.Id);
        await WithData(async data =>
        {
            await data.RecordedDecisions.Where(d => d.MetadataId == failed.Id).ExecuteDeleteAsync();
            await data.WorkQueues.Where(q => q.MetadataId == failed.Id).ExecuteDeleteAsync();
            return await data.Metadatas.Where(m => m.Id == failed.Id).ExecuteDeleteAsync();
        });

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var retry = await DispatchQueuedAsync(manifest);

        retry
            .TrainState.Should()
            .Be(
                TrainState.Completed,
                $"a manifest's retry that cannot replay asks afresh rather than failing. See {Adr}"
            );
        retry.ReplayDecisionsOf.Should().Be(failed.Id, "the link reached the run");
        (_decider.Requests.Count - asked).Should().Be(2, "both questions were asked afresh");
        DecisionProbe.TracksOf("vanished").TakeLast(2).Should().Equal("Fast", "Small");
    }

    [Test]
    public async Task A_linked_retry_on_a_host_that_records_no_decisions_asks_afresh()
    {
        var manifest = await CreateManifestAsync("unrecording-host");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        await RunManifestManagerAsync();
        (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().Be(failed.Id);

        // The retry is claimed and run by a host without AddDecisionRecording.
        await using var unrecording = BuildProvider(_decider, recordDecisions: false);
        _override = unrecording;
        try
        {
            DecisionProbe.FailAt = ProbeFailure.None;
            Answer(ProbeLane.Fast, ProbeSize.Small);
            var asked = _decider.Requests.Count;
            var retry = await DispatchQueuedAsync(manifest);

            retry
                .TrainState.Should()
                .Be(
                    TrainState.Completed,
                    $"a manifest's retry that cannot replay asks afresh rather than failing. See {Adr}"
                );
            retry.ReplayDecisionsOf.Should().Be(failed.Id, "the link reached the run");
            (_decider.Requests.Count - asked).Should().Be(2);
            DecisionProbe.TracksOf("unrecording-host").TakeLast(2).Should().Equal("Fast", "Small");
        }
        finally
        {
            _override = null;
        }
    }

    [Test]
    public async Task A_retry_whose_recorded_question_no_longer_matches_asks_it_afresh()
    {
        var manifest = await CreateManifestAsync("reworded");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // What a reworded question, or one offered different options, leaves behind: answers
        // recorded under a fingerprint the question no longer has.
        await WithData(data =>
            data.RecordedDecisions.Where(d => d.MetadataId == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Fingerprint, "an-older-question"))
        );

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .Be(failed.Id, "the run is linked; the answers no longer fit");
        (_decider.Requests.Count - asked)
            .Should()
            .Be(2, $"an answer given to another question is not replayed. See {Adr}");
        DecisionProbe
            .TracksOf("reworded")
            .Should()
            .Equal(["Slow", "Large", "Fast", "Small"], "the retry took the fresh answers");
    }

    [TestCase(DeadLetterRequeue.Single, false)]
    [TestCase(DeadLetterRequeue.Batch, false)]
    [TestCase(DeadLetterRequeue.All, false)]
    [TestCase(DeadLetterRequeue.Single, true)]
    [TestCase(DeadLetterRequeue.Batch, true)]
    [TestCase(DeadLetterRequeue.All, true)]
    public async Task A_dead_letter_requeue_replays_the_failed_runs_decisions_unless_asked_afresh(
        DeadLetterRequeue how,
        bool askAfresh
    )
    {
        var manifest = await CreateManifestAsync("dead-letter", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // No retries: the next cycle dead-letters the manifest instead of queueing one.
        await RunManifestManagerAsync();

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;

        var requeued = await RequeueDeadLetterAndRunAsync(manifest, askAfresh, how);

        requeued.TrainState.Should().Be(TrainState.Completed, requeued.FailureReason);
        if (askAfresh)
        {
            requeued
                .ReplayDecisionsOf.Should()
                .BeNull($"the operator asked the requeue to ask afresh. See {Adr}");
            (_decider.Requests.Count - asked).Should().Be(2);
            DecisionProbe
                .TracksOf("dead-letter")
                .Should()
                .Equal(["Slow", "Large", "Fast", "Small"]);
        }
        else
        {
            requeued
                .ReplayDecisionsOf.Should()
                .Be(failed.Id, $"a dead-letter requeue retries the failed run. See {Adr}");
            (_decider.Requests.Count - asked).Should().Be(0, "the requeue asked nothing");
            DecisionProbe
                .TracksOf("dead-letter")
                .Should()
                .Equal(["Slow", "Large", "Slow", "Large"]);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_trigger_that_releases_a_queued_retry_keeps_its_replay_unless_asked_afresh(
        bool askAfresh
    )
    {
        var manifest = await CreateManifestAsync("triggered");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The retry is queued with its link; an operator triggers the manifest before it runs.
        await RunManifestManagerAsync();
        using (var scope = Provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
            if (askAfresh)
                await scheduler.TriggerAsync(manifest.ExternalId, askAfresh: true);
            else
                await scheduler.TriggerAsync(manifest.ExternalId);
        }

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var retry = await DispatchQueuedAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .Be(
                askAfresh ? null : failed.Id,
                $"a trigger asked to ask afresh clears the link. See {Adr}"
            );
        DecisionProbe
            .TracksOf("triggered")
            .TakeLast(2)
            .Should()
            .Equal(askAfresh ? ["Fast", "Small"] : ["Slow", "Large"]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_manifest_that_stops_replaying_while_its_retry_waits_asks_afresh(
        bool throughOperations
    )
    {
        var manifest = await CreateManifestAsync("flipped");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The retry is queued, linked, and waits; meanwhile the manifest stops replaying.
        await RunManifestManagerAsync();
        (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().Be(failed.Id);

        if (throughOperations)
        {
            using var scope = Provider.CreateScope();
            var result = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .SetManifestsReplayDecisionsOnRetryAsync(
                    [manifest.Id],
                    false,
                    CancellationToken.None
                );
            result.Success.Should().BeTrue();
            result.Count.Should().Be(1);
            (await QueuedEntryOf(manifest))
                .ReplayDecisionsOf.Should()
                .BeNull("turning the flag off clears the queued retry's link at once");
        }
        else
            // A write that leaves the queued link in place: the dispatcher must still honour it.
            await WithData(data =>
                data.Manifests.Where(m => m.Id == manifest.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplayDecisionsOnRetry, false))
            );

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var retry = await DispatchQueuedAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .BeNull($"the manifest no longer replays decisions on retry. See {Adr}");
        (await EntryOf(retry.Id)).ReplayDecisionsOf.Should().BeNull();
        (_decider.Requests.Count - asked).Should().Be(2);
    }

    [Test]
    public async Task A_retry_whose_source_lookup_fails_is_queued_to_ask_afresh()
    {
        var manifest = await CreateManifestAsync("lookup-fails");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // A host whose replay lookup cannot reach its store.
        await using var faulty = BuildProvider(
            _decider,
            services =>
                services.AddScoped(_ => new RetryDecisionReplay(
                    new UnreachableStore(),
                    NullLogger.Instance
                ))
        );
        _override = faulty;
        try
        {
            await AssertRetryAsksAfreshAsync(manifest, "lookup-fails");
        }
        finally
        {
            _override = null;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_manifest_that_opted_out_asks_afresh_on_retry(bool deadLetter)
    {
        var manifest = await CreateManifestAsync(
            "opted-out",
            maxRetries: deadLetter ? 0 : 3,
            replayDecisionsOnRetry: false
        );
        var before = _decider.Requests.Count;

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);

        Metadata retry;
        if (deadLetter)
        {
            await RunManifestManagerAsync();
            retry = await RequeueDeadLetterAndRunAsync(manifest, askAfresh: false);
        }
        else
            retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .BeNull($"the manifest set ReplayDecisionsOnRetry(false). See {Adr}");
        (_decider.Requests.Count - before)
            .Should()
            .Be(4, "the failed run and its retry each asked both questions");
        DecisionProbe
            .TracksOf("opted-out")
            .Should()
            .Equal(["Slow", "Large", "Fast", "Small"], "the retry took the fresh answers");
    }

    [Test]
    public async Task A_retry_given_a_different_input_asks_afresh()
    {
        var manifest = await CreateManifestAsync("before-edit");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The manifest's properties are edited between the failure and its retry. The answers
        // were given about the old input.
        var edited = manifest.Properties!.Replace("before-edit", "after-edit");
        await WithData(data =>
            data.Manifests.Where(m => m.Id == manifest.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Properties, edited))
        );

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .BeNull($"answers given about one input are not replayed into another. See {Adr}");
        (await EntryOf(retry.Id)).ReplayDecisionsOf.Should().BeNull();
        (_decider.Requests.Count - asked).Should().Be(2, "the retry asked both questions afresh");
        DecisionProbe.TracksOf("after-edit").Should().Equal("Fast", "Small");
    }

    [Test]
    public async Task A_retry_of_a_run_that_did_not_record_its_decisions_asks_afresh()
    {
        var manifest = await CreateManifestAsync("unrecorded");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // What a run on a host that did not record decisions leaves: it may have acted on
        // answers nobody can know. Replaying it would fail the retry permanently.
        await WithData(data =>
            data.Metadatas.Where(m => m.Id == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.DecisionsRecorded, false))
        );

        await AssertRetryAsksAfreshAsync(manifest, "unrecorded");
    }

    [Test]
    public async Task A_retry_of_a_run_whose_queue_entry_is_gone_asks_afresh()
    {
        var manifest = await CreateManifestAsync("entry-gone");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);

        // With no entry there is no input to compare the retry's with.
        await WithData(data =>
            data.WorkQueues.Where(q => q.MetadataId == failed.Id).ExecuteDeleteAsync()
        );

        await AssertRetryAsksAfreshAsync(manifest, "entry-gone");
    }

    [Test]
    public async Task A_retry_does_not_compare_against_a_queue_entry_of_another_manifest()
    {
        var other = await CreateManifestAsync("elsewhere");
        var manifest = await CreateManifestAsync("own-entry");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);

        // The failed run's entry claims another manifest: it was not queued by this one.
        await WithData(data =>
            data.WorkQueues.Where(q => q.MetadataId == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.ManifestId, (long?)other.Id))
        );

        await AssertRetryAsksAfreshAsync(manifest, "own-entry");
    }

    [Test]
    public async Task A_retry_of_a_run_queued_under_a_subject_key_asks_afresh()
    {
        var manifest = await CreateManifestAsync("subject");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);

        await WithData(data =>
            data.WorkQueues.Where(q => q.MetadataId == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.SubjectKey, "tenant-a"))
        );

        await AssertRetryAsksAfreshAsync(manifest, "subject");
    }

    [Test]
    public async Task A_retry_whose_input_type_differs_from_the_failed_runs_asks_afresh()
    {
        var manifest = await CreateManifestAsync("retyped");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);

        // Same JSON, read as another type: not the input the answers were given about.
        await WithData(data =>
            data.WorkQueues.Where(q => q.MetadataId == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.InputTypeName, "Some.Other.Input"))
        );

        await AssertRetryAsksAfreshAsync(manifest, "retyped");
    }

    [Test]
    public async Task A_retry_does_not_replay_a_failed_run_recorded_under_another_train()
    {
        var manifest = await CreateManifestAsync("renamed");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        await WithData(data =>
            data.Metadatas.Where(m => m.Id == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Name, "Some.Other.ITrain"))
        );

        await AssertRetryAsksAfreshAsync(manifest, "renamed");
    }

    [Test]
    public async Task An_occurrence_after_a_success_replays_nothing()
    {
        var manifest = await CreateManifestAsync("ordinary");
        var completed = await CycleAsync(manifest);
        completed.TrainState.Should().Be(TrainState.Completed, completed.FailureReason);

        // Due again at once, as an ordinary occurrence rather than a retry.
        await WithData(data =>
            data.Manifests.Where(m => m.Id == manifest.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(m => m.LastSuccessfulRun, DateTime.UtcNow.AddHours(-2))
                )
        );

        var next = await CycleAsync(manifest);
        next.ReplayDecisionsOf.Should().BeNull($"only a retry replays. See {Adr}");
    }

    [Test]
    public void No_public_scheduler_api_accepts_a_run_to_replay()
    {
        // The retry's source is read from the database by the scheduler. A public parameter or
        // settable property that names a replay and could carry a run (an id or an external id)
        // would let a caller point a run at any other run's answers. The bool opt-out names no
        // run, so it is not one.
        const BindingFlags members =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        // A result the scheduler returns reports the run a replay names, or whether an answer was
        // replayed; no API accepts one back.
        Type[] results =
        [
            typeof(ManifestTriggerResult),
            typeof(RecordedDecisionRecord),
            typeof(WorkQueueEntryDetail),
        ];

        var offending = typeof(ITraxScheduler)
            .Assembly.GetExportedTypes()
            .Except(results)
            .SelectMany(type =>
                type.GetMethods(members)
                    .Concat<MethodBase>(type.GetConstructors(members))
                    .SelectMany(m => m.GetParameters())
                    .Where(p =>
                        p.Name?.Contains("Replay", StringComparison.OrdinalIgnoreCase) == true
                        && CouldNameARun(p.ParameterType)
                    )
                    .Select(p => $"{type.FullName}.{p.Member.Name}({p.Name})")
                    .Concat(
                        type.GetProperties(members)
                            .Where(p =>
                                p.CanWrite
                                && p.Name.Contains("Replay", StringComparison.OrdinalIgnoreCase)
                                && CouldNameARun(p.PropertyType)
                            )
                            .Select(p => $"{type.FullName}.{p.Name}")
                    )
            )
            .ToList();

        offending
            .Should()
            .BeEmpty($"only the scheduler chooses the run a retry replays. See {Adr}");

        static bool CouldNameARun(Type type) =>
            (Nullable.GetUnderlyingType(type) ?? type) is var t && t != typeof(bool) && !t.IsEnum;
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_dependents_retry_dates_its_failed_run_by_its_dispatch_not_its_start(
        bool dispatchedAfterTheParentsSuccess
    )
    {
        var parent = await CreateManifestAsync("clock-parent");
        var dependent = await CreateManifestAsync("clock", dependsOn: parent);
        (await CycleAsync(parent)).TrainState.Should().Be(TrainState.Completed);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(dependent);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The parent's LastSuccessfulRun and the entry's DispatchedAt are stamped by the database's
        // clock, the run's StartTime by its host's. Here the two clocks disagree about whether the
        // parent succeeded after the failed run.
        var success = DateTime.UtcNow.AddMinutes(-10);
        var startedAt = dispatchedAfterTheParentsSuccess
            ? success.AddMinutes(-5)
            : success.AddMinutes(5);
        var dispatchedAt = dispatchedAfterTheParentsSuccess
            ? success.AddMinutes(5)
            : success.AddMinutes(-5);
        await WithData(async data =>
        {
            await data
                .Metadatas.Where(m => m.Id == failed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, startedAt));
            await data
                .WorkQueues.Where(q => q.MetadataId == failed.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(q => q.DispatchedAt, (DateTime?)dispatchedAt)
                );
            return await data
                .Manifests.Where(m => m.Id == parent.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(m => m.LastSuccessfulRun, (DateTime?)success)
                );
        });

        var stored = await WithData(data =>
            data.Manifests.AsNoTracking().SingleAsync(m => m.Id == dependent.Id)
        );
        using var scope = Provider.CreateScope();
        var source = await scope
            .ServiceProvider.GetRequiredService<RetryDecisionReplay>()
            .SourceForRetryAsync(stored, CancellationToken.None);

        source
            .Should()
            .Be(
                dispatchedAfterTheParentsSuccess ? failed.Id : null,
                "the parent's success is compared with the failed run's dispatch, both by the "
                    + $"database's clock. See {Adr}"
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_requeue_of_a_failed_run_replays_it_unless_asked_afresh(bool askAfresh)
    {
        var manifest = await CreateManifestAsync("requeue-afresh", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        using var scope = Provider.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .RequeueExecutionAsync(failed.Id, askAfresh, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        (await WithData(data => data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == result.Id)))
            .ReplayDecisionsOf.Should()
            .Be(askAfresh ? null : failed.Id, $"an operator can requeue without replay. See {Adr}");
    }

    [Test]
    public async Task A_requeue_of_a_failed_run_a_queued_retry_already_replays_asks_afresh()
    {
        var manifest = await CreateManifestAsync("retry-then-requeue");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The ManifestManager queues the retry, linked to the failed run, and then an operator
        // requeues the failed run itself before the retry is dispatched.
        await RunManifestManagerAsync();
        (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().Be(failed.Id);

        OperationResult result;
        using (var scope = Provider.CreateScope())
            result = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .RequeueExecutionAsync(failed.Id, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("afresh");
        (await WithData(data => data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == result.Id)))
            .ReplayDecisionsOf.Should()
            .BeNull($"the failed run's answers are already being replayed once. See {Adr}");
        (await QueuedEntryOf(manifest))
            .ReplayDecisionsOf.Should()
            .Be(failed.Id, "the retry queued first keeps its replay");
    }

    [Test]
    public async Task Two_manifest_managers_queueing_the_same_retry_at_once_queue_one_entry_that_replays_it()
    {
        var manifest = await CreateManifestAsync("racing");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // Two ManifestManagers (outside the leader lock, which would otherwise keep them apart)
        // both find the retry due and both look up its source before either inserts.
        var gate = new LookupGate(participants: 2);
        var logs = new ExceptionCapture();
        void Configure(IServiceCollection services) =>
            services
                .AddSingleton<ILoggerProvider>(logs)
                .AddScoped<RetryDecisionReplay>(sp => new GatedLookup(
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    gate
                ));
        await using var first = BuildProvider(_decider, Configure);
        await using var second = BuildProvider(_decider, Configure);

        static async Task Cycle(IServiceProvider host)
        {
            using var scope = host.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IManifestManagerTrain>()
                .Run(Unit.Default);
        }
        await Task.WhenAll(Cycle(first), Cycle(second));

        var entries = await WithData(data =>
            data.WorkQueues.AsNoTracking()
                .Where(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued)
                .ToListAsync()
        );
        entries.Should().ContainSingle("a manifest has one queued entry at a time");
        entries[0]
            .ReplayDecisionsOf.Should()
            .Be(failed.Id, $"the entry that won replays the failed run. See {Adr}");

        // The loser was refused by the one-queued-entry-per-manifest index, whichever index the
        // database checked first: a refusal by the queued-replay index alone is retried unlinked.
        logs.Exceptions.Should()
            .ContainSingle("only the second insert is refused")
            .Which.ToString()
            .Should()
            .Contain("ix_work_queue_unique_queued_manifest");

        var retry = await DispatchQueuedAsync(manifest);
        retry.ReplayDecisionsOf.Should().Be(failed.Id);
    }

    [Test]
    public async Task A_retry_that_loses_the_race_inside_the_leader_transaction_is_queued_to_ask_afresh()
    {
        var manifest = await CreateManifestAsync("leader-race");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // As A_retry_that_loses_the_race_for_a_replay_is_queued_to_ask_afresh, but through the
        // polling service's real cycle: the ManifestManager runs inside the leader transaction,
        // where the refused insert survives only because EF Core rolls back to the savepoint it
        // takes before each save, so the retry can be saved again without its link.
        await QueueCompetingReplayAsync(manifest, failed.Id);
        await using var stale = BuildProvider(
            _decider,
            services =>
                services.AddScoped<RetryDecisionReplay>(sp => new StaleLookup(
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    manifest.Id,
                    failed.Id
                ))
        );
        var polling = new ManifestManagerPollingService(
            stale,
            stale.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            stale.GetRequiredService<ISqlDialect>()
        );

        await polling.RunManifestManager(CancellationToken.None);

        (await QueuedEntryOf(manifest))
            .ReplayDecisionsOf.Should()
            .BeNull($"the index holds one queued replay of a run. See {Adr}");
    }

    /// <summary>Holds every host's lookup until all of them have made it.</summary>
    private sealed class LookupGate(int participants)
    {
        private int _arrived;
        private readonly TaskCompletionSource _all = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == participants)
                _all.TrySetResult();
            return _all.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class GatedLookup(IDataContextProviderFactory factory, LookupGate gate)
        : RetryDecisionReplay(factory, NullLogger.Instance)
    {
        public override async Task<IReadOnlyDictionary<long, long>> SourcesForRetriesAsync(
            IReadOnlyCollection<Manifest> manifests,
            CancellationToken ct
        )
        {
            var sources = await base.SourcesForRetriesAsync(manifests, ct);
            await gate.ArriveAndWaitAsync();
            return sources;
        }
    }

    /// <summary>Captures every exception logged by a host.</summary>
    private sealed class ExceptionCapture : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _exceptions =
            new();

        public IReadOnlyCollection<Exception> Exceptions => _exceptions.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(_exceptions);

        public void Dispose() { }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<Exception> sink)
            : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (exception is not null)
                    sink.Enqueue(exception);
            }
        }
    }

    [Test]
    public async Task A_trigger_asked_afresh_reports_a_retry_the_dispatcher_claimed_first()
    {
        var manifest = await CreateManifestAsync("claimed-first");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        await RunManifestManagerAsync();
        var queued = await QueuedEntryOf(manifest);
        DecisionProbe.FailAt = ProbeFailure.None;

        using var scope = Provider.CreateScope();
        var scheduler = (TraxScheduler)scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        // The dispatcher claims the entry between the trigger reading it and releasing it.
        scheduler.BeforeTriggerRelease = async (_, _) => await DispatchEntryAsync(queued.Id);

        var result = await scheduler.TriggerAsync(manifest.ExternalId, askAfresh: true);

        result.WorkQueueId.Should().Be(queued.Id);
        result.Created.Should().BeFalse();
        result.AlreadyDispatched.Should().BeTrue($"the trigger did not reach the entry. See {Adr}");
        result
            .ReplayDecisionsOf.Should()
            .Be(
                failed.Id,
                "the run the dispatcher claimed replays, though the trigger asked afresh"
            );
    }

    [Test]
    public async Task A_trigger_asked_afresh_reports_the_retry_it_released_as_asking_afresh()
    {
        var manifest = await CreateManifestAsync("released");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        await CycleAsync(manifest);
        await RunManifestManagerAsync();
        var queued = await QueuedEntryOf(manifest);

        using var scope = Provider.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<ITraxScheduler>()
            .TriggerAsync(manifest.ExternalId, askAfresh: true);

        result.WorkQueueId.Should().Be(queued.Id);
        result.AlreadyDispatched.Should().BeFalse();
        result.ReplayDecisionsOf.Should().BeNull();
    }

    [Test]
    public async Task Turning_replay_off_clears_queued_links_in_its_message_and_signals_the_queue()
    {
        var recording = new RecordingChangeSignal();
        await using var signalled = BuildProvider(
            _decider,
            services => services.AddSingleton<ITraxChangeSignal>(recording)
        );
        _override = signalled;
        try
        {
            var manifest = await CreateManifestAsync("setter");

            DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
            var failed = await CycleAsync(manifest);
            await RunManifestManagerAsync();
            (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().Be(failed.Id);

            // The flag is already off, as a re-seed would leave it, but the queued retry still
            // carries its link.
            await WithData(data =>
                data.Manifests.Where(m => m.Id == manifest.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplayDecisionsOnRetry, false))
            );
            recording.Clear();

            OperationResult result;
            using (var scope = Provider.CreateScope())
                result = await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .SetManifestsReplayDecisionsOnRetryAsync(
                        [manifest.Id],
                        false,
                        CancellationToken.None
                    );

            result.Success.Should().BeTrue();
            result.Count.Should().Be(0, "the flag was already off");
            result.Message.Should().Contain("1 queued retry(s) no longer replay");
            recording.Domains.Should().Contain(ChangeDomain.WorkQueue);
            (await QueuedEntryOf(manifest)).ReplayDecisionsOf.Should().BeNull();
        }
        finally
        {
            _override = null;
        }
    }

    /// <summary>Queues a manifest-less entry replaying <paramref name="source"/>, as a requeue does.</summary>
    private Task QueueCompetingReplayAsync(Manifest manifest, long source) =>
        WithData(async data =>
        {
            data.WorkQueues.Add(
                WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = manifest.Name,
                        Input = manifest.Properties,
                        InputTypeName = manifest.PropertyTypeName,
                        ReplayDecisionsOf = source,
                        ScheduledAt = DateTime.UtcNow.AddHours(1),
                    }
                )
            );
            await data.SaveChanges(CancellationToken.None);
            return true;
        });

    [Test]
    public async Task A_requeue_that_loses_the_race_for_a_replay_asks_afresh()
    {
        var manifest = await CreateManifestAsync("requeue-race", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        using var scope = Provider.CreateScope();
        var operations = (OperationsService)
            scope.ServiceProvider.GetRequiredService<IOperationsService>();
        // Another requeue of the same run lands between this one's check and its insert.
        operations.BeforeReplayEnqueue = _ => QueueCompetingReplayAsync(manifest, failed.Id);

        var result = await operations.RequeueExecutionAsync(failed.Id, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("afresh");
        (await WithData(data => data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == result.Id)))
            .ReplayDecisionsOf.Should()
            .BeNull($"the index holds one queued replay of a run. See {Adr}");
    }

    [Test]
    public async Task A_dead_letter_requeue_that_loses_the_race_for_a_replay_asks_afresh()
    {
        var manifest = await CreateManifestAsync("dead-letter-race", maxRetries: 0);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        await RunManifestManagerAsync();
        var deadLetterId = await WithData(data =>
            data.DeadLetters.AsNoTracking()
                .Where(d => d.ManifestId == manifest.Id)
                .Select(d => d.Id)
                .SingleAsync()
        );

        using var scope = Provider.CreateScope();
        var scheduler = (TraxScheduler)scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        scheduler.BeforeRequeueInsert = _ => QueueCompetingReplayAsync(manifest, failed.Id);

        (await scheduler.RequeueDeadLettersAsync([deadLetterId])).Count.Should().Be(1);

        (await QueuedEntryOf(manifest))
            .ReplayDecisionsOf.Should()
            .BeNull($"the index holds one queued replay of a run. See {Adr}");
    }

    [Test]
    public async Task A_retry_that_loses_the_race_for_a_replay_is_queued_to_ask_afresh()
    {
        var manifest = await CreateManifestAsync("retry-race");

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);

        // The lookup ran before a requeue of the failed run was queued, so it still names it.
        await QueueCompetingReplayAsync(manifest, failed.Id);
        await using var stale = BuildProvider(
            _decider,
            services =>
                services.AddScoped<RetryDecisionReplay>(sp => new StaleLookup(
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    manifest.Id,
                    failed.Id
                ))
        );
        _override = stale;
        try
        {
            await RunManifestManagerAsync();
            (await QueuedEntryOf(manifest))
                .ReplayDecisionsOf.Should()
                .BeNull($"the index holds one queued replay of a run. See {Adr}");
        }
        finally
        {
            _override = null;
        }
    }

    private sealed class StaleLookup(
        IDataContextProviderFactory factory,
        long manifestId,
        long source
    ) : RetryDecisionReplay(factory, NullLogger.Instance)
    {
        public override Task<IReadOnlyDictionary<long, long>> SourcesForRetriesAsync(
            IReadOnlyCollection<Manifest> manifests,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyDictionary<long, long>>(
                manifests.Any(m => m.Id == manifestId)
                    ? new Dictionary<long, long> { [manifestId] = source }
                    : new Dictionary<long, long>()
            );
    }

    [Test]
    public async Task A_retry_whose_replay_was_abandoned_replays_the_answers_it_asked_afresh()
    {
        var manifest = await CreateManifestAsync("abandoned", maxRetries: 3);

        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var first = await CycleAsync(manifest);
        var second = await CycleAsync(manifest);
        second.ReplayDecisionsOf.Should().Be(first.Id);

        // The second run abandoned its replay and asked its deciders itself.
        await WithData(data =>
            data.Metadatas.Where(m => m.Id == second.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplayAbandoned, true))
        );

        var stored = await WithData(data =>
            data.Manifests.AsNoTracking().SingleAsync(m => m.Id == manifest.Id)
        );
        using var scope = Provider.CreateScope();
        (
            await scope
                .ServiceProvider.GetRequiredService<RetryDecisionReplay>()
                .SourceForRetryAsync(stored, CancellationToken.None)
        )
            .Should()
            .Be(second.Id, $"a run that abandoned its replay asked its deciders itself. See {Adr}");

        // Nor does it count as the first run's one replay.
        var result = await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .RequeueExecutionAsync(first.Id, CancellationToken.None);
        result.Success.Should().BeTrue(result.Message);
        (await WithData(data => data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == result.Id)))
            .ReplayDecisionsOf.Should()
            .Be(first.Id);
    }

    [Test]
    public async Task A_second_entry_replaying_a_run_whose_replay_was_already_dispatched_asks_afresh()
    {
        var manifest = await CreateManifestAsync("replayed-after-dispatch", maxRetries: 0);
        var failed = await FailOnceAsync(manifest);

        // The first replay is queued, dispatched and run, so it has left the queue.
        var first = await RequeueAndDispatchAsync(failed.Id);
        first.ReplayDecisionsOf.Should().Be(failed.Id);

        // The unique index holds one *queued* replay per run, so a second entry naming the run
        // can be queued now: a requeue or a retry whose check ran before the first was dispatched.
        var second = await DispatchEntryAsync(await QueueReplayAsync(manifest, failed.Id));

        second.TrainState.Should().Be(TrainState.Completed, second.FailureReason);
        second
            .ReplayDecisionsOf.Should()
            .BeNull($"a run's answers are replayed at most once, checked at dispatch. See {Adr}");
        (await RunsReplaying(failed.Id)).Should().Equal([first.Id]);
    }

    [Test]
    public async Task An_unreadable_entry_replaying_a_run_whose_replay_was_already_dispatched_records_no_link()
    {
        var manifest = await CreateManifestAsync("unreadable-after-dispatch", maxRetries: 0);
        var failed = await FailOnceAsync(manifest);
        var first = await RequeueAndDispatchAsync(failed.Id);

        var unreadable = await DispatchEntryAsync(
            await QueueReplayAsync(manifest, failed.Id, input: """{"value": {"not": "a string"}}""")
        );

        unreadable.TrainState.Should().Be(TrainState.Failed);
        unreadable
            .ReplayDecisionsOf.Should()
            .BeNull($"the run recorded for an unreadable input is no replay either. See {Adr}");
        (await RunsReplaying(failed.Id)).Should().Equal([first.Id]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task An_earlier_run_that_never_replayed_does_not_stop_the_dispatch_replaying(
        bool abandoned
    )
    {
        var manifest = await CreateManifestAsync("not-a-replay", maxRetries: 0);
        var failed = await FailOnceAsync(manifest);

        // A dispatch attempt that failed and was requeued (its entry is the one dispatched below),
        // or a run that abandoned its replay and asked afresh, named the run without replaying it.
        await WithData(async data =>
        {
            var earlier = Metadata.Create(
                new CreateMetadata
                {
                    Name = manifest.Name,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ReplayDecisionsOf = failed.Id,
                }
            );
            await data.Track(earlier);
            await data.SaveChanges(CancellationToken.None);
            return await data
                .Metadatas.Where(m => m.Id == earlier.Id)
                .ExecuteUpdateAsync(s =>
                {
                    if (abandoned)
                        s.SetProperty(m => m.TrainState, TrainState.Completed)
                            .SetProperty(m => m.ReplayAbandoned, true);
                    else
                        s.SetProperty(m => m.TrainState, TrainState.Failed)
                            .SetProperty(m => m.FailureException, DispatchFailure.Requeued);
                });
        });

        var replay = await DispatchEntryAsync(await QueueReplayAsync(manifest, failed.Id));

        replay.TrainState.Should().Be(TrainState.Completed, replay.FailureReason);
        replay
            .ReplayDecisionsOf.Should()
            .Be(failed.Id, $"neither earlier run replayed the answers. See {Adr}");
    }

    /// <summary>Runs the manifest once, failing after both questions, and returns the run.</summary>
    private async Task<Metadata> FailOnceAsync(Manifest manifest)
    {
        DecisionProbe.FailAt = ProbeFailure.AfterQuestions;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        DecisionProbe.FailAt = ProbeFailure.None;
        return failed;
    }

    /// <summary>Requeues the run through the operations service and dispatches the entry.</summary>
    private async Task<Metadata> RequeueAndDispatchAsync(long metadataId)
    {
        OperationResult result;
        using (var scope = Provider.CreateScope())
            result = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .RequeueExecutionAsync(metadataId, CancellationToken.None);
        result.Success.Should().BeTrue(result.Message);
        return await DispatchEntryAsync(result.Id!.Value);
    }

    /// <summary>
    /// Queues a manifest-less entry replaying <paramref name="source"/>, due now, written straight
    /// to the table as a check that ran too early would have written it.
    /// </summary>
    private Task<long> QueueReplayAsync(Manifest manifest, long source, string? input = null) =>
        WithData(async data =>
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = manifest.Name,
                    Input = input ?? manifest.Properties,
                    InputTypeName = manifest.PropertyTypeName,
                    ReplayDecisionsOf = source,
                }
            );
            data.WorkQueues.Add(entry);
            await data.SaveChanges(CancellationToken.None);
            return entry.Id;
        });

    private Task<List<long>> RunsReplaying(long source) =>
        WithData(data =>
            data.Metadatas.AsNoTracking()
                .Where(m => m.ReplayDecisionsOf == source)
                .Select(m => m.Id)
                .ToListAsync()
        );

    /// <summary>
    /// Runs the manifest's retry and asserts it was queued without a link, asked both questions,
    /// and completed rather than failing on a replay it could not honour.
    /// </summary>
    private async Task AssertRetryAsksAfreshAsync(
        Manifest manifest,
        string value,
        int? expectedTracksBefore = null
    )
    {
        var tracksBefore = expectedTracksBefore ?? DecisionProbe.TracksOf(value).Count;
        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;

        var retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry
            .ReplayDecisionsOf.Should()
            .BeNull($"a replay that could not be honoured is not linked. See {Adr}");
        (await EntryOf(retry.Id)).ReplayDecisionsOf.Should().BeNull();
        (_decider.Requests.Count - asked).Should().Be(2, "the retry asked both questions afresh");
        DecisionProbe.TracksOf(value).Skip(tracksBefore).Should().Equal("Fast", "Small");
    }

    private void Answer(ProbeLane lane, ProbeSize size) => _decider.Choose(lane).Choose(size);

    public enum DeadLetterRequeue
    {
        Single,
        Batch,
        All,
    }

    /// <summary>Requeues the manifest's awaiting dead letter one of the three ways, and runs it.</summary>
    private async Task<Metadata> RequeueDeadLetterAndRunAsync(
        Manifest manifest,
        bool askAfresh,
        DeadLetterRequeue how = DeadLetterRequeue.Single
    )
    {
        var deadLetterId = await WithData(data =>
            data.DeadLetters.AsNoTracking()
                .Where(d =>
                    d.ManifestId == manifest.Id && d.Status == DeadLetterStatus.AwaitingIntervention
                )
                .Select(d => d.Id)
                .SingleAsync()
        );

        using (var scope = Provider.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
            switch (how)
            {
                case DeadLetterRequeue.Single:
                    (await scheduler.RequeueDeadLetterAsync(deadLetterId, askAfresh))
                        .Success.Should()
                        .BeTrue();
                    break;
                case DeadLetterRequeue.Batch:
                    (await scheduler.RequeueDeadLettersAsync([deadLetterId], askAfresh))
                        .Count.Should()
                        .Be(1);
                    break;
                default:
                    (await scheduler.RequeueAllDeadLettersAsync(askAfresh)).Count.Should().Be(1);
                    break;
            }
        }

        return await DispatchQueuedAsync(manifest);
    }

    private Task<WorkQueue> QueuedEntryOf(Manifest manifest) =>
        WithData(data =>
            data.WorkQueues.AsNoTracking()
                .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued)
        );

    /// <summary>A store that cannot be reached, for the replay lookup only.</summary>
    private sealed class UnreachableStore : IDataContextProviderFactory
    {
        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The store cannot be reached.");

        public Trax.Effect.Services.EffectProvider.IEffectProvider Create() =>
            throw new InvalidOperationException("The store cannot be reached.");
    }

    private async Task<Manifest> CreateManifestAsync(
        string value,
        int maxRetries = 3,
        string? owner = null,
        bool replayDecisionsOnRetry = true,
        Manifest? dependsOn = null
    )
    {
        using var scope = Provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var group = await TestSetup.CreateAndSaveManifestGroup(
            data,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(IDecisionProbeTrain),
                IsEnabled = true,
                ScheduleType = dependsOn is null ? ScheduleType.Interval : ScheduleType.Dependent,
                IntervalSeconds = dependsOn is null ? 3600 : null,
                DependsOnManifestId = dependsOn?.Id,
                MaxRetries = maxRetries,
                Properties = new DecisionProbeInput { Value = value },
                ReplayDecisionsOnRetry = replayDecisionsOnRetry,
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.Owner = owner;
        await data.Track(manifest);
        await data.SaveChanges(CancellationToken.None);
        return manifest;
    }

    /// <summary>
    /// One polling cycle for the manifest: the ManifestManager queues its next run (a first run,
    /// an occurrence or a retry), the dispatcher runs it, and the run is returned.
    /// </summary>
    private async Task<Metadata> CycleAsync(Manifest manifest)
    {
        await RunManifestManagerAsync();
        return await DispatchQueuedAsync(manifest);
    }

    private async Task RunManifestManagerAsync()
    {
        using var scope = Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
    }

    private async Task<Metadata> DispatchQueuedAsync(Manifest manifest)
    {
        var entryId = await WithData(data =>
            data.WorkQueues.AsNoTracking()
                .Where(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued)
                .Select(q => q.Id)
                .SingleAsync()
        );

        return await DispatchEntryAsync(entryId);
    }

    private async Task<Metadata> DispatchEntryAsync(long entryId)
    {
        using (var scope = Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        return await WithData(async data =>
        {
            var entry = await data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entryId);
            entry.Status.Should().Be(WorkQueueStatus.Dispatched);
            return await data.Metadatas.AsNoTracking().SingleAsync(m => m.Id == entry.MetadataId);
        });
    }

    private Task<WorkQueue> EntryOf(long metadataId) =>
        WithData(data =>
            data.WorkQueues.AsNoTracking().SingleAsync(q => q.MetadataId == metadataId)
        );

    private Task<List<Effect.Models.RecordedDecision.RecordedDecision>> DecisionsOf(
        long metadataId
    ) =>
        WithData(data =>
            data.RecordedDecisions.AsNoTracking()
                .Where(d => d.MetadataId == metadataId)
                .OrderBy(d => d.Id)
                .ToListAsync()
        );

    private async Task<T> WithData<T>(Func<IDataContext, Task<T>> read)
    {
        using var scope = Provider.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }
}
