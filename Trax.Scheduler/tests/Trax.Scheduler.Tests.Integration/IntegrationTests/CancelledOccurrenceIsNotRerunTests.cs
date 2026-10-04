using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A timed-out or operator-cancelled run is not retried and does not create a dead letter: it
/// consumes the occurrence it ran for, so the manifest next runs at its next scheduled occurrence.
///
/// <para>Enforces <c>docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
)]
public class CancelledOccurrenceIsNotRerunTests : TestSetup
{
    [Test]
    public async Task An_hourly_manifest_whose_run_was_cancelled_is_not_queued_again_on_the_next_cycle()
    {
        var manifest = await CreateHourlyManifest(
            lastSuccessfulRun: DateTime.UtcNow.AddMinutes(-61)
        );

        // This hour's run started a minute ago and was cancelled (a timeout, or an operator).
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.Cancelled;
        run.EndTime = DateTime.UtcNow;
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        await train.Run(Unit.Default);

        DataContext.Reset();
        var queued = await DataContext
            .WorkQueues.AsNoTracking()
            .CountAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);

        queued
            .Should()
            .Be(
                0,
                "this hour's occurrence ran and was cancelled; a cancelled run is not retried, so "
                    + "the next run is the next hourly occurrence, not the next five-second cycle. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    [Test]
    public async Task An_hourly_cron_manifest_whose_run_was_cancelled_is_not_queued_again_on_the_next_cycle()
    {
        var manifest = await CreateManifest(
            ScheduleType.Cron,
            cronExpression: "0 * * * *",
            lastSuccessfulRun: DateTime.UtcNow.AddHours(-2)
        );
        await CreateCancelledRun(manifest, endTime: DateTime.UtcNow);

        await RunManifestManager();

        (await QueuedCount(manifest))
            .Should()
            .Be(0, "the cancelled run consumed this hour's occurrence; the next is on the hour");
    }

    [Test]
    public async Task An_hourly_manifest_is_due_again_an_interval_after_its_cancelled_run()
    {
        var manifest = await CreateHourlyManifest(lastSuccessfulRun: DateTime.UtcNow.AddHours(-3));
        await CreateCancelledRun(manifest, endTime: DateTime.UtcNow.AddMinutes(-61));

        await RunManifestManager();

        (await QueuedCount(manifest))
            .Should()
            .Be(1, "the occurrence after the cancelled one has come due");
    }

    [Test]
    public async Task A_cancelled_run_older_than_the_last_success_does_not_hold_back_the_schedule()
    {
        var manifest = await CreateHourlyManifest(
            lastSuccessfulRun: DateTime.UtcNow.AddMinutes(-61)
        );
        await CreateCancelledRun(manifest, endTime: DateTime.UtcNow.AddHours(-3));

        await RunManifestManager();

        (await QueuedCount(manifest))
            .Should()
            .Be(1, "the last success is the later run, and an hour has passed since it");
    }

    [Test]
    public async Task A_once_manifest_whose_run_was_cancelled_is_not_run_again()
    {
        var manifest = await CreateManifest(
            ScheduleType.Once,
            scheduledAt: DateTime.UtcNow.AddMinutes(-10),
            lastSuccessfulRun: null
        );
        await CreateCancelledRun(manifest, endTime: DateTime.UtcNow.AddMinutes(-5));

        await RunManifestManager();

        (await QueuedCount(manifest)).Should().Be(0, "its one occurrence ran and was cancelled");
    }

    [Test]
    public async Task A_dependent_whose_run_was_cancelled_waits_for_the_parents_next_success()
    {
        var parent = await CreateHourlyManifest(lastSuccessfulRun: DateTime.UtcNow.AddMinutes(-10));
        // The parent's success is backed by a completed run, as the dependent check requires.
        await CreateRun(parent, TrainState.Completed, DateTime.UtcNow.AddMinutes(-10));
        var dependent = await CreateManifest(
            ScheduleType.Dependent,
            lastSuccessfulRun: DateTime.UtcNow.AddHours(-2),
            dependsOn: parent.Id
        );
        await CreateCancelledRun(dependent, endTime: DateTime.UtcNow.AddMinutes(-5));

        await RunManifestManager();

        (await QueuedCount(dependent))
            .Should()
            .Be(0, "the cancelled run was the one the parent's latest success started");
    }

    [Test]
    public async Task A_dependent_whose_cancelled_run_predates_its_last_success_counts_from_the_success()
    {
        var parent = await CreateHourlyManifest(lastSuccessfulRun: DateTime.UtcNow.AddMinutes(-15));
        await CreateRun(parent, TrainState.Completed, DateTime.UtcNow.AddMinutes(-15));
        var dependent = await CreateManifest(
            ScheduleType.Dependent,
            lastSuccessfulRun: DateTime.UtcNow.AddMinutes(-10),
            dependsOn: parent.Id
        );
        await CreateCancelledRun(dependent, endTime: DateTime.UtcNow.AddMinutes(-20));

        await RunManifestManager();

        (await QueuedCount(dependent))
            .Should()
            .Be(
                0,
                "the dependent already succeeded after the parent's latest success; the older "
                    + "cancel does not make that success count again"
            );
    }

    private async Task RunManifestManager()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    private Task<int> QueuedCount(Manifest manifest) =>
        DataContext
            .WorkQueues.AsNoTracking()
            .CountAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);

    private Task CreateCancelledRun(Manifest manifest, DateTime endTime) =>
        CreateRun(manifest, TrainState.Cancelled, endTime.AddMinutes(-1), endTime);

    private async Task CreateRun(
        Manifest manifest,
        TrainState state,
        DateTime startTime,
        DateTime? endTime = null
    )
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = state;
        run.StartTime = startTime;
        run.EndTime = endTime ?? startTime.AddSeconds(30);
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task<Manifest> CreateManifest(
        ScheduleType scheduleType,
        DateTime? lastSuccessfulRun,
        string? cronExpression = null,
        DateTime? scheduledAt = null,
        long? dependsOn = null
    )
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = scheduleType,
                CronExpression = cronExpression,
                ScheduledAt = scheduledAt,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = scheduleType.ToString() },
                DependsOnManifestId = dependsOn,
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = lastSuccessfulRun;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Manifest> CreateHourlyManifest(DateTime lastSuccessfulRun)
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "hourly" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = lastSuccessfulRun;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }
}
