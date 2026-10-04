using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// Which failed runs count on SQLite, which stores timestamps as text: a manifest's own failure
/// window is computed in the query, per row, and has to compare like the instants it encodes.
/// </summary>
[TestFixture]
public class SqliteFailureWindowTests : TestSetup
{
    [Test]
    public async Task A_manifests_own_window_overrides_the_scheduler_window()
    {
        // The scheduler counts a day of failures; this manifest counts only its last hour.
        var manifest = await CreateDueManifest(maxRetries: 0, failureWindow: TimeSpan.FromHours(1));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-2));

        await RunManifestManager();

        (await DataContext.DeadLetters.AsNoTracking().CountAsync(d => d.ManifestId == manifest.Id))
            .Should()
            .Be(0, "the failure started before the manifest's own one hour window");
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry.ScheduledAt.Should().BeNull("a failure outside the window does not back off the run");
    }

    [Test]
    public async Task A_failure_inside_the_manifests_own_window_still_counts()
    {
        var manifest = await CreateDueManifest(maxRetries: 0, failureWindow: TimeSpan.FromHours(3));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-2));

        await RunManifestManager();

        (await DataContext.DeadLetters.AsNoTracking().CountAsync(d => d.ManifestId == manifest.Id))
            .Should()
            .Be(1, "the failure is inside the manifest's three hour window");
    }

    [Test]
    public async Task A_success_after_failures_in_the_window_does_not_delay_the_next_run()
    {
        var manifest = await CreateDueManifest(maxRetries: 3);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-4));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-3));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-2));
        await CreateRun(manifest, TrainState.Completed, DateTime.UtcNow.AddHours(-1));

        await RunManifestManager();

        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry
            .ScheduledAt.Should()
            .BeNull(
                "the latest run succeeded, so the next occurrence is not a retry. The failures "
                    + "still count toward the dead letter. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    [Test]
    public async Task A_failure_after_a_success_backs_off_by_every_failure_in_the_window()
    {
        var manifest = await CreateDueManifest(maxRetries: 3);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-3));
        await CreateRun(manifest, TrainState.Completed, DateTime.UtcNow.AddHours(-2));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-1));

        await RunManifestManager();

        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry.ScheduledAt.Should().NotBeNull("the latest run failed, so this run is a retry");
    }

    private async Task RunManifestManager()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    /// <summary>An interval manifest whose next run is due now.</summary>
    private async Task<Manifest> CreateDueManifest(int maxRetries, TimeSpan? failureWindow = null)
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
                IntervalSeconds = 60,
                MaxRetries = maxRetries,
                Properties = new SchedulerTestInput { Value = "window" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-5);
        manifest.FailureWindowSeconds = (int?)failureWindow?.TotalSeconds;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task CreateRun(Manifest manifest, TrainState state, DateTime startTime)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "window" },
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = state;
        run.StartTime = startTime;
        run.EndTime = startTime.AddMinutes(1);
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }
}
