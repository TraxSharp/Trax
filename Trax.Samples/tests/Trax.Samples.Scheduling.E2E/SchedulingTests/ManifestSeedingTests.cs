using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// What the builder calls in <c>Program.cs</c> write to <c>trax.manifest</c> at startup.
/// </summary>
public class ManifestSeedingTests : SchedulingTestFixture
{
    [Test]
    public async Task Interval_manifests_store_their_interval()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var import = await Db.Manifest(ManifestNames.ImportSupplierFeed);

        refresh.ScheduleType.Should().Be(ScheduleType.Interval);
        refresh.IntervalSeconds.Should().Be(5);
        import.ScheduleType.Should().Be(ScheduleType.Interval);
        import.IntervalSeconds.Should().Be(15);
    }

    [Test]
    public async Task Cron_manifest_stores_its_expression()
    {
        var digest = await Db.Manifest(ManifestNames.SendDailyDigest);

        digest.ScheduleType.Should().Be(ScheduleType.Cron);
        digest.CronExpression.Should().Be("0 7 * * *");
    }

    [Test]
    public async Task Dependent_and_dormant_dependent_point_at_their_parent()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var reprice = await Db.Manifest(ManifestNames.RepriceCatalog);
        var alert = await Db.Manifest(ManifestNames.AlertRateSpike);

        reprice.ScheduleType.Should().Be(ScheduleType.Dependent);
        reprice.DependsOnManifestId.Should().Be(refresh.Id);
        reprice.IntervalSeconds.Should().BeNull();
        reprice.CronExpression.Should().BeNull();

        alert.ScheduleType.Should().Be(ScheduleType.DormantDependent);
        alert.DependsOnManifestId.Should().Be(refresh.Id, "Include parents from the Schedule");
    }

    [Test]
    public async Task One_off_manifest_is_a_once_schedule()
    {
        var launch = await Db.Manifest(ManifestNames.SendLaunchAnnouncement);

        launch.ScheduleType.Should().Be(ScheduleType.Once);
        launch.ScheduledAt.Should().NotBeNull();
    }

    [Test]
    public async Task Failing_manifest_carries_its_retry_limit()
    {
        var import = await Db.Manifest(ManifestNames.ImportSupplierFeed);
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);

        import.MaxRetries.Should().Be(2);
        refresh.MaxRetries.Should().Be(3, "a manifest that states none takes DefaultMaxRetries");
    }
}
