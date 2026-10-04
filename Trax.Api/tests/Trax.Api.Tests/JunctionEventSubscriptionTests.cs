using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Validation;
using Trax.Core.Exceptions;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// <c>onJunctionEvent</c> carries the authorization of the train events of the run it follows:
/// the same forwarding rule decides whether a train's steps are published at all, the same
/// per-subscriber visibility decides who receives them, with the same host detail withheld outside
/// the operations view, and the same refusal at subscribe time for a subscriber who could receive
/// nothing.
///
/// <para>Enforces <c>docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md")]
[TestFixture]
public class JunctionEventSubscriptionTests
{
    private const string Adr =
        "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md";

    private const long Run = 42;

    private static string OnJunctionEvent(long metadataId) =>
        "subscription { onJunctionEvent(metadataId: "
        + metadataId
        + ") { sequence metadataId trainName eventType junction { position kind name state "
        + "questionKey answer confidence answerWithheld decider failureException failureClass "
        + "attempt nameWithheld trackPosition } } }";

    public interface IAdminOnlyTrain;

    public interface IPublicTrain;

    public interface IUnbroadcastTrain;

    private ServiceProvider? _provider;

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _provider = null;
    }

    #region Who may subscribe

    [Test]
    public async Task OperationsGatedByRole_Player_IsRefused()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        (await SubscribeAsync(executor, OnJunctionEvent(Run), User("Player")))
            .Refused.Should()
            .BeTrue("a run's steps carry the authorization of its train events, per " + Adr);
    }

    [Test]
    public async Task OperationsGatedByRole_Anonymous_IsRefused()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        (await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous()))
            .Refused.Should()
            .BeTrue();
    }

    [Test]
    public async Task OnlyAuthorizedBroadcastTrains_Anonymous_IsRefused()
    {
        var executor = await BuildAsync(g => g, Broadcast<IAdminOnlyTrain>(roles: "Admin"));

        (await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous()))
            .Refused.Should()
            .BeTrue("a subscriber who could receive nothing is refused, per " + Adr);
    }

    [Test]
    public async Task MetadataId_IsRequired()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument("subscription { onJunctionEvent { sequence } }")
                .SetGlobalState("ClaimsPrincipal", User("Admin"))
                .Build()
        );

        result
            .Should()
            .BeOfType<OperationResult>("a subscription without a run is a validation error")
            .Which.Errors.Should()
            .NotBeNullOrEmpty();
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task MetadataId_ThatIsNotPositive_IsRefused(long metadataId)
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(OnJunctionEvent(metadataId))
                .SetGlobalState("ClaimsPrincipal", User("Admin"))
                .Build()
        );

        var refused = result
            .Should()
            .BeOfType<OperationResult>(
                "an id of 0 would follow every unsaved run at once, so it is refused, per " + Adr
            )
            .Subject;
        refused.Errors.Should().Contain(e => e.Code == RunIdArgument.ErrorCode);
    }

    #endregion

    #region What each subscriber receives

    [Test]
    public async Task OperationsGatedByRole_Admin_ReceivesTheRunsStepsWithFullDetail()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));
        sub.Refused.Should().BeFalse();

        var received = await sub.NextAsync(() =>
            Handle(Failed("Some.Unbroadcast.Train", Run, exception: "NpgsqlException"))
        );

        received["trainName"].Should().Be("Some.Unbroadcast.Train");
        received["eventType"].Should().Be("JUNCTION_FAILED");
        received["sequence"].Should().Be(1L);
        var step = Step(received);
        step["failureException"].Should().Be("NpgsqlException");
        step["failureClass"].Should().Be("TRANSIENT");
        step["decider"].Should().Be("My.Deciders.ModelDecider");
        step["attempt"].Should().Be(2);
    }

    [Test]
    public async Task OnlyTheSubscribedRunsSteps_AreDelivered()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        var received = await sub.NextAsync(() =>
        {
            Handle(Started("Some.Train", Run + 1, position: 7));
            Handle(Started("Some.Train", Run, position: 3));
        });

        received["metadataId"].Should().Be(Run);
        Step(received)["position"].Should().Be(3, "a subscription follows one run");
    }

    [Test]
    public async Task BroadcastTrainGatedByRole_PlayerReceivesNoneOfItsSteps()
    {
        var executor = await BuildAsync(
            g => g,
            Broadcast<IAdminOnlyTrain>(roles: "Admin"),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Player"));
        sub.Refused.Should().BeFalse();

        // The gated train's step goes first, for the same run id; the first thing the player
        // receives must be the public one.
        var received = await sub.NextAsync(() =>
        {
            Handle(Started(typeof(IAdminOnlyTrain).FullName!, Run, position: 0));
            Handle(Started(typeof(IPublicTrain).FullName!, Run, position: 1));
        });

        received["trainName"]
            .Should()
            .Be(
                typeof(IPublicTrain).FullName,
                "a subscriber who would not see a train's events sees none of its steps, per " + Adr
            );
    }

    [Test]
    public async Task BroadcastView_WithholdsHostDetail()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Failed(typeof(IPublicTrain).FullName!, Run, exception: "NpgsqlException"))
        );

        var step = Step(received);
        step["failureException"]
            .Should()
            .BeNull(
                "outside the operations view an exception type is host detail, as a train "
                    + "event's failure reason is, per "
                    + Adr
            );
        step["decider"].Should().BeNull();
        step["failureClass"].Should().Be("TRANSIENT");
    }

    [Test]
    public async Task BroadcastView_TrainExceptionType_IsShown()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Failed(typeof(IPublicTrain).FullName!, Run, exception: "TrainException"))
        );

        Step(received)["failureException"].Should().Be("TrainException");
    }

    [Test]
    public async Task BroadcastView_WithholdsAnswersByDefault()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Decided(typeof(IPublicTrain).FullName!, withheld: false))
        );

        var step = Step(received);
        step["answer"]
            .Should()
            .BeNull(
                "outside the operations view an answer is shown only when the host opts in, per "
                    + Adr
            );
        step["confidence"].Should().BeNull();
        step["answerWithheld"].Should().Be(false);
        step["questionKey"].Should().Be("Lane", "that the question was asked is still shown");
    }

    [Test]
    public async Task BroadcastView_WithTheOptIn_ShowsAnswers()
    {
        var executor = await BuildAsync(
            g => g.AllowJunctionAnswersForBroadcastSubscribers(),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Decided(typeof(IPublicTrain).FullName!, withheld: false))
        );

        var step = Step(received);
        step["answer"].Should().Be("Fast");
        step["confidence"].Should().Be(0.8);
        step["decider"].Should().BeNull("the opt-in shares answers, not host detail");
    }

    [Test]
    public async Task BroadcastView_WithTheOptIn_StillWithholdsASensitiveAnswer()
    {
        var executor = await BuildAsync(
            g => g.AllowJunctionAnswersForBroadcastSubscribers(),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Decided(typeof(IPublicTrain).FullName!, withheld: true))
        );

        var step = Step(received);
        step["answerWithheld"].Should().Be(true);
        step["answer"].Should().BeNull("a [TraxSensitive] answer is withheld from everyone");
        step["confidence"].Should().BeNull();
    }

    [Test]
    public async Task BroadcastView_WithholdsTheNamesOfJunctionsOnATrackByDefault()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(OnTrack(typeof(IPublicTrain).FullName!, nameWithheld: false))
        );

        var step = Step(received);
        step["name"]
            .Should()
            .Be(
                JunctionStep.WithheldName,
                "which junction ran on a track gives away the answer the subscriber is not shown, per "
                    + Adr
            );
        step["nameWithheld"].Should().Be(true);
        step["trackPosition"].Should().Be(2);
    }

    [Test]
    public async Task BroadcastView_KeepsTheNameOfAJunctionBeforeAnyTrack()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(Started(typeof(IPublicTrain).FullName!, Run, position: 0))
        );

        Step(received)["name"].Should().Be("Step");
        Step(received)["nameWithheld"].Should().Be(false);
    }

    [Test]
    public async Task BroadcastView_WithTheOptIn_ShowsTheNamesOfJunctionsOnATrack()
    {
        var executor = await BuildAsync(
            g => g.AllowJunctionAnswersForBroadcastSubscribers(),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(OnTrack(typeof(IPublicTrain).FullName!, nameWithheld: false))
        );

        Step(received)["name"].Should().Be("ChargeCard");
        Step(received)["nameWithheld"].Should().Be(false);
    }

    private static readonly object[] StepsOnATrack =
    [
        new object[] { JunctionRunKind.Choice, TrainLifecycleEventMessage.DecidedEventType },
        new object[] { JunctionRunKind.Score, TrainLifecycleEventMessage.DecidedEventType },
        new object[] { JunctionRunKind.YesNo, TrainLifecycleEventMessage.DecidedEventType },
        new object[] { JunctionRunKind.Route, TrainLifecycleEventMessage.RoutedEventType },
    ];

    [TestCaseSource(nameof(StepsOnATrack))]
    public async Task BroadcastView_WithholdsWhatNamesAQuestionOrRouteOnATrackByDefault(
        JunctionRunKind kind,
        string eventType
    )
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(QuestionOnTrack(typeof(IPublicTrain).FullName!, kind, eventType))
        );

        var step = Step(received);
        step["name"]
            .Should()
            .Be(
                JunctionStep.WithheldName,
                "a question or route on a track is withheld as a junction on it is, per " + Adr
            );
        step["nameWithheld"].Should().Be(true);
        step["questionKey"].Should().BeNull("the key names the question as the name does");
        step["answer"].Should().BeNull();
        step["confidence"].Should().BeNull();
        step["position"].Should().Be(5);
        step["trackPosition"].Should().Be(2);
    }

    [TestCaseSource(nameof(StepsOnATrack))]
    public async Task BroadcastView_WithTheOptIn_ShowsAQuestionOrRouteOnATrack(
        JunctionRunKind kind,
        string eventType
    )
    {
        var executor = await BuildAsync(
            g => g.AllowJunctionAnswersForBroadcastSubscribers(),
            Broadcast<IPublicTrain>(anonymous: true)
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            Handle(QuestionOnTrack(typeof(IPublicTrain).FullName!, kind, eventType))
        );

        var step = Step(received);
        step["name"].Should().Be("Region");
        step["nameWithheld"].Should().Be(false);
        step["questionKey"].Should().Be("Region");
        step["answer"].Should().Be("North");
        step["confidence"].Should().Be(0.7);
    }

    [TestCaseSource(nameof(StepsOnATrack))]
    public async Task OperationsView_ShowsAQuestionOrRouteOnATrack(
        JunctionRunKind kind,
        string eventType
    )
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        var received = await sub.NextAsync(() =>
            Handle(QuestionOnTrack("Some.Train", kind, eventType))
        );

        var step = Step(received);
        step["name"].Should().Be("Region");
        step["nameWithheld"].Should().Be(false);
        step["questionKey"].Should().Be("Region");
        step["answer"].Should().Be("North");
        step["confidence"].Should().Be(0.7);
    }

    [TestCase(JunctionRunKind.Choice)]
    [TestCase(JunctionRunKind.Route)]
    public void RecordedStep_QuestionOrRouteOnATrack_KeepsWhatTheRunRecorded(JunctionRunKind kind)
    {
        // operations.junctionRuns reads rows through JunctionStep.From: the operations view is
        // shown everything the run did not withhold itself.
        var step = JunctionStep.From(
            new JunctionRun
            {
                Position = 5,
                Kind = kind,
                Name = "Region",
                State = JunctionRunState.Completed,
                StartedAt = DateTime.UtcNow,
                QuestionKey = "Region",
                Answer = "North",
                Confidence = 0.7,
                TrackPosition = 2,
            }
        );

        step.OnATrack.Should().BeTrue();
        step.Name.Should().Be("Region");
        step.NameWithheld.Should().BeFalse();
        step.QuestionKey.Should().Be("Region");
        step.Answer.Should().Be("North");
        step.Confidence.Should().Be(0.7);
    }

    [Test]
    public async Task NameTheRunWithheld_IsWithheldEvenInTheOperationsView()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        // The run writes the placeholder itself; a payload that still carries a name must not
        // leak it.
        var received = await sub.NextAsync(() => Handle(OnTrack("Some.Train", nameWithheld: true)));

        Step(received)["name"].Should().Be(JunctionStep.WithheldName);
        Step(received)["nameWithheld"].Should().Be(true);
    }

    [Test]
    public void RecordedStep_NameTheRunWithheld_IsWithheld()
    {
        var step = JunctionStep.From(
            new JunctionRun
            {
                Position = 3,
                Kind = JunctionRunKind.Junction,
                Name = "ChargeCard",
                State = JunctionRunState.Completed,
                StartedAt = DateTime.UtcNow,
                NameWithheld = true,
                TrackPosition = 2,
            }
        );

        step.Name.Should()
            .Be(JunctionStep.WithheldName, "the timeline honours it as the feed does");
        step.NameWithheld.Should().BeTrue();
        step.TrackPosition.Should().Be(2);
    }

    [Test]
    public void LiveStep_NameTheRunWithheld_IsWithheld()
    {
        var step = JunctionStep.From(
            new JunctionEventPayload(
                3,
                JunctionRunKind.Junction,
                "ChargeCard",
                JunctionRunState.InProgress,
                DateTime.UtcNow,
                NameWithheld: true,
                TrackPosition: 2
            )
        );

        step.Name.Should().Be(JunctionStep.WithheldName);
    }

    [Test]
    public async Task OperationsView_AlwaysShowsAnswers()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        var received = await sub.NextAsync(() => Handle(Decided("Some.Train", withheld: false)));

        var step = Step(received);
        step["answer"].Should().Be("Fast");
        step["confidence"].Should().Be(0.8);
    }

    [Test]
    public async Task WithheldAnswer_IsNullEvenWhenThePayloadCarriesOne()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        var received = await sub.NextAsync(() =>
            Handle(
                Message(
                    "Some.Train",
                    Run,
                    TrainLifecycleEventMessage.DecidedEventType,
                    new JunctionEventPayload(
                        Position: 1,
                        Kind: JunctionRunKind.Choice,
                        Name: "Tier",
                        State: JunctionRunState.Completed,
                        StartedAt: DateTime.UtcNow,
                        QuestionKey: "Tier",
                        Answer: "Red",
                        Confidence: 0.9,
                        AnswerWithheld: true
                    )
                )
            )
        );

        var step = Step(received);
        step["answerWithheld"].Should().Be(true);
        step["answer"].Should().BeNull("a [TraxSensitive] question's answer is never published");
        step["confidence"].Should().BeNull();
    }

    #endregion

    #region What is published at all

    [Test]
    public async Task TrainWithoutBroadcast_StreamAllTrainsOff_PublishesNothing()
    {
        var sender = Substitute.For<ITopicEventSender>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Broadcast<IPublicTrain>(anonymous: true)]);
        var handler = new GraphQLJunctionEventHandler(
            sender,
            new LifecycleStreamRule(discovery, new TrainLifecycleStreamOptions())
        );

        await handler.HandleAsync(Started(typeof(IUnbroadcastTrain).FullName!, Run, 0), default);

        await sender
            .DidNotReceiveWithAnyArgs()
            .SendAsync(default(string)!, default(JunctionEvent)!, default);
    }

    [Test]
    public async Task BroadcastTrain_StreamAllTrainsOff_IsPublished()
    {
        var sender = Substitute.For<ITopicEventSender>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Broadcast<IPublicTrain>(anonymous: true)]);
        var handler = new GraphQLJunctionEventHandler(
            sender,
            new LifecycleStreamRule(discovery, new TrainLifecycleStreamOptions())
        );

        await handler.HandleAsync(Started(typeof(IPublicTrain).FullName!, Run, 0), default);

        await sender
            .Received(1)
            .SendAsync(
                nameof(LifecycleSubscriptions.OnJunctionEvent),
                Arg.Is<JunctionEvent>(e => e != null && e.MetadataId == Run),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task TrainEvent_IsNotForwardedAsAJunctionEvent()
    {
        var sender = Substitute.For<ITopicEventSender>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Broadcast<IPublicTrain>(anonymous: true)]);
        var handler = new GraphQLJunctionEventHandler(
            sender,
            new LifecycleStreamRule(discovery, new TrainLifecycleStreamOptions())
        );

        await handler.HandleAsync(
            Message(typeof(IPublicTrain).FullName!, Run, "Completed", junction: null),
            default
        );

        await sender
            .DidNotReceiveWithAnyArgs()
            .SendAsync(default(string)!, default(JunctionEvent)!, default);
    }

    [Test]
    public void TheHandler_IsRegisteredForJunctionEvents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddTraxGraphQLForTesting();

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(IJunctionEventHandler)
                && sd.ImplementationType == typeof(GraphQLJunctionEventHandler)
            );

        // A singleton handler may hold only singletons: the run's path resolves it per step.
        services
            .Where(sd => sd.ServiceType == typeof(ITopicEventSender))
            .Should()
            .OnlyContain(sd => sd.Lifetime == ServiceLifetime.Singleton);
    }

    #endregion

    #region A run on another host

    [Test]
    public async Task RemoteRun_ItsStepReachesOnJunctionEventThroughTheReceiver()
    {
        var executor = await BuildAsync(g =>
            g.ExposeOperationQueries().GateOperations(roles: "Admin")
        );
        await using var receiver = await StartReceiverAsync();
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), User("Admin"));

        var received = await sub.NextAsync(() =>
            receiver.Deliver(
                FromAnotherHost(Failed("Some.Unbroadcast.Train", Run, exception: "IOException"))
            )
        );

        received["metadataId"].Should().Be(Run);
        received["eventType"].Should().Be("JUNCTION_FAILED");
        var step = Step(received);
        step["name"].Should().Be("Charge");
        step["failureException"].Should().Be("IOException");
        step["attempt"].Should().Be(2);
    }

    [Test]
    public async Task RemoteRun_BroadcastView_WithholdsTheNameOfAJunctionOnATrack()
    {
        var executor = await BuildAsync(g => g, Broadcast<IPublicTrain>(anonymous: true));
        await using var receiver = await StartReceiverAsync();
        await using var sub = await SubscribeAsync(executor, OnJunctionEvent(Run), Anonymous());

        var received = await sub.NextAsync(() =>
            receiver.Deliver(
                FromAnotherHost(OnTrack(typeof(IPublicTrain).FullName!, nameWithheld: false))
            )
        );

        var step = Step(received);
        step["name"]
            .Should()
            .Be(
                JunctionStep.WithheldName,
                "a step from another host is shown to each subscriber as a local one is, per " + Adr
            );
        step["nameWithheld"].Should().Be(true);
    }

    private static TrainLifecycleEventMessage FromAnotherHost(TrainLifecycleEventMessage message) =>
        message with
        {
            InstanceId = "another-host",
        };

    /// <summary>
    /// Starts the hosted service <c>UseBroadcaster()</c> registers, over this test's provider, with
    /// a transport that hands its callback to the test.
    /// </summary>
    private async Task<RunningReceiver> StartReceiverAsync()
    {
        var transport = new CapturingReceiver();
        var service = new TrainEventReceiverService(transport, _provider!);
        await service.StartAsync(default);
        await transport.Started.WaitAsync(TimeSpan.FromSeconds(10));
        return new RunningReceiver(service, transport);
    }

    private sealed class RunningReceiver(
        TrainEventReceiverService service,
        CapturingReceiver transport
    ) : IAsyncDisposable
    {
        public void Deliver(TrainLifecycleEventMessage message) =>
            transport.Started.Result(message, default).GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await service.StopAsync(default);
            service.Dispose();
        }
    }

    private sealed class CapturingReceiver : ITrainEventReceiver
    {
        private readonly TaskCompletionSource<
            Func<TrainLifecycleEventMessage, CancellationToken, Task>
        > _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Func<TrainLifecycleEventMessage, CancellationToken, Task>> Started =>
            _started.Task;

        public Task StartAsync(
            Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
            CancellationToken ct
        )
        {
            _started.TrySetResult(handler);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion

    #region Helpers

    private static IReadOnlyDictionary<string, object?> Step(
        IReadOnlyDictionary<string, object?> received
    ) => (IReadOnlyDictionary<string, object?>)received["junction"]!;

    private static ClaimsPrincipal User(string role) =>
        new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)],
                "Test"
            )
        );

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static TrainRegistration Broadcast<T>(string? roles = null, bool anonymous = false) =>
        new()
        {
            ServiceType = typeof(T),
            ImplementationType = typeof(T),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = typeof(T).FullName!,
            ImplementationTypeName = typeof(T).FullName!,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [],
            RequiredRoles = roles is null ? [] : [roles],
            HasAuthorizeAttribute = !anonymous,
            HasAllowAnonymousAttribute = anonymous,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = true,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private static TrainLifecycleEventMessage Started(string train, long run, int position) =>
        Message(
            train,
            run,
            TrainLifecycleEventMessage.JunctionStartedEventType,
            new JunctionEventPayload(
                position,
                JunctionRunKind.Junction,
                "Step",
                JunctionRunState.InProgress,
                DateTime.UtcNow
            )
        );

    private static TrainLifecycleEventMessage OnTrack(string train, bool nameWithheld) =>
        Message(
            train,
            Run,
            TrainLifecycleEventMessage.JunctionStartedEventType,
            new JunctionEventPayload(
                Position: 3,
                Kind: JunctionRunKind.Junction,
                Name: "ChargeCard",
                State: JunctionRunState.InProgress,
                StartedAt: DateTime.UtcNow,
                NameWithheld: nameWithheld,
                TrackPosition: 2
            )
        );

    private static TrainLifecycleEventMessage QuestionOnTrack(
        string train,
        JunctionRunKind kind,
        string eventType
    ) =>
        Message(
            train,
            Run,
            eventType,
            new JunctionEventPayload(
                Position: 5,
                Kind: kind,
                Name: "Region",
                State: JunctionRunState.Completed,
                StartedAt: DateTime.UtcNow,
                QuestionKey: "Region",
                Answer: "North",
                Confidence: 0.7,
                TrackPosition: 2
            )
        );

    private static TrainLifecycleEventMessage Decided(string train, bool withheld) =>
        Message(
            train,
            Run,
            TrainLifecycleEventMessage.DecidedEventType,
            new JunctionEventPayload(
                Position: 1,
                Kind: JunctionRunKind.Choice,
                Name: "Lane",
                State: JunctionRunState.Completed,
                StartedAt: DateTime.UtcNow,
                QuestionKey: "Lane",
                Answer: "Fast",
                Confidence: 0.8,
                Decider: "My.Deciders.ModelDecider",
                AnswerWithheld: withheld
            )
        );

    private static TrainLifecycleEventMessage Failed(string train, long run, string exception) =>
        Message(
            train,
            run,
            TrainLifecycleEventMessage.JunctionFailedEventType,
            new JunctionEventPayload(
                Position: 2,
                Kind: JunctionRunKind.Junction,
                Name: "Charge",
                State: JunctionRunState.Failed,
                StartedAt: DateTime.UtcNow,
                EndedAt: DateTime.UtcNow,
                DurationMs: 3,
                FailureClass: FailureClass.Transient,
                FailureException: exception,
                Decider: "My.Deciders.ModelDecider",
                Attempt: 2
            )
        );

    private static TrainLifecycleEventMessage Message(
        string train,
        long run,
        string eventType,
        JunctionEventPayload? junction
    ) =>
        new(
            MetadataId: run,
            ExternalId: "ext-" + run,
            TrainName: train,
            TrainState: nameof(TrainState.InProgress),
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null,
            HostName: "worker-1",
            HostEnvironment: "Production"
        )
        {
            Junction = junction,
        };

    private void Handle(TrainLifecycleEventMessage message)
    {
        foreach (var handler in _provider!.GetServices<IJunctionEventHandler>())
            handler.HandleAsync(message, default).GetAwaiter().GetResult();
    }

    private async Task<IRequestExecutor> BuildAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure,
        params TrainRegistration[] trains
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        // A query root the broadcast-only hosts need; its fields are irrelevant here.
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("JunctionEventSubscription_" + Guid.NewGuid())
        );
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            g => configure(g.AddDbContext<OrderTestDbContext>())
        );

        _provider = services.BuildServiceProvider();
        return await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static async Task<Subscription> SubscribeAsync(
        IRequestExecutor executor,
        string query,
        ClaimsPrincipal user
    )
    {
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(query)
                .SetGlobalState("ClaimsPrincipal", user)
                .Build()
        );

        return result is IResponseStream stream ? new Subscription(stream) : new Subscription(null);
    }

    private sealed class Subscription(IResponseStream? stream) : IAsyncDisposable
    {
        public bool Refused => stream is null;

        /// <summary>
        /// Publishes with <paramref name="publish"/> until the subscriber receives something, and
        /// returns the first payload it receives.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, object?>> NextAsync(Action publish)
        {
            stream.Should().NotBeNull();
            var received = new TaskCompletionSource<OperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var reader = Task.Run(async () =>
            {
                await foreach (var item in stream!.ReadResultsAsync())
                {
                    received.TrySetResult(item);
                    break;
                }
            });

            // Publish until the subscriber picks one up: ExecuteAsync can return before the topic
            // subscription is registered, so a single publish can race ahead of it.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!received.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                publish();
                // allowed-delay: re-publish interval, bounded by the 10s deadline; WhenAny wakes
                // the instant the subscriber receives.
                await Task.WhenAny(received.Task, Task.Delay(100));
            }

            received.Task.IsCompleted.Should().BeTrue("an event should reach the subscriber");
            var payload = await received.Task;
            payload.Errors.Should().BeNullOrEmpty();
            await reader;
            return (IReadOnlyDictionary<string, object?>)payload.DataMap()["onJunctionEvent"]!;
        }

        public async ValueTask DisposeAsync()
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    #endregion
}
