using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
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
/// </summary>
[TestFixture]
public class RabbitMqBroadcasterSenderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static TrainLifecycleEventMessage Message(string externalId) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "Sender.ITrain",
            TrainState: "InProgress",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Started",
            Executor: null,
            Output: null
        );

    private static RabbitMqTrainEventBroadcaster Broadcaster(
        IConnection connection,
        ILogger<RabbitMqTrainEventBroadcaster>? logger,
        int queueCapacity = RabbitMqTrainEventBroadcaster.DefaultQueueCapacity
    )
    {
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = "amqp://unused/" },
            logger,
            queueCapacity
        );
        Set(broadcaster, "_connection", connection);
        return broadcaster;
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
        var broadcaster = Broadcaster(connection, logger);

        await broadcaster.PublishAsync(Message("m0"), CancellationToken.None);
        // Two failed attempts wait 1s and then 2s, inside the 5s the dispose drain allows.
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

    private static void Set(object target, string field, object value) =>
        typeof(RabbitMqTrainEventBroadcaster)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

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
