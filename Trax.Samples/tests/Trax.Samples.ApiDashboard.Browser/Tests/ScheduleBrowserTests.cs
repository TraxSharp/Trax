using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Schedules area: a manifest's schedule in words and when it runs next, its run history, its on/off switch,
/// and the group graph toned by whether each group is enabled and whether its latest run failed.
/// </summary>
[TestFixture]
public class ScheduleBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/schedules")]
    public async Task A_nightly_manifest_reads_in_words_with_its_next_run()
    {
        var nightly = await Seed.NightlyManifestAsync();
        var manifests = new ManifestsPage(Console);

        await manifests.OpenAsync(Caller.Operator);

        var row = manifests.Row(nightly.Id);
        await Assertions
            .Expect(row)
            .ToContainTextAsync("Daily at 02:00 UTC", new() { Timeout = Render });
        await Assertions.Expect(row).ToContainTextAsync("02:00 UTC");
        await Assertions.Expect(row).Not.ToContainTextAsync("0 2 * * *");
        await Assertions.Expect(row).Not.ToContainTextAsync("Not scheduled");
        await Assertions
            .Expect(manifests.Tab("Manifests"))
            .ToHaveAttributeAsync("aria-selected", "true");
    }

    [Test]
    [Covers("route:/schedules/:id")]
    public async Task A_disabled_manifest_has_no_next_run_and_its_switch_is_off()
    {
        var disabled = await Seed.DisabledManifestAsync();
        var page = new ManifestDetailPage(Console, disabled.Id, nameof(IImportFeedTrain));

        await page.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(page.EnabledSwitch)
            .Not.ToBeCheckedAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Schedule)
            .ToContainTextAsync("No upcoming run while it is disabled", new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/schedules/:id")]
    [Covers("mutation:DisableManifest")]
    public async Task The_switch_disables_a_manifest_after_asking_and_the_next_runs_go()
    {
        var nightly = await Seed.NightlyManifestAsync();
        var page = new ManifestDetailPage(Console, nightly.Id, nameof(IImportFeedTrain));

        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.Schedule.GetByRole(AriaRole.Listitem))
            .ToHaveCountAsync(5, new() { Timeout = Render });

        await page.DisableAsync();

        await Assertions
            .Expect(page.EnabledSwitch)
            .Not.ToBeCheckedAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Schedule)
            .ToContainTextAsync("No upcoming run while it is disabled", new() { Timeout = Render });
        (
            await Host.QueryAsync(dc =>
                dc.Manifests.AsNoTracking()
                    .Where(m => m.Id == nightly.Id)
                    .Select(m => m.IsEnabled)
                    .SingleAsync()
            )
        )
            .Should()
            .BeFalse("the switch disables the manifest on the host");
    }

    [Test]
    [Covers("route:/schedules/:id")]
    public async Task A_manifests_run_history_counts_its_failed_run()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new ManifestDetailPage(Console, deadLetter.ManifestId, nameof(IImportFeedTrain));

        await page.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(page.RunHistory)
            .ToContainTextAsync("1 failed", new() { Timeout = Render });
        await page.RunHistory.GetByRole(AriaRole.Button, new() { Name = "7 days" }).ClickAsync();
        await Assertions
            .Expect(page.RunHistory)
            .ToContainTextAsync("1 failed", new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/schedules/groups")]
    public async Task The_group_graph_says_which_group_is_off_and_which_failed()
    {
        var off = await Seed.DisabledGroupAsync();
        var failing = await Seed.FailingGroupAsync();
        var groups = new ManifestGroupsPage(Console);

        await groups.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(groups.Node(off))
            .ToContainTextAsync("Disabled", new() { Timeout = Render });
        await Assertions
            .Expect(groups.Node(failing))
            .ToContainTextAsync("Last run failed", new() { Timeout = Render });
    }
}
