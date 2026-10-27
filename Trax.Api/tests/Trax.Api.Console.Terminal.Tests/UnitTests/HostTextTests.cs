using System.Text.Json;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Machines;
using Trax.Api.Console.Terminal.Operations;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// Text the host sends is drawn as text: an escape sequence in a row, the live feed, a trace or a machine's graph
/// reaches the screen with its control characters replaced, so the terminal never acts on it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class HostTextTests
{
    // An OSC 52 clipboard write and a screen clear, as JSON writes them.
    private const string Hostile = @"\u001b]52;c;ZXZpbA==\u0007";
    private const string Clear = @"\u001b[2J";

    [Test]
    public async Task An_escape_sequence_in_a_row_or_the_feed_never_reaches_the_screen()
    {
        var host = HostData
            .Console()
            .Answer(
                "Executions",
                HostData.Runs(1, null, HostData.Run(4812, $"Send{Hostile}Invoice", reason: Clear))
            );
        var (terminal, window) = await TerminalHarness.StartAsync(app => new MainWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql"),
                new ScriptedPrompts()
            ),
            state: null,
            banner: "ready"
        ));
        await using var _ = terminal;
        await terminal.UntilShown("]52;c;");
        await terminal.Until(() => host.Follows("OnTrainStateChanged"), "the feed is followed");

        host.Push(
            "OnTrainStateChanged",
            Host(
                """{"onTrainStateChanged":{"metadataId":2,"externalId":"b","trainName":"ShipHOSTILEOrder","trainState":"FAILED","timestamp":"2026-10-22T10:05:01Z","failureJunction":"Pack","failureReason":"CLEAR"}}"""
            )
        );
        await terminal.Until(() => window.Feed.Count == 1, "the feed shows the change");

        var screen = await terminal.Screen();
        screen.Should().Contain("ShipOrder".Insert(4, "�]52;c;ZXZpbA==�"));
        Controls(screen).Should().BeEmpty();
        Controls(window.Feed.Single()).Should().BeEmpty();
    }

    [Test]
    public async Task An_escape_sequence_in_a_runs_trace_never_reaches_the_screen()
    {
        var host = new FakeHost()
            .Answer("JunctionRuns", """{"operations":{"junctionRuns":[]}}""")
            .Answer("Decisions", """{"operations":{"decisions":{"take":500,"items":[]}}}""")
            .Answer(
                "Logs",
                Host(
                    """
                    {"operations":{"logs":{"logs":{"items":[
                      {"id":90,"metadataId":4812,"eventId":0,"level":"WARNING","category":"Billing","message":"declinedHOSTILE\nsecond line"}
                    ],"totalCount":1,"isEstimatedCount":false,"isCountCapped":false}}}}
                    """
                )
            );
        var (terminal, window) = await TerminalHarness.StartAsync(app => new RunTraceWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql")
            ),
            4812
        ));
        await using var _ = terminal;

        await terminal.UntilShown("]52;c;");

        Controls(await terminal.Screen()).Should().BeEmpty();
    }

    [Test]
    public void An_escape_sequence_in_a_machines_names_never_reaches_its_graph()
    {
        var machine = JsonSerializer.Deserialize<MachinesResultOperationsMachines>(
            $$"""
            {"name":"m","version":1,"systemOwned":false,"initialState":"Open",
             "states":[{"name":"Open","isInitial":true,"isFinal":false,"isCommitted":false},
                       {"name":"Shut{{Hostile}}","isInitial":false,"isFinal":true,"isCommitted":false,"invokesTrain":"T{{Clear}}"}],
             "transitions":[{"from":"Open","command":"Close{{Clear}}","to":"Shut{{Hostile}}","kind":"COMMAND"}],
             "timers":[]}
            """,
            TraxJson.Options
        )!;

        var lines = MachineGraph.Lines(machine, current: $"Shut\u001b]52;c;ZXZpbA==\u0007");
        TestContext.Out.WriteLine(string.Join("\n", lines));

        lines.SelectMany(Controls).Should().BeEmpty();
        string.Join("\n", lines)
            .Should()
            .Contain("║ Shut�]52;c;ZXZpbA==�", "the current state is still found");
    }

    private static string Host(string json) =>
        json.Replace("HOSTILE", Hostile).Replace("CLEAR", Clear);

    private static IEnumerable<char> Controls(string text) =>
        text.Where(c => char.IsControl(c) && c != '\n');
}
