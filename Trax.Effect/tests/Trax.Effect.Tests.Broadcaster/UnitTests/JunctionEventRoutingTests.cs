using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// A junction event rides the train event transport, but reaches only the handlers written for
/// one: a train event handler is never handed an event type it does not know.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
public class JunctionEventRoutingTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static TrainLifecycleEventMessage Message(
        string eventType,
        JunctionEventPayload? junction = null
    ) =>
        new(
            MetadataId: 7,
            ExternalId: "run-7",
            TrainName: "Routing.ITrain",
            TrainState: "InProgress",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: "Remote",
            Output: null
        )
        {
            Junction = junction,
        };

    private static readonly JunctionEventPayload Step = new(
        3,
        JunctionRunKind.Junction,
        "Ship",
        JunctionRunState.Failed,
        new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 2, 9, 0, 1, DateTimeKind.Utc),
        1000,
        FailureClass.Transient,
        "TimeoutException"
    );

    private static async Task<(
        Func<TrainLifecycleEventMessage, CancellationToken, Task> Deliver,
        TrainEventReceiverService Service
    )> Start(IServiceProvider services)
    {
        var receiver = Substitute.For<ITrainEventReceiver>();
        var started = new TaskCompletionSource<
            Func<TrainLifecycleEventMessage, CancellationToken, Task>
        >(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver
            .StartAsync(
                Arg.Do<Func<TrainLifecycleEventMessage, CancellationToken, Task>>(h =>
                    started.TrySetResult(h)
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var service = new TrainEventReceiverService(receiver, services);
        await service.StartAsync(CancellationToken.None);
        return (await started.Task.WaitAsync(Timeout), service);
    }

    [Test]
    public async Task A_junction_event_reaches_junction_handlers_and_never_train_handlers()
    {
        var trainHandler = Substitute.For<ITrainEventHandler>();
        var junctionHandler = Substitute.For<IJunctionEventHandler>();
        await using var services = new ServiceCollection()
            .AddSingleton(trainHandler)
            .AddSingleton(junctionHandler)
            .AddSingleton<BroadcastInstance>()
            .BuildServiceProvider();
        var (deliver, service) = await Start(services);

        await deliver(Message("JunctionFailed", Step), CancellationToken.None);
        await deliver(Message("Completed"), CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await junctionHandler
            .Received(1)
            .HandleAsync(
                Arg.Is<TrainLifecycleEventMessage>(m => m.EventType == "JunctionFailed"),
                Arg.Any<CancellationToken>()
            );
        await junctionHandler
            .DidNotReceive()
            .HandleAsync(
                Arg.Is<TrainLifecycleEventMessage>(m => m.EventType == "Completed"),
                Arg.Any<CancellationToken>()
            );
        await trainHandler
            .Received(1)
            .HandleAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());
        await trainHandler
            .DidNotReceive()
            .HandleAsync(
                Arg.Is<TrainLifecycleEventMessage>(m => m.EventType == "JunctionFailed"),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task A_junction_event_type_with_no_step_reaches_no_handler()
    {
        var trainHandler = Substitute.For<ITrainEventHandler>();
        var junctionHandler = Substitute.For<IJunctionEventHandler>();
        await using var services = new ServiceCollection()
            .AddSingleton(trainHandler)
            .AddSingleton(junctionHandler)
            .AddSingleton<BroadcastInstance>()
            .BuildServiceProvider();
        var (deliver, service) = await Start(services);

        await deliver(Message("Decided"), CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        trainHandler
            .ReceivedCalls()
            .Should()
            .BeEmpty($"a train event handler never sees a junction event type. See {Adr}.");
        junctionHandler.ReceivedCalls().Should().BeEmpty();
    }

    [TestCase("JunctionStarted", true)]
    [TestCase("JunctionCompleted", true)]
    [TestCase("JunctionFailed", true)]
    [TestCase("JunctionCancelled", true)]
    [TestCase("Decided", true)]
    [TestCase("DecisionRefused", true)]
    [TestCase("Routed", true)]
    [TestCase("Started", false)]
    [TestCase("Completed", false)]
    [TestCase("StateChanged", false)]
    [TestCase("DataChanged", false)]
    public void Junction_event_types_are_told_apart_from_train_event_types(
        string eventType,
        bool junction
    ) =>
        TrainLifecycleEventMessage
            .IsJunctionEvent(eventType)
            .Should()
            .Be(junction, $"a junction event reaches junction event handlers only. See {Adr}.");

    [Test]
    public void A_junction_event_round_trips_with_its_step_and_names_its_enums()
    {
        var json = JsonSerializer.Serialize(Message("JunctionFailed", Step));

        json.Should().Contain("\"kind\":\"Junction\"");
        json.Should().Contain("\"state\":\"Failed\"");
        json.Should().Contain("\"failureClass\":\"Transient\"");

        var back = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json)!;
        back.Junction.Should().BeEquivalentTo(Step);
    }

    [Test]
    public void A_train_event_from_a_publisher_that_predates_junction_events_has_no_step()
    {
        var json = """
            {"metadataId":1,"externalId":"x","trainName":"T","trainState":"Completed",
             "timestamp":"2026-10-02T09:00:00Z","failureJunction":null,"failureReason":null,
             "eventType":"Completed","executor":"W","output":null}
            """;

        JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json)!.Junction.Should().BeNull();
    }
}
