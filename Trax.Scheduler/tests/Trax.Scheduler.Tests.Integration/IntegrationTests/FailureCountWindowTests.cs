using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest's failed runs count toward its retry backoff and its dead letter only while they
/// started within <see cref="SchedulerConfiguration.FailureCountWindow"/>, so failures spread
/// over weeks neither delay every later run nor dead-letter a healthy manifest.
///
/// <para>Enforces <c>docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
)]
public class FailureCountWindowTests : TestSetup
{
    private SchedulerConfiguration _config = null!;
    private TimeSpan _previousWindow;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _previousWindow = _config.FailureCountWindow;
    }

    [TearDown]
    public void RestoreWindow() => _config.FailureCountWindow = _previousWindow;

    [Test]
    public async Task A_failure_thirty_days_ago_does_not_delay_the_next_run()
    {
        var manifest = await CreateDueManifest(maxRetries: 3);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddDays(-30));
        await CreateRun(manifest, TrainState.Completed, DateTime.UtcNow.AddDays(-29));

        await RunManifestManager();

        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry
            .ScheduledAt.Should()
            .BeNull(
                "a failure outside the 24 hour window no longer backs off the next run. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    [Test]
    public async Task Three_failures_over_ninety_days_do_not_dead_letter_the_manifest()
    {
        var manifest = await CreateDueManifest(maxRetries: 2);
        foreach (var daysAgo in new[] { 85, 55, 25 })
        {
            await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddDays(-daysAgo));
            await CreateRun(manifest, TrainState.Completed, DateTime.UtcNow.AddDays(-daysAgo + 1));
        }

        await RunManifestManager();

        (await DataContext.DeadLetters.AsNoTracking().CountAsync(d => d.ManifestId == manifest.Id))
            .Should()
            .Be(
                0,
                "each failure is weeks old and was followed by successes. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
        (await DataContext.WorkQueues.AsNoTracking().CountAsync(q => q.ManifestId == manifest.Id))
            .Should()
            .Be(1, "a healthy interval manifest keeps running");
    }

    [Test]
    public async Task Failures_inside_the_window_beyond_max_retries_dead_letter_the_manifest()
    {
        var manifest = await CreateDueManifest(maxRetries: 2);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-20));
        await CreateRun(manifest, TrainState.Completed, DateTime.UtcNow.AddHours(-15));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-10));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-1));

        await RunManifestManager();

        var deadLetters = await DataContext
            .DeadLetters.AsNoTracking()
            .Where(d => d.ManifestId == manifest.Id)
            .ToListAsync();
        deadLetters
            .Should()
            .ContainSingle("three failures in 24 hours exceed two retries, success or not");
        deadLetters[0].RetryCountAtDeadLetter.Should().Be(3);
    }

    [Test]
    public async Task A_failure_inside_the_window_still_backs_off_the_next_run()
    {
        var manifest = await CreateDueManifest(maxRetries: 3);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-2));

        await RunManifestManager();

        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry.ScheduledAt.Should().NotBeNull("a recent failure is retried with backoff");
    }

    [Test]
    public async Task The_configured_window_decides_which_failures_count()
    {
        _config.FailureCountWindow = TimeSpan.FromHours(1);
        var manifest = await CreateDueManifest(maxRetries: 0);
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-2));

        await RunManifestManager();

        (await DataContext.DeadLetters.AsNoTracking().CountAsync(d => d.ManifestId == manifest.Id))
            .Should()
            .Be(0, "the only failure started before the one hour window");
    }

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
    public async Task A_manifests_own_window_can_count_further_back_than_the_scheduler_window()
    {
        _config.FailureCountWindow = TimeSpan.FromHours(1);
        var manifest = await CreateDueManifest(maxRetries: 0, failureWindow: TimeSpan.FromDays(2));
        await CreateRun(manifest, TrainState.Failed, DateTime.UtcNow.AddHours(-30));

        await RunManifestManager();

        (await DataContext.DeadLetters.AsNoTracking().CountAsync(d => d.ManifestId == manifest.Id))
            .Should()
            .Be(1, "the failure is inside the manifest's two day window");
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
