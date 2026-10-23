using System.Text.Json.Nodes;
using AwesomeAssertions;
using Terminal.Gui.Input;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// A list while the host changes under it: an action runs on the row the operator chose, whatever a refresh moved,
/// every prompt names the rows by id, an answer overtaken by a newer read is dropped, and all matching runs only
/// with the filter the count it shows was read with.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ListScreenTests
{
    private FakeHost _host = null!;
    private ScriptedPrompts _prompts = null!;
    private TerminalHarness _terminal = null!;
    private MainWindow _window = null!;

    [SetUp]
    public async Task Open()
    {
        MainWindow.RefreshDelay = TimeSpan.FromMilliseconds(300);
        _host = HostData
            .Console()
            .Answer(
                "CancelExecutions",
                """{"operations":{"cancelExecutions":{"success":true,"count":1,"message":"Asked."}}}"""
            )
            .Answer("Manifests", Manifests(Manifest(9, "NightlyBilling")));
        _prompts = new ScriptedPrompts();
        (_terminal, _window) = await TerminalHarness.StartAsync(app => new MainWindow(
            new ConsoleContext(
                app,
                new TraxClient(_host),
                new Uri("https://ops.example.com/trax/graphql"),
                _prompts
            ),
            state: null,
            banner: "ready"
        ));
        await _terminal.UntilShown("SyncCustomers");
        await _terminal.OnUi(() => _window.Lists[0].SetFocus());
    }

    [TearDown]
    public async Task Close() => await _terminal.DisposeAsync();

    [Test]
    public async Task A_refresh_that_puts_a_row_above_keeps_the_cursor_on_the_same_row()
    {
        await _terminal.Press(Key.J);
        _host.Answer(
            "Executions",
            HostData.Runs(
                3,
                null,
                HostData.Run(4900, "ShipOrder"),
                HostData.Run(4812, "SendInvoice"),
                HostData.Run(4807, "SyncCustomers", "COMPLETED", null)
            )
        );
        await _terminal.OnUi(() => _window.Lists[0].Refresh());
        await _terminal.Until(() => _window.Lists[0].Rows.Count == 3, "the new row is read");

        _prompts.Choices.Enqueue(0);
        await _terminal.Press(Key.C);
        await _terminal.Until(
            () => _host.Sent("CancelExecutions").Count == 1,
            "the cancel is sent"
        );

        _host.Sent("CancelExecutions")[0]["ids"]!
            .ToJsonString()
            .Should()
            .Be("[4807]", "the cursor followed the row it was on, not its place");
        _prompts.Asked.Should().ContainSingle(a => a.StartsWith("Cancel: Cancel runs 4807?"));
    }

    [Test]
    public async Task A_prompt_names_every_marked_row()
    {
        await _terminal.Press(Key.Space);
        await _terminal.Press(Key.Space);
        _prompts.Choices.Enqueue(null);

        await _terminal.Press(Key.C);
        await _terminal.OnUi(() => { });

        _prompts
            .Asked.Should()
            .ContainSingle(a => a.StartsWith("Cancel: Cancel 2 runs (4807, 4812)?"));
        _host.Sent("CancelExecutions").Should().BeEmpty();
    }

    [Test]
    public async Task The_note_form_names_the_row_it_acknowledges()
    {
        await _terminal.Press(Key.L);
        await _terminal.UntilShown("NightlyBilling");

        await _terminal.Press(Key.A);
        await _terminal.OnUi(() => { });

        _prompts.Asked.Should().ContainSingle(a => a == "Acknowledge dead letters 7: Note");
        _host.Sent("AcknowledgeDeadLetters").Should().BeEmpty("the form was cancelled");
    }

    [Test]
    public async Task Enabling_a_manifest_is_confirmed_first()
    {
        await _terminal.Press(Key.D4);
        await _terminal.UntilShown("NightlyBilling");

        _prompts.Choices.Enqueue(null);
        await _terminal.Press(Key.E);
        await _terminal.OnUi(() => { });

        _prompts.Asked.Should().ContainSingle(a => a.StartsWith("Enable: Enable manifests 9?"));
        _host.Sent("SetManifestsEnabled").Should().BeEmpty();
    }

    private static JsonObject Parse(string data) => JsonNode.Parse(data)!.AsObject();

    private static string Manifest(long id, string name) =>
        $$"""
            {"id":{{id}},"externalId":"m-{{id}}","name":"{{name}}","isEnabled":false,"scheduleType":"CRON",
             "cronExpression":"0 2 * * *","maxRetries":3,"lastSuccessfulRun":null,"manifestGroupId":1,"priority":0,
             "replayDecisionsOnRetry":true}
            """;

    private static string Manifests(params string[] rows) =>
        """{"operations":{"manifests":{"items":["""
        + string.Join(",", rows)
        + $"],\"totalCount\":{rows.Length},\"isEstimatedCount\":false,\"nextCursor\":null}}}}}}";
}
