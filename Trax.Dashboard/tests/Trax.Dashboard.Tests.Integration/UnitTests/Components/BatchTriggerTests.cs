using AwesomeAssertions;
using Bunit;
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
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// Trigger Selected on the manifests and manifest groups pages, and Cancel Running on the groups
/// page, send the whole selection to the operations service in one call, the call the API's
/// batch mutations make, and report what the service said. A refused batch keeps the selection;
/// an accepted one clears it so a retry does not send the handled items again.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class BatchTriggerTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private OperationsCallLog _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _operations = RecordingOperationsService.Register(_ctx.Services, _data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Trigger_selected_manifests_is_one_service_call_and_reports_its_counts()
    {
        var (_, a) = await SeedManifestAsync("group-a", "manifest-a");
        var (_, b) = await SeedManifestAsync("group-b", "manifest-b");
        var (_, c) = await SeedManifestAsync("group-c", "manifest-c");
        await SeedQueuedEntryAsync(b);

        var page = _ctx.RenderComponent<ManifestsPage>();
        foreach (var group in new[] { "group-a", "group-b", "group-c" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Trigger Selected (3)");

        page.WaitForAssertion(
            () =>
            {
                Messages()
                    .Should()
                    .Contain(
                        "2 queued, 1 already queued (that entry now runs as the trigger) across 3 of 3 manifest(s)."
                    );
                ButtonText(page, "Trigger Selected")
                    .Should()
                    .BeEmpty("the handled manifests must not be sent again by a retry");
            },
            WaitTimeout
        );
        var call = _operations
            .CallsTo(nameof(IOperationsService.TriggerManifestsAsync))
            .Should()
            .ContainSingle("the selection goes to the service in one call")
            .Subject;
        ((IEnumerable<long>)call[0]!).Should().BeEquivalentTo([a, b, c]);
        call[1].Should().Be(false);
        page.FindAll(".rz-alert").Should().BeEmpty();
    }

    [Test]
    public async Task A_refused_trigger_shows_the_refusal_and_keeps_the_selection()
    {
        await SeedManifestAsync("group-a", "manifest-a");
        _operations.Respond = (method, _) =>
            method == nameof(IOperationsService.TriggerManifestsAsync)
                ? Task.FromResult(
                    new BatchTriggerResult(false, 0, 0, 0, 0, 0, "Too many ids: at most 1000.", [])
                )
                : null;

        var page = _ctx.RenderComponent<ManifestsPage>();
        WaitForRow(page, "group-a");
        await ToggleRow(page, "group-a");
        await ClickButton(page, "Trigger Selected (1)");

        page.WaitForAssertion(
            () =>
                page.Find(".rz-alert").TextContent.Should().Contain("Too many ids: at most 1000."),
            WaitTimeout
        );
        Messages().Should().BeEmpty();
        ButtonText(page, "Trigger Selected (1)")
            .Should()
            .NotBeEmpty("a refused batch leaves the selection to be changed and sent again");
    }

    [Test]
    public async Task A_note_from_the_service_is_shown_and_the_notice_is_a_warning()
    {
        await SeedManifestAsync("group-a", "manifest-a");
        _operations.Respond = (method, args) =>
            method == nameof(IOperationsService.TriggerManifestsAsync)
                ? Task.FromResult(
                    new BatchTriggerResult(
                        true,
                        1,
                        0,
                        0,
                        0,
                        1,
                        "0 queued, 1 not found across 0 of 1 manifest(s).",
                        [
                            new BatchItemNote(
                                ((IEnumerable<long>)args[0]!).Single(),
                                "Manifest gone not found."
                            ),
                        ]
                    )
                )
                : null;

        var page = _ctx.RenderComponent<ManifestsPage>();
        WaitForRow(page, "group-a");
        await ToggleRow(page, "group-a");
        await ClickButton(page, "Trigger Selected (1)");

        page.WaitForAssertion(
            () =>
            {
                _ctx.Services.GetRequiredService<NotificationService>()
                    .Messages.Should()
                    .ContainSingle(m =>
                        m.Severity == NotificationSeverity.Warning
                        && m.Detail == "0 queued, 1 not found across 0 of 1 manifest(s)."
                    );
                page.Find(".rz-alert").TextContent.Should().Contain("Manifest gone not found.");
            },
            WaitTimeout
        );
    }

    [Test]
    public async Task Trigger_selected_groups_is_one_service_call()
    {
        var (groupA, _) = await SeedManifestAsync("group-a", "manifest-a");
        var (groupB, _) = await SeedManifestAsync("group-b", "manifest-b");

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        foreach (var group in new[] { "group-a", "group-b" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Trigger Selected");

        page.WaitForAssertion(
            () =>
            {
                Messages().Should().Contain("2 queued across 2 of 2 manifest group(s).");
                ButtonText(page, "Trigger Selected").Should().BeEmpty();
            },
            WaitTimeout
        );
        (
            (IEnumerable<long>)
                _operations
                    .CallsTo(nameof(IOperationsService.TriggerManifestGroupsAsync))
                    .Should()
                    .ContainSingle()
                    .Subject[0]!
        )
            .Should()
            .BeEquivalentTo([groupA, groupB]);
    }

    [Test]
    public async Task Cancel_running_groups_is_one_service_call()
    {
        var (groupA, _) = await SeedManifestAsync("group-a", "manifest-a");
        var (groupB, _) = await SeedManifestAsync("group-b", "manifest-b");

        var page = _ctx.RenderComponent<ManifestGroupsPage>();
        foreach (var group in new[] { "group-a", "group-b" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }

        await ClickButton(page, "Cancel Running");

        page.WaitForAssertion(
            () =>
            {
                Messages()
                    .Should()
                    .Contain(
                        "Cancellation requested for 0 execution(s) across 2 of 2 manifest group(s)."
                    );
                ButtonText(page, "Cancel Running").Should().BeEmpty();
            },
            WaitTimeout
        );
        (
            (IEnumerable<long>)
                _operations
                    .CallsTo(nameof(IOperationsService.CancelManifestGroupsAsync))
                    .Should()
                    .ContainSingle()
                    .Subject[0]!
        )
            .Should()
            .BeEquivalentTo([groupA, groupB]);
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
