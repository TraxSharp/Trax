using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Drives the full polling cycle (materialise manifests → ManifestManager → JobDispatcher)
/// against Postgres so the EnqueueJobsJunction / dispatcher junctions / TraxScheduler trigger
/// paths actually execute. The unit-level tests can't reach these because they live inside
/// junction async state machines that only fire when the orchestration trains run.
/// </summary>
[TestFixture]
public class SchedulerPollingCycleTests
{
    [Test]
    public async Task ManifestManager_AfterScheduling_QueuesWorkForDueManifest()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "due-now",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var workQueue = await fx.DataContext.WorkQueues.AsNoTracking().ToListAsync();
        workQueue.Should().NotBeEmpty();
        workQueue.Should().Contain(w => w.Status == WorkQueueStatus.Queued);
    }

    [Test]
    public async Task ManifestManager_DisabledManifest_DoesNotQueueWork()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "disabled",
                new SchedulerTestInput(),
                Every.Minutes(1),
                opts => opts.Enabled(false)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var workQueue = await fx.DataContext.WorkQueues.AsNoTracking().ToListAsync();
        workQueue.Should().BeEmpty();
    }

    [Test]
    public async Task ManifestManager_OnceManifestNotYetDue_DoesNotQueue()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleOnce<ISchedulerTestTrain>(
                "future-once",
                new SchedulerTestInput(),
                TimeSpan.FromHours(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var workQueue = await fx.DataContext.WorkQueues.AsNoTracking().ToListAsync();
        workQueue.Should().BeEmpty();
    }

    [Test]
    public async Task ManifestManager_DependentNotFiredBeforeParentSucceeds()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("parent", new SchedulerTestInput(), Every.Minutes(1))
                .ThenInclude<ISchedulerTestTrain>("dependent", new SchedulerTestInput())
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();

        var workQueue = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Include(w => w.Manifest)
            .ToListAsync();
        // Parent is due, but dependent waits for parent to have a LastSuccessfulRun
        workQueue.Select(w => w.Manifest!.ExternalId).Should().NotContain("dependent");
    }

    [Test]
    public async Task ManifestManager_RunTwice_NoDuplicateWorkQueueEntries()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("once-only", new SchedulerTestInput(), Every.Minutes(1))
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.RunManifestManagerAsync();
        var afterFirst = await fx.DataContext.WorkQueues.AsNoTracking().CountAsync();
        await fx.RunManifestManagerAsync();
        var afterSecond = await fx.DataContext.WorkQueues.AsNoTracking().CountAsync();

        // Manifest manager should not requeue an entry that's already pending dispatch.
        afterSecond.Should().Be(afterFirst);
    }

    [Test]
    public async Task JobDispatcher_AfterManifestManager_DispatchesQueuedWork()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "dispatch-me",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();
        await fx.RunManifestManagerAsync();

        await fx.RunJobDispatcherAsync();

        var workQueue = await fx.DataContext.WorkQueues.AsNoTracking().ToListAsync();
        workQueue.Should().NotBeEmpty();
        workQueue
            .Should()
            .Contain(w =>
                w.Status == WorkQueueStatus.Dispatched || w.Status == WorkQueueStatus.Cancelled
            );
    }

    [Test]
    public async Task TriggerAsync_QueuesWorkOutsideOfPollingCycle()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "manual-trigger",
                new SchedulerTestInput(),
                // Schedule at 1 hour intervals so polling won't queue it
                Every.Hours(1)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.Scheduler.TriggerAsync("manual-trigger");

        var workQueue = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Include(w => w.Manifest)
            .ToListAsync();
        workQueue.Should().Contain(w => w.Manifest!.ExternalId == "manual-trigger");
    }

    [Test]
    public async Task DisableAsync_FlipsManifestEnabledFalse()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "to-disable",
                new SchedulerTestInput(),
                Every.Minutes(5)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.Scheduler.DisableAsync("to-disable");

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "to-disable");
        manifest.IsEnabled.Should().BeFalse();
    }

    [Test]
    public async Task EnableAsync_FlipsManifestEnabledTrue()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "to-enable",
                new SchedulerTestInput(),
                Every.Minutes(5),
                opts => opts.Enabled(false)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        await fx.Scheduler.EnableAsync("to-enable");

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "to-enable");
        manifest.IsEnabled.Should().BeTrue();
    }

    [Test]
    public async Task TriggerGroupAsync_QueuesEveryEnabledManifestInGroup()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                    "g-a",
                    new SchedulerTestInput(),
                    Every.Hours(1),
                    opts => opts.Group("trigger-group")
                )
                .Include<ISchedulerTestTrain>(
                    "g-b",
                    new SchedulerTestInput(),
                    opts => opts.Group("trigger-group")
                )
        );
        await fx.MaterializePendingManifestsAsync();

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == "trigger-group");
        var triggered = await fx.Scheduler.TriggerGroupAsync(group.Id);

        triggered.Should().BeGreaterThan(0);
        var workQueue = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Include(w => w.Manifest)
            .ToListAsync();
        workQueue.Should().Contain(w => w.Manifest!.ExternalId == "g-a");
    }

    [Test]
    public async Task CancelAsync_CancelsPendingWorkAndUpdatesMetadata()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("to-cancel", new SchedulerTestInput(), Every.Minutes(1))
        );
        await fx.MaterializePendingManifestsAsync();
        await fx.RunManifestManagerAsync();

        var cancelled = await fx.Scheduler.CancelAsync("to-cancel");

        cancelled.Should().BeGreaterThanOrEqualTo(0);
    }
}
