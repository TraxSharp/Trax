using AwesomeAssertions;
using Terminal.Gui.Input;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// The console in a headless terminal over a scripted host: it shows each list, pages and filters with the browser
/// console's variables, acts on the marked rows, applies an action to all matching, and keeps the lists current from
/// the operator streams.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MainWindowTests
{
    private FakeHost _host = null!;
    private ScriptedPrompts _prompts = null!;
    private TerminalHarness _terminal = null!;
    private MainWindow _window = null!;

    [SetUp]
    public async Task Open()
    {
        MainWindow.RefreshDelay = TimeSpan.FromMilliseconds(300);
        _host = HostData.Console();
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
    }

    [TearDown]
    public async Task Close() => await _terminal.DisposeAsync();

    [Test]
    public async Task It_shows_the_runs_the_host_lists()
    {
        await _terminal.UntilShown("SendInvoice");

        var screen = await _terminal.Screen();
        screen.Should().Contain("trax-console  ·  https://ops.example.com");
        screen.Should().Contain("card_declined").And.Contain("2 in all");
    }

    [Test]
    public async Task The_next_page_starts_after_the_cursor_the_host_gave()
    {
        _host.Answer(
            "Executions",
            v =>
                System
                    .Text.Json.Nodes.JsonNode.Parse(
                        v["afterId"] is null
                            ? HostData.Runs(30, 4800, HostData.Run(4812, "SendInvoice"))
                            : HostData.Runs(
                                30,
                                null,
                                HostData.Run(4799, "Archive", "COMPLETED", null)
                            )
                    )!
                    .AsObject()
        );
        await _terminal.OnUi(() => _window.Lists[0].Refresh());
        await _terminal.UntilShown("SendInvoice");

        await _terminal.OnUi(() => _window.Lists[0].SetFocus());
        await _terminal.Press(Key.N);
        await _terminal.UntilShown("Archive");

        _host.Sent("Executions").Last()["afterId"]!.GetValue<long>().Should().Be(4800);
        (await _terminal.Screen()).Should().Contain("page 2");
    }

    [Test]
    public async Task A_filter_is_sent_as_the_documents_variable()
    {
        await _terminal.UntilShown("SendInvoice");
        _prompts.Forms.Enqueue(["failed", "SendInvoice", "", "", "", "", ""]);

        await _terminal.OnUi(() => _window.Lists[0].SetFocus());
        await _terminal.Press(Key.F);
        await _terminal.Until(
            () => _host.Sent("Executions").Count >= 2,
            "the runs are read again with the filter"
        );

        var sent = _host.Sent("Executions").Last();
        sent["trainState"]!.GetValue<string>().Should().Be("FAILED");
        sent["trainName"]!.GetValue<string>().Should().Be("SendInvoice");
        sent.ContainsKey("hostName").Should().BeFalse("a filter left empty is not sent");
    }

    [Test]
    public async Task An_action_runs_on_the_marked_rows_after_it_is_confirmed()
    {
        _host.Answer(
            "CancelExecutions",
            """{"operations":{"cancelExecutions":{"success":true,"count":1,"message":"Cancellation requested for 1 run."}}}"""
        );
        await _terminal.UntilShown("SyncCustomers");
        await _terminal.OnUi(() => _window.Lists[0].SetFocus());

        await _terminal.Press(Key.CursorDown);
        await _terminal.Press(Key.Space);
        _prompts.Choices.Enqueue(0);
        await _terminal.Press(Key.C);
        await _terminal.Until(
            () => _host.Sent("CancelExecutions").Count == 1,
            "the cancel is sent"
        );

        _host.Sent("CancelExecutions")[0]["ids"]!.ToJsonString().Should().Be("[4807]");
        _prompts.Asked.Should().ContainSingle(a => a.StartsWith("Cancel: Cancel runs 4807?"));
        await _terminal.UntilShown("Cancellation requested for 1 run.");
    }

    [Test]
    public async Task An_action_refused_at_the_prompt_sends_nothing()
    {
        await _terminal.UntilShown("SendInvoice");
        await _terminal.OnUi(() => _window.Lists[0].SetFocus());

        _prompts.Choices.Enqueue(null);
        await _terminal.Press(Key.C);
        await _terminal.OnUi(() => { });

        _host.Sent("CancelExecutions").Should().BeEmpty();
    }

    [Test]
    public async Task All_matching_confirms_the_count_the_list_shows()
    {
        _host.Answer(
            "StartBulkOperation",
            """{"operations":{"startBulkOperation":{"success":true,"message":"Started.","matched":2,"operation":null}}}"""
        );
        await _terminal.UntilShown("SendInvoice");
        await _terminal.OnUi(() => _window.Lists[0].SetFocus());

        _prompts.Choices.Enqueue(0);
        await _terminal.Press(Key.B);
        await _terminal.Until(
            () => _host.Sent("StartBulkOperation").Count == 1,
            "the bulk operation starts"
        );

        var sent = _host.Sent("StartBulkOperation")[0];
        sent["kind"]!.GetValue<string>().Should().Be("CANCEL");
        sent["expectedCount"]!.GetValue<int>().Should().Be(2);
        sent["filter"]!["executions"].Should().NotBeNull();
    }

    [Test]
    public async Task Requeue_asks_whether_to_ask_afresh_and_sends_the_answer()
    {
        _host.Answer(
            "RequeueDeadLetters",
            """{"operations":{"deadLetters":{"requeueDeadLetters":{"count":1,"message":"Requeued 1."}}}}"""
        );
        await _terminal.OnUi(() => _window.Lists[1].SetFocus());
        await _terminal.UntilShown("NightlyBilling");

        _prompts.Choices.Enqueue(1);
        await _terminal.Press(Key.R);
        await _terminal.Until(
            () => _host.Sent("RequeueDeadLetters").Count == 1,
            "the requeue is sent"
        );

        var sent = _host.Sent("RequeueDeadLetters")[0];
        sent["ids"]!.ToJsonString().Should().Be("[7]");
        sent["askAfresh"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task Enter_shows_the_rows_detail()
    {
        _host.Answer(
            "ExecutionDetail",
            """{"operations":{"executionDetail":{"id":4812,"externalId":"run-4812","name":"SendInvoice","trainState":"FAILED","startTime":"2026-10-22T10:00:00Z","failureReason":"card_declined","stackTrace":"at ChargeCard\n at Run","cancellationRequested":false,"childCount":0,"failureClass":"PERMANENT","replayAbandoned":false}}}"""
        );
        await _terminal.UntilShown("SendInvoice");
        await _terminal.OnUi(() => _window.Lists[0].SetFocus());

        await _terminal.Press(Key.Enter);
        await _terminal.Until(() => _prompts.Shown.Count == 1, "the detail opens");

        _prompts.Shown[0].Title.Should().Be("Runs 4812");
        _prompts
            .Shown[0]
            .Lines.Should()
            .Contain(l => l.StartsWith("failureClass") && l.EndsWith("PERMANENT"));
    }

    [Test]
    public async Task Vim_keys_move_through_rows_and_tabs()
    {
        _host.Answer(
            "CancelExecutions",
            """{"operations":{"cancelExecutions":{"success":true,"count":1,"message":"Asked."}}}"""
        );
        await _terminal.UntilShown("SyncCustomers");

        await _terminal.Press(Key.J);
        await _terminal.Press(Key.Space);
        _prompts.Choices.Enqueue(0);
        await _terminal.Press(Key.C);
        await _terminal.Until(
            () => _host.Sent("CancelExecutions").Count == 1,
            "the cancel is sent"
        );
        _host.Sent("CancelExecutions")[0]["ids"]!
            .ToJsonString()
            .Should()
            .Be("[4807]", "j moved to the second run");

        await _terminal.Press(Key.L);
        await _terminal.UntilShown("NightlyBilling");
        (await _terminal.OnUi(() => _window.Lists[1].HasFocus))
            .Should()
            .BeTrue("l moved to the next tab");
        await _terminal.Press(Key.D4);
        (await _terminal.OnUi(() => _window.Lists[3].HasFocus))
            .Should()
            .BeTrue("4 moved to the fourth tab");
    }

    [Test]
    public async Task The_schedulers_own_trains_are_hidden_from_the_runs_and_the_feed()
    {
        await _terminal.Until(() => _host.Follows("OnTrainStateChanged"), "the feed is followed");

        _host.Push(
            "OnTrainStateChanged",
            """{"onTrainStateChanged":{"metadataId":1,"externalId":"a","trainName":"Trax.Scheduler.Trains.JobRunner.IJobRunnerTrain","trainState":"COMPLETED","timestamp":"2026-10-22T10:05:00Z"}}"""
        );
        _host.Push(
            "OnTrainStateChanged",
            """{"onTrainStateChanged":{"metadataId":2,"externalId":"b","trainName":"ShipOrder","trainState":"COMPLETED","timestamp":"2026-10-22T10:05:01Z"}}"""
        );

        await _terminal.Until(
            () => _window.Feed.Count == 1,
            "the run that is not the scheduler's shows"
        );
        _window.Feed.Single().Should().Contain("ShipOrder");
        _host.Sent("Executions").Should().OnlyContain(v => v["hideAdminTrains"]!.GetValue<bool>());
    }

    [Test]
    public async Task A_run_that_changes_state_shows_in_the_live_feed()
    {
        await _terminal.Until(() => _host.Follows("OnTrainStateChanged"), "the feed is followed");

        _host.Push(
            "OnTrainStateChanged",
            """{"onTrainStateChanged":{"metadataId":4900,"externalId":"run-4900","trainName":"ShipOrder","trainState":"IN_PROGRESS","timestamp":"2026-10-22T10:05:00Z","failureJunction":null,"failureReason":null}}"""
        );

        await _terminal.Until(
            () => _window.Feed.Any(l => l.Contains("ShipOrder")),
            "the feed shows the change"
        );
        _window.Feed.Last().Should().Contain("IN_PROGRESS").And.Contain("4900");
    }

    [Test]
    public async Task A_change_the_host_announces_reads_the_list_it_touches_again()
    {
        await _terminal.UntilShown("SendInvoice");
        await _terminal.Until(() => _host.Follows("OnDataChanged"), "changes are followed");
        var before = _host.Sent("DeadLetters").Count;

        _host.Push(
            "OnDataChanged",
            """{"onDataChanged":{"domain":"DEAD_LETTER","timestamp":"2026-10-22T10:05:00Z"}}"""
        );
        await _terminal.OnUi(() => { });
        await _terminal.OnUi(() => _terminal.Time.Advance(TimeSpan.FromSeconds(1)));

        await _terminal.Until(
            () => _host.Sent("DeadLetters").Count > before,
            "the dead letters are read again"
        );
        _host
            .Sent("WorkQueue")
            .Should()
            .HaveCount(1, "a change to dead letters does not touch the work queue");
    }
}
