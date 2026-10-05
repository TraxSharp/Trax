using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.OperationsTests;

/// <summary>Reading and steering manifests over the GraphQL operations surface.</summary>
public class ManifestOperationsTests : SchedulingTestFixture
{
    [Test]
    public async Task Manifests_query_lists_every_manifest_with_its_schedule()
    {
        var response = await Operator.Send(
            """
            {
              operations {
                manifests(take: 50) {
                  items { id externalId scheduleType intervalSeconds cronExpression maxRetries dependsOnManifestId }
                  totalCount
                }
              }
            }
            """
        );

        var items = response.Data("operations", "manifests", "items").EnumerateArray().ToList();
        var byId = items.ToDictionary(i => i.GetProperty("externalId").GetString()!);

        byId.Keys.Should()
            .BeEquivalentTo(
                ManifestNames.RefreshExchangeRates,
                ManifestNames.RepriceCatalog,
                ManifestNames.AlertRateSpike,
                ManifestNames.SendDailyDigest,
                ManifestNames.SendLaunchAnnouncement,
                ManifestNames.ImportSupplierFeed
            );
        byId[ManifestNames.RefreshExchangeRates]
            .GetProperty("scheduleType")
            .GetString()
            .Should()
            .Be("INTERVAL");
        byId[ManifestNames.SendDailyDigest]
            .GetProperty("cronExpression")
            .GetString()
            .Should()
            .Be("0 7 * * *");
        byId[ManifestNames.RepriceCatalog]
            .GetProperty("scheduleType")
            .GetString()
            .Should()
            .Be("DEPENDENT");
        byId[ManifestNames.AlertRateSpike]
            .GetProperty("scheduleType")
            .GetString()
            .Should()
            .Be("DORMANT_DEPENDENT");
        byId[ManifestNames.SendLaunchAnnouncement]
            .GetProperty("scheduleType")
            .GetString()
            .Should()
            .Be("ONCE");
        byId[ManifestNames.ImportSupplierFeed].GetProperty("maxRetries").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task Manifest_query_reads_one_manifest_by_id()
    {
        var digest = await Db.Manifest(ManifestNames.SendDailyDigest);

        var response = await Operator.Send(
            $$"""{ operations { manifest(id: {{digest.Id}}) { id externalId isEnabled } } }"""
        );

        var manifest = response.Data("operations", "manifest");
        manifest.GetProperty("externalId").GetString().Should().Be(ManifestNames.SendDailyDigest);
        manifest.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Trigger_runs_a_dependent_now_without_waiting_for_its_parent()
    {
        // Not the cron manifest: a success moves its next run to the occurrence after that
        // success, which CronScheduleTests reads.
        var reprice = await Db.Manifest(ManifestNames.RepriceCatalog);
        var marker = await Db.LatestMetadataId();

        var response = await Operator.Send(
            $$"""
            mutation {
              operations { triggerManifest(externalId: "{{ManifestNames.RepriceCatalog}}") { success message } }
            }
            """
        );
        response
            .Data("operations", "triggerManifest")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(response.Body);

        await Db.WaitForRun(reprice.Id, TrainState.Completed, marker, TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task Disable_and_enable_switch_a_manifest_off_and_on()
    {
        const string disable =
            $$"""mutation { operations { disableManifest(externalId: "{{ManifestNames.SendDailyDigest}}") { success } } }""";
        const string enable =
            $$"""mutation { operations { enableManifest(externalId: "{{ManifestNames.SendDailyDigest}}") { success } } }""";

        try
        {
            (await Operator.Send(disable)).Data("operations", "disableManifest");
            (await Db.Manifest(ManifestNames.SendDailyDigest)).IsEnabled.Should().BeFalse();

            (await Operator.Send(enable)).Data("operations", "enableManifest");
            (await Db.Manifest(ManifestNames.SendDailyDigest)).IsEnabled.Should().BeTrue();
        }
        finally
        {
            await Scheduler.EnableAsync(ManifestNames.SendDailyDigest);
        }
    }

    [Test]
    public async Task Executions_query_filters_runs_by_manifest()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        await Db.WaitForRun(refresh.Id, TrainState.Completed, 0, TimeSpan.FromSeconds(20));

        var response = await Operator.Send(
            $$"""
            {
              operations {
                executions(manifestId: {{refresh.Id}}, trainState: COMPLETED, take: 5) {
                  items { id manifestId trainState }
                }
              }
            }
            """
        );

        var items = response.Data("operations", "executions", "items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items
            .Should()
            .AllSatisfy(i => i.GetProperty("manifestId").GetInt64().Should().Be(refresh.Id));
    }
}
