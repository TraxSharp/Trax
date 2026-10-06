using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="IOperationsService.UpdateManifestAsync"/>, the one path the GraphQL
/// <c>updateManifest</c> mutation takes. Every check runs before a field is written, the cron
/// expression included, so a schedule the scheduler cannot evaluate is never saved.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>:
/// the validation lives in the shared service, not in a surface.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceUpdateManifestTests : TestSetup
{
    private IOperationsService _operations = null!;

    [SetUp]
    public void GetService() =>
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();

    private async Task<Manifest> SeedCronManifest(string cron = "0 * * * *")
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Cron,
                CronExpression = cron,
                Properties = new SchedulerTestInput { Value = "update" },
            }
        );
        manifest.ManifestGroupId = (
            await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}")
        ).Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Manifest> Reload(long id) =>
        await DataContext.Manifests.AsNoTracking().SingleAsync(m => m.Id == id);

    [TestCase("99 * * * *", "not a valid cron expression")]
    [TestCase("0 0 30 2 *", "never fires")]
    [TestCase("* * *", "has 3 field(s)")]
    public async Task A_cron_expression_the_scheduler_cannot_use_is_refused_and_nothing_saved(
        string cron,
        string reason
    )
    {
        var manifest = await SeedCronManifest();

        var result = await _operations.UpdateManifestAsync(
            manifest.Id,
            new ManifestUpdate(MaxRetries: 9, CronExpression: cron),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(reason);
        var stored = await Reload(manifest.Id);
        stored.CronExpression.Should().Be("0 * * * *");
        stored.MaxRetries.Should().NotBe(9, "a refused patch changes nothing");
    }

    [TestCase(-1, null, null, "maxRetries")]
    [TestCase(null, 32, null, "priority")]
    [TestCase(null, null, 0, "timeoutSeconds")]
    public async Task A_value_out_of_range_is_refused(
        int? maxRetries,
        int? priority,
        int? timeoutSeconds,
        string field
    )
    {
        var manifest = await SeedCronManifest();

        var result = await _operations.UpdateManifestAsync(
            manifest.Id,
            new ManifestUpdate(
                MaxRetries: maxRetries,
                Priority: priority,
                TimeoutSeconds: timeoutSeconds
            ),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(field);
    }

    [Test]
    public async Task A_switch_to_a_schedule_that_needs_a_parent_is_refused()
    {
        var manifest = await SeedCronManifest();

        var result = await _operations.UpdateManifestAsync(
            manifest.Id,
            new ManifestUpdate(ScheduleType: ScheduleType.Dependent),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("parent manifest");
    }

    [Test]
    public async Task A_valid_patch_is_saved()
    {
        var manifest = await SeedCronManifest();

        var result = await _operations.UpdateManifestAsync(
            manifest.Id,
            new ManifestUpdate(
                IsEnabled: false,
                MaxRetries: 5,
                Priority: 7,
                TimeoutSeconds: 30,
                CronExpression: "*/15 * * * *"
            ),
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Id.Should().Be(manifest.Id);
        var stored = await Reload(manifest.Id);
        stored.IsEnabled.Should().BeFalse();
        stored.MaxRetries.Should().Be(5);
        stored.Priority.Should().Be(7);
        stored.TimeoutSeconds.Should().Be(30);
        stored.CronExpression.Should().Be("*/15 * * * *");
    }

    [Test]
    public async Task A_manifest_with_a_bad_stored_cron_can_still_be_disabled()
    {
        var manifest = await SeedCronManifest("99 * * * *");

        var result = await _operations.UpdateManifestAsync(
            manifest.Id,
            new ManifestUpdate(IsEnabled: false),
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        (await Reload(manifest.Id)).IsEnabled.Should().BeFalse();
    }

    [Test]
    public async Task An_unknown_manifest_is_refused()
    {
        var result = await _operations.UpdateManifestAsync(
            long.MaxValue,
            new ManifestUpdate(IsEnabled: false),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }
}
