using AwesomeAssertions;
using Microsoft.Playwright;
using Trax.Samples.Recovery.Browser.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace Trax.Samples.Recovery.Browser.Tests;

/// <summary>
/// The page as the demo user drives it, on its generated client: a refund that crashes once is followed into its
/// retry through runProgress, and a placed order's delivery run is the one the order's answer names. Neither view sends
/// anything under <c>operations</c>.
/// <para>Enforces <c>Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class UserPageBrowserTests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    [Test]
    public async Task A_refund_that_crashes_once_is_followed_into_its_retry()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.Page.GotoAsync(RecoveryPage.PageOrigin + "/");
        await page.Page.GetByRole(AriaRole.Tab, new() { Name = "Workflows" }).ClickAsync();
        await page.Page.GetByRole(AriaRole.Tab, new() { Name = "Refund" }).ClickAsync();

        await page
            .Page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true })
            .ClickAsync();
        var statuses = page.Page.Locator(".lane-row .lane-status .status");
        await Expect(statuses).ToHaveCountAsync(2, new() { Timeout = BrowserTimeouts.Server.Ms() });
        await Expect(statuses.Nth(0))
            .ToHaveTextAsync("Crashed", new() { Timeout = BrowserTimeouts.Server.Ms() });
        await Expect(statuses.Nth(1))
            .ToHaveTextAsync(
                new System.Text.RegularExpressions.Regex("^(Recovered|Completed)$"),
                new() { Timeout = BrowserTimeouts.Server.Ms() }
            );

        page.RequestsNaming("operations")
            .Should()
            .BeEmpty("the page is a user's and follows its runs through runProgress, per " + Adr);
        page.RequestsNaming("trax_progress_query_")
            .Should()
            .NotBeEmpty("each attempt is read once through runProgress");
    }

    [Test]
    public async Task A_placed_order_is_delivered_and_its_run_followed_by_its_owner()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.Page.GotoAsync(RecoveryPage.PageOrigin + "/");
        await page.Page.GetByLabel("Scenario").SelectOptionAsync("order");

        var place = page.Page.GetByRole(AriaRole.Button, new() { Name = "Place order" });
        var readTheRun = page.Page.WaitForRequestAsync(
            r => (r.PostData ?? "").Contains("trax_machine_order_get_", StringComparison.Ordinal),
            new() { Timeout = BrowserTimeouts.Server.Ms() }
        );
        await place.ClickAsync();

        // The page reads the run the order queued from the order's answer, follows it, and is done when it ends.
        await readTheRun;
        await Expect(place).ToBeEnabledAsync(new() { Timeout = BrowserTimeouts.Server.Ms() });
        await Expect(page.Page.Locator(".run-problem")).ToHaveCountAsync(0);
        page.RequestsNaming("operations")
            .Should()
            .BeEmpty("the order's run is found through the order's own answer, per " + Adr);
        page.RequestsNaming("trax_machine_order_get_")
            .Should()
            .NotBeEmpty(
                "the page reads the run the order queued from the order's answer (invokedRun)"
            );
    }
}
