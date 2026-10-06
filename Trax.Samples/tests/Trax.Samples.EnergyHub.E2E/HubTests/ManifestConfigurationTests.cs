using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class ManifestConfigurationTests : HubTestFixture
{
    [Test]
    public async Task Startup_CreatesAllExpectedManifests()
    {
        var manifests = await DataContext.Manifests.AsNoTracking().ToListAsync();
        var externalIds = manifests.Select(m => m.ExternalId).ToHashSet();

        externalIds.Should().Contain(ManifestNames.MonitorSolarProduction);
        externalIds.Should().Contain(ManifestNames.ManageBatteryStorage);
        externalIds.Should().Contain(ManifestNames.OptimizeMicrogrid);
        externalIds.Should().Contain(ManifestNames.TradeGridEnergy);
        externalIds.Should().Contain(ManifestNames.GenerateSustainabilityReport);

        foreach (var zone in ManifestNames.Zones)
        {
            externalIds
                .Should()
                .Contain(ManifestNames.WithIndex(ManifestNames.ProcessChargingSession, zone));
        }
    }

    [Test]
    public async Task MonitorSolarProduction_HasInterval300Seconds()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MonitorSolarProduction);

        manifest.IntervalSeconds.Should().Be(300);
        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
    }

    [Test]
    public async Task MonitorSolarProduction_HasVariance60Seconds()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MonitorSolarProduction);

        manifest.VarianceSeconds.Should().Be(60);
    }

    [Test]
    public async Task ManageBatteryStorage_IsDependentOnSolar()
    {
        var solar = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MonitorSolarProduction);

        var battery = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.ManageBatteryStorage);

        battery.ScheduleType.Should().Be(ScheduleType.Dependent);
        battery.DependsOnManifestId.Should().Be(solar.Id);
    }

    [Test]
    public async Task ProcessChargingSession_ShareSameGroup()
    {
        var manifests = await DataContext
            .Manifests.AsNoTracking()
            .Where(m =>
                m.ExternalId
                    == ManifestNames.WithIndex(ManifestNames.ProcessChargingSession, "plaza")
                || m.ExternalId
                    == ManifestNames.WithIndex(ManifestNames.ProcessChargingSession, "data-center")
                || m.ExternalId
                    == ManifestNames.WithIndex(ManifestNames.ProcessChargingSession, "parking")
            )
            .ToListAsync();

        manifests.Should().HaveCount(3);

        var groupIds = manifests.Select(m => m.ManifestGroupId).Distinct().ToList();
        groupIds.Should().HaveCount(1, "all 3 zone manifests should share the same group");
    }

    [Test]
    public async Task ProcessChargingSession_GroupHasMaxActiveJobs3()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m =>
                m.ExternalId
                == ManifestNames.WithIndex(ManifestNames.ProcessChargingSession, "plaza")
            );

        var group = await DataContext
            .ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Id == manifest.ManifestGroupId);

        group.MaxActiveJobs.Should().Be(3);
    }

    [Test]
    public async Task TradeGridEnergy_IsCronSchedule()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.TradeGridEnergy);

        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task GenerateSustainabilityReport_IsDailyCron()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.GenerateSustainabilityReport);

        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().NotBeNullOrEmpty();
    }
}
