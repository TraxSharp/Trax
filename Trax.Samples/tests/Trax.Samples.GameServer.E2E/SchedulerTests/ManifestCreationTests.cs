using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class ManifestCreationTests : SchedulerTestFixture
{
    [Test]
    public async Task Startup_CreatesAllExpectedManifests()
    {
        var manifests = await DataContext
            .Manifests.AsNoTracking()
            .Select(m => m.ExternalId)
            .ToListAsync();

        // Core manifests
        manifests.Should().Contain(ManifestNames.RecalculateLeaderboard);
        manifests.Should().Contain(ManifestNames.GenerateSeasonReport);
        manifests.Should().Contain(ManifestNames.DistributeDailyRewards);
        manifests.Should().Contain(ManifestNames.CleanupInactivePlayers);
        manifests.Should().Contain(ManifestNames.CorruptedDataRepair);

        // Misfire policy
        manifests.Should().Contain(ManifestNames.MisfireFireOnce);
        manifests.Should().Contain(ManifestNames.MisfireDoNothing);

        // One-off
        manifests.Should().Contain(ManifestNames.WelcomeBonus);

        // Variance
        manifests.Should().Contain(ManifestNames.VarianceInterval);
        manifests.Should().Contain(ManifestNames.VarianceCron);

        // Exclusion window
        manifests.Should().Contain(ManifestNames.WeekdayLeaderboard);

        // Batch per region (process-match-na, process-match-eu, process-match-ap)
        foreach (var region in ManifestNames.Regions)
        {
            manifests.Should().Contain(ManifestNames.WithIndex(ManifestNames.ProcessMatch, region));
            manifests.Should().Contain(ManifestNames.WithIndex(ManifestNames.DetectCheat, region));
        }
    }

    [Test]
    public async Task DetectCheatManifests_AreDormantDependents()
    {
        foreach (var region in ManifestNames.Regions)
        {
            var cheatExternalId = ManifestNames.WithIndex(ManifestNames.DetectCheat, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ExternalId == cheatExternalId);

            manifest.Should().NotBeNull($"manifest '{cheatExternalId}' should exist");
            manifest!.ScheduleType.Should().Be(ScheduleType.DormantDependent);
            manifest.DependsOnManifestId.Should().NotBeNull();

            // Verify it depends on the corresponding process-match manifest
            var processMatchExternalId = ManifestNames.WithIndex(
                ManifestNames.ProcessMatch,
                region
            );
            var parentManifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ExternalId == processMatchExternalId);

            parentManifest.Should().NotBeNull();
            manifest.DependsOnManifestId.Should().Be(parentManifest!.Id);
        }
    }

    [Test]
    public async Task GenerateSeasonReport_IsDependentOnRecalculateLeaderboard()
    {
        var reportManifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == ManifestNames.GenerateSeasonReport);

        var leaderboardManifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);

        reportManifest.Should().NotBeNull();
        leaderboardManifest.Should().NotBeNull();

        reportManifest!.ScheduleType.Should().Be(ScheduleType.Dependent);
        reportManifest.DependsOnManifestId.Should().Be(leaderboardManifest!.Id);
    }

    [Test]
    public async Task CorruptedDataRepair_HasMaxRetries1()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

        manifest.Should().NotBeNull();
        manifest!.MaxRetries.Should().Be(1);
    }

    [Test]
    public async Task WelcomeBonus_IsOnceScheduleType()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == ManifestNames.WelcomeBonus);

        manifest.Should().NotBeNull();
        manifest!.ScheduleType.Should().Be(ScheduleType.Once);
    }
}
