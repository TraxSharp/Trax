using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Enums;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Overview leads with what needs attention, read in one call, and every entry and every chart bar opens the
/// list it counts.
/// </summary>
[TestFixture]
public class OverviewBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    public async Task A_paused_dispatcher_leads_the_overview_and_opens_the_scheduler_settings()
    {
        await Seed.PausedDispatcherAsync();
        var overview = new OverviewPage(Console);

        await overview.OpenAsync(Caller.Operator);
        var paused = overview.AttentionLink(new Regex("^Job dispatcher paused"));
        await Assertions.Expect(paused).ToBeVisibleAsync(new() { Timeout = Render });

        await paused.ClickAsync();
        var settings = new SchedulerPage(Console);
        await Assertions.Expect(settings.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        Page.Url.Should().EndWith("/trax/dashboard/system/scheduler");
    }

    [Test]
    public async Task Dead_letters_that_failed_alike_are_one_entry_that_opens_them_filtered_by_train_and_reason()
    {
        var (first, _) = await Seed.DeadLettersSharingAReasonAsync();
        var train = typeof(IImportFeedTrain).FullName!;
        // Earlier tests on this host may have left dead letters of the same train and reason: the entry
        // counts them all.
        var awaiting = await Host.QueryAsync(dc =>
            dc.DeadLetters.AsNoTracking()
                .CountAsync(d =>
                    d.Status == DeadLetterStatus.AwaitingIntervention
                    && d.Reason == first.Reason
                    && d.Manifest!.Name == train
                )
        );
        awaiting.Should().BeGreaterThanOrEqualTo(2);

        var overview = new OverviewPage(Console);
        await overview.OpenAsync(Caller.Operator);
        var group = overview.AttentionLink(
            new Regex($"^{awaiting} dead letters of {nameof(IImportFeedTrain)}")
        );
        await Assertions.Expect(group).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(group).ToContainTextAsync(first.Reason);

        await group.ClickAsync();
        await Assertions
            .Expect(new DeadLettersPage(Console).Heading)
            .ToBeVisibleAsync(new() { Timeout = Render });
        var url = new Uri(Page.Url);
        url.AbsolutePath.Should().Be("/trax/dashboard/runs/dead-letters");
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        query["status"].Should().Be("AWAITING_INTERVENTION");
        query["train"].Should().Be(train);
        query["reason"].Should().Be(first.Reason);
    }

    [Test]
    public async Task A_failure_bar_opens_the_trains_failed_runs()
    {
        await Seed.DeadLetterAsync();
        var train = typeof(IImportFeedTrain).FullName!;
        var failed = await Host.QueryAsync(dc =>
            dc.Metadatas.AsNoTracking()
                .CountAsync(m =>
                    m.Name == train
                    && m.TrainState == TrainState.Failed
                    && m.StartTime >= DateTime.UtcNow.AddDays(-7)
                )
        );

        var overview = new OverviewPage(Console);
        await overview.OpenAsync(Caller.Operator);
        var bar = overview.ChartBar(
            $"{failed} failed {(failed == 1 ? "run" : "runs")} of {nameof(IImportFeedTrain)}"
        );
        await Assertions.Expect(bar).ToBeVisibleAsync(new() { Timeout = Render });

        await bar.ClickAsync();
        // A train's failures open the Failed view of Runs, filtered to the train.
        await Assertions
            .Expect(Console.Heading("Failed runs"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        var url = new Uri(Page.Url);
        url.AbsolutePath.Should().Be("/trax/dashboard/runs");
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        query["state"].Should().Be("FAILED");
        query["train"].Should().Be(train);
    }
}
