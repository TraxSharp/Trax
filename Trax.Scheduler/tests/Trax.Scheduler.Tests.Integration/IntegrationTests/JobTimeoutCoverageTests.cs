using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// "Each polling cycle, the CancelTimedOutJobsJunction checks all InProgress metadata and cancels
/// any where the elapsed time exceeds the manifest's TimeoutSeconds (or the global
/// DefaultJobTimeout)" (scheduling-options, Timeout Enforcement).
/// </summary>
[TestFixture]
public class JobTimeoutCoverageTests : TestSetup
{
    private SchedulerConfiguration _config = null!;
    private TimeSpan _previousTimeout;
    private TimeSpan _previousStaleTimeout;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _previousTimeout = _config.DefaultJobTimeout;
        _previousStaleTimeout = _config.StaleInProgressTimeout;
        _config.DefaultJobTimeout = TimeSpan.FromMinutes(20);
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(60);
    }

    [TearDown]
    public void RestoreTimeout()
    {
        _config.DefaultJobTimeout = _previousTimeout;
        _config.StaleInProgressTimeout = _previousStaleTimeout;
        _config.ExcludedTrainTypeNames.Remove(typeof(SchedulerTestTrain).FullName!);
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_queued_run_with_no_manifest_past_DefaultJobTimeout_is_cancelled()
    {
        var run = await CreateInProgressRun(manifestId: null, startedMinutesAgo: 30);
        await LinkWorkQueueEntry(run.Id);

        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);

        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id))
            .CancellationRequested.Should()
            .BeTrue("the run has been InProgress for 30 minutes against a 20 minute default");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_run_of_a_manifest_disabled_while_it_runs_is_still_timed_out()
    {
        var manifest = await CreateManifest(isEnabled: false);
        var run = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 30);

        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);

        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id))
            .CancellationRequested.Should()
            .BeTrue("disabling a manifest stops new runs; it does not exempt a running one");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_run_started_outside_the_scheduler_is_not_cancelled_at_DefaultJobTimeout()
    {
        // No manifest, no work queue entry, no background job: a train run on the train bus by a
        // host that shares the database. The scheduler did not start it and does not bound it.
        var run = await CreateInProgressRun(manifestId: null, startedMinutesAgo: 30);

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id))
            .CancellationRequested.Should()
            .BeFalse("DefaultJobTimeout bounds only the runs a scheduler dispatched");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_nested_run_inside_a_long_manifest_timeout_is_not_cancelled_at_DefaultJobTimeout()
    {
        var manifest = await CreateManifest(isEnabled: true, timeoutSeconds: 7200);
        var parent = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 30);
        var child = await CreateInProgressRun(
            manifestId: null,
            startedMinutesAgo: 30,
            parentId: parent.Id
        );

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == parent.Id))
            .CancellationRequested.Should()
            .BeFalse("30 minutes is inside the manifest's 2 hour timeout");
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == child.Id))
            .CancellationRequested.Should()
            .BeFalse("the nested run is part of a run inside its 2 hour timeout");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_nested_run_is_cancelled_once_its_root_manifest_timeout_passes()
    {
        var manifest = await CreateManifest(isEnabled: true, timeoutSeconds: 600);
        var parent = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 15);
        var child = await CreateInProgressRun(
            manifestId: null,
            startedMinutesAgo: 15,
            parentId: parent.Id
        );

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == child.Id))
            .CancellationRequested.Should()
            .BeTrue("the nested run takes its root's 10 minute timeout");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task An_excluded_train_past_DefaultJobTimeout_is_not_cancelled()
    {
        _config.ExcludedTrainTypeNames.Add(typeof(SchedulerTestTrain).FullName!);
        var manifest = await CreateManifest(isEnabled: true);
        var run = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 30);

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id))
            .CancellationRequested.Should()
            .BeFalse("a train the host excluded is left alone, as the stale reaper leaves it");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_run_inside_a_long_DefaultJobTimeout_is_not_reaped_as_stale()
    {
        _config.DefaultJobTimeout = TimeSpan.FromHours(2);
        var manifest = await CreateManifest(isEnabled: true);
        var run = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 61);

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id))
            .TrainState.Should()
            .Be(TrainState.InProgress, "61 minutes is inside the 2 hour default job timeout");
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run that no host is running, to time it out or leave it; a cancel only flags it."
    )]
    [Test]
    public async Task A_nested_run_inside_a_long_manifest_timeout_is_not_reaped_as_stale()
    {
        var manifest = await CreateManifest(isEnabled: true, timeoutSeconds: 7200);
        var parent = await CreateInProgressRun(manifest.Id, startedMinutesAgo: 90);
        var child = await CreateInProgressRun(
            manifestId: null,
            startedMinutesAgo: 90,
            parentId: parent.Id
        );

        await RunManifestManager();

        (await DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == child.Id))
            .TrainState.Should()
            .Be(TrainState.InProgress, "the nested run is inside its root's 2 hour timeout");
    }

    private async Task RunManifestManager()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    private async Task LinkWorkQueueEntry(long metadataId)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = "{}",
                InputTypeName = typeof(SchedulerTestInput).FullName,
            }
        );
        entry.Status = WorkQueueStatus.Dispatched;
        entry.MetadataId = metadataId;
        entry.DispatchedAt = DateTime.UtcNow;
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task<Manifest> CreateManifest(bool isEnabled, int? timeoutSeconds = null)
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = isEnabled,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = 3,
                TimeoutSeconds = timeoutSeconds,
                Properties = new SchedulerTestInput { Value = "timeout" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Metadata> CreateInProgressRun(
        long? manifestId,
        int startedMinutesAgo,
        long? parentId = null
    )
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "timeout" },
                ManifestId = manifestId,
                ParentId = parentId,
            }
        );
        run.TrainState = TrainState.InProgress;
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        await DataContext
            .Metadatas.Where(m => m.Id == run.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-startedMinutesAgo))
            );
        DataContext.Reset();
        return run;
    }
}
