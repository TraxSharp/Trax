using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Live events page draws only what the server pushes over the subscription socket: a run's state changes
/// arrive on <c>onTrainStateChanged</c> and land in its stream, and the change signals the list pages refetch on
/// arrive on <c>onDataChanged</c> and are counted by domain. Nothing on the page polls.
/// </summary>
[TestFixture]
public class LiveBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/system/live")]
    [Covers("subscription:OnTrainStateChanged")]
    [Covers("subscription:OnDataChanged")]
    public async Task A_run_queued_after_the_page_opened_is_pushed_into_its_stream_and_its_signal_counted()
    {
        await Console.OpenAsync("system/live", Caller.Operator);
        await Assertions
            .Expect(Console.Heading("Live events"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Checks.SubscribedAsync("OnTrainStateChanged");
        await Checks.SubscribedAsync("OnDataChanged");

        var run = await Seed.GatedRunAsync();

        await Checks.PushedAsync("onTrainStateChanged", $"\"metadataId\":{run.Id}");
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Link, new() { Name = $"#{run.Id}", Exact = true }).First
            )
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Checks.PushedAsync("onDataChanged", "\"WORK_QUEUE\"");
        await Assertions
            .Expect(
                Page.GetByText("Work queue", new() { Exact = true })
                    .Locator("xpath=following-sibling::p")
            )
            .Not.ToHaveTextAsync("0", new() { Timeout = Render });
    }
}
