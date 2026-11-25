using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.Drain;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.LocalWorkerService;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The drain (Trax.Docs/adr/0065) on Postgres: every part of the scheduler that starts work honours
/// the row, the operations service sets, reads and ends it, a lapsed row holds nothing, a starting
/// scheduler host ends it, a schedule due meanwhile is a misfire once it ends, and two hubs can
/// overlap once nothing is busy.
/// </summary>
[TestFixture]
[NonParallelizable]
public class DrainTests : TestSetup
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private IOperationsService Operations =>
        Scope.ServiceProvider.GetRequiredService<IOperationsService>();

    [SetUp]
    public async Task ClearDrain() => await DataContext.Drains.ExecuteDeleteAsync();

    [TearDown]
    public async Task EndDrain()
    {
        var scope = Scope.ServiceProvider.CreateAsyncScope();
        await using var disposal = scope.ConfigureAwait(false);
        await scope.ServiceProvider.GetRequiredService<IDataContext>().Drains.ExecuteDeleteAsync();
    }

    [Test]
    public async Task Drain_is_set_read_and_ended_through_the_operations_service()
    {
        var before = TimeProvider.System.GetUtcNow().UtcDateTime;
        var set = await Operations.DrainAsync(
            duration: null,
            "operator:alice",
            CancellationToken.None
        );
        set.Success.Should().BeTrue(set.Message);

        var status = await Operations.GetDrainAsync(CancellationToken.None);
        status.Drained.Should().BeTrue();
        status.StartedBy.Should().Be("operator:alice");
        status
            .Until.Should()
            .BeCloseTo(before + OperationsService.DefaultDrainDuration, TimeSpan.FromMinutes(1));
        (await SchedulerDrain.IsDrainedAsync(Scope.ServiceProvider, CancellationToken.None))
            .Should()
            .BeTrue();

        var ended = await Operations.EndDrainAsync(CancellationToken.None);
        ended.Count.Should().Be(1);
        (await Operations.GetDrainAsync(CancellationToken.None)).Drained.Should().BeFalse();
        (await Operations.EndDrainAsync(CancellationToken.None))
            .Count.Should()
            .Be(0, "ending a drain that is not in force changes nothing");
    }

    [TestCase(0)]
    [TestCase(59)]
    [TestCase(24 * 60 * 60 + 1)]
    public async Task Drain_refuses_a_duration_outside_a_minute_to_a_day(int seconds)
    {
        var result = await Operations.DrainAsync(
            TimeSpan.FromSeconds(seconds),
            "operator:alice",
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        (await Operations.GetDrainAsync(CancellationToken.None)).Drained.Should().BeFalse();
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Drain_refuses_a_blank_operator(string startedBy)
    {
        var result = await Operations.DrainAsync(duration: null, startedBy, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Test]
    public async Task A_lapsed_drain_holds_nothing_and_resume_still_deletes_it()
    {
        var now = TimeProvider.System.GetUtcNow();
        DataContext.Drains.Add(
            new Trax.Effect.Models.Drain.Drain
            {
                Until = (now - TimeSpan.FromSeconds(1)).UtcDateTime,
                StartedAt = (now - TimeSpan.FromMinutes(30)).UtcDateTime,
                StartedBy = TraxDrain.DeployActor,
            }
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        (await SchedulerDrain.IsDrainedAsync(Scope.ServiceProvider, CancellationToken.None))
            .Should()
            .BeFalse("a deploy that died leaves a drain that lapses at its time");
        (await Operations.GetDrainAsync(CancellationToken.None)).Drained.Should().BeFalse();
        (await Operations.EndDrainAsync(CancellationToken.None)).Count.Should().Be(0);
        (await DataContext.Drains.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task While_drained_the_manifest_manager_queues_nothing_and_a_FireOnceNow_misfire_runs_after()
    {
        var manifest = await OverdueIntervalManifest(MisfirePolicy.FireOnceNow);
        await Operations.DrainAsync(duration: null, "operator:alice", CancellationToken.None);

        await RunManifestManager();
        (await QueuedFor(manifest.Id))
            .Should()
            .Be(0, "a drained manifest manager queues no scheduled run");

        await Operations.EndDrainAsync(CancellationToken.None);
        await RunManifestManager();
        (await QueuedFor(manifest.Id))
            .Should()
            .Be(1, "FireOnceNow runs a schedule that came due while drained once the drain ends");
    }

    [Test]
    public async Task A_DoNothing_misfire_past_its_threshold_is_skipped_after_the_drain()
    {
        var manifest = await OverdueIntervalManifest(MisfirePolicy.DoNothing, threshold: 60);
        await Operations.DrainAsync(duration: null, "operator:alice", CancellationToken.None);
        await RunManifestManager();

        await Operations.EndDrainAsync(CancellationToken.None);
        await RunManifestManager();

        (await QueuedFor(manifest.Id))
            .Should()
            .Be(0, "DoNothing skips an occurrence the drain made late by more than its threshold");
    }

    [Test]
    public async Task While_drained_the_dispatcher_dispatches_nothing_and_reports_the_running_run_until_it_ends()
    {
        var manifest = await PlainManifest();
        var running = await SaveRun(TrainState.InProgress);
        var entry = await SaveQueuedEntry(manifest);
        await Operations.DrainAsync(duration: null, "operator:alice", CancellationToken.None);

        await RunDispatcherCycle();
        DataContext.Reset();
        (await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "a drained dispatcher dispatches nothing");

        var busy = await Operations.GetDrainAsync(CancellationToken.None);
        busy.Busy.Should()
            .ContainSingle(b =>
                string.Equals(b.Kind, "run", StringComparison.Ordinal)
                && string.Equals(
                    b.Id,
                    running.Id.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal
                )
            );
        busy.BusyTotal.Should().Be(1);

        await SetState(running.Id, TrainState.Completed);
        (await Operations.GetDrainAsync(CancellationToken.None))
            .BusyTotal.Should()
            .Be(0, "the run ended, so nothing is busy");

        await Operations.EndDrainAsync(CancellationToken.None);
        await RunDispatcherCycle();
        DataContext.Reset();
        (await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "the dispatcher takes work again once resumed");
    }

    [Test]
    public async Task While_drained_a_worker_claims_nothing_and_its_queued_job_is_not_busy()
    {
        var run = await SaveRun(TrainState.Pending);
        var job = await SaveJob(run.Id, fetchedAt: null);
        await Operations.DrainAsync(duration: null, "operator:alice", CancellationToken.None);

        var worker = Worker();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // allowed-delay: proves an absence; the worker polls every 50 ms, so a second is
            // twenty claims it did not make.
            await Task.Delay(TimeSpan.FromSeconds(1), TimeProvider.System);
            DataContext.Reset();
            (await DataContext.BackgroundJobs.FirstAsync(j => j.Id == job.Id))
                .FetchedAt.Should()
                .BeNull("a drained worker claims nothing");
            (await Operations.GetDrainAsync(CancellationToken.None))
                .BusyTotal.Should()
                .Be(0, "a job still waiting in the queue survives the roll, so it is not busy");

            await Operations.EndDrainAsync(CancellationToken.None);
            (await WaitUntil(async () => !await JobExists(job.Id).ConfigureAwait(false)))
                .Should()
                .BeTrue("the worker claims again once resumed");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [LeavesStuckRuns("Seeds in-progress runs no host is running, to read them back as busy.")]
    [Test]
    public async Task Busy_counts_live_runs_and_leaves_out_finished_queued_and_scheduler_runs()
    {
        var inProgress = await SaveRun(TrainState.InProgress);
        var pendingClaimed = await SaveRun(TrainState.Pending);
        await SaveJob(pendingClaimed.Id, fetchedAt: TimeProvider.System.GetUtcNow().UtcDateTime);
        var pendingRemote = await SaveRun(TrainState.Pending);
        var pendingQueued = await SaveRun(TrainState.Pending);
        await SaveJob(pendingQueued.Id, fetchedAt: null);
        await SaveRun(TrainState.Completed);
        await SaveRun(TrainState.Failed);
        await SaveRun(TrainState.InProgress, typeof(IManifestManagerTrain).FullName.NotNull());

        var status = await Operations.GetDrainAsync(CancellationToken.None);

        status
            .Busy.Select(b => b.Id)
            .Should()
            .BeEquivalentTo(
                [
                    inProgress.Id.ToString(CultureInfo.InvariantCulture),
                    pendingClaimed.Id.ToString(CultureInfo.InvariantCulture),
                    pendingRemote.Id.ToString(CultureInfo.InvariantCulture),
                ],
                "InProgress, a claimed Pending run and a Pending run with no queued job are live"
            );
        status.BusyTotal.Should().Be(3);
        status
            .Busy.Should()
            .OnlyContain(b => string.Equals(b.Kind, "run", StringComparison.Ordinal));
    }

    [LeavesStuckRuns("Seeds an in-progress run no host is running, to read it back as busy.")]
    [Test]
    public async Task A_train_left_out_of_MaxActiveJobs_is_still_busy()
    {
        // ExcludeFromMaxActiveJobs is about concurrency. A replaced host cuts the run off all the same, and the
        // deploy's drain (DrainCommand) waits on it, so the console and the API count it too.
        var name = typeof(SchedulerTestTrain).FullName.NotNull();
        var configuration = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        configuration.ExcludedTrainTypeNames.Add(name);
        try
        {
            var run = await SaveRun(TrainState.InProgress);

            var status = await Operations.GetDrainAsync(CancellationToken.None);

            status.BusyTotal.Should().Be(1);
            status
                .Busy.Should()
                .ContainSingle(b =>
                    string.Equals(
                        b.Id,
                        run.Id.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal
                    )
                );
        }
        finally
        {
            configuration.ExcludedTrainTypeNames.Remove(name);
        }
    }

    [Test]
    public async Task A_starting_scheduler_host_ends_the_drain()
    {
        await Operations.DrainAsync(duration: null, TraxDrain.DeployActor, CancellationToken.None);

        await StartupService().StartAsync(CancellationToken.None);

        (await Operations.GetDrainAsync(CancellationToken.None))
            .Drained.Should()
            .BeFalse("a new hub ends the drain the deploy set before replacing the old one");
    }

    [Test]
    public async Task Two_hubs_overlap_after_a_drain_without_failing_a_run_or_dispatching_twice()
    {
        // The old hub's run is live when the deploy drains; the deploy waits until it ends.
        var oldHubRun = await SaveRun(
            TrainState.InProgress,
            startTime: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-1)
        );
        await Operations.DrainAsync(duration: null, TraxDrain.DeployActor, CancellationToken.None);
        (await Operations.GetDrainAsync(CancellationToken.None)).BusyTotal.Should().Be(1);
        await SetState(oldHubRun.Id, TrainState.Completed);
        (await Operations.GetDrainAsync(CancellationToken.None)).BusyTotal.Should().Be(0);

        // The new hub starts beside the old one: its recovery finds nothing live to fail, and it
        // ends the drain.
        await StartupService().StartAsync(CancellationToken.None);
        DataContext.Reset();
        (await DataContext.Metadatas.CountAsync(m => m.TrainState == TrainState.Failed))
            .Should()
            .Be(0, "nothing was live when the new hub's recovery ran");
        (await Operations.GetDrainAsync(CancellationToken.None)).Drained.Should().BeFalse();

        // Both hubs dispatch at once: the claim skips locked entries, so the entry runs once.
        var entry = await SaveQueuedEntry(await PlainManifest());
        await Task.WhenAll(DispatchInOwnScope(), DispatchInOwnScope());

        DataContext.Reset();
        var dispatched = await DataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        dispatched.Status.Should().Be(WorkQueueStatus.Dispatched);
        (await DataContext.Metadatas.CountAsync(m => m.ManifestId == entry.ManifestId))
            .Should()
            .Be(1, "two dispatchers claiming one entry start one run");
    }

    private async Task DispatchInOwnScope()
    {
        var scope = Scope.ServiceProvider.CreateAsyncScope();
        await using var disposal = scope.ConfigureAwait(false);
        await scope
            .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
            .Run(Unit.Default)
            .ConfigureAwait(false);
    }

    private SchedulerStartupService StartupService() =>
        new(
            Scope.ServiceProvider,
            new SchedulerConfiguration
            {
                RecoverStuckJobsOnStartup = true,
                PruneOrphanedManifests = false,
                HasDatabaseProvider = true,
            },
            Scope.ServiceProvider.GetRequiredService<ILogger<SchedulerStartupService>>()
        );

    private async Task RunManifestManager()
    {
        var scope = Scope.ServiceProvider.CreateAsyncScope();
        await using var disposal = scope.ConfigureAwait(false);
        await scope
            .ServiceProvider.GetRequiredService<IManifestManagerTrain>()
            .Run(Unit.Default)
            .ConfigureAwait(false);
    }

    private async Task RunDispatcherCycle()
    {
        var monitor = new SchedulerLivenessMonitor(TimeProvider.System);
        var service = new JobDispatcherPollingService(
            Scope.ServiceProvider,
            Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>(),
            monitor,
            new DispatcherWake(),
            Scope.ServiceProvider.GetRequiredService<ILogger<JobDispatcherPollingService>>()
        );
        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            (
                await WaitUntil(() => Task.FromResult(monitor.LastDispatchCompletedAt is not null))
                    .ConfigureAwait(false)
            )
                .Should()
                .BeTrue("the dispatcher runs a cycle as it starts");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private LocalWorkerService Worker() =>
        new(
            Scope.ServiceProvider,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(50),
                VisibilityTimeout = TimeSpan.FromMinutes(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            },
            new CancellationRegistry(),
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>(),
            Scope.ServiceProvider.GetRequiredService<ISqlDialect>()
        );

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = TimeProvider.System.GetUtcNow() + Wait;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            if (await condition().ConfigureAwait(false))
                return true;
            // allowed-delay: poll interval while awaiting the condition, bounded by Wait.
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System)
                .ConfigureAwait(false);
        }
        return await condition().ConfigureAwait(false);
    }

    private async Task<bool> JobExists(long id)
    {
        var scope = Scope.ServiceProvider.CreateAsyncScope();
        await using var disposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .BackgroundJobs.AnyAsync(j => j.Id == id)
            .ConfigureAwait(false);
    }

    private async Task<int> QueuedFor(long manifestId)
    {
        DataContext.Reset();
        return await DataContext
            .WorkQueues.CountAsync(q => q.ManifestId == manifestId)
            .ConfigureAwait(false);
    }

    private async Task SetState(long id, TrainState state)
    {
        await DataContext
            .Metadatas.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, state))
            .ConfigureAwait(false);
        DataContext.Reset();
    }

    private async Task<Metadata> SaveRun(
        TrainState state,
        string? name = null,
        DateTime? startTime = null
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name ?? typeof(SchedulerTestTrain).FullName.NotNull(),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "drain" },
            },
            TimeProvider.System
        );
        metadata.TrainState = state;
        if (startTime is { } start)
            metadata.StartTime = start;
        await DataContext.Track(metadata).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
        return metadata;
    }

    private async Task<BackgroundJob> SaveJob(long metadataId, DateTime? fetchedAt)
    {
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = metadataId },
            TimeProvider.System
        );
        job.FetchedAt = fetchedAt;
        await DataContext.Track(job).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
        return job;
    }

    private async Task<Manifest> PlainManifest() =>
        await SaveManifest(
                ScheduleType.None,
                intervalSeconds: null,
                MisfirePolicy.FireOnceNow,
                threshold: null
            )
            .ConfigureAwait(false);

    private async Task<Manifest> OverdueIntervalManifest(
        MisfirePolicy policy,
        int? threshold = null
    )
    {
        var manifest = await SaveManifest(ScheduleType.Interval, 300, policy, threshold)
            .ConfigureAwait(false);
        await DataContext
            .Manifests.Where(m => m.Id == manifest.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(
                    m => m.LastSuccessfulRun,
                    TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-12)
                )
            )
            .ConfigureAwait(false);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Manifest> SaveManifest(
        ScheduleType scheduleType,
        int? intervalSeconds,
        MisfirePolicy policy,
        int? threshold
    )
    {
        var group = await CreateAndSaveManifestGroup(DataContext, name: $"group-{Guid.NewGuid():N}")
            .ConfigureAwait(false);
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = scheduleType,
                IntervalSeconds = intervalSeconds,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "drain" },
                MisfirePolicy = policy,
                MisfireThresholdSeconds = threshold,
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
        return manifest;
    }

    private async Task<WorkQueue> SaveQueuedEntry(Manifest manifest)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName.NotNull(),
                Input = manifest.Properties,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
            },
            TimeProvider.System
        );
        await DataContext.Track(entry).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
        return entry;
    }
}
