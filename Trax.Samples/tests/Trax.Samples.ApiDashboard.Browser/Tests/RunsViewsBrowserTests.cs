using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// Runs is one area of four views, All, Failed, Queued and Dead letters, behind one tab strip, and every id on
/// them opens its page: a failed run is found under Failed, and a queued entry blocked behind another run about
/// the same subject links the entry it waits on.
/// </summary>
[TestFixture]
public class RunsViewsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/runs")]
    [Covers("route:/runs/queued")]
    [Covers("route:/runs/dead-letters")]
    public async Task The_tab_strip_moves_between_the_four_views_and_Failed_lists_the_failed_run()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var runs = new RunsPage(Console);
        await runs.OpenAsync(Caller.Operator);

        await RunViews.Tab(Console, "Failed").ClickAsync();
        await Assertions.Expect(runs.FailedHeading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(RunViews.Tab(Console, "Failed"))
            .ToHaveAttributeAsync("aria-current", "page");
        var row = await runs.FindAsync(failed.ExternalId, nameof(ISettleClaimTrain));
        await Assertions.Expect(row).ToContainTextAsync("Failed", new() { Timeout = Render });
        await Assertions
            .Expect(row.GetByRole(AriaRole.Link, new() { Name = $"#{failed.Id}", Exact = true }))
            .ToHaveAttributeAsync(
                "href",
                new System.Text.RegularExpressions.Regex($"/runs/{failed.Id}$")
            );

        await RunViews.Tab(Console, "Queued").ClickAsync();
        await Assertions
            .Expect(new WorkQueuePage(Console).Heading)
            .ToBeVisibleAsync(new() { Timeout = Render });

        await RunViews.Tab(Console, "Dead letters").ClickAsync();
        await Assertions
            .Expect(new DeadLettersPage(Console).Heading)
            .ToBeVisibleAsync(new() { Timeout = Render });

        await RunViews.Tab(Console, "All").ClickAsync();
        await Assertions.Expect(runs.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(RunViews.Tab(Console, "All"))
            .ToHaveAttributeAsync("aria-current", "page");
    }

    [Test]
    [Covers("route:/runs/queued")]
    [Covers("route:/runs/queued/:id")]
    public async Task A_blocked_entry_links_itself_and_its_page_links_the_entry_it_waits_on()
    {
        var staged = await Seed.StagedQueueEntryAsync();
        var (blocked, holding) = await Seed.BlockedQueueEntryAsync();
        var holder = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking()
                .Where(w => w.MetadataId == holding.Id)
                .Select(w => w.Id)
                .SingleAsync()
        );

        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(queue.Row(staged.Id))
            .ToContainTextAsync("Staged", new() { Timeout = Render });

        await queue.IdLink(queue.Row(blocked.Id), blocked.Id).ClickAsync();
        var entry = new WorkQueueEntryPage(Console);
        await Assertions
            .Expect(entry.WaitingOn(holder))
            .ToBeVisibleAsync(new() { Timeout = Render });

        await entry.WaitingOn(holder).ClickAsync();
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(
                new System.Text.RegularExpressions.Regex($"/runs/queued/{holder}$"),
                new() { Timeout = Render }
            );
    }
}
