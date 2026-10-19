using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// An operator finds the instances stuck in one machine state: the machines list names every registered machine,
/// a machine's page draws its states with how many instances each holds and how many are stuck, and clicking a
/// state or asking for the stuck ones lists them with what each is waiting for.
/// </summary>
[TestFixture]
public class MachineBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    public async Task The_machines_list_names_a_machine_with_no_instance()
    {
        var machines = new MachinesPage(Console);
        await machines.OpenAsync(Caller.Operator);

        // The host registers the gate and the reminder whether or not anything started one.
        await Assertions
            .Expect(machines.Machine(GateMachine.Id))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(machines.Machine(ReminderMachine.Id))
            .ToBeVisibleAsync(new() { Timeout = Render });

        await machines
            .Machine(ReminderMachine.Id)
            .GetByRole(AriaRole.Link, new() { Name = ReminderMachine.Id, Exact = true })
            .ClickAsync();
        await Assertions
            .Expect(Console.Heading(ReminderMachine.Id))
            .ToBeVisibleAsync(new() { Timeout = Render });
    }

    [Test]
    public async Task An_overdue_timer_is_counted_stuck_on_its_state_and_listed_by_it()
    {
        var overdue = await Seed.OverdueTimerAsync();
        var page = new MachinePage(Console, ReminderMachine.Id);

        await page.OpenAsync(Caller.Operator);
        var pending = nameof(ReminderState.Pending);
        await Assertions
            .Expect(page.Badge(pending, "Stuck"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(page.Badge(pending, "Instances"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        // The timer's edge says how long the state is held.
        await Assertions
            .Expect(page.Diagram.GetByText("after 1 second", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = Render });

        await page.State(pending).ClickAsync();
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new System.Text.RegularExpressions.Regex("[?&]state=Pending"));
        var row = page.Instance(overdue.Id);
        await Assertions.Expect(row).ToContainTextAsync("Overdue", new() { Timeout = Render });
        await Assertions.Expect(row).ToContainTextAsync("Stuck", new() { Timeout = Render });

        // Another state lists none of them.
        await page.State(nameof(ReminderState.Reminded)).ClickAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0, new() { Timeout = Render });
    }

    [Test]
    public async Task An_instance_waiting_on_an_ended_run_is_listed_under_stuck_only_with_that_run()
    {
        var (waiting, entry) = await Seed.InstanceWaitingOnAnEndedRunAsync();
        var page = new MachinePage(Console, RelayMachine.Id);

        await page.OpenAsync(Caller.Operator);
        // The box follows the URL, so it is checked once the filter lands there.
        await page.StuckOnly.ClickAsync();
        await Assertions.Expect(page.StuckOnly).ToBeCheckedAsync(new() { Timeout = Render });
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new System.Text.RegularExpressions.Regex("[?&]stuck=true"));

        var row = page.Instance(waiting.Id);
        await Assertions.Expect(row).ToContainTextAsync("Stuck", new() { Timeout = Render });
        await Assertions.Expect(row).ToContainTextAsync("Cancelled", new() { Timeout = Render });
        await Assertions
            .Expect(
                row.GetByRole(
                    AriaRole.Link,
                    new() { Name = $"Queue entry {entry.Id}", Exact = true }
                )
            )
            .ToHaveAttributeAsync(
                "href",
                new System.Text.RegularExpressions.Regex($"/runs/queued/{entry.Id}$")
            );

        // Its state is drawn with the train it runs, linked to the train's page.
        var relaying = nameof(RelayState.Relaying);
        await Assertions
            .Expect(page.Badge(relaying, "Stuck"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.List, new() { Name = "What its states start" })
                    .GetByRole(
                        AriaRole.Link,
                        new() { Name = nameof(IRelayParcelTrain), Exact = true }
                    )
            )
            .ToHaveAttributeAsync(
                "href",
                new System.Text.RegularExpressions.Regex(
                    $"/trains/{typeof(IRelayParcelTrain).FullName}$"
                )
            );
    }
}
