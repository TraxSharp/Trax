using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Exceptions;
using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Broadcaster.SignalR.Fakes.Trains;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

/// <summary>
/// Junction events reach SignalR clients only from a sink that asked for them, only for the trains
/// its filters already send, and through their own client method, carrying no failure message.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
public class SignalRJunctionEventTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private IHubContext<TraxTrainEventHub, ITraxTrainEventClient> _hub = null!;
    private ITraxTrainEventClient _client = null!;
    private readonly List<SignalRTrainEventDispatcher> _created = [];

    [SetUp]
    public void SetUp()
    {
        _hub = Substitute.For<IHubContext<TraxTrainEventHub, ITraxTrainEventClient>>();
        _client = Substitute.For<ITraxTrainEventClient>();
        var clients = Substitute.For<IHubClients<ITraxTrainEventClient>>();
        clients.All.Returns(_client);
        _hub.Clients.Returns(clients);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var dispatcher in _created)
            await dispatcher.DisposeAsync();
        _created.Clear();
    }

    private static SignalRSinkOptions NewOptions() =>
        (SignalRSinkOptions)Activator.CreateInstance(typeof(SignalRSinkOptions), nonPublic: true)!;

    private SignalRTrainEventDispatcher Create(SignalRSinkConfiguration config)
    {
        var dispatcher = new SignalRTrainEventDispatcher(_hub, config);
        _created.Add(dispatcher);
        return dispatcher;
    }

    private static TrainLifecycleEventMessage Step(
        string trainName = "Some.Other.IFoo",
        string eventType = "JunctionFailed"
    ) =>
        new(
            MetadataId: 9,
            ExternalId: "run-9",
            TrainName: trainName,
            TrainState: "InProgress",
            Timestamp: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        )
        {
            Junction = new JunctionEventPayload(
                2,
                JunctionRunKind.Junction,
                "Ship",
                JunctionRunState.Failed,
                new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 2, 0, 0, 2, DateTimeKind.Utc),
                2000,
                FailureClass.Transient,
                "TimeoutException"
            ),
        };

    [Test]
    public async Task A_sink_that_did_not_ask_for_junction_events_sends_none()
    {
        var d = Create(NewOptions().Build());

        await d.HandleAsync(Step(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        _client
            .ReceivedCalls()
            .Should()
            .BeEmpty($"the SignalR sink sends junction events only when asked. See {Adr}.");
    }

    [Test]
    public async Task A_sink_that_asked_sends_each_step_through_JunctionEvent_without_a_failure_message()
    {
        var d = Create(NewOptions().WithJunctionEvents().Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        await d.HandleAsync(Step(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        await _client.DidNotReceive().TrainEvent(Arg.Any<object>());
        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.Name.Should().Be("Ship");
        step.State.Should().Be("Failed");
        step.FailureClass.Should().Be("Transient");
        step.FailureException.Should().Be("TimeoutException");
        JsonSerializer.Serialize(step).Should().NotContain("failureReason");
    }

    [Test]
    public async Task The_train_filter_applies_to_junction_events()
    {
        var d = Create(
            NewOptions().WithJunctionEvents().OnlyForTrains<ICheckGeocodeDriftTrain>().Build()
        );

        await d.HandleAsync(Step(), CancellationToken.None);
        await d.HandleAsync(
            Step(typeof(ICheckGeocodeDriftTrain).FullName!),
            CancellationToken.None
        );
        await d.StopAsync(CancellationToken.None);

        await _client
            .Received(1)
            .JunctionEvent(
                Arg.Is<object>(p =>
                    ((TraxJunctionClientEvent)p).TrainName
                    == typeof(ICheckGeocodeDriftTrain).FullName
                )
            );
    }

    [Test]
    public async Task An_event_type_filter_must_list_the_junction_event_types_it_sends()
    {
        var d = Create(NewOptions().WithJunctionEvents().OnlyForEvents("Completed").Build());

        await d.HandleAsync(Step(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        _client.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task A_withheld_answer_is_not_sent_even_to_a_sink_that_sends_answers()
    {
        var d = Create(NewOptions().WithJunctionAnswers().Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));
        var message = Step(eventType: "Decided");
        message = message with
        {
            Junction = message.Junction! with
            {
                Kind = JunctionRunKind.Choice,
                State = JunctionRunState.Completed,
                Answer = "Red",
                Confidence = 0.9,
                AnswerWithheld = true,
            },
        };

        await d.HandleAsync(message, CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.AnswerWithheld.Should().BeTrue();
        step.Answer.Should().BeNull();
        step.Confidence.Should().BeNull();
    }

    private static TrainLifecycleEventMessage Decided(string trainName = "Some.Other.IFoo")
    {
        var message = Step(trainName, eventType: "Decided");
        return message with
        {
            Junction = message.Junction! with
            {
                Kind = JunctionRunKind.Choice,
                State = JunctionRunState.Completed,
                Answer = "Express",
                Confidence = 0.9,
                QuestionKey = "Lane",
                Replayed = true,
            },
        };
    }

    [Test]
    public async Task By_default_a_question_is_sent_without_its_answer_or_confidence()
    {
        var d = Create(NewOptions().WithJunctionEvents().Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        await d.HandleAsync(Decided(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.QuestionKey.Should().Be("Lane");
        step.Replayed.Should().BeTrue();
        step.Answer.Should()
            .BeNull(
                $"every client sees every train, so answers are sent only when asked. See {Adr}."
            );
        step.Confidence.Should().BeNull();
    }

    [Test]
    public async Task A_sink_that_asks_for_answers_sends_them()
    {
        var d = Create(NewOptions().WithJunctionAnswers().Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        await d.HandleAsync(Decided(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.Answer.Should().Be("Express");
        step.Confidence.Should().Be(0.9);
    }

    [Test]
    public async Task A_host_projection_shapes_junction_events()
    {
        var d = Create(
            NewOptions()
                .WithJunctionProjection(m => new RedactedStep(m.MetadataId, m.Junction!.Name))
                .Build()
        );
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        await d.HandleAsync(Decided(), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        sent.Should().Be(new RedactedStep(9, "Ship"));
    }

    public sealed record RedactedStep(long MetadataId, string Name);

    [Test]
    public async Task A_full_queue_gives_up_junction_events_before_a_train_event()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _client
            .JunctionEvent(Arg.Any<object>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                return release.Task;
            });
        var d = Create(NewOptions().WithJunctionEvents().WithDeliveryQueueCapacity(2).Build());

        await d.HandleAsync(Step(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); // the sender holds this one
        await d.HandleAsync(Step(), CancellationToken.None);
        await d.HandleAsync(Step(), CancellationToken.None); // the queue is full
        var completed = Step(eventType: "Completed") with { Junction = null, ExternalId = "other" };
        await d.HandleAsync(completed, CancellationToken.None);
        await d.HandleAsync(Step(), CancellationToken.None);

        release.SetResult();
        await d.StopAsync(CancellationToken.None);

        await _client
            .Received(1)
            .TrainEvent(Arg.Is<object>(p => ((TraxClientEvent)p).ExternalId == "other"));
        d.DroppedEvents.Should()
            .Be(2, $"a run's steps are given up before another run's outcome. See {Adr}.");
    }

    private static TrainLifecycleEventMessage OnTrack(bool nameWithheld = false)
    {
        var message = Step(eventType: "JunctionStarted");
        return message with
        {
            Junction = message.Junction! with { TrackPosition = 1, NameWithheld = nameWithheld },
        };
    }

    [TestCase(false, false, "(withheld)")]
    [TestCase(true, false, "Ship")]
    [TestCase(true, true, "(withheld)")]
    public async Task A_junction_on_a_decision_track_is_named_only_where_answers_are_sent(
        bool answers,
        bool nameWithheld,
        string name
    )
    {
        var options = answers
            ? NewOptions().WithJunctionAnswers()
            : NewOptions().WithJunctionEvents();
        var d = Create(options.Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        await d.HandleAsync(OnTrack(nameWithheld), CancellationToken.None);
        await d.StopAsync(CancellationToken.None);

        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.Name.Should().Be(name, $"a junction on a track gives its answer away. See {Adr}.");
        step.NameWithheld.Should().Be(name == "(withheld)");
        step.TrackPosition.Should().Be(1);
    }

    [TestCase(false, null, null)]
    [TestCase(true, "Lane", "Express")]
    public async Task A_question_on_a_decision_track_is_named_only_where_answers_are_sent(
        bool answers,
        string? key,
        string? answer
    )
    {
        var options = answers
            ? NewOptions().WithJunctionAnswers()
            : NewOptions().WithJunctionEvents();
        var d = Create(options.Build());
        object? sent = null;
        await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

        var decided = Decided();
        await d.HandleAsync(
            decided with
            {
                Junction = decided.Junction! with { TrackPosition = 1, Name = "Lane" },
            },
            CancellationToken.None
        );
        await d.StopAsync(CancellationToken.None);

        var step = sent.Should().BeOfType<TraxJunctionClientEvent>().Subject;
        step.QuestionKey.Should()
            .Be(key, $"a question asked on a track gives its answer away. See {Adr}.");
        step.Name.Should().Be(key ?? "(withheld)");
        step.Answer.Should().Be(answer);
    }

    [Test]
    public async Task A_host_projection_shapes_junction_events_whichever_is_called_last()
    {
        foreach (
            var options in new[]
            {
                NewOptions()
                    .WithJunctionProjection(m => new RedactedStep(m.MetadataId, "redacted"))
                    .WithJunctionAnswers(),
                NewOptions()
                    .WithJunctionAnswers()
                    .WithJunctionProjection(m => new RedactedStep(m.MetadataId, "redacted")),
            }
        )
        {
            var d = Create(options.Build());
            object? sent = null;
            await _client.JunctionEvent(Arg.Do<object>(p => sent = p));

            await d.HandleAsync(Decided(), CancellationToken.None);
            await d.StopAsync(CancellationToken.None);

            sent.Should()
                .Be(
                    new RedactedStep(9, "redacted"),
                    $"a host's projection is never swapped for the default one. See {Adr}."
                );
        }
    }

    [Test]
    public void Only_a_sink_that_asked_registers_as_a_junction_event_handler()
    {
        using var off = Provider(o => { });
        using var on = Provider(o => o.WithJunctionEvents());

        off.GetServices<IJunctionEventHandler>().Should().BeEmpty();
        on.GetServices<IJunctionEventHandler>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeSameAs(on.GetRequiredService<SignalRTrainEventDispatcher>());
    }

    private static ServiceProvider Provider(Action<SignalRSinkOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        new TraxBuilder(services, new EffectRegistry()).AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseSignalRHub(configure))
        );
        return services.BuildServiceProvider();
    }
}
