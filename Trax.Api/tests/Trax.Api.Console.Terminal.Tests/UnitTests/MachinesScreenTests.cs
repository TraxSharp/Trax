using System.Text.Json.Nodes;
using AwesomeAssertions;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>The machines tab while answers arrive out of order: each machine shows only its own instances.</summary>
[TestFixture]
[NonParallelizable]
public class MachinesScreenTests
{
    [Test]
    public async Task An_answer_for_the_machine_selected_before_is_dropped()
    {
        var alpha = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new FakeHost()
            .Answer(
                "Machines",
                """{"operations":{"machines":[""" + Machine("alpha") + "," + Machine("beta") + "]}}"
            )
            .AnswerLater(
                "MachineInstancesAttention",
                v =>
                    v["machine"]!.GetValue<string>() == "alpha"
                        ? alpha.Task
                        : Task.FromResult(Instances("beta", "bbbbbbbb-0000-4000-8000-000000000002"))
            );
        MachinesScreen screen = null!;
        var (terminal, _) = await TerminalHarness.StartAsync(app =>
        {
            screen = new MachinesScreen(
                new ConsoleContext(
                    app,
                    new TraxClient(host),
                    new Uri("https://ops.example.com/trax/graphql")
                )
            );
            var window = new Window();
            window.Add(screen);
            return window;
        });
        await using var _ = terminal;

        await terminal.OnUi(screen.Refresh);
        await terminal.Until(
            () => host.Sent("MachineInstancesAttention").Count >= 1,
            "alpha's instances are asked for"
        );
        await terminal.OnUi(() => screen.SetFocus());
        await terminal.Press(Key.J);
        await terminal.Until(
            () => screen.Instances.Count == 1 && screen.Selected?.Name == "beta",
            "beta's instances show"
        );

        alpha.SetResult(Instances("alpha", "aaaaaaaa-0000-4000-8000-000000000001"));
        await terminal.OnUi(() => { });
        await terminal.OnUi(() => { });

        (await terminal.OnUi(() => screen.Instances.Select(i => i.Machine).ToList()))
            .Should()
            .Equal("beta");
        (await terminal.Screen()).Should().NotContain("aaaaaaaa");
    }

    private static string Machine(string name) =>
        $$"""
            {"name":"{{name}}","version":1,"systemOwned":false,"initialState":"Open",
             "states":[{"name":"Open","isInitial":true,"isFinal":false,"isCommitted":false},
                       {"name":"Closed","isInitial":false,"isFinal":true,"isCommitted":false}],
             "transitions":[{"from":"Open","command":"Close","to":"Closed","kind":"COMMAND"}],
             "timers":[]}
            """;

    private static JsonObject Instances(string machine, string id) =>
        JsonNode
            .Parse(
                """
                {"operations":{"machineInstances":{"totalCount":1,"isCountCapped":false,"items":[
                  {"machine":"MACHINE","ownerKind":"USER","id":"ID","rowId":1,"state":"Open",
                   "updatedAt":"2026-10-22T10:01:00Z","inStateSince":"2026-10-22T10:01:00Z","overdue":false,"stuck":false}]}}}
                """.Replace("MACHINE", machine).Replace("ID", id)
            )!
            .AsObject();
}
