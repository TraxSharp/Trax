using System.Text.Json.Nodes;
using AwesomeAssertions;
using Terminal.Gui.Input;
using Trax.Api.Console.Terminal.Screens;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Api.Console.Terminal.Trace;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// A run's trace: its steps and decisions in time order, then its log, the steps followed live, and o on a step
/// naming the junction to open.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RunTraceWindowTests
{
    private const string Steps = """
        {"operations":{"junctionRuns":[
          {"position":1,"kind":"JUNCTION","name":"LoadInvoice","state":"COMPLETED","startedAt":"2026-10-22T10:00:00Z","endedAt":"2026-10-22T10:00:00.040Z","durationMs":40,"replayed":false,"answerWithheld":false,"nameWithheld":false},
          {"position":2,"kind":"JUNCTION","name":"ChargeCard","state":"IN_PROGRESS","startedAt":"2026-10-22T10:00:02Z","replayed":false,"answerWithheld":false,"nameWithheld":false}
        ]}}
        """;

    private const string Decisions = """
        {"operations":{"decisions":{"take":500,"items":[
          {"id":11,"metadataId":4812,"questionKey":"retry_card","occurrence":1,"answer":"yes","isRefused":false,"decider":"SystemOne","replayed":false,"decidedAt":"2026-10-22T10:00:01Z","answerWithheld":false,"trackWithheld":false}
        ]}}}
        """;

    private const string Logs = """
        {"operations":{"logs":{"logs":{"items":[
          {"id":90,"metadataId":4812,"eventId":0,"level":"WARNING","category":"Billing","message":"Stripe returned card_declined"}
        ],"totalCount":1,"isEstimatedCount":false,"isCountCapped":false}}}}
        """;

    [Test]
    public async Task Steps_and_decisions_read_in_time_order_and_the_log_after()
    {
        var host = new FakeHost()
            .Answer("JunctionRuns", Steps)
            .Answer("Decisions", Decisions)
            .Answer("Logs", Logs);
        var (terminal, window) = await Open(host);
        await using var _ = terminal;

        await terminal.Until(() => window.Entries.Count == 4, "the trace is read");

        window
            .Entries.Select(e => e.Kind)
            .Should()
            .Equal(TraceKind.Step, TraceKind.Decision, TraceKind.Step, TraceKind.Log);
        await terminal.UntilShown("decision retry_card  answer yes  by SystemOne");
        (await terminal.Screen()).Should().Contain("Stripe returned card_declined");
    }

    [Test]
    public async Task A_step_that_ends_replaces_its_line()
    {
        var host = new FakeHost()
            .Answer("JunctionRuns", Steps)
            .Answer("Decisions", Decisions)
            .Answer("Logs", Logs);
        var (terminal, window) = await Open(host);
        await using var _ = terminal;
        await terminal.Until(
            () => window.Entries.Count == 4 && host.Follows("OnJunctionEvent"),
            "the trace is read and followed"
        );

        host.Push(
            "OnJunctionEvent",
            """{"onJunctionEvent":{"metadataId":4812,"eventType":"JUNCTION_FAILED","timestamp":"2026-10-22T10:00:03Z","sequence":1,"junction":{"position":2,"kind":"JUNCTION","name":"ChargeCard","state":"FAILED","startedAt":"2026-10-22T10:00:02Z","durationMs":1200,"failureException":"card_declined","replayed":false,"answerWithheld":false,"nameWithheld":false}}}"""
        );

        await terminal.Until(
            () => window.Entries.Any(e => e.Key == "step:2" && e.Text.Contains("FAILED")),
            "the step's line shows it failed"
        );
        window.Entries.Count(e => e.Key == "step:2").Should().Be(1);
    }

    [Test]
    public async Task O_on_a_step_closes_the_trace_naming_its_junction()
    {
        var host = new FakeHost()
            .Answer("JunctionRuns", Steps)
            .Answer("Decisions", Decisions)
            .Answer("Logs", Logs);
        var (terminal, window) = await Open(host);
        await using var _ = terminal;
        await terminal.Until(() => window.Entries.Count == 4, "the trace is read");

        await terminal.Press(Key.CursorDown);
        await terminal.Press(Key.CursorDown);
        await terminal.Press(Key.O);

        await terminal.Stopped.WaitAsync(TimeSpan.FromSeconds(15));
        window
            .CodeToOpen.Should()
            .Be("ChargeCard", "o closes the trace naming the step's junction");
    }

    [Test]
    public async Task A_step_that_ends_before_the_first_read_answers_is_kept()
    {
        var held = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new FakeHost()
            .AnswerLater("JunctionRuns", _ => held.Task)
            .Answer("Decisions", Decisions)
            .Answer("Logs", Logs);
        var (terminal, window) = await Open(host);
        await using var _ = terminal;
        await terminal.Until(
            () => host.Follows("OnJunctionEvent") && host.Sent("JunctionRuns").Count == 1,
            "the steps are followed and read"
        );

        host.Push("OnJunctionEvent", Failed(sequence: 1));
        await terminal.OnUi(() => { });
        held.SetResult(Parse(Steps));

        await terminal.Until(() => window.Entries.Count == 4, "the trace is read");
        window
            .Entries.Single(e => e.Key == "step:2")
            .Text.Should()
            .Contain(
                "FAILED",
                "the read answered before the step ended, and the stream said it had"
            );
    }

    [Test]
    public async Task A_step_that_ends_while_the_trace_is_read_again_is_kept()
    {
        var held = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reads = 0;
        var host = new FakeHost()
            .AnswerLater(
                "JunctionRuns",
                _ =>
                    Interlocked.Increment(ref reads) == 1
                        ? Task.FromResult(Parse(Steps))
                        : held.Task
            )
            .Answer("Decisions", Decisions)
            .Answer("Logs", Logs);
        var (terminal, window) = await Open(host);
        await using var _ = terminal;
        await terminal.Until(
            () => window.Entries.Count == 4 && host.Follows("OnJunctionEvent"),
            "the trace is read and followed"
        );

        // Sequence 1, then 3: a frame was lost, so the trace is read again, and the step ends while it is.
        host.Push("OnJunctionEvent", Failed(sequence: 1));
        host.Push("OnJunctionEvent", Failed(sequence: 3));
        await terminal.Until(() => host.Sent("JunctionRuns").Count == 2, "the trace is read again");
        held.SetResult(Parse(Steps));
        await terminal.Until(() => host.Sent("Logs").Count == 2, "the second read answers");
        // negative-wait: the overtaken read, were it applied, would land within this window.
        await Task.Delay(200);
        await terminal.OnUi(() => { });

        window.Entries.Should().HaveCount(4);
        window.Entries.Single(e => e.Key == "step:2").Text.Should().Contain("FAILED");
    }

    private static string Failed(int sequence) =>
        """{"onJunctionEvent":{"metadataId":4812,"eventType":"JUNCTION_FAILED","timestamp":"2026-10-22T10:00:03Z","sequence":SEQ,"junction":{"position":2,"kind":"JUNCTION","name":"ChargeCard","state":"FAILED","startedAt":"2026-10-22T10:00:02Z","durationMs":1200,"failureException":"card_declined","replayed":false,"answerWithheld":false,"nameWithheld":false}}}""".Replace(
            "SEQ",
            sequence.ToString()
        );

    private static JsonObject Parse(string data) => JsonNode.Parse(data)!.AsObject();

    private static Task<(TerminalHarness, RunTraceWindow)> Open(FakeHost host) =>
        TerminalHarness.StartAsync(app => new RunTraceWindow(
            new ConsoleContext(
                app,
                new TraxClient(host),
                new Uri("https://ops.example.com/trax/graphql")
            ),
            4812
        ));
}
