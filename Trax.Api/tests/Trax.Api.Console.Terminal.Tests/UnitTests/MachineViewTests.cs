using System.Text.Json;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Machines;
using Trax.Api.Console.Terminal.Operations;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// A machine read only: its graph as text with the current state marked, and an instance's history, read again
/// when the host says it changed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MachineViewTests
{
    private const string Checkout = """
        {"name":"checkout","version":2,"systemOwned":false,"initialState":"Cart",
         "states":[
           {"name":"Cart","isInitial":true,"isFinal":false,"isCommitted":false},
           {"name":"Paying","isInitial":false,"isFinal":false,"isCommitted":true,"invokesTrain":"Shop.IChargeTrain"},
           {"name":"Shipped","isInitial":false,"isFinal":true,"isCommitted":false},
           {"name":"Abandoned","isInitial":false,"isFinal":true,"isCommitted":false}],
         "transitions":[
           {"from":"Cart","command":"Checkout","to":"Paying","kind":"COMMAND"},
           {"from":"Paying","command":"Paid","to":"Shipped","kind":"OUTCOME"},
           {"from":"Paying","command":"Expire","to":"Abandoned","kind":"TIMER","after":"15m"}],
         "timers":[]}
        """;

    [Test]
    public void The_graph_draws_each_state_as_a_box_and_each_transition_as_a_labelled_arrow()
    {
        var lines = MachineGraph.Lines(Machine(), current: "Paying");
        var text = string.Join("\n", lines);
        TestContext.Out.WriteLine(text);

        text.Should().Contain("●─▶│ Cart", "the initial state is entered by the arrow");
        text.Should().Contain("║ Paying ", "the current state has a double border");
        text.Should().Contain("╭─").And.Contain("│ Shipped │", "a final state has rounded corners");
        text.Should().Contain("Checkout").And.Contain("on Paid").And.Contain("after 15m");
        lines
            .Sum(l => l.Count(c => c is '▶' or '◀' or '▲' or '▼'))
            .Should()
            .Be(4, "the entry and three transitions");
        lines.Should().Contain("Paying: committed, invokes Shop.IChargeTrain");
    }

    [Test]
    public void A_transition_back_and_one_to_the_same_state_are_drawn_too()
    {
        var lines = GraphLayout.Render(
            [
                new GraphState("Idle", Initial: true, Final: false),
                new GraphState("Busy", Initial: false, Final: false),
                new GraphState("Done", Initial: false, Final: true),
            ],
            [
                new GraphEdge("Idle", "Busy", "Start"),
                new GraphEdge("Busy", "Idle", "Pause"),
                new GraphEdge("Busy", "Busy", "Tick"),
                new GraphEdge("Busy", "Done", "Finish"),
            ]
        );
        var text = string.Join("\n", lines);
        TestContext.Out.WriteLine(text);

        text.Should().Contain("↻ Tick", "a transition to the same state is written in its box");
        text.Should().Contain("Pause").And.Contain("Start").And.Contain("Finish");
        lines.Sum(l => l.Count(c => c is '▶' or '◀' or '▲' or '▼')).Should().Be(4);
    }

    [Test]
    public void Drawn_down_the_states_go_in_rows_and_the_lines_enter_from_above()
    {
        var lines = MachineGraph.Lines(Machine(), current: "Paying", vertical: true).ToList();
        var text = string.Join("\n", lines);
        TestContext.Out.WriteLine(text);

        lines[0].Trim().Should().Be("●", "the initial state is entered from above");
        lines[1].Trim().Should().Be("▼");
        text.Should().Contain("Checkout").And.Contain("on Paid").And.Contain("after 15m");
        lines.Sum(l => l.Count(c => c is '▶' or '◀' or '▲' or '▼')).Should().Be(4);
        var cart = lines.FindIndex(l => l.Contains("Cart"));
        var paying = lines.FindIndex(l => l.Contains("Paying"));
        var shipped = lines.FindIndex(l => l.Contains("Shipped"));
        cart.Should().BeLessThan(paying, "each state sits below the one before it");
        paying.Should().BeLessThan(shipped);
    }

    [Test]
    public void Drawn_down_a_transition_back_is_routed_around()
    {
        var lines = GraphLayout.Render(
            [
                new GraphState("Idle", Initial: true, Final: false),
                new GraphState("Busy", Initial: false, Final: false),
                new GraphState("Done", Initial: false, Final: true),
            ],
            [
                new GraphEdge("Idle", "Busy", "Start"),
                new GraphEdge("Busy", "Idle", "Pause"),
                new GraphEdge("Busy", "Busy", "Tick"),
                new GraphEdge("Busy", "Done", "Finish"),
            ],
            vertical: true
        );
        TestContext.Out.WriteLine(string.Join("\n", lines));

        lines.Should().NotContain(l => l.Contains("──▶"), "every label found room on the graph");
        string.Join("\n", lines)
            .Should()
            .Contain("Pause")
            .And.Contain("Start")
            .And.Contain("Finish");
        lines.Sum(l => l.Count(c => c is '▶' or '◀' or '▲' or '▼')).Should().Be(4);
    }

    [Test]
    public void Drawn_down_lines_leaving_one_box_fan_out_so_each_label_has_room()
    {
        var lines = GraphLayout.Render(
            [
                new GraphState("Building", Initial: true, Final: false),
                new GraphState("BuildCancelled", Initial: false, Final: true),
                new GraphState("BuildFailed", Initial: false, Final: true),
                new GraphState("Built", Initial: false, Final: true),
            ],
            [
                new GraphEdge("Building", "BuildCancelled", "on cancelled"),
                new GraphEdge("Building", "BuildFailed", "on failed"),
                new GraphEdge("Building", "Built", "on done"),
            ],
            vertical: true
        );
        TestContext.Out.WriteLine(string.Join("\n", lines));

        lines.Should().NotContain(l => l.Contains("──▶"), "every label found room on the graph");
        string.Join("\n", lines)
            .Should()
            .Contain("on cancelled")
            .And.Contain("on failed")
            .And.Contain("on done");
    }

    [Test]
    public async Task An_instance_shows_where_it_is_and_how_it_got_there_and_follows_its_changes()
    {
        var state = "Paying";
        var host = new FakeHost()
            .Answer(
                "MachineInstance",
                _ =>
                    (System.Text.Json.Nodes.JsonObject)
                        System.Text.Json.Nodes.JsonNode.Parse(
                            """
                            {"operations":{"machineInstance":{"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001",
                             "rowId":5,"state":"STATE","version":2,"updatedAt":"2026-10-22T10:01:00Z","hasLiveInvokedRun":true,
                             "isInvokedRunsCapped":false,"inStateSince":"2026-10-22T10:01:00Z","overdue":false,"stuck":false,
                             "waitingOn":{"executionId":4900,"trainState":"IN_PROGRESS","workQueueEntryId":null},
                             "invokedRuns":[{"id":4900,"externalId":"run-4900","name":"Shop.IChargeTrain","trainState":"IN_PROGRESS","startTime":"2026-10-22T10:01:00Z","failureClass":"UNCLASSIFIED","cancellationRequested":false,"isLive":true}]}}}
                            """.Replace("STATE", state)
                        )!
            )
            .Answer(
                "MachineInstanceTransitions",
                """
                {"operations":{"machineInstance":{"rowId":5,"transitions":{"items":[
                  {"revision":2,"kind":"COMMAND","trigger":"Checkout","actor":"OWNER","fromState":"Cart","toState":"Paying","at":"2026-10-22T10:01:00Z","startedRun":{"executionId":4900}}]}}}}
                """
            );
        var instance =
            JsonSerializer.Deserialize<MachineInstancesAttentionResultOperationsMachineInstancesItems>(
                """{"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001","rowId":5,"state":"Paying","updatedAt":"2026-10-22T10:01:00Z","inStateSince":"2026-10-22T10:01:00Z","overdue":false,"stuck":false}""",
                TraxJson.Options
            )!;
        var (terminal, window) = await TerminalHarness.StartAsync(app => new MachineInstanceWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql")
            ),
            Machine(),
            instance
        ));
        await using var _ = terminal;

        await terminal.Until(
            () => window.Lines.Any(l => l.Contains("║ Paying ")),
            "the instance is read"
        );
        window.Lines.Should().Contain("Waits on run 4900 (IN_PROGRESS)");
        window
            .Lines.Should()
            .Contain(l =>
                l.Contains("Cart ──▶ Paying  command Checkout by owner  started run 4900")
            );

        state = "Shipped";
        host.Push(
            "OnMachineInstanceChanged",
            """{"onMachineInstanceChanged":{"rowId":5,"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001","state":"Shipped","updatedAt":"2026-10-22T10:02:00Z","hasLiveInvokedRun":false,"deleted":false,"reload":false}}"""
        );
        await terminal.OnUi(() => { });
        await terminal.OnUi(() => terminal.Time.Advance(TimeSpan.FromSeconds(1)));

        await terminal.Until(
            () => window.Lines.Any(l => l.Contains("║ Shipped ")),
            "the change is read"
        );
    }

    [Test]
    public async Task A_burst_of_changes_is_read_once_and_an_overtaken_answer_is_dropped()
    {
        MainWindow.RefreshDelay = TimeSpan.FromMilliseconds(300);
        var held = new TaskCompletionSource<System.Text.Json.Nodes.JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reads = 0;
        var host = new FakeHost()
            .AnswerLater(
                "MachineInstance",
                _ =>
                    Interlocked.Increment(ref reads) switch
                    {
                        1 => Task.FromResult(Detail("Paying")),
                        2 => held.Task,
                        _ => Task.FromResult(Detail("Shipped")),
                    }
            )
            .Answer("MachineInstanceTransitions", Transitions);
        var (terminal, window) = await TerminalHarness.StartAsync(app => new MachineInstanceWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql")
            ),
            Machine(),
            Instance()
        ));
        await using var _ = terminal;
        await terminal.Until(
            () =>
                window.Lines.Any(l => l.Contains("║ Paying "))
                && host.Follows("OnMachineInstanceChanged"),
            "the instance is read and followed"
        );

        for (var i = 0; i < 5; i++)
            host.Push("OnMachineInstanceChanged", Changed);
        await terminal.OnUi(() => { });
        await terminal.OnUi(() => terminal.Time.Advance(TimeSpan.FromSeconds(1)));
        await terminal.Until(
            () => host.Sent("MachineInstance").Count == 2,
            "the burst is read once"
        );
        host.Push("OnMachineInstanceChanged", Changed);
        await terminal.OnUi(() => { });
        await terminal.OnUi(() => terminal.Time.Advance(TimeSpan.FromSeconds(1)));
        await terminal.Until(
            () => window.Lines.Any(l => l.Contains("║ Shipped ")),
            "the newer read is shown"
        );

        held.SetResult(Detail("Cart"));
        await terminal.Until(
            () => host.Sent("MachineInstanceTransitions").Count == 3,
            "the overtaken read finishes"
        );
        // negative-wait: a fourth read, if one were sent, would be sent within this window.
        await Task.Delay(200);
        await terminal.OnUi(() => { });

        host.Sent("MachineInstance").Should().HaveCount(3);
        window.Lines.Should().Contain(l => l.Contains("║ Shipped "));
        window
            .Lines.Should()
            .NotContain(l => l.Contains("║ Cart "), "the older answer was dropped");
    }

    [Test]
    public void A_machine_drawn_again_the_same_way_is_not_laid_out_again()
    {
        var machine = Machine();

        MachineGraph
            .Lines(machine, "Paying", vertical: true)
            .Should()
            .BeSameAs(MachineGraph.Lines(machine, "Paying", vertical: true));
        MachineGraph
            .Lines(machine, "Paying")
            .Should()
            .NotBeSameAs(MachineGraph.Lines(machine, "Paying", vertical: true));
    }

    private const string Changed =
        """{"onMachineInstanceChanged":{"rowId":5,"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001","state":"Shipped","updatedAt":"2026-10-22T10:02:00Z","hasLiveInvokedRun":false,"deleted":false,"reload":false}}""";

    [Test]
    public async Task Closing_the_instance_window_cancels_a_read_still_out()
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new FakeHost()
            .HoldUntilCancelled("MachineInstance", cancelled)
            .Answer("MachineInstanceTransitions", Transitions);
        var (terminal, window) = await TerminalHarness.StartAsync(app => new MachineInstanceWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql")
            ),
            Machine(),
            Instance()
        ));
        await using var _ = terminal;
        await terminal.Until(() => host.Sent("MachineInstance").Count == 1, "the instance is read");

        await terminal.OnUi(() => window.RequestStop());

        await terminal.Stopped.WaitAsync(TimeSpan.FromSeconds(15));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
        host.Sent("MachineInstanceTransitions").Should().BeEmpty();
    }

    private const string Transitions = """
        {"operations":{"machineInstance":{"rowId":5,"transitions":{"items":[]}}}}
        """;

    private static System.Text.Json.Nodes.JsonObject Detail(string state) =>
        (System.Text.Json.Nodes.JsonObject)
            System.Text.Json.Nodes.JsonNode.Parse(
                """
                {"operations":{"machineInstance":{"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001",
                 "rowId":5,"state":"STATE","version":2,"updatedAt":"2026-10-22T10:01:00Z","hasLiveInvokedRun":false,
                 "isInvokedRunsCapped":false,"inStateSince":"2026-10-22T10:01:00Z","overdue":false,"stuck":false,
                 "waitingOn":null,"invokedRuns":[]}}}
                """.Replace("STATE", state)
            )!;

    private static MachineInstancesAttentionResultOperationsMachineInstancesItems Instance() =>
        JsonSerializer.Deserialize<MachineInstancesAttentionResultOperationsMachineInstancesItems>(
            """{"machine":"checkout","ownerKind":"USER","id":"3f2a1c4e-0000-4000-8000-000000000001","rowId":5,"state":"Paying","updatedAt":"2026-10-22T10:01:00Z","inStateSince":"2026-10-22T10:01:00Z","overdue":false,"stuck":false}""",
            TraxJson.Options
        )!;

    private static MachinesResultOperationsMachines Machine() =>
        JsonSerializer.Deserialize<MachinesResultOperationsMachines>(Checkout, TraxJson.Options)!;
}
