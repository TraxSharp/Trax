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
