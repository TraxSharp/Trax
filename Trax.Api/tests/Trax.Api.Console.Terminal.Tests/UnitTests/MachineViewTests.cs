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
    public void The_graph_marks_each_state_and_what_takes_it_where()
    {
        var lines = MachineGraph.Lines(Machine(), current: "Paying");

        lines
            .Should()
            .Equal(
                "● Cart  (initial)",
                "    Checkout ──▶ Paying  (command)",
                "▶ [Paying]  (committed, invokes Shop.IChargeTrain, current)",
                "    Paid ──▶ Shipped  (outcome)",
                "    after 15m ──▶ Abandoned  (timer)",
                "■ Shipped  (final)",
                "■ Abandoned  (final)"
            );
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
            () => window.Lines.Any(l => l.Contains("[Paying]")),
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

        await terminal.Until(
            () => window.Lines.Any(l => l.Contains("[Shipped]")),
            "the change is read"
        );
    }

    private static MachinesResultOperationsMachines Machine() =>
        JsonSerializer.Deserialize<MachinesResultOperationsMachines>(Checkout, TraxJson.Options)!;
}
