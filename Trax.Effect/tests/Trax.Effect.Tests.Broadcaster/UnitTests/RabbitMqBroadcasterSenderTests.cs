using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// The background sender of <see cref="RabbitMqTrainEventBroadcaster"/> against a connection the test
/// controls: how it retries a broker it cannot reach, reports dropped events once the queue drains,
/// gives up on shutdown, and closes a channel the broker already closed.
///
/// <para>Its junction-exchange backoff test enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md:
/// a failing junction exchange must not cost train events.</para>
/// </summary>
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
[TestFixture]
public class RabbitMqBroadcasterSenderTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static TrainLifecycleEventMessage Message(
        string externalId,
        string eventType = "Started"
    ) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "Sender.ITrain",
            TrainState: "InProgress",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        );

    private static RabbitMqTrainEventBroadcaster Broadcaster(
        IConnection connection,
        ILogger<RabbitMqTrainEventBroadcaster>? logger,
        int queueCapacity = RabbitMqTrainEventBroadcaster.DefaultQueueCapacity,
        Func<CancellationToken, Task<IConnection>>? connect = null,
        TimeSpan? publishTimeout = null,
        TimeSpan? firstRetryDelay = null
    )
    {
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = "amqp://unused/" },
            logger,
            queueCapacity,
            connect,
            publishTimeout,
            firstRetryDelay
        );
        Set(broadcaster, "_connection", connection);
        return broadcaster;
    }

    /// <summary>
    /// Records every event published on <paramref name="channel"/>, in order. <paramref name="refuse"/>
    /// decides, per event, whether the publish throws instead; a throwing publish closes the channel,
    /// as the broker's channel-close would.
    /// </summary>
    private static Published Records(
        IChannel channel,
        Published? into = null,
        Func<TrainLifecycleEventMessage, Exception?>? refuse = null
    )
    {
        var published = into ?? new Published();
        channel
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var message = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(
                    call.ArgAt<ReadOnlyMemory<byte>>(4).Span
                )!;
                var refusal = refuse?.Invoke(message);
                published.Attempt(message);
                if (refusal is not null)
                {
                    channel.IsOpen.Returns(false);
                    return ValueTask.FromException(refusal);
                }

                published.Add(message);
                return ValueTask.CompletedTask;
            });
        return published;
    }

    private static IConnection OpenConnection()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);
        return connection;
    }

    private static IChannel OpenChannel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        return channel;
    }

    private static void ChannelsFrom(IConnection connection, params Func<Task<IChannel>>[] attempts)
    {
        var calls = 0;
        connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => attempts[Math.Min(calls++, attempts.Length - 1)]());
    }

    private static AlreadyClosedException ClosedByPeer() =>
        new(new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "CONNECTION_FORCED"));

    [Test]
    public async Task Sender_RetriesAfterFailedAttempts_AndReportsWhenItReachesTheBrokerAgain()
    {
        var logger = new CapturingLogger();
        var connection = OpenConnection();
        var channel = OpenChannel();
        ChannelsFrom(
            connection,
            () => throw new BrokerUnreachableException(new IOException("first")),
            () => throw new BrokerUnreachableException(new IOException("second")),
            () => Task.FromResult(channel)
        );
        var published = Records(channel);
        var broadcaster = Broadcaster(connection, logger);

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        // Two failed attempts wait 1s and then 2s before the third reaches the broker.
        await published.Reaches(1).WaitAsync(Timeout);
        await broadcaster.DisposeAsync();

        await channel
            .Received(1)
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            );
        logger
            .Entries(LogLevel.Warning)
            .Should()
            .ContainSingle("an outage is reported once, not once per attempt")
            .Which.Should()
            .Contain("cannot publish");
        logger.Entries(LogLevel.Debug).Should().Contain(e => e.Contains("attempt failed"));
        logger
            .Entries(LogLevel.Information)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("after 2 failed attempts");
    }

    [Test]
    public async Task Sender_AfterDroppingEvents_ReportsHowManyOnceTheQueueDrains()
    {
        var logger = new CapturingLogger();
        var connection = OpenConnection();
        var held = new Gate();
        ChannelsFrom(connection, () => held.Pass(OpenChannel()));
        var broadcaster = Broadcaster(connection, logger, queueCapacity: 1);

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await held.Entered.WaitAsync(Timeout); // the sender holds m0, so the queue is empty
        await broadcaster.PublishAsync(Message("m1"), CancellationToken.None); // fills the queue
        await broadcaster.PublishAsync(Message("m2"), CancellationToken.None); // dropped

        broadcaster.DroppedEvents.Should().Be(1);
        logger.Entries(LogLevel.Warning).Should().ContainSingle(e => e.Contains("queue is full"));

        held.Release();
        await broadcaster.DisposeAsync();

        logger
            .Entries(LogLevel.Warning)
            .Should()
            .ContainSingle(e => e.Contains("drained after dropping 1 events"));
    }

    [Test]
    public async Task PublishAsync_WithoutALogger_StillDropsAndCountsAFullQueue()
    {
        var connection = OpenConnection();
        var held = new Gate();
        ChannelsFrom(connection, () => held.Pass(OpenChannel()));
        var broadcaster = Broadcaster(connection, logger: null, queueCapacity: 1);

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await held.Entered.WaitAsync(Timeout);
        await broadcaster.PublishAsync(Message("m1"), CancellationToken.None);
        var publish = () => broadcaster.PublishAsync(Message("m2"), CancellationToken.None);

        await publish.Should().NotThrowAsync();
        broadcaster.DroppedEvents.Should().Be(1);

        held.Release();
        await broadcaster.DisposeAsync();
    }

    [Test]
    public async Task PublishAsync_AfterDisposal_IsNotCountedAsADrop()
    {
        var logger = new CapturingLogger();
        var broadcaster = Broadcaster(OpenConnection(), logger);
        await broadcaster.DisposeAsync();

        await broadcaster.PublishAsync(Message("late"), CancellationToken.None);

        broadcaster.DroppedEvents.Should().Be(0);
        logger.Entries(LogLevel.Warning).Should().BeEmpty();
    }

    [Test]
    public async Task DisposeAsync_WhenTheSenderIsStuck_AbandonsTheQueueAndReportsWhatWasNotSent()
    {
        var logger = new CapturingLogger();
        var connection = OpenConnection();
        var held = new Gate();
        connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call => held.PassWhenCancelled(call.ArgAt<CancellationToken>(1)));
        var broadcaster = Broadcaster(connection, logger);

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await held.Entered.WaitAsync(Timeout);
        await broadcaster.PublishAsync(Message("m1"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("m2"), CancellationToken.None);

        // measuring-interval: the stopwatch measures how long shutdown is held up.
        var clock = Stopwatch.StartNew();
        await broadcaster.DisposeAsync();
        clock.Stop();

        clock
            .Elapsed.Should()
            .BeLessThan(
                RabbitMqTrainEventBroadcaster.DisposeDrainTimeout + TimeSpan.FromSeconds(2)
            );
        logger
            .Entries(LogLevel.Warning)
            .Should()
            .ContainSingle(e => e.Contains("stopped before its queue drained"))
            .Which.Should()
            .Contain("2 events were not sent");
    }

    [Test]
    public async Task DisposeAsync_WhenTheBrokerAlreadyClosedTheChannelAndConnection_StillDisposesBoth()
    {
        var connection = OpenConnection();
        connection
            .CloseAsync(
                Arg.Any<ushort>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());
        var channel = OpenChannel();
        channel
            .CloseAsync(
                Arg.Any<ushort>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());
        var broadcaster = Broadcaster(connection, new CapturingLogger());
        Set(broadcaster, "_channel", channel);

        var dispose = async () => await broadcaster.DisposeAsync();

        await dispose.Should().NotThrowAsync();
        channel.Received(1).Dispose();
        connection.Received(1).Dispose();
    }

    [Test]
    public async Task Sender_OpensItsChannelWithPublisherConfirmsTracked()
    {
        var connection = OpenConnection();
        var channel = OpenChannel();
        ChannelsFrom(connection, () => Task.FromResult(channel));
        var published = Records(channel);
        var broadcaster = Broadcaster(connection, new CapturingLogger());

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await published.Reaches(1).WaitAsync(Timeout);
        await broadcaster.DisposeAsync();

        await connection
            .Received(1)
            .CreateChannelAsync(
                Arg.Is<CreateChannelOptions?>(o =>
                    o != null
                    && o.PublisherConfirmationsEnabled
                    && o.PublisherConfirmationTrackingEnabled
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Sender_WhenAPublishIsNotConfirmedInTime_RetriesTheEventOnANewConnection()
    {
        // The first connection's socket is dead but still reports open: the publish waits for a
        // confirm that never comes.
        var stale = OpenConnection();
        var staleChannel = OpenChannel();
        ChannelsFrom(stale, () => Task.FromResult(staleChannel));
        staleChannel
            .BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ValueTask(
                // negative-wait: never completes on its own; only the publish bound ends it.
                Task.Delay(System.Threading.Timeout.Infinite, call.ArgAt<CancellationToken>(5))
            ));

        var fresh = OpenConnection();
        var freshChannel = OpenChannel();
        ChannelsFrom(fresh, () => Task.FromResult(freshChannel));
        var published = Records(freshChannel);

        var broadcaster = Broadcaster(
            stale,
            new CapturingLogger(),
            connect: _ => Task.FromResult(fresh),
            publishTimeout: TimeSpan.FromMilliseconds(200),
            firstRetryDelay: TimeSpan.FromMilliseconds(10)
        );

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);

        await published.Reaches(1).WaitAsync(Timeout);
        published.ExternalIds.Should().Equal("m0");
        stale.Received(1).Dispose();
        await broadcaster.DisposeAsync();
    }

    [Test]
    public async Task AJunctionExchangeThatFails_IsNotTriedAgainUntilItsBackoffPasses()
    {
        var connection = OpenConnection();
        var opened = 0;
        var refusing = OpenChannel();
        refusing
            .ExchangeDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());
        var trains = OpenChannel();
        var published = Records(trains);
        connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Task.FromResult(Interlocked.Increment(ref opened) == 2 ? trains : refusing)
            );
        var now = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var broadcaster = Broadcaster(connection, new CapturingLogger());
        broadcaster.UtcNow = () => now;

        for (var i = 0; i < 5; i++)
            await broadcaster.PublishAsync(Junction($"j{i}"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("train"), CancellationToken.None);
        await published.Reaches(1).WaitAsync(Timeout);

        opened
            .Should()
            .Be(
                2,
                $"one junction attempt, then the train channel; the rest were dropped. See {Adr}."
            );
        broadcaster
            .JunctionUnavailableUntil.Should()
            .Be(
                now + RabbitMqTrainEventBroadcaster.FirstJunctionBackoff,
                $"a failing junction exchange waits out its backoff. See {Adr}."
            );

        // Past the backoff, a junction event is tried again, and a second failure doubles it.
        now += RabbitMqTrainEventBroadcaster.FirstJunctionBackoff;
        await broadcaster.PublishAsync(Junction("again"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("train-2"), CancellationToken.None);
        await published.Reaches(2).WaitAsync(Timeout);

        opened.Should().Be(3, $"past the backoff a junction event is tried again. See {Adr}.");
        broadcaster
            .JunctionUnavailableUntil.Should()
            .Be(
                now + RabbitMqTrainEventBroadcaster.FirstJunctionBackoff * 2,
                $"a second failure doubles the backoff. See {Adr}."
            );
        published
            .ExternalIds.Should()
            .Equal(new[] { "train", "train-2" }, $"train events keep flowing. See {Adr}.");
        await broadcaster.DisposeAsync();
    }

    private static TrainLifecycleEventMessage Junction(string externalId) =>
        Message(externalId, "JunctionStarted") with
        {
            Junction = new JunctionEventPayload(
                0,
                Trax.Effect.Enums.JunctionRunKind.Junction,
                "Ship",
                Trax.Effect.Enums.JunctionRunState.InProgress,
                DateTime.UtcNow
            ),
        };

    [Test]
    public async Task Sender_DeclaresTheExchangeOnEveryChannelItOpens()
    {
        var connection = OpenConnection();
        var first = OpenChannel();
        var second = OpenChannel();
        ChannelsFrom(connection, () => Task.FromResult(first), () => Task.FromResult(second));
        var published = Records(first, refuse: _ => ClosedByPeer());
        Records(second, into: published);
        var broadcaster = Broadcaster(
            connection,
            new CapturingLogger(),
            firstRetryDelay: TimeSpan.FromMilliseconds(10)
        );

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await published.Reaches(1).WaitAsync(Timeout);
        await broadcaster.DisposeAsync();

        await second
            .Received(1)
            .ExchangeDeclareAsync(
                "trax.lifecycle",
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        await second
            .DidNotReceive()
            .ExchangeDeclareAsync(
                "trax.lifecycle.junctions",
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Sender_WhenTheBrokerRefusesAnEvent_GivesUpOnItAfterItsAttempts_AndSendsTheEventsBehindIt()
    {
        var logger = new CapturingLogger();
        var connection = OpenConnection();
        var published = new Published();
        connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var channel = OpenChannel();
                Records(
                    channel,
                    into: published,
                    refuse: m =>
                        m.ExternalId == "refused"
                            ? new OperationInterruptedException(
                                new ShutdownEventArgs(
                                    ShutdownInitiator.Peer,
                                    406,
                                    "PRECONDITION_FAILED - inequivalent arg 'type' for exchange"
                                )
                            )
                            : null
                );
                return Task.FromResult(channel);
            });
        var broadcaster = Broadcaster(
            connection,
            logger,
            firstRetryDelay: TimeSpan.FromMilliseconds(10)
        );

        await broadcaster.PublishAsync(Message("refused"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("behind"), CancellationToken.None);

        await published.Reaches(1).WaitAsync(Timeout);
        published.ExternalIds.Should().Equal("behind");
        published
            .AttemptsFor("refused")
            .Should()
            .Be(3, "an event the broker refuses is tried a bounded number of times");
        logger.Entries(LogLevel.Error).Should().ContainSingle(e => e.Contains("refused"));
        await broadcaster.DisposeAsync();
    }

    [Test]
    public async Task AFullQueue_KeepsATerminalEvent_ByDroppingAStartedEventInstead()
    {
        var connection = OpenConnection();
        var channel = OpenChannel();
        var held = new Gate();
        ChannelsFrom(connection, () => held.Pass(channel));
        var published = Records(channel);
        var broadcaster = Broadcaster(connection, new CapturingLogger(), queueCapacity: 2);

        await broadcaster.PublishAsync(Message("held"), CancellationToken.None);
        await held.Entered.WaitAsync(Timeout); // the sender holds this one; the queue is empty
        await broadcaster.PublishAsync(Message("a"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("b"), CancellationToken.None); // the queue is full
        await broadcaster.PublishAsync(Message("a", "Completed"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("c"), CancellationToken.None);

        held.Release();
        await published.Reaches(3).WaitAsync(Timeout);
        await broadcaster.DisposeAsync();

        published
            .Events.Should()
            .Equal(
                [("held", "Started"), ("b", "Started"), ("a", "Completed")],
                "a full queue gives up a Started event before a run's terminal one"
            );
        broadcaster.DroppedEvents.Should().Be(2);
    }

    [Test]
    public async Task AFullQueue_GivesUpJunctionEventsBeforeAnyTrainEvent()
    {
        var connection = OpenConnection();
        var channel = OpenChannel();
        var held = new Gate();
        ChannelsFrom(connection, () => held.Pass(channel));
        var published = Records(channel);
        var broadcaster = Broadcaster(connection, new CapturingLogger(), queueCapacity: 2);

        await broadcaster.PublishAsync(Message("held"), CancellationToken.None);
        await held.Entered.WaitAsync(Timeout); // the sender holds this one; the queue is empty
        await broadcaster.PublishAsync(Message("j1", "JunctionStarted"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("j2", "Decided"), CancellationToken.None); // full
        await broadcaster.PublishAsync(Message("s"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("j3", "JunctionCompleted"), CancellationToken.None);
        await broadcaster.PublishAsync(Message("s", "Completed"), CancellationToken.None);

        held.Release();
        await published.Reaches(3).WaitAsync(Timeout);
        await broadcaster.DisposeAsync();

        published
            .Events.Should()
            .Equal(
                [("held", "Started"), ("s", "Started"), ("s", "Completed")],
                "a full queue gives up a run's steps before any run's own events"
            );
        broadcaster.DroppedEvents.Should().Be(3);
    }

    [Test]
    public async Task DisposeAsync_WhileTheBrokerIsUnreachable_DoesNotWaitOutTheDrain()
    {
        var connection = OpenConnection();
        var attempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                attempted.TrySetResult();
                throw new BrokerUnreachableException(new IOException("refused"));
            });
        var broadcaster = Broadcaster(connection, new CapturingLogger());

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        await attempted.Task.WaitAsync(Timeout);

        // measuring-interval: the stopwatch measures how long shutdown is held up.
        var clock = Stopwatch.StartNew();
        await broadcaster.DisposeAsync();
        clock.Stop();

        clock
            .Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromSeconds(2),
                "shutdown does not wait on a broker the sender already cannot reach"
            );
    }

    private static void Set(object target, string field, object value) =>
        typeof(RabbitMqTrainEventBroadcaster)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    /// <summary>What the broker was handed, and how often each event was tried.</summary>
    private sealed class Published
    {
        private readonly List<TrainLifecycleEventMessage> _messages = [];
        private readonly Dictionary<string, int> _attempts = [];
        private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];

        public IReadOnlyList<string> ExternalIds
        {
            get
            {
                lock (_messages)
                    return _messages.Select(m => m.ExternalId).ToList();
            }
        }

        public IReadOnlyList<(string, string)> Events
        {
            get
            {
                lock (_messages)
                    return _messages.Select(m => (m.ExternalId, m.EventType)).ToList();
            }
        }

        public int AttemptsFor(string externalId)
        {
            lock (_messages)
                return _attempts.GetValueOrDefault(externalId);
        }

        public void Attempt(TrainLifecycleEventMessage message)
        {
            lock (_messages)
                _attempts[message.ExternalId] = _attempts.GetValueOrDefault(message.ExternalId) + 1;
        }

        public void Add(TrainLifecycleEventMessage message)
        {
            lock (_messages)
            {
                _messages.Add(message);
                foreach (var (count, signal) in _waiters)
                    if (_messages.Count >= count)
                        signal.TrySetResult();
            }
        }

        /// <summary>Completes once at least <paramref name="count"/> events were published.</summary>
        public Task Reaches(int count)
        {
            var signal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (_messages)
            {
                if (_messages.Count >= count)
                    signal.TrySetResult();
                else
                    _waiters.Add((count, signal));
            }
            return signal.Task;
        }
    }

    /// <summary>Holds the sender inside its channel open until the test lets it through.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        public async Task<IChannel> Pass(IChannel channel)
        {
            _entered.TrySetResult();
            await _released.Task;
            return channel;
        }

        public async Task<IChannel> PassWhenCancelled(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            // negative-wait: never completes on its own; only the dispose's cancellation ends it.
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class CapturingLogger : ILogger<RabbitMqTrainEventBroadcaster>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<string> Entries(LogLevel level)
        {
            lock (_entries)
                return _entries.Where(e => e.Level == level).Select(e => e.Message).ToList();
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
