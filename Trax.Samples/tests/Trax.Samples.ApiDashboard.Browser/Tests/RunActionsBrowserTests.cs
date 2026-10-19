using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// What an operator does to runs: cancel one from its page or several from the list, requeue a failed one, cancel
/// the run a machine instance waits on, and watch a running run's steps arrive on its timeline as they happen.
/// </summary>
[TestFixture]
public class RunActionsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/runs/:id")]
    [Covers("mutation:CancelExecution")]
    public async Task Cancel_on_a_running_runs_page_asks_it_to_stop()
    {
        var (run, _) = await Seed.HeldRunAsync();
        var page = new RunPage(Console, run.Id, nameof(IReconcileAccountTrain));
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("CancelExecution");
        await page.Action("Cancel").ClickAsync(new() { Timeout = Render });
        await Console.ConfirmAsync("Cancel run", $"Request cancellation of run #{run.Id}?");
        await sent;

        await Assertions
            .Expect(Console.Toast("Cancellation requested."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await CancellationRequested(run.Id)).Should().BeTrue();
    }

    [Test]
    [Covers("route:/runs")]
    [Covers("mutation:CancelExecutions")]
    public async Task Cancel_selected_on_the_runs_list_asks_each_selected_run_to_stop()
    {
        var held = await Seed.HeldRunsAsync(2);
        var runs = new FilteredRunsPage(
            Console,
            $"state=IN_PROGRESS&train={Uri.EscapeDataString(typeof(IReconcileAccountTrain).FullName!)}"
        );
        await runs.OpenAsync(Caller.Operator);
        foreach (var run in held)
            await runs.Row(run.Id)
                .GetByRole(AriaRole.Checkbox)
                .CheckAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("CancelExecutions");
        await Console.Button("Cancel selected").ClickAsync();
        await Console.ConfirmAsync("Cancel runs", "Request cancellation of 2 run(s)?");
        var ids = (await sent).PostDataJSON()!.Value.GetProperty("variables").GetProperty("ids");
        ids.EnumerateArray()
            .Select(i => i.GetInt64())
            .Should()
            .BeEquivalentTo(held.Select(r => r.Id));

        foreach (var run in held)
            await Wait.UntilAsync(
                () => CancellationRequested(run.Id),
                BrowserTimeouts.Server,
                $"run {run.Id} is asked to cancel"
            );
    }

    [Test]
    [Covers("route:/runs/:id")]
    [Covers("mutation:RequeueExecution")]
    public async Task Requeue_on_a_failed_run_queues_its_input_again_and_opens_the_entry()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var page = new RunPage(Console, failed.Id, nameof(ISettleClaimTrain));
        await page.OpenAsync(Caller.Operator);

        var sent = Console.SendsAsync("RequeueExecution");
        await page.Action("Requeue").ClickAsync(new() { Timeout = Render });
        await Console.ConfirmAsync("Requeue run", $"Requeue run #{failed.Id}?");
        await sent;

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new Regex(@"/runs/queued/\d+$"), new() { Timeout = Render });
        var id = long.Parse(Page.Url[(Page.Url.LastIndexOf('/') + 1)..]);
        var entry = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == id)
        );
        entry.TrainName.Should().Be(typeof(ISettleClaimTrain).FullName);
        var key = System
            .Text.Json.JsonDocument.Parse(failed.Input!)
            .RootElement.EnumerateObject()
            .Single()
            .Value.GetString();
        entry.Input.Should().Contain(key!, "the run's own input is queued again");
        Checks.Sent.Count(s => s.OperationName == "RequeueExecution").Should().Be(1);
    }

    [Test]
    [Covers("route:/runs/:id")]
    [Covers("subscription:OnJunctionEvent")]
    public async Task A_running_runs_steps_are_pushed_to_its_timeline_as_they_end()
    {
        var (run, release) = await Seed.HeldRunAsync();
        var page = new RunPage(Console, run.Id, nameof(IReconcileAccountTrain));
        await page.OpenAsync(Caller.Operator);
        await Checks.SubscribedAsync("OnJunctionEvent", $"\"metadataId\":{run.Id}");

        release();

        await Checks.PushedAsync("onJunctionEvent", $"\"metadataId\":{run.Id}", "\"COMPLETED\"");
        await Assertions
            .Expect(page.TimelineStep(nameof(WaitForLedger)))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Timeline.Locator("[title^='COMPLETED']").First)
            .ToBeVisibleAsync(new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/machines/:machine/:owner/:id")]
    [Covers("mutation:CancelMachineInstance")]
    public async Task Cancel_on_an_instance_page_cancels_the_run_its_state_waits_on()
    {
        var (instance, run) = await Seed.InstanceWaitingOnAHeldRunAsync();
        var page = new MachineInstancePage(Console, RelayMachine.Id, instance.Id);
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.Lit)
            .ToHaveTextAsync(
                new Regex($"^{nameof(RelayState.Relaying)}"),
                new() { Timeout = Render }
            );

        var sent = Console.SendsAsync("CancelMachineInstance");
        await Console.Button("Cancel").ClickAsync(new() { Timeout = Render });
        await Console.ConfirmAsync("Cancel the run it waits on", "Cancel the train run");
        await sent;

        await Wait.UntilAsync(
            () => CancellationRequested(run.Id),
            BrowserTimeouts.Server,
            $"the run {RelayMachine.Id} {instance.Id} waits on is asked to cancel"
        );
    }

    private static Task<bool> CancellationRequested(long run) =>
        Host.QueryAsync(dc =>
            dc.Metadatas.AsNoTracking()
                .Where(m => m.Id == run)
                .Select(m => m.CancellationRequested)
                .SingleAsync()
        );
}
