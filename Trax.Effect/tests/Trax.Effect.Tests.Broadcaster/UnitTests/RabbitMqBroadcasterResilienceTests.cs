using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// A lifecycle hook awaits the publish inline, so the broker's health must not reach the train:
/// publishing queues the event and returns, and a background sender does the network work.
/// </summary>
[TestFixture]
public class RabbitMqBroadcasterResilienceTests
{
    // TEST-NET-1 (RFC 5737): reserved for documentation, never routed, so a connect to it hangs
    // until the client's own connection timeout rather than being refused.
    private const string BlackholedBroker = "amqp://trax:trax123@192.0.2.1:5672/";

    private static readonly string AmqpUri =
        $"amqp://trax:trax123@localhost:{PortOrDefault(Environment.GetEnvironmentVariable("TRAX_TEST_RABBITMQ_PORT"))}/";

    private static string PortOrDefault(string? port) =>
        string.IsNullOrWhiteSpace(port) ? "5672" : port;

    private static TrainLifecycleEventMessage Message(string externalId = "ext") =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "Resilience.ITrain",
            TrainState: "InProgress",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Started",
            Executor: null,
            Output: null
        );

    [Test]
    public async Task PublishAsync_ToAnUnreachableBroker_ReturnsWithoutWaitingOnTheNetwork()
    {
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = BlackholedBroker },
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );

        // measuring-interval: the stopwatch measures how long the publishing train is held up.
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(
            broadcaster.PublishAsync(Message("a"), CancellationToken.None),
            broadcaster.PublishAsync(Message("b"), CancellationToken.None),
            broadcaster.PublishAsync(Message("c"), CancellationToken.None)
        );
        clock.Stop();

        clock
            .Elapsed.Should()
            .BeLessThan(
                TimeSpan.FromSeconds(1),
                "a train must not wait on a broker it cannot reach"
            );
    }

    [Test]
    public async Task DisposeAsync_WithAnUnreachableBroker_GivesUpWithinItsDrainBound()
    {
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = BlackholedBroker },
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await broadcaster.PublishAsync(Message(), CancellationToken.None);

        // measuring-interval: the stopwatch measures how long shutdown is held up.
        var clock = Stopwatch.StartNew();
        await broadcaster.DisposeAsync();
        clock.Stop();

        clock
            .Elapsed.Should()
            .BeLessThan(
                RabbitMqTrainEventBroadcaster.DisposeDrainTimeout + TimeSpan.FromSeconds(2)
            );
    }

    [Test]
    public async Task PublishAsync_WhenTheQueueIsFull_DropsAndCountsInsteadOfWaiting()
    {
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = BlackholedBroker },
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance,
            queueCapacity: 2
        );

        for (var i = 0; i < 5; i++)
            await broadcaster.PublishAsync(Message($"m{i}"), CancellationToken.None);

        // Two fit in the queue. The sender may already hold the first while it tries to connect,
        // which frees one more slot.
        broadcaster.DroppedEvents.Should().BeInRange(2, 3);
    }

    [Test]
    public async Task PublishAsync_AfterTheConnectionClosed_DisposesTheConnectionItReplaces()
    {
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions
            {
                ConnectionString = AmqpUri,
                ExchangeName = $"trax.test.replace.{Guid.NewGuid():N}",
            },
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );

        // A connection the broker has closed (a broker restart) and its channel.
        var closedConnection = Substitute.For<IConnection>();
        closedConnection.IsOpen.Returns(false);
        var closedChannel = Substitute.For<IChannel>();
        closedChannel.IsOpen.Returns(false);
        Set(broadcaster, "_connection", closedConnection);
        Set(broadcaster, "_channel", closedChannel);

        await broadcaster.PublishAsync(Message(), CancellationToken.None);
        await broadcaster.DisposeAsync(); // drains the queue, so the publish has been sent

        closedConnection.Received(1).Dispose();
        closedChannel.Received(1).Dispose();
        broadcaster.DroppedEvents.Should().Be(0);
    }

    [Test]
    public async Task PublishedEvents_ReachTheReceiver_InOrder()
    {
        var options = new RabbitMqBroadcasterOptions
        {
            ConnectionString = AmqpUri,
            ExchangeName = $"trax.test.order.{Guid.NewGuid():N}",
        };
        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        var received = new List<string>();
        var all = new TaskCompletionSource();
        await receiver.StartAsync(
            (m, _) =>
            {
                lock (received)
                {
                    received.Add(m.ExternalId);
                    if (received.Count == 20)
                        all.TrySetResult();
                }
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        await using (
            var broadcaster = new RabbitMqTrainEventBroadcaster(
                options,
                NullLogger<RabbitMqTrainEventBroadcaster>.Instance
            )
        )
        {
            for (var i = 0; i < 20; i++)
                await broadcaster.PublishAsync(Message($"m{i}"), CancellationToken.None);
        }

        // determinism: the wait is on the TaskCompletionSource; the delay is only the ceiling.
        (await Task.WhenAny(all.Task, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should()
            .Be(all.Task);
        received.Should().Equal(Enumerable.Range(0, 20).Select(i => $"m{i}"));

        await receiver.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task AnExchangeDeclaredWithAnotherType_IsRefused_AndTheEventDroppedAfterItsAttempts()
    {
        var exchange = $"trax.test.mismatch.{Guid.NewGuid():N}";
        var factory = new ConnectionFactory { Uri = new Uri(AmqpUri) };
        await using var setup = await factory.CreateConnectionAsync();
        await using (var channel = await setup.CreateChannelAsync())
        {
            await channel.ExchangeDeclareAsync(
                exchange,
                ExchangeType.Direct,
                durable: false,
                autoDelete: false
            );
        }

        var logger = new ErrorSignal();
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = AmqpUri, ExchangeName = exchange },
            logger
        );
        try
        {
            await broadcaster.PublishAsync(Message("mismatch"), CancellationToken.None);

            // determinism: the wait is on the logged refusal; the timeout is only the ceiling.
            await logger.Logged.WaitAsync(TimeSpan.FromSeconds(15));
            broadcaster.RefusedEvents.Should().Be(1);
        }
        finally
        {
            await broadcaster.DisposeAsync();
            await using var cleanup = await setup.CreateChannelAsync();
            await cleanup.ExchangeDeleteAsync(exchange);
        }
    }

    /// <summary>Completes <see cref="Logged"/> at the first error logged.</summary>
    private sealed class ErrorSignal : ILogger<RabbitMqTrainEventBroadcaster>
    {
        private readonly TaskCompletionSource _logged = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Logged => _logged.Task;

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
            if (logLevel == LogLevel.Error)
                _logged.TrySetResult();
        }
    }

    private static void Set(object target, string field, object value) =>
        typeof(RabbitMqTrainEventBroadcaster)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);
}
