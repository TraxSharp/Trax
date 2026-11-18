using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Terminal.Gui.Input;
using Trax.Api.Console.Terminal.Checks;
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
    private FakeHost? _host;
    private ScriptedPrompts? _prompts;
    private TerminalHarness? _terminal;
    private MainWindow? _window;
    private ConsoleContext? _context;

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
            _context = new ConsoleContext(
                app,
                new TraxClient(_host.NotNull()),
                new Uri("https://ops.example.com/trax/graphql"),
                _prompts.NotNull()
            ),
            state: null,
            banner: "ready"
        ));
        await _terminal.NotNull().UntilShown("SyncCustomers");
        await _terminal.NotNull().OnUi(() => _window.NotNull().Lists[0].SetFocus());
    }

    [TearDown]
    public async Task Close()
    {
        if (_terminal is not null)
            await _terminal.DisposeAsync();
    }

    [Test]
    public async Task A_refresh_that_puts_a_row_above_keeps_the_cursor_on_the_same_row()
    {
        await _terminal.NotNull().Press(Key.J);
        _host
            .NotNull()
            .Answer(
                "Executions",
                HostData.Runs(
                    3,
                    next: null,
                    HostData.Run(4900, "ShipOrder"),
                    HostData.Run(4812, "SendInvoice"),
                    HostData.Run(4807, "SyncCustomers", "COMPLETED", reason: null)
                )
            );
        await _terminal.NotNull().OnUi(() => _window.NotNull().Lists[0].Refresh());
        await _terminal
            .NotNull()
            .Until(() => _window.NotNull().Lists[0].Rows.Count == 3, "the new row is read");

        _prompts.NotNull().Choices.Enqueue(0);
        await _terminal.NotNull().Press(Key.C);
        await _terminal
            .NotNull()
            .Until(() => _host.NotNull().Sent("CancelExecutions").Count == 1, "the cancel is sent");

        _host
            .NotNull()
            .Sent("CancelExecutions")[0]["ids"]
            .NotNull()
            .ToJsonString()
            .Should()
            .Be("[4807]", "the cursor followed the row it was on, not its place");
        _prompts
            .NotNull()
            .Asked.Should()
            .ContainSingle(a => a.StartsWith("Cancel: Cancel runs 4807?"));
    }

    [Test]
    public async Task A_prompt_names_every_marked_row()
    {
        await _terminal.NotNull().Press(Key.Space);
        await _terminal.NotNull().Press(Key.Space);
        _prompts.NotNull().Choices.Enqueue(item: null);

        await _terminal.NotNull().Press(Key.C);
        await _terminal.NotNull().OnUi(() => { });

        _prompts
            .NotNull()
            .Asked.Should()
            .ContainSingle(a => a.StartsWith("Cancel: Cancel 2 runs (4807, 4812)?"));
        _host.NotNull().Sent("CancelExecutions").Should().BeEmpty();
    }

    [Test]
    public async Task The_note_form_names_the_row_it_acknowledges()
    {
        await _terminal.NotNull().Press(Key.L);
        await _terminal.NotNull().UntilShown("NightlyBilling");

        await _terminal.NotNull().Press(Key.A);
        await _terminal.NotNull().OnUi(() => { });

        _prompts
            .NotNull()
            .Asked.Should()
            .ContainSingle(a => a == "Acknowledge dead letters 7: Note");
        _host.NotNull().Sent("AcknowledgeDeadLetters").Should().BeEmpty("the form was cancelled");
    }

    [Test]
    public async Task Enabling_a_manifest_is_confirmed_first()
    {
        await _terminal.NotNull().Press(Key.D4);
        await _terminal.NotNull().UntilShown("NightlyBilling");

        _prompts.NotNull().Choices.Enqueue(item: null);
        await _terminal.NotNull().Press(Key.E);
        await _terminal.NotNull().OnUi(() => { });

        _prompts
            .NotNull()
            .Asked.Should()
            .ContainSingle(a => a.StartsWith("Enable: Enable manifests 9?"));
        _host.NotNull().Sent("SetManifestsEnabled").Should().BeEmpty();
    }

    [Test]
    public async Task An_answer_overtaken_by_a_newer_read_is_dropped()
    {
        var held = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reads = 0;
        _host
            .NotNull()
            .AnswerLater(
                "Executions",
                _ =>
                    Interlocked.Increment(ref reads) == 1
                        ? held.Task
                        : Task.FromResult(
                            Parse(HostData.Runs(1, next: null, HostData.Run(4799, "Archive")))
                        )
            );

        await _terminal.NotNull().OnUi(() => _window.NotNull().Lists[0].Refresh());
        await _terminal.NotNull().OnUi(() => _window.NotNull().Lists[0].Refresh());
        await _terminal.NotNull().UntilShown("Archive");
        held.SetResult(Parse(HostData.Runs(1, next: null, HostData.Run(4700, "Stale"))));
        await _terminal
            .NotNull()
            .Until(() => _host.NotNull().Sent("Executions").Count >= 3, "both reads were sent");
        await _terminal.NotNull().OnUi(() => { });

        (
            await _terminal
                .NotNull()
                .OnUi(() => _window.NotNull().Lists[0].Rows.Select(r => r.Id).ToList())
        )
            .Should()
            .Equal(4799);
    }

    [Test]
    public async Task Closing_the_console_cancels_a_read_still_out_and_reports_nothing()
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _host.NotNull().HoldUntilCancelled("Executions", cancelled);
        var reads = _host.NotNull().Sent("Executions").Count;

        await _terminal.NotNull().OnUi(() => _window.NotNull().Lists[0].Refresh());
        await _terminal
            .NotNull()
            .Until(() => _host.NotNull().Sent("Executions").Count > reads, "the read is sent");
        _context.NotNull().Close();

        await cancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(15),
            _context.Clock,
            cancellationToken: CancellationToken.None
        );
        // negative-wait: a report of the cancelled read, were one made, would be drawn within this window.
        await Task.Delay(200, cancellationToken: CancellationToken.None);
        await _terminal.NotNull().OnUi(() => { });
        (await _terminal.NotNull().Screen()).Should().NotContain("canceled");
    }

    [Test]
    public async Task All_matching_waits_for_the_count_of_a_new_filter()
    {
        var held = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _host.NotNull().AnswerLater("Executions", _ => held.Task);
        _host
            .NotNull()
            .Answer(
                "StartBulkOperation",
                """{"operations":{"startBulkOperation":{"success":true,"message":"Started.","matched":1,"operation":null}}}"""
            );
        _prompts.NotNull().Forms.Enqueue(["failed", "", "", "", "", "", ""]);
        await _terminal.NotNull().Press(Key.F);
        await _terminal
            .NotNull()
            .Until(
                () => _host.NotNull().Sent("Executions").Count >= 2,
                "the runs are read with the filter"
            );

        _prompts.NotNull().Choices.Enqueue(0);
        await _terminal.NotNull().Press(Key.B);
        await _terminal.NotNull().OnUi(() => { });

        _host
            .NotNull()
            .Sent("StartBulkOperation")
            .Should()
            .BeEmpty("the count shown is the one read without the filter");
        _prompts
            .NotNull()
            .Asked.Should()
            .Contain(a => a.StartsWith("All matching: The list is being read"));

        held.SetResult(Parse(HostData.Runs(1, next: null, HostData.Run(4812, "SendInvoice"))));
        await _terminal.NotNull().UntilShown("1 in all");
        await _terminal.NotNull().Press(Key.B);
        await _terminal
            .NotNull()
            .Until(
                () => _host.NotNull().Sent("StartBulkOperation").Count == 1,
                "the bulk operation starts"
            );
        var sent = _host.NotNull().Sent("StartBulkOperation")[0];
        sent["expectedCount"].NotNull().GetValue<int>().Should().Be(1);
        sent["filter"]
            .NotNull()["executions"]
            .NotNull()["trainState"]
            .NotNull()
            .GetValue<string>()
            .Should()
            .Be("FAILED");
    }

    private static JsonObject Parse(string data) => JsonNode.Parse(data).NotNull().AsObject();

    private static string Manifest(long id, string name) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {"id":{{id}},"externalId":"m-{{id}}","name":"{{name}}","isEnabled":false,"scheduleType":"CRON",
             "cronExpression":"0 2 * * *","maxRetries":3,"lastSuccessfulRun":null,"manifestGroupId":1,"priority":0,
             "replayDecisionsOnRetry":true}
            """
        );

    private static string Manifests(params string[] rows) =>
        """{"operations":{"manifests":{"items":["""
        + string.Join(',', rows)
        + $"],\"totalCount\":{rows.Length},\"isEstimatedCount\":false,\"nextCursor\":null}}}}}}";
}
