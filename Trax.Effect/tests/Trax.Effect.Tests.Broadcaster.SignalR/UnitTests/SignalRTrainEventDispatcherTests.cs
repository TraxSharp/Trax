using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Broadcaster.SignalR.Fakes.Trains;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

[TestFixture]
public class SignalRTrainEventDispatcherTests
{
    private IHubContext<TraxTrainEventHub, ITraxTrainEventClient> _hub = null!;
    private ITraxTrainEventClient _client = null!;
    private IHubClients<ITraxTrainEventClient> _clients = null!;
    private TestLogger<SignalRTrainEventDispatcher> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _hub = Substitute.For<IHubContext<TraxTrainEventHub, ITraxTrainEventClient>>();
        _client = Substitute.For<ITraxTrainEventClient>();
        _clients = Substitute.For<IHubClients<ITraxTrainEventClient>>();
        _clients.All.Returns(_client);
        _hub.Clients.Returns(_clients);
        _logger = new TestLogger<SignalRTrainEventDispatcher>();
    }

    private static SignalRSinkOptions NewOptions() =>
        (SignalRSinkOptions)Activator.CreateInstance(typeof(SignalRSinkOptions), nonPublic: true)!;

    private SignalRTrainEventDispatcher Create(SignalRSinkConfiguration config) =>
        new(_hub, config, _logger);

    private static TrainLifecycleEventMessage Message(
        string trainName = "Some.Other.IFoo",
        string eventType = "Completed",
        string externalId = "ext-1"
    ) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: trainName,
            TrainState: "Completed",
            Timestamp: new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        );

    [Test]
    public async Task Handle_EventTypeNotInFilter_DoesNotCallHubClient()
    {
        var d = Create(NewOptions().OnlyForEvents("Completed").Build());

        await d.HandleAsync(Message(eventType: "Failed"), CancellationToken.None);

        await _client.Received(0).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_EventTypeInFilter_CallsHubClient()
    {
        var d = Create(NewOptions().OnlyForEvents("Completed").Build());

        await d.HandleAsync(Message(eventType: "Completed"), CancellationToken.None);

        await _client.Received(1).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TrainNameNotInFilter_DoesNotCallHubClient()
    {
        var d = Create(NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain>().Build());

        await d.HandleAsync(Message(trainName: "Some.Other.IFoo"), CancellationToken.None);

        await _client.Received(0).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TrainNameMatchesInterfaceFullName_CallsHubClient()
    {
        var d = Create(NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain>().Build());

        await d.HandleAsync(
            Message(trainName: typeof(ICheckGeocodeDriftTrain).FullName!),
            CancellationToken.None
        );

        await _client.Received(1).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TrainShortNameOnly_DoesNotMatch()
    {
        var d = Create(NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain>().Build());

        await d.HandleAsync(
            Message(trainName: nameof(ICheckGeocodeDriftTrain)),
            CancellationToken.None
        );

        await _client.Received(0).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_NoFilters_AllEventsAllowed()
    {
        var d = Create(NewOptions().Build());

        await d.HandleAsync(Message(eventType: "Started"), CancellationToken.None);
        await d.HandleAsync(Message(eventType: "Completed"), CancellationToken.None);
        await d.HandleAsync(Message(eventType: "Failed"), CancellationToken.None);
        await d.HandleAsync(Message(eventType: "Cancelled"), CancellationToken.None);

        await _client.Received(4).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_BothFiltersCombined_AppliesAnd()
    {
        var d = Create(
            NewOptions().OnlyForEvents("Completed").OnlyForTrains<ICheckGeocodeDriftTrain>().Build()
        );

        await d.HandleAsync(
            Message(trainName: typeof(ICheckGeocodeDriftTrain).FullName!, eventType: "Completed"),
            CancellationToken.None
        );
        await d.HandleAsync(
            Message(trainName: "Other.IFoo", eventType: "Completed"),
            CancellationToken.None
        );
        await d.HandleAsync(
            Message(trainName: typeof(ICheckGeocodeDriftTrain).FullName!, eventType: "Failed"),
            CancellationToken.None
        );
        await d.HandleAsync(
            Message(trainName: "Other.IFoo", eventType: "Failed"),
            CancellationToken.None
        );

        await _client.Received(1).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task Handle_DefaultProjection_HubReceivesTraxClientEvent()
    {
        var d = Create(NewOptions().Build());

        await d.HandleAsync(
            Message(trainName: "X.IY", externalId: "external-abc"),
            CancellationToken.None
        );

        await _client
            .Received(1)
            .TrainEvent(
                Arg.Is<object>(o =>
                    o is TraxClientEvent && ((TraxClientEvent)o).ExternalId == "external-abc"
                )
            );
    }

    [Test]
    public async Task Handle_CustomProjection_HubReceivesProjectedShape()
    {
        var d = Create(
            NewOptions().WithProjection(msg => new MyShape(msg.ExternalId, msg.TrainName)).Build()
        );

        await d.HandleAsync(
            Message(trainName: "T.IT", externalId: "id-xyz"),
            CancellationToken.None
        );

        await _client
            .Received(1)
            .TrainEvent(
                Arg.Is<object>(o =>
                    o is MyShape && ((MyShape)o).Id == "id-xyz" && ((MyShape)o).TrainName == "T.IT"
                )
            );
    }

    [Test]
    public async Task Handle_HubSendThrows_ExceptionSwallowedAndLogged()
    {
        _client.TrainEvent(Arg.Any<object>()).Throws(new InvalidOperationException("boom"));
        var d = Create(NewOptions().Build());

        Func<Task> act = () => d.HandleAsync(Message(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger
            .Entries.Should()
            .Contain(e =>
                e.Level == LogLevel.Error && e.Exception != null && e.Exception.Message == "boom"
            );
    }

    private static Effect.Models.Metadata.Metadata Metadata(
        string externalId = "x",
        string name = "T.IT",
        DateTime? endTime = null
    ) =>
        new()
        {
            ExternalId = externalId,
            Name = name,
            TrainState = Effect.Enums.TrainState.Completed,
            StartTime = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            EndTime = endTime,
        };

    [TestCase("Started")]
    [TestCase("Completed")]
    [TestCase("Failed")]
    [TestCase("Cancelled")]
    [TestCase("StateChanged")]
    public async Task LifecycleMethod_BuildsMessageWithMatchingEventType_AndSendsToHub(
        string eventType
    )
    {
        var d = Create(NewOptions().Build());
        var metadata = Metadata(externalId: $"life-{eventType}");

        switch (eventType)
        {
            case "Started":
                await d.OnStarted(metadata, CancellationToken.None);
                break;
            case "Completed":
                await d.OnCompleted(metadata, CancellationToken.None);
                break;
            case "Failed":
                await d.OnFailed(metadata, new Exception("test"), CancellationToken.None);
                break;
            case "Cancelled":
                await d.OnCancelled(metadata, CancellationToken.None);
                break;
            case "StateChanged":
                await d.OnStateChanged(metadata, CancellationToken.None);
                break;
        }

        await _client
            .Received(1)
            .TrainEvent(
                Arg.Is<object>(o =>
                    o is TraxClientEvent
                    && ((TraxClientEvent)o).EventType == eventType
                    && ((TraxClientEvent)o).ExternalId == $"life-{eventType}"
                )
            );
    }

    [Test]
    public async Task LifecycleMethod_RespectsFilter_NonMatchingEventTypeNotSent()
    {
        var d = Create(NewOptions().OnlyForEvents("Completed").Build());
        var metadata = Metadata();

        await d.OnStarted(metadata, CancellationToken.None);
        await d.OnFailed(metadata, new Exception("x"), CancellationToken.None);
        await d.OnCancelled(metadata, CancellationToken.None);
        await d.OnCompleted(metadata, CancellationToken.None);

        // Only the Completed call should reach the hub.
        await _client.Received(1).TrainEvent(Arg.Any<object>());
        await _client
            .Received(1)
            .TrainEvent(
                Arg.Is<object>(o =>
                    o is TraxClientEvent && ((TraxClientEvent)o).EventType == "Completed"
                )
            );
    }

    [Test]
    public async Task BuildMessage_EndTimeSet_PayloadTimestampMatchesEndTime()
    {
        var endTime = new DateTime(2026, 5, 15, 12, 34, 56, DateTimeKind.Utc);
        var d = Create(NewOptions().Build());
        TraxClientEvent? captured = null;
        await _client.TrainEvent(
            Arg.Do<object>(o =>
            {
                if (o is TraxClientEvent e)
                    captured = e;
            })
        );

        await d.OnCompleted(Metadata(endTime: endTime), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Timestamp.Should().Be(endTime);
    }

    [Test]
    public async Task BuildMessage_EndTimeNull_PayloadTimestampUsesNowFallback()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var d = Create(NewOptions().Build());
        TraxClientEvent? captured = null;
        await _client.TrainEvent(
            Arg.Do<object>(o =>
            {
                if (o is TraxClientEvent e)
                    captured = e;
            })
        );

        await d.OnCompleted(Metadata(endTime: null), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Timestamp.Should().BeOnOrAfter(before);
        captured.Timestamp.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Test]
    public async Task DispatchAsync_NullLogger_HubSendThrows_StillDoesNotThrow()
    {
        _client.TrainEvent(Arg.Any<object>()).Throws(new InvalidOperationException("boom"));
        var dispatcher = new SignalRTrainEventDispatcher(_hub, NewOptions().Build(), logger: null);

        Func<Task> act = () => dispatcher.HandleAsync(Message(), CancellationToken.None);

        // The dispatcher swallows the exception via the null-conditional logger call;
        // no log can be observed, but the contract is "never throw out of the pipeline".
        await act.Should().NotThrowAsync();
        await _client.Received(1).TrainEvent(Arg.Any<object>());
    }

    [Test]
    public async Task OnCompleted_HubSendThrows_DoesNotThrow()
    {
        _client.TrainEvent(Arg.Any<object>()).Throws(new InvalidOperationException("boom"));
        var d = Create(NewOptions().Build());

        var metadata = new Effect.Models.Metadata.Metadata
        {
            ExternalId = "x",
            Name = "T.IT",
            TrainState = Effect.Enums.TrainState.Completed,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
        };

        Func<Task> act = () => d.OnCompleted(metadata, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    private record MyShape(string Id, string TrainName);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } =
            new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
