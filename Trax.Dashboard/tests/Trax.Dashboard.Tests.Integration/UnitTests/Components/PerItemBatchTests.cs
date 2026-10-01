using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The batch actions the scheduler offers only one item at a time (Trigger Selected on the
/// manifests and manifest groups pages, Cancel Running on the groups page) call it once per
/// item, carry on past an item that fails, report how many were handled and which failed, and
/// clear the selection so a retry does not send the handled items again.
/// </summary>
[TestFixture]
public class PerItemBatchTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingScheduler _scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        var (scheduler, recorder) = RecordingScheduler.Create();
        _scheduler = recorder;
        _ctx.Services.AddSingleton(scheduler);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Trigger_selected_manifests_carries_on_past_a_failure_and_reports_each_outcome()
    {
        await SeedManifestAsync("group-a", "manifest-a");
        var (_, b) = await SeedManifestAsync("group-b", "manifest-b");
        await SeedManifestAsync("group-c", "manifest-c");
        await SeedManifestAsync("group-d", "manifest-d");
        await SeedQueuedEntryAsync(b);
        _scheduler.Respond = (method, args) =>
            method == nameof(ITraxScheduler.TriggerAsync)
                ? (string)args[0]! == "manifest-c"
                    ? throw new InvalidOperationException("manifest-c is broken")
                    : Task.CompletedTask
                : throw new InvalidOperationException($"{method} was not expected.");

        var page = _ctx.RenderComponent<ManifestsPage>();
        foreach (var group in new[] { "group-a", "group-b", "group-c", "group-d" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Trigger Selected");

        _scheduler
            .CallsTo(nameof(ITraxScheduler.TriggerAsync))
            .Select(args => (string)args[0]!)
            .Should()
            .BeEquivalentTo(
                ["manifest-a", "manifest-b", "manifest-c", "manifest-d"],
                "a failure on one manifest does not stop the others"
            );
        page.WaitForAssertion(
            () =>
            {
                Messages()
                    .Should()
                    .Contain(
                        "2 queued, 1 already queued (that entry now runs as the trigger), 1 failed."
                    );
                page.Find(".rz-alert").TextContent.Should().Contain("manifest-c is broken");
                ButtonText(page, "Trigger Selected")
                    .Should()
                    .BeEmpty("the handled manifests must not be sent again by a retry");
            },
            WaitTimeout
        );
    }

    [Test]
    public async Task Trigger_selected_groups_carries_on_past_a_failure()
    {
        var (groupA, _) = await SeedManifestAsync("group-a", "manifest-a");
        var (groupB, _) = await SeedManifestAsync("group-b", "manifest-b");
        var (groupC, _) = await SeedManifestAsync("group-c", "manifest-c");
        _scheduler.Respond = (method, args) =>
            method == nameof(ITraxScheduler.TriggerGroupAsync)
                ? (long)args[0]! == groupB
                    ? throw new InvalidOperationException("group-b is broken")
                    : Task.FromResult(1)
                : throw new InvalidOperationException($"{method} was not expected.");

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        foreach (var group in new[] { "group-a", "group-b", "group-c" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Trigger Selected");

        _scheduler
            .CallsTo(nameof(ITraxScheduler.TriggerGroupAsync))
            .Select(args => (long)args[0]!)
            .Should()
            .BeEquivalentTo([groupA, groupB, groupC]);
        page.WaitForAssertion(
            () =>
            {
                Messages()
                    .Should()
                    .Contain("2 manifest(s) queued across 2 group(s), 1 group(s) failed.");
                page.Find(".rz-alert").TextContent.Should().Contain("group-b is broken");
                ButtonText(page, "Trigger Selected").Should().BeEmpty();
            },
            WaitTimeout
        );
    }

    [Test]
    public async Task Cancel_running_groups_carries_on_past_a_failure()
    {
        var (groupA, _) = await SeedManifestAsync("group-a", "manifest-a");
        var (groupB, _) = await SeedManifestAsync("group-b", "manifest-b");
        _scheduler.Respond = (method, args) =>
            method == nameof(ITraxScheduler.CancelGroupAsync)
                ? (long)args[0]! == groupA
                    ? throw new InvalidOperationException("group-a is broken")
                    : Task.FromResult(3)
                : throw new InvalidOperationException($"{method} was not expected.");

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        foreach (var group in new[] { "group-a", "group-b" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Cancel Running");

        _scheduler
            .CallsTo(nameof(ITraxScheduler.CancelGroupAsync))
            .Select(args => (long)args[0]!)
            .Should()
            .BeEquivalentTo([groupA, groupB]);
        page.WaitForAssertion(
            () =>
            {
                Messages()
                    .Should()
                    .Contain(
                        "Cancellation requested for 3 execution(s) across 1 group(s), 1 group(s) failed."
                    );
                page.Find(".rz-alert").TextContent.Should().Contain("group-a is broken");
                ButtonText(page, "Cancel Running").Should().BeEmpty();
            },
            WaitTimeout
        );
    }

    private IEnumerable<string> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages.Select(m => m.Detail);

    private async Task<(long GroupId, long ManifestId)> SeedManifestAsync(
        string groupName,
        string externalId
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = groupName };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(ITriggeredTrain) });
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return (group.Id, manifest.Id);
    }

    private async Task SeedQueuedEntryAsync(long manifestId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ITriggeredTrain).FullName!,
                ManifestId = manifestId,
            }
        );
        db.WorkQueues.Add(entry);
        await db.SaveChanges(default);
    }

    public interface ITriggeredTrain { }
}
