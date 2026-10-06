using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A dependent runs at least once after each success of its parent. A parent success that
/// lands while the dependent is already running was not seen by that run, so it earns another.
///
/// <para>Enforces <c>docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
)]
public class DependentRunsAfterEachParentSuccessTests : TestSetup
{
    private static readonly DateTime T0 = DateTime.UtcNow.AddHours(-1);

    [Test]
    public async Task A_parent_success_during_the_dependents_run_queues_the_dependent_again()
    {
        // The dependent started at T0+5, the parent succeeded again at T0+10, and the dependent
        // finished at T0+20: that run began before the parent's latest output existed.
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: T0.AddMinutes(5),
            dependentSucceededAt: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent)).Should().Be(1);
    }

    [Test]
    public async Task A_dependent_that_started_after_the_parents_latest_success_is_not_queued()
    {
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: T0.AddMinutes(15),
            dependentSucceededAt: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent)).Should().Be(0, "nothing new happened since it started");
    }

    [Test]
    public async Task The_extra_run_is_queued_once_and_not_again_after_it_succeeds()
    {
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: T0.AddMinutes(5),
            dependentSucceededAt: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();
        await RunManifestManagerAsync();
        (await QueuedCount(dependent)).Should().Be(1, "a queued entry is not duplicated");

        // The queued run goes ahead and succeeds.
        var entry = await DataContext.WorkQueues.SingleAsync(q => q.ManifestId == dependent.Id);
        entry.Status = WorkQueueStatus.Dispatched;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        await AddMetadataAsync(dependent.Id, TrainState.Completed, T0.AddMinutes(30));
        await SetLastSuccessAsync(dependent.Id, T0.AddMinutes(31));

        await RunManifestManagerAsync();

        (await QueuedCount(dependent)).Should().Be(0);
    }

    [Test]
    public async Task A_dependent_whose_run_history_was_pruned_falls_back_to_its_last_success()
    {
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: null,
            dependentSucceededAt: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent)).Should().Be(0);
    }

    [Test]
    public async Task A_parent_success_during_a_cancelled_dependent_run_queues_the_dependent_again()
    {
        // The dependent started at T0+5 for the parent's earlier output, the parent succeeded
        // again at T0+10, and the dependent run was cancelled at T0+20. The cancelled run began
        // before the T0+10 output existed, so it cannot have consumed it.
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: T0.AddMinutes(-40),
            dependentSucceededAt: T0.AddMinutes(-30)
        );
        await AddMetadataAsync(
            dependent.Id,
            TrainState.Cancelled,
            T0.AddMinutes(5),
            endTime: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent))
            .Should()
            .Be(1, "the T0+10 parent success was never run against");
    }

    [Test]
    public async Task A_dependent_run_cancelled_after_the_parents_latest_success_consumes_it()
    {
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: T0.AddMinutes(-40),
            dependentSucceededAt: T0.AddMinutes(-30)
        );
        await AddMetadataAsync(
            dependent.Id,
            TrainState.Cancelled,
            T0.AddMinutes(15),
            endTime: T0.AddMinutes(20)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent))
            .Should()
            .Be(0, "the cancelled run started after the parent's latest success");
    }

    [Test]
    public async Task A_dependent_whose_dispatcher_clock_runs_behind_is_not_queued_again_after_every_run()
    {
        // The parent succeeded at T0+10 and the dependent was dispatched for it at T0+11, both by
        // the database's clock. The dependent's run row was stamped by a dispatcher whose clock
        // is three minutes behind, so it claims to have started at T0+8, before the parent's
        // success. Read from that row, every run of the dependent looked like it missed the
        // parent's output and was queued again.
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: null,
            dependentSucceededAt: T0.AddMinutes(12)
        );
        await AddDispatchedRunAsync(
            dependent.Id,
            TrainState.Completed,
            startTime: T0.AddMinutes(8),
            dispatchedAt: T0.AddMinutes(11)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent))
            .Should()
            .Be(
                0,
                "the run was dispatched after the parent's latest success, by one clock. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    [Test]
    public async Task A_parent_success_after_the_dependents_dispatch_queues_it_again_whatever_its_run_row_says()
    {
        // The dispatcher's clock runs ahead this time: the run row claims T0+12, but the dependent
        // was dispatched at T0+5 by the database's clock, before the parent's T0+10 success.
        var dependent = await ArrangeAsync(
            parentSucceededAt: T0.AddMinutes(10),
            dependentStartedAt: null,
            dependentSucceededAt: T0.AddMinutes(20)
        );
        await AddDispatchedRunAsync(
            dependent.Id,
            TrainState.Completed,
            startTime: T0.AddMinutes(12),
            dispatchedAt: T0.AddMinutes(5)
        );

        await RunManifestManagerAsync();

        (await QueuedCount(dependent))
            .Should()
            .Be(
                1,
                "the parent succeeded while it ran. See docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    private async Task<Manifest> ArrangeAsync(
        DateTime parentSucceededAt,
        DateTime? dependentStartedAt,
        DateTime dependentSucceededAt
    )
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var parent = await SaveManifestAsync(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 86400,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "parent" },
            },
            group.Id
        );
        await AddMetadataAsync(parent.Id, TrainState.Completed, parentSucceededAt.AddMinutes(-1));
        await SetLastSuccessAsync(parent.Id, parentSucceededAt);

        var dependent = await SaveManifestAsync(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Dependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "dependent" },
                DependsOnManifestId = parent.Id,
            },
            group.Id
        );
        if (dependentStartedAt is not null)
            await AddMetadataAsync(dependent.Id, TrainState.Completed, dependentStartedAt.Value);
        await SetLastSuccessAsync(dependent.Id, dependentSucceededAt);

        return dependent;
    }

    private async Task<Manifest> SaveManifestAsync(CreateManifest create, long groupId)
    {
        var manifest = Manifest.Create(create);
        manifest.ManifestGroupId = groupId;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task AddMetadataAsync(
        long manifestId,
        TrainState state,
        DateTime startTime,
        DateTime? endTime = null
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput(),
                ManifestId = manifestId,
            }
        );
        metadata.TrainState = state;
        metadata.StartTime = startTime;
        metadata.EndTime = endTime;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    /// <summary>
    /// A run the dispatcher started: its run row, stamped by the dispatcher's clock, and its
    /// dispatched work queue entry, stamped by the database's.
    /// </summary>
    private async Task AddDispatchedRunAsync(
        long manifestId,
        TrainState state,
        DateTime startTime,
        DateTime dispatchedAt
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput(),
                ManifestId = manifestId,
            }
        );
        metadata.TrainState = state;
        metadata.StartTime = startTime;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                ManifestId = manifestId,
            }
        );
        entry.Status = WorkQueueStatus.Dispatched;
        entry.MetadataId = metadata.Id;
        entry.DispatchedAt = dispatchedAt;
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task SetLastSuccessAsync(long manifestId, DateTime at)
    {
        var manifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifestId);
        manifest.LastSuccessfulRun = at;
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task RunManifestManagerAsync()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    private Task<int> QueuedCount(Manifest dependent) =>
        DataContext.WorkQueues.CountAsync(q =>
            q.ManifestId == dependent.Id && q.Status == WorkQueueStatus.Queued
        );
}
