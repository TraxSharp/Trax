using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that every scheduler feature configured in Program.cs is correctly
/// persisted to the database — misfire policies, exclusion windows, variance,
/// group config, cron expressions, and interval seconds.
/// </summary>
[TestFixture]
public class ManifestConfigurationTests : SchedulerTestFixture
{
    #region MisfirePolicies

    [Test]
    public async Task MisfireFireOnce_HasFireOnceNowPolicy()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MisfireFireOnce);

        manifest.MisfirePolicy.Should().Be(MisfirePolicy.FireOnceNow);
    }

    [Test]
    public async Task MisfireDoNothing_HasDoNothingPolicyWithThreshold()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MisfireDoNothing);

        manifest.MisfirePolicy.Should().Be(MisfirePolicy.DoNothing);
        manifest.MisfireThresholdSeconds.Should().Be(10);
    }

    #endregion

    #region ExclusionWindows

    [Test]
    public async Task WeekdayLeaderboard_HasTwoExclusions()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.WeekdayLeaderboard);

        var exclusions = manifest.GetExclusions();

        exclusions.Should().HaveCount(2);
    }

    [Test]
    public async Task WeekdayLeaderboard_HasDaysOfWeekExclusion()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.WeekdayLeaderboard);

        var exclusions = manifest.GetExclusions();

        var daysExclusion = exclusions.FirstOrDefault(e => e.Type == ExclusionType.DaysOfWeek);
        daysExclusion.Should().NotBeNull();
        daysExclusion!.DaysOfWeek.Should().Contain(DayOfWeek.Saturday);
        daysExclusion.DaysOfWeek.Should().Contain(DayOfWeek.Sunday);
    }

    [Test]
    public async Task WeekdayLeaderboard_HasTimeWindowExclusion()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.WeekdayLeaderboard);

        var exclusions = manifest.GetExclusions();

        var timeExclusion = exclusions.FirstOrDefault(e => e.Type == ExclusionType.TimeWindow);
        timeExclusion.Should().NotBeNull();
        timeExclusion!.StartTime.Should().Be(TimeOnly.Parse("02:00"));
        timeExclusion.EndTime.Should().Be(TimeOnly.Parse("04:00"));
    }

    #endregion

    #region Variance

    [Test]
    public async Task VarianceInterval_Has15SecondsVariance()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.VarianceInterval);

        manifest.VarianceSeconds.Should().Be(15);
    }

    [Test]
    public async Task VarianceCron_Has1800SecondsVariance()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.VarianceCron);

        // 30 minutes = 1800 seconds
        manifest.VarianceSeconds.Should().Be(1800);
    }

    #endregion

    #region BatchScheduling

    [Test]
    public async Task ProcessMatchManifests_ShareSameManifestGroup()
    {
        var manifests = new List<Effect.Models.Manifest.Manifest>();
        foreach (var region in ManifestNames.Regions)
        {
            var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == externalId);
            manifests.Add(manifest);
        }

        var groupIds = manifests.Select(m => m.ManifestGroupId).Distinct().ToList();
        groupIds.Should().HaveCount(1, "all process-match manifests should share one group");
    }

    [Test]
    public async Task ProcessMatchGroup_HasMaxActiveJobs5()
    {
        var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, "na");
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == externalId);

        var group = await DataContext
            .ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Id == manifest.ManifestGroupId);

        group.MaxActiveJobs.Should().Be(5);
    }

    [Test]
    public async Task ProcessMatchManifests_HavePriority24()
    {
        foreach (var region in ManifestNames.Regions)
        {
            var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == externalId);

            manifest.Priority.Should().Be(24, $"process-match-{region} should have priority 24");
        }
    }

    #endregion

    #region CronSchedule

    [Test]
    public async Task DistributeDailyRewards_IsCronSchedule()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.DistributeDailyRewards);

        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region IntervalSchedule

    [Test]
    public async Task RecalculateLeaderboard_HasInterval300Seconds()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);

        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
        manifest.IntervalSeconds.Should().Be(300); // 5 minutes
    }

    [Test]
    public async Task CorruptedDataRepair_HasInterval30Seconds()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
        manifest.IntervalSeconds.Should().Be(30);
    }

    [Test]
    public async Task CleanupInactivePlayers_HasInterval3600Seconds()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.CleanupInactivePlayers);

        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
        manifest.IntervalSeconds.Should().Be(3600); // 1 hour
    }

    #endregion

    #region ProcessMatchVariance

    [Test]
    public async Task ProcessMatchManifests_HaveVariance120Seconds()
    {
        foreach (var region in ManifestNames.Regions)
        {
            var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == externalId);

            // 2 minutes = 120 seconds
            manifest
                .VarianceSeconds.Should()
                .Be(120, $"process-match-{region} should have 120s variance");
        }
    }

    #endregion
}
