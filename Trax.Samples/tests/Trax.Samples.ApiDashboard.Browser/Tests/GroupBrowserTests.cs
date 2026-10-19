using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Trax.Effect.Models.ManifestGroup;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;
using Trax.Scheduler.Services.Operations;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// Manifest groups: a group's page saves its settings, queues its manifests and cancels its runs; the list does
/// the same for a selection, and switches every group off and on.
/// </summary>
[TestFixture]
public class GroupBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/schedules/groups/:id")]
    [Covers("mutation:UpdateManifestGroup")]
    public async Task A_groups_page_lists_its_manifest_and_saves_its_settings()
    {
        var (group, _) = await Seed.GroupOfOneAsync();
        var page = new ManifestGroupPage(Console, group.Id, group.Name);
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(
                page.Main.GetByRole(
                    AriaRole.Link,
                    new() { Name = nameof(IImportFeedTrain), Exact = true }
                )
            )
            .ToBeVisibleAsync(new() { Timeout = Render });

        await page.Setting("Max active jobs in this group (blank = no group limit)").FillAsync("2");
        await page.Setting("Priority").FillAsync("9");
        var sent = Console.SendsAsync("UpdateManifestGroup");
        await Console.Button("Save").ClickAsync();
        await sent;

        await Assertions.Expect(Console.Toast("")).ToBeVisibleAsync(new() { Timeout = Render });
        var stored = await Stored(group.Id);
        stored.MaxActiveJobs.Should().Be(2);
        stored.Priority.Should().Be(9);
        stored.IsEnabled.Should().BeTrue("the switch was left on");
    }

    [Test]
    [Covers("route:/schedules/groups/:id")]
    [Covers("mutation:TriggerGroup")]
    public async Task Queue_group_queues_each_manifest_in_the_group()
    {
        var (group, manifest) = await Seed.GroupOfOneAsync();
        var page = new ManifestGroupPage(Console, group.Id, group.Name);
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("TriggerGroup");
        await Console.Button("Queue group").ClickAsync();
        await Console.ConfirmAsync("Queue group", $"Queue every manifest in \"{group.Name}\" now?");
        await sent;

        await Wait.UntilAsync(
            () => Queued(manifest.Id),
            BrowserTimeouts.Server,
            $"the manifest of {group.Name} is queued"
        );
    }

    [Test]
    [Covers("route:/schedules/groups/:id")]
    [Covers("mutation:CancelGroup")]
    public async Task Cancel_group_asks_the_groups_running_run_to_stop()
    {
        var name = Seed.NewKey("busy");
        var (_, run) = await Seed.ManifestWithAHeldRunAsync(name);
        var group = await Seed.GroupNamedAsync(name);
        var page = new ManifestGroupPage(Console, group.Id, group.Name);
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("CancelGroup");
        await Console.Button("Cancel group").ClickAsync();
        await Console.ConfirmAsync(
            "Cancel group",
            $"Cancel every pending and running run in \"{name}\"?"
        );
        await sent;

        await Assertions
            .Expect(Console.Toast("Cancellation requested for 1 execution(s)"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await CancellationRequested(run.Id)).Should().BeTrue();
    }

    [Test]
    [Covers("route:/schedules/groups")]
    [Covers("mutation:TriggerGroups")]
    public async Task Queue_selected_queues_the_manifests_of_each_selected_group()
    {
        var (group, manifest) = await Seed.GroupOfOneAsync();
        var list = await OpenFilteredAsync(group.Name);
        await list.Row(group.Id)
            .GetByRole(AriaRole.Checkbox)
            .CheckAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("TriggerGroups");
        await Console.Button("Queue selected").ClickAsync();
        await Console.ConfirmAsync("Queue groups", "Queue every manifest in 1 group(s) now?");
        await sent;

        await Wait.UntilAsync(
            () => Queued(manifest.Id),
            BrowserTimeouts.Server,
            $"the manifest of {group.Name} is queued"
        );
    }

    [Test]
    [Covers("route:/schedules/groups")]
    [Covers("mutation:SetManifestGroupsEnabled")]
    public async Task Disable_selected_and_Enable_selected_switch_the_selected_group()
    {
        var (group, _) = await Seed.GroupOfOneAsync();
        var list = await OpenFilteredAsync(group.Name);
        var row = list.Row(group.Id);

        await row.GetByRole(AriaRole.Checkbox).CheckAsync(new() { Timeout = Render });
        await Console.Button("Disable selected").ClickAsync();
        await Assertions.Expect(row).ToContainTextAsync("Disabled", new() { Timeout = Render });
        (await Stored(group.Id)).IsEnabled.Should().BeFalse();

        await row.GetByRole(AriaRole.Checkbox).CheckAsync(new() { Timeout = Render });
        await Console.Button("Enable selected").ClickAsync();
        await Assertions.Expect(row).ToContainTextAsync("Enabled", new() { Timeout = Render });
        (await Stored(group.Id)).IsEnabled.Should().BeTrue();
    }

    [Test]
    [Covers("route:/schedules/groups")]
    [Covers("mutation:CancelGroups")]
    public async Task Cancel_running_asks_the_runs_of_each_selected_group_to_stop()
    {
        var name = Seed.NewKey("busy");
        var (_, run) = await Seed.ManifestWithAHeldRunAsync(name);
        var group = await Seed.GroupNamedAsync(name);
        var list = await OpenFilteredAsync(name);
        await list.Row(group.Id)
            .GetByRole(AriaRole.Checkbox)
            .CheckAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("CancelGroups");
        await Console.Button("Cancel running").ClickAsync();
        await Console.ConfirmAsync("Cancel running work", "in 1 group(s)?");
        await sent;

        await Wait.UntilAsync(
            () => CancellationRequested(run.Id),
            BrowserTimeouts.Server,
            $"the run of {name} is asked to cancel"
        );
    }

    [Test]
    [Covers("route:/schedules/groups")]
    [Covers("mutation:SetAllManifestGroupsEnabled")]
    public async Task Disable_all_asks_for_all_typed_and_Enable_all_switches_every_group_back_on()
    {
        var (group, _) = await Seed.GroupOfOneAsync();
        try
        {
            var list = new ManifestGroupsPage(Console);
            await list.OpenAsync(Caller.Operator);

            var off = Console.SendsAsync("SetAllManifestGroupsEnabled");
            await Console.Button("Disable all").ClickAsync(new() { Timeout = Render });
            await Console.ConfirmTypingAsync(
                "Disable all groups",
                "Disable every manifest group?",
                "all"
            );
            await off;
            await Wait.UntilAsync(
                () => Host.QueryAsync(dc => dc.ManifestGroups.AllAsync(g => !g.IsEnabled)),
                BrowserTimeouts.Server,
                "every group is disabled"
            );

            var on = Console.SendsAsync("SetAllManifestGroupsEnabled");
            await Console.Button("Enable all").ClickAsync(new() { Timeout = Render });
            await Console.ConfirmAsync("Enable all groups", "Enable every manifest group?");
            await on;
            await Wait.UntilAsync(
                () => Host.QueryAsync(dc => dc.ManifestGroups.AllAsync(g => g.IsEnabled)),
                BrowserTimeouts.Server,
                "every group is enabled again"
            );
            (await Stored(group.Id)).IsEnabled.Should().BeTrue();
        }
        finally
        {
            await using var scope = Host.Services.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .SetAllManifestGroupsEnabledAsync(true, CancellationToken.None);
        }
    }

    private async Task<ManifestGroupsPage> OpenFilteredAsync(string name)
    {
        var list = new ManifestGroupsPage(Console);
        await list.OpenAsync(Caller.Operator);
        await list.NameFilter.FillAsync(name);
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link) })
            )
            .ToHaveCountAsync(1, new() { Timeout = Render });
        return list;
    }

    private static Task<ManifestGroup> Stored(long id) =>
        Host.QueryAsync(dc => dc.ManifestGroups.AsNoTracking().SingleAsync(g => g.Id == id));

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
