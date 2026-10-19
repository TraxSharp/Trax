using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// A train's page on the host: its declared chain is drawn whether or not it has run, its recent runs are a grid of
/// steps whose cells open the run, and what starts it (a manifest, a machine's state) is listed with Run and Queue.
/// </summary>
[TestFixture]
public class TrainPageBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/trains/:name")]
    public async Task A_train_that_never_ran_draws_its_chain_and_offers_Run_and_Queue()
    {
        var page = new TrainPage(Console, typeof(IAuditLedgerTrain));

        await page.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(page.Step("ReadLedger"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Step("SealLedger"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.NeverRan).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.Grid).ToHaveCountAsync(0);

        await page.Button(Page.GetByRole(AriaRole.Main), "Queue a run").ClickAsync();
        await Assertions
            .Expect(Console.Heading("Queue a train"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

        await page.Button(Page.GetByRole(AriaRole.Main), "Run now").ClickAsync();
        await Assertions
            .Expect(Console.Heading("Run a train"))
            .ToBeVisibleAsync(new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/trains/:name")]
    public async Task A_failed_run_is_drawn_on_the_step_it_stopped_at()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var page = new TrainPage(Console, typeof(ISettleClaimTrain));

        await page.OpenAsync(Caller.Operator);

        var cell = page.Cell(failed.Id, nameof(NotifyClaimant), "Failed");
        await Assertions.Expect(cell).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(cell)
            .ToHaveAttributeAsync(
                "href",
                new System.Text.RegularExpressions.Regex($"/runs/{failed.Id}$")
            );
        await Assertions
            .Expect(page.Cell(failed.Id, nameof(LoadClaim), "Completed"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.FailureCount).ToHaveCountAsync(1, new() { Timeout = Render });

        await cell.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(Console.Url($"runs/{failed.Id}"));
    }

    [Test]
    [Covers("route:/trains/:name")]
    public async Task A_scheduled_train_says_when_its_manifest_runs_next()
    {
        var page = new TrainPage(Console, typeof(IRefreshRatesTrain));

        await page.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(
                page.Manifests.GetByRole(
                    AriaRole.Link,
                    new() { Name = "refresh-rates", Exact = true }
                )
            )
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Manifests)
            .ToContainTextAsync("Next run", new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/trains/:name")]
    public async Task A_train_a_machine_state_invokes_names_that_state()
    {
        var page = new TrainPage(Console, typeof(IRelayParcelTrain));

        await page.OpenAsync(Caller.Operator);

        await Assertions
            .Expect(
                page.StartedByMachines.GetByRole(
                    AriaRole.Link,
                    new()
                    {
                        Name = $"Instances of {RelayMachine.Id} in {nameof(RelayState.Relaying)}",
                        Exact = true,
                    }
                )
            )
            .ToBeVisibleAsync(new() { Timeout = Render });
    }
}
