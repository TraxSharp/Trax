using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Models.Manifest;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// What an operator does to manifests, on a manifest's page and on the list: queue one now or later, edit its
/// settings, switch it on, cancel its runs, choose whether its retries replay decisions, and the same for a
/// selection. Each is checked on the host.
/// </summary>
[TestFixture]
public class ManifestActionsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:TriggerManifest")]
    public async Task Queue_now_queues_a_run_of_the_manifest()
    {
        var manifest = await Seed.OnDemandManifestAsync();
        var page = new ManifestDetailPage(Console, manifest.Id, nameof(IImportFeedTrain));
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("TriggerManifest");
        await Console.Button("Queue now").ClickAsync();
        await Console.ConfirmAsync("Queue now", $"Queue \"{nameof(IImportFeedTrain)}\" now?");
        await sent;

        await Wait.UntilAsync(
            () => Queued(manifest.Id),
            BrowserTimeouts.Server,
            $"manifest {manifest.Id} is queued"
        );
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:TriggerManifestDelayed")]
    public async Task Queue_later_queues_a_run_for_the_minutes_given()
    {
        var manifest = await Seed.OnDemandManifestAsync();
        var page = new ManifestDetailPage(Console, manifest.Id, nameof(IImportFeedTrain));
        await page.OpenAsync(Caller.Operator);

        await Console.Button("Queue later").ClickAsync();
        var dialog = Console.Dialog("Queue later");
        await dialog.GetByLabel("Minutes from now", new() { Exact = true }).FillAsync("30");
        var sent = Console.SendsAsync("TriggerManifestDelayed");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Queue later", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(Console.Toast("Queued to run in 30 min."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        var due = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking()
                .Where(w => w.ManifestId == manifest.Id)
                .Select(w => w.ScheduledAt)
                .SingleAsync()
        );
        due.Should().NotBeNull().And.BeAfter(DateTime.UtcNow.AddMinutes(25));
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:UpdateManifest")]
    public async Task Edit_saves_the_manifests_retries_and_priority()
    {
        var manifest = await Seed.OnDemandManifestAsync();
        var page = new ManifestDetailPage(Console, manifest.Id, nameof(IImportFeedTrain));
        await page.OpenAsync(Caller.Operator);

        await Console.Button("Edit").ClickAsync();
        var dialog = Console.Dialog("Edit manifest");
        var type = dialog.GetByRole(
            AriaRole.Combobox,
            new() { Name = "Schedule type", Exact = true }
        );
        await Assertions.Expect(type).ToHaveValueAsync("CRON", new() { Timeout = Render });
        // The schedule types read as words, not as the enum's members.
        await Assertions
            .Expect(type.GetByRole(AriaRole.Option))
            .ToHaveTextAsync(new[] { "None", "Cron", "Interval", "On demand", "Dependent" });
        await dialog
            .GetByRole(AriaRole.Spinbutton, new() { Name = "Max retries", Exact = true })
            .FillAsync("5");
        await dialog
            .GetByRole(AriaRole.Spinbutton, new() { Name = "Priority", Exact = true })
            .FillAsync("7");
        var sent = Console.SendsAsync("UpdateManifest");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(Console.Toast("Manifest updated."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.DetailField("Max retries"))
            .ToHaveTextAsync("5", new() { Timeout = Render });
        var stored = await Stored(manifest.Id);
        stored.MaxRetries.Should().Be(5);
        stored.Priority.Should().Be(7);
        stored
            .CronExpression.Should()
            .Be(manifest.CronExpression, "the schedule was left as it was");
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:EnableManifest")]
    public async Task The_switch_enables_a_disabled_manifest_without_asking()
    {
        var disabled = await Seed.DisabledManifestAsync();
        var page = new ManifestDetailPage(Console, disabled.Id, nameof(IImportFeedTrain));
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.EnabledSwitch)
            .Not.ToBeCheckedAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("EnableManifest");
        await page.EnabledSwitch.ClickAsync();
        await sent;

        await Assertions.Expect(page.EnabledSwitch).ToBeCheckedAsync(new() { Timeout = Render });
        (await Stored(disabled.Id)).IsEnabled.Should().BeTrue();
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:CancelManifest")]
    public async Task Cancel_runs_asks_the_manifests_running_run_to_stop()
    {
        var (manifest, run) = await Seed.ManifestWithAHeldRunAsync();
        var page = new ManifestDetailPage(Console, manifest.Id, nameof(IReconcileAccountTrain));
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("CancelManifest");
        await Console.Button("Cancel runs").ClickAsync();
        await Console.ConfirmAsync(
            "Cancel runs",
            $"Cancel every running run of \"{nameof(IReconcileAccountTrain)}\"?"
        );
        await sent;

        await Assertions
            .Expect(Console.Toast("Cancellation requested for 1 run(s)."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await CancellationRequested(run.Id)).Should().BeTrue();
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:SetManifestsReplayDecisionsOnRetry")]
    public async Task The_replay_switch_makes_retries_ask_afresh()
    {
        var manifest = await Seed.DecidingManifestAsync();
        var page = new ManifestDetailPage(Console, manifest.Id, nameof(ISettleClaimTrain));
        await page.OpenAsync(Caller.Operator);
        var replay = Page.GetByRole(
            AriaRole.Switch,
            new() { Name = "Replay decisions on retry", Exact = true }
        );
        await Assertions.Expect(replay).ToBeCheckedAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("SetManifestsReplayDecisionsOnRetry");
        await replay.ClickAsync();
        await sent;

        await Assertions.Expect(replay).Not.ToBeCheckedAsync(new() { Timeout = Render });
        (await Stored(manifest.Id)).ReplayDecisionsOnRetry.Should().BeFalse();
    }

    [Test]
    [Covers("route:/schedules")]
    [Covers("mutation:TriggerManifest")]
    public async Task Queue_on_a_manifest_row_asks_and_queues_a_run_of_it()
    {
        var manifest = await Seed.OnDemandManifestAsync();
        var list = new ManifestsPage(Console);
        await list.OpenAsync(Caller.Operator);
        var row = list.Row(manifest.Id);

        var sent = Console.SendsAsync("TriggerManifest");
        await list.Button(row, "Queue").ClickAsync(new() { Timeout = Render });
        await Console.ConfirmAsync("Queue manifest", $"Queue \"{nameof(IImportFeedTrain)}\" now?");
        await sent;

        await Wait.UntilAsync(
            () => Queued(manifest.Id),
            BrowserTimeouts.Server,
            $"manifest {manifest.Id} is queued"
        );
    }

    [Test]
    [Covers("route:/schedules")]
    [Covers("mutation:DisableManifest")]
    [Covers("mutation:EnableManifest")]
    public async Task Disable_and_Enable_on_a_manifest_row_switch_it_off_and_on()
    {
        var manifest = await Seed.OnDemandManifestAsync();
        var list = new ManifestsPage(Console);
        await list.OpenAsync(Caller.Operator);
        var row = list.Row(manifest.Id);

        await list.Button(row, "Disable").ClickAsync(new() { Timeout = Render });
        await Assertions.Expect(row).ToContainTextAsync("Disabled", new() { Timeout = Render });
        (await Stored(manifest.Id)).IsEnabled.Should().BeFalse();

        await list.Button(row, "Enable").ClickAsync(new() { Timeout = Render });
        await Assertions
            .Expect(list.Button(row, "Disable"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await Stored(manifest.Id)).IsEnabled.Should().BeTrue();
    }

    [Test]
    [Covers("route:/schedules")]
    [Covers("mutation:TriggerManifests")]
    public async Task Queue_selected_queues_each_selected_manifest_in_one_request()
    {
        var first = await Seed.OnDemandManifestAsync();
        var second = await Seed.OnDemandManifestAsync();
        var list = new ManifestsPage(Console);
        await list.OpenAsync(Caller.Operator);
        await SelectAsync(list, first.Id, second.Id);

        var sent = Console.SendsAsync("TriggerManifests");
        await Console.Button("Queue selected").ClickAsync();
        await Console.ConfirmAsync("Queue manifests", "Queue 2 manifest(s) now?");
        await sent;

        foreach (var m in new[] { first, second })
            await Wait.UntilAsync(
                () => Queued(m.Id),
                BrowserTimeouts.Server,
                $"manifest {m.Id} is queued"
            );
        Checks.Sent.Count(s => s.OperationName == "TriggerManifests").Should().Be(1);
    }

    [Test]
    [Covers("route:/schedules")]
    [Covers("mutation:SetManifestsEnabled")]
    public async Task Disable_selected_and_Enable_selected_switch_each_selected_manifest()
    {
        var first = await Seed.OnDemandManifestAsync();
        var second = await Seed.OnDemandManifestAsync();
        var list = new ManifestsPage(Console);
        await list.OpenAsync(Caller.Operator);

        await SelectAsync(list, first.Id, second.Id);
        await Console.Button("Disable selected").ClickAsync();
        await Assertions
            .Expect(list.Row(second.Id))
            .ToContainTextAsync("Disabled", new() { Timeout = Render });
        (await Stored(first.Id)).IsEnabled.Should().BeFalse();
        (await Stored(second.Id)).IsEnabled.Should().BeFalse();

        await SelectAsync(list, first.Id, second.Id);
        await Console.Button("Enable selected").ClickAsync();
        await Assertions
            .Expect(list.Row(second.Id))
            .ToContainTextAsync("Enabled", new() { Timeout = Render });
        (await Stored(first.Id)).IsEnabled.Should().BeTrue();
        (await Stored(second.Id)).IsEnabled.Should().BeTrue();
        Checks.Sent.Count(s => s.OperationName == "SetManifestsEnabled").Should().Be(2);
    }

    [Test]
    [Covers("route:/schedules")]
    [Covers("mutation:SetManifestsReplayDecisionsOnRetry")]
    public async Task Ask_afresh_on_retry_applies_to_the_selected_manifests()
    {
        var manifest = await Seed.DecidingManifestAsync();
        var list = new ManifestsPage(Console);
        await list.OpenAsync(Caller.Operator);
        await SelectAsync(list, manifest.Id);

        var sent = Console.SendsAsync("SetManifestsReplayDecisionsOnRetry");
        await Console.Button("Ask afresh on retry").ClickAsync(new() { Timeout = Render });
        await sent;

        await Assertions
            .Expect(list.Row(manifest.Id))
            .ToContainTextAsync("Ask afresh", new() { Timeout = Render });
        (await Stored(manifest.Id)).ReplayDecisionsOnRetry.Should().BeFalse();
    }

    private static async Task SelectAsync(ManifestsPage list, params long[] ids)
    {
        foreach (var id in ids)
            await list.Row(id).GetByRole(AriaRole.Checkbox).CheckAsync(new() { Timeout = Render });
    }

    private static Task<Manifest> Stored(long id) =>
        Host.QueryAsync(dc => dc.Manifests.AsNoTracking().SingleAsync(m => m.Id == id));

    // A trigger queues an entry, which the dispatcher may already have turned into a run.
    private static Task<bool> Queued(long manifest) =>
        Host.QueryAsync(async dc =>
            await dc.WorkQueues.AsNoTracking().AnyAsync(w => w.ManifestId == manifest)
            || await dc.Metadatas.AsNoTracking().AnyAsync(m => m.ManifestId == manifest)
        );

    private static Task<bool> CancellationRequested(long run) =>
        Host.QueryAsync(dc =>
            dc.Metadatas.AsNoTracking()
                .Where(m => m.Id == run)
                .Select(m => m.CancellationRequested)
                .SingleAsync()
        );
}
