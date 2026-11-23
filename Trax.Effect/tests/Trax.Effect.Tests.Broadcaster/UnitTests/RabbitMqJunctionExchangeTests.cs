using System.Collections.Concurrent;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Trax.Core.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// Junction events go to an exchange of their own, so a receiver bound only to the train exchange,
/// as one from before junction events is, never receives one, while a current receiver gets both.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
[TestFixture]
public class RabbitMqJunctionExchangeTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly string AmqpUri =
        $"amqp://trax:trax123@localhost:{PortOrDefault(Environment.GetEnvironmentVariable("TRAX_TEST_RABBITMQ_PORT"))}/";

    private static string PortOrDefault(string? port) =>
        string.IsNullOrWhiteSpace(port) ? "5672" : port;

    private static TrainLifecycleEventMessage Message(string eventType, bool junction) =>
        new(
            MetadataId: 1,
            ExternalId: eventType,
            TrainName: "Exchange.ITrain",
            TrainState: "InProgress",
            Timestamp: TestTime.UtcDateTime,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        )
        {
            Junction = junction
                ? new JunctionEventPayload(
                    0,
                    JunctionRunKind.Junction,
                    "Ship",
                    JunctionRunState.InProgress,
                    TestTime.UtcDateTime
                )
                : null,
        };

    [Test]
    public async Task A_receiver_bound_only_to_the_train_exchange_never_receives_a_junction_event()
    {
        var options = NewOptions();

        // What a receiver from before junction events does: one queue, bound to the train exchange.
        var connection = await ConnectOrIgnoreAsync();

        var _ = connection;
        await using var _Disposal = _.ConfigureAwait(false);
        var channel = await connection.CreateChannelAsync(
            cancellationToken: CancellationToken.None
        );
        await using var channelDisposal = channel.ConfigureAwait(false);
        var queue = await BindLegacyQueueAsync(channel, options.ExchangeName);
        var legacy = new ConcurrentQueue<TrainLifecycleEventMessage>();
        var legacyGotTrainEvent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await ConsumeLegacyAsync(channel, queue, legacy, legacyGotTrainEvent);

        var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        await using var receiverDisposal = receiver.ConfigureAwait(false);
        var current = new ConcurrentQueue<string>();
        var currentGotBoth = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await StartCurrentAsync(receiver, current, currentGotBoth);

        var broadcaster = new RabbitMqTrainEventBroadcaster(
            options,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await using (broadcaster.ConfigureAwait(false))
        {
            await PublishBothAndWaitAsync(broadcaster, currentGotBoth, legacyGotTrainEvent);
        }

        current.Should().BeEquivalentTo(["JunctionStarted", "Completed"]);
        legacy
            .Select(m => m.EventType)
            .Should()
            .Equal(
                ["Completed"],
                $"a receiver that predates junction events never receives one. See {Adr}."
            );

        await receiver.StopAsync(CancellationToken.None);
    }

    private static async Task<IConnection> ConnectOrIgnoreAsync()
    {
        try
        {
            return await new ConnectionFactory { Uri = new Uri(AmqpUri) }
                .CreateConnectionAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"RabbitMQ not reachable at {AmqpUri}: {ex.Message}");
            throw;
        }
    }

    // What a receiver from before junction events binds: one queue, on the train exchange.
    private static async Task<string> BindLegacyQueueAsync(IChannel channel, string exchangeName)
    {
        await channel
            .ExchangeDeclareAsync(
                exchangeName,
                ExchangeType.Fanout,
                durable: true,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        var queue = (
            await channel
                .QueueDeclareAsync(
                    string.Empty,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false)
        ).QueueName;
        await channel
            .QueueBindAsync(
                queue,
                exchangeName,
                string.Empty,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        return queue;
    }

    private static async Task ConsumeLegacyAsync(
        IChannel channel,
        string queue,
        ConcurrentQueue<TrainLifecycleEventMessage> legacy,
        TaskCompletionSource legacyGotTrainEvent
    )
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            var message = JsonSerializer
                .Deserialize<TrainLifecycleEventMessage>(ea.Body.Span)
                .NotNull();
            legacy.Enqueue(message);
            if (string.Equals(message.EventType, "Completed", StringComparison.Ordinal))
                legacyGotTrainEvent.TrySetResult();
            return Task.CompletedTask;
        };
        await channel
            .BasicConsumeAsync(
                queue,
                autoAck: true,
                consumer,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    private static Task StartCurrentAsync(
        RabbitMqTrainEventReceiver receiver,
        ConcurrentQueue<string> current,
        TaskCompletionSource currentGotBoth
    ) =>
        receiver.StartAsync(
            (message, _) =>
            {
                current.Enqueue(message.EventType);
                if (current.Count == 2)
                    currentGotBoth.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

    private static async Task PublishBothAndWaitAsync(
        RabbitMqTrainEventBroadcaster broadcaster,
        TaskCompletionSource currentGotBoth,
        TaskCompletionSource legacyGotTrainEvent
    )
    {
        // In order, each confirmed by the broker before the next is sent.
        await broadcaster
            .PublishAsync(Message("JunctionStarted", junction: true), CancellationToken.None)
            .ConfigureAwait(false);
        await broadcaster
            .PublishAsync(Message("Completed", junction: false), CancellationToken.None)
            .ConfigureAwait(false);
        await currentGotBoth
            .Task.WaitAsync(Timeout, TimeProvider.System, CancellationToken.None)
            .ConfigureAwait(false);
        await legacyGotTrainEvent
            .Task.WaitAsync(Timeout, TimeProvider.System, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static RabbitMqBroadcasterOptions NewOptions() =>
        new()
        {
            ConnectionString = AmqpUri,
            SigningKey = TestBroadcastKey.Value,
            ExchangeName = $"trax.test.junctions.{Guid.NewGuid():N}",
        };

    private static async Task<IConnection> Connect()
    {
        try
        {
            return await new ConnectionFactory { Uri = new Uri(AmqpUri) }
                .CreateConnectionAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"RabbitMQ not reachable at {AmqpUri}: {ex.Message}");
            throw;
        }
    }

    private static async Task Publish(
        IChannel channel,
        string exchange,
        TrainLifecycleEventMessage message
    )
    {
        await channel
            .ExchangeDeclareAsync(
                exchange,
                ExchangeType.Fanout,
                durable: true,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        // Signed for the exchange it is published to, so only the exchange check can drop it.
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        await channel
            .BasicPublishAsync(
                exchange,
                string.Empty,
                mandatory: false,
                new BasicProperties
                {
                    Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [BroadcastMessageSignature.Header] = BroadcastMessageSignature.Create(
                            TestBroadcastKey.Value,
                            exchange,
                            body,
                            TimeProvider.System.GetUtcNow()
                        ),
                    },
                },
                body,
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_receiver_takes_each_kind_of_event_only_from_its_own_exchange()
    {
        var options = NewOptions();
        var connection = await Connect();
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        await using var receiverDisposal = receiver.ConfigureAwait(false);
        var received = new ConcurrentQueue<string>();
        var gotBoth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await receiver.StartAsync(
            (message, _) =>
            {
                received.Enqueue(message.ExternalId);
                if (received.Count >= 2)
                    gotBoth.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        var channel = await connection.CreateChannelAsync(
            cancellationToken: CancellationToken.None
        );
        await using var channelDisposal = channel.ConfigureAwait(false);
        await PublishMisplacedThenPlacedAsync(channel, options);
        await gotBoth.Task.WaitAsync(Timeout, TimeProvider.System, CancellationToken.None);

        received
            .Should()
            .BeEquivalentTo(
                ["train", "step"],
                $"an event from an exchange that is not its own is dropped. See {Adr}."
            );
        await receiver.StopAsync(CancellationToken.None);
    }

    private static async Task PublishMisplacedThenPlacedAsync(
        IChannel channel,
        RabbitMqBroadcasterOptions options
    )
    {
        // Each published where it does not belong, then each where it does, in that order.
        await Publish(
                channel,
                options.ExchangeName,
                Message("JunctionStarted", junction: true) with
                {
                    ExternalId = "misplaced-step",
                }
            )
            .ConfigureAwait(false);
        await Publish(
                channel,
                options.EffectiveJunctionExchangeName,
                Message("Completed", junction: false) with
                {
                    ExternalId = "misplaced-train",
                }
            )
            .ConfigureAwait(false);
        await Publish(
                channel,
                options.ExchangeName,
                Message("Completed", junction: false) with
                {
                    ExternalId = "train",
                }
            )
            .ConfigureAwait(false);
        await Publish(
                channel,
                options.EffectiveJunctionExchangeName,
                Message("JunctionStarted", junction: true) with
                {
                    ExternalId = "step",
                }
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task A_junction_exchange_the_broker_refuses_does_not_stop_train_events()
    {
        var options = NewOptions();
        var connection = await Connect();
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var setup = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        await using (setup.ConfigureAwait(false))
        {
            // Declared elsewhere with another type, so declaring it as a fanout is refused.
            await setup.ExchangeDeclareAsync(
                options.EffectiveJunctionExchangeName,
                ExchangeType.Direct,
                durable: false,
                autoDelete: true,
                cancellationToken: CancellationToken.None
            );
        }

        var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        await using var receiverDisposal = receiver.ConfigureAwait(false);
        var received = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await receiver.StartAsync(
            (message, _) =>
            {
                received.TrySetResult(message.ExternalId);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        var broadcaster = new RabbitMqTrainEventBroadcaster(
            options,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await using var broadcasterDisposal = broadcaster.ConfigureAwait(false);
        await broadcaster.PublishAsync(
            Message("JunctionStarted", junction: true),
            CancellationToken.None
        );
        await broadcaster.PublishAsync(
            Message("Completed", junction: false),
            CancellationToken.None
        );

        (await received.Task.WaitAsync(Timeout, TimeProvider.System, CancellationToken.None))
            .Should()
            .Be(
                "Completed",
                $"train events keep flowing when the junction exchange is refused. See {Adr}."
            );
        await receiver.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task The_junction_exchange_is_declared_only_where_junction_events_are_used()
    {
        var options = NewOptions();
        var connection = await Connect();
        await using var connectionDisposal = connection.ConfigureAwait(false);
        var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        )
        {
            BindJunctionExchange = false,
        };
        await using (receiver.ConfigureAwait(false))
        {
            var received = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await receiver.StartAsync(
                (_, _) =>
                {
                    received.TrySetResult();
                    return Task.CompletedTask;
                },
                CancellationToken.None
            );

            var broadcaster = new RabbitMqTrainEventBroadcaster(
                options,
                NullLogger<RabbitMqTrainEventBroadcaster>.Instance
            );
            await using (broadcaster.ConfigureAwait(false))
            {
                await broadcaster.PublishAsync(
                    Message("Completed", junction: false),
                    CancellationToken.None
                );
                await received.Task.WaitAsync(Timeout, TimeProvider.System, CancellationToken.None);
            }

            await receiver.StopAsync(CancellationToken.None);
        }

        var probe = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        await using var probeDisposal = probe.ConfigureAwait(false);
        var passive = () =>
            probe.ExchangeDeclarePassiveAsync(
                options.EffectiveJunctionExchangeName,
                CancellationToken.None
            );
        (
            await passive
                .Should()
                .ThrowAsync<RabbitMQ.Client.Exceptions.OperationInterruptedException>()
        )
            .Which.ShutdownReason.NotNull()
            .ReplyCode.Should()
            .Be(
                (ushort)404,
                $"no host without junction events declares their exchange. See {Adr}."
            );
    }
}
