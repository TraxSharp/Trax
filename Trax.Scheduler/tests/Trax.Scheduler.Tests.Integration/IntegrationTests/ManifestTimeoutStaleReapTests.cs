using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
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
/// A run inside its manifest's own Timeout is not failed as stale because that Timeout is longer
/// than <see cref="SchedulerConfiguration.StaleInProgressTimeout"/>: it is reaped at the manifest's
/// timeout plus the grace between the default job timeout and the stale timeout.
/// </summary>
[TestFixture]
public class ManifestTimeoutStaleReapTests : TestSetup
{
    private SchedulerConfiguration _config = null!;
    private TimeSpan _previousStale;
    private TimeSpan _previousDefault;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _previousStale = _config.StaleInProgressTimeout;
        _previousDefault = _config.DefaultJobTimeout;
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(60);
        _config.DefaultJobTimeout = TimeSpan.FromMinutes(20);
    }

    [TearDown]
    public void RestoreTimeouts()
    {
        _config.StaleInProgressTimeout = _previousStale;
        _config.DefaultJobTimeout = _previousDefault;
    }

    [Test]
    public async Task A_run_61_minutes_into_a_three_hour_timeout_stays_in_progress()
    {
        var run = await CreateInProgressRun(TimeSpan.FromHours(3), startedMinutesAgo: 61);

        await RunManifestManager();

        (await Load(run))
            .TrainState.Should()
            .Be(TrainState.InProgress, "the run is well inside its manifest's three hour timeout");
    }

    [Test]
    public async Task A_run_past_its_manifest_timeout_and_the_grace_is_failed()
    {
        // 3 h timeout + 40 min grace (60 min stale - 20 min default) = 220 min.
        var run = await CreateInProgressRun(TimeSpan.FromHours(3), startedMinutesAgo: 225);

        await RunManifestManager();

        (await Load(run)).TrainState.Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task A_run_past_its_manifest_timeout_but_inside_the_grace_stays_in_progress()
    {
        var run = await CreateInProgressRun(TimeSpan.FromHours(3), startedMinutesAgo: 200);

        await RunManifestManager();

        (await Load(run))
            .TrainState.Should()
            .Be(TrainState.InProgress, "its cancellation still has the grace period to land");
    }

    [Test]
    public async Task A_run_with_a_short_manifest_timeout_is_still_reaped_at_the_stale_timeout()
    {
        var run = await CreateInProgressRun(TimeSpan.FromMinutes(5), startedMinutesAgo: 61);

        await RunManifestManager();

        (await Load(run)).TrainState.Should().Be(TrainState.Failed);
    }

    [TestCase(165, TrainState.InProgress)]
    [TestCase(185, TrainState.Failed)]
    public async Task With_a_stale_timeout_shorter_than_the_default_job_timeout_a_run_is_reaped_at_its_manifest_timeout(
        int startedMinutesAgo,
        TrainState expected
    )
    {
        // The grace (stale minus default) would be -30 minutes; it counts as none, so a run is
        // never failed before its own manifest's timeout.
        _config.StaleInProgressTimeout = TimeSpan.FromMinutes(30);
        _config.DefaultJobTimeout = TimeSpan.FromMinutes(60);
        var run = await CreateInProgressRun(TimeSpan.FromHours(3), startedMinutesAgo);

        await RunManifestManager();

        (await Load(run)).TrainState.Should().Be(expected);
    }

    private async Task RunManifestManager()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    private Task<Metadata> Load(Metadata run) =>
        DataContext.Metadatas.AsNoTracking().FirstAsync(m => m.Id == run.Id);

    private async Task<Metadata> CreateInProgressRun(TimeSpan timeout, int startedMinutesAgo)
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                // Disabled, so the ManifestManager does not also queue it.
                IsEnabled = false,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 86400,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "long" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.TimeoutSeconds = (int)timeout.TotalSeconds;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);

        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "long" },
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.InProgress;
        run.StartTime = DateTime.UtcNow.AddMinutes(-startedMinutesAgo);
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return run;
    }
}
