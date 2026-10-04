using System.Collections.Concurrent;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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
            Timestamp: DateTime.UtcNow,
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
                    DateTime.UtcNow
                )
                : null,
        };

    [Test]
    public async Task A_receiver_bound_only_to_the_train_exchange_never_receives_a_junction_event()
    {
        var options = new RabbitMqBroadcasterOptions
        {
            ConnectionString = AmqpUri,
            ExchangeName = $"trax.test.junctions.{Guid.NewGuid():N}",
        };

        // What a receiver from before junction events does: one queue, bound to the train exchange.
        IConnection connection;
        try
        {
            connection = await new ConnectionFactory
            {
                Uri = new Uri(AmqpUri),
            }.CreateConnectionAsync();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"RabbitMQ not reachable at {AmqpUri}: {ex.Message}");
            return;
        }

        await using var _ = connection;
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            options.ExchangeName,
            ExchangeType.Fanout,
            durable: true
        );
        var queue = (await channel.QueueDeclareAsync(string.Empty, false, true, true)).QueueName;
        await channel.QueueBindAsync(queue, options.ExchangeName, string.Empty);
        var legacy = new ConcurrentQueue<TrainLifecycleEventMessage>();
        var legacyGotTrainEvent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            var message = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(ea.Body.Span)!;
            legacy.Enqueue(message);
            if (message.EventType == "Completed")
                legacyGotTrainEvent.TrySetResult();
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue, autoAck: true, consumer);

        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        var current = new ConcurrentQueue<string>();
        var currentGotBoth = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await receiver.StartAsync(
            (message, _) =>
            {
                current.Enqueue(message.EventType);
                if (current.Count == 2)
                    currentGotBoth.TrySetResult();
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
            // In order, each confirmed by the broker before the next is sent.
            await broadcaster.PublishAsync(
                Message("JunctionStarted", true),
                CancellationToken.None
            );
            await broadcaster.PublishAsync(Message("Completed", false), CancellationToken.None);
            await currentGotBoth.Task.WaitAsync(Timeout);
            await legacyGotTrainEvent.Task.WaitAsync(Timeout);
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

    private static RabbitMqBroadcasterOptions NewOptions() =>
        new()
        {
            ConnectionString = AmqpUri,
            ExchangeName = $"trax.test.junctions.{Guid.NewGuid():N}",
        };

    private static async Task<IConnection?> Connect()
    {
        try
        {
            return await new ConnectionFactory { Uri = new Uri(AmqpUri) }.CreateConnectionAsync();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"RabbitMQ not reachable at {AmqpUri}: {ex.Message}");
            return null;
        }
    }

    private static async Task Publish(
        IChannel channel,
        string exchange,
        TrainLifecycleEventMessage message
    )
    {
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);
        await channel.BasicPublishAsync(
            exchange,
            string.Empty,
            JsonSerializer.SerializeToUtf8Bytes(message)
        );
    }

    [Test]
    public async Task A_receiver_takes_each_kind_of_event_only_from_its_own_exchange()
    {
        var options = NewOptions();
        await using var connection = await Connect();
        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
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

        await using var channel = await connection!.CreateChannelAsync();
        // Each published where it does not belong, then each where it does, in that order.
        await Publish(
            channel,
            options.ExchangeName,
            Message("JunctionStarted", true) with
            {
                ExternalId = "misplaced-step",
            }
        );
        await Publish(
            channel,
            options.EffectiveJunctionExchangeName,
            Message("Completed", false) with
            {
                ExternalId = "misplaced-train",
            }
        );
        await Publish(
            channel,
            options.ExchangeName,
            Message("Completed", false) with
            {
                ExternalId = "train",
            }
        );
        await Publish(
            channel,
            options.EffectiveJunctionExchangeName,
            Message("JunctionStarted", true) with
            {
                ExternalId = "step",
            }
        );
        await gotBoth.Task.WaitAsync(Timeout);

        received
            .Should()
            .BeEquivalentTo(
                ["train", "step"],
                $"an event from an exchange that is not its own is dropped. See {Adr}."
            );
        await receiver.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task A_junction_exchange_the_broker_refuses_does_not_stop_train_events()
    {
        var options = NewOptions();
        await using var connection = await Connect();
        await using (var setup = await connection!.CreateChannelAsync())
        {
            // Declared elsewhere with another type, so declaring it as a fanout is refused.
            await setup.ExchangeDeclareAsync(
                options.EffectiveJunctionExchangeName,
                ExchangeType.Direct,
                durable: false,
                autoDelete: true
            );
        }

        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
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

        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            options,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await broadcaster.PublishAsync(Message("JunctionStarted", true), CancellationToken.None);
        await broadcaster.PublishAsync(Message("Completed", false), CancellationToken.None);

        (await received.Task.WaitAsync(Timeout))
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
        await using var connection = await Connect();
        await using (
            var receiver = new RabbitMqTrainEventReceiver(
                options,
                NullLogger<RabbitMqTrainEventReceiver>.Instance
            )
            {
                BindJunctionExchange = false,
            }
        )
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

            await using (
                var broadcaster = new RabbitMqTrainEventBroadcaster(
                    options,
                    NullLogger<RabbitMqTrainEventBroadcaster>.Instance
                )
            )
            {
                await broadcaster.PublishAsync(Message("Completed", false), CancellationToken.None);
                await received.Task.WaitAsync(Timeout);
            }

            await receiver.StopAsync(CancellationToken.None);
        }

        await using var probe = await connection!.CreateChannelAsync();
        var passive = () =>
            probe.ExchangeDeclarePassiveAsync(options.EffectiveJunctionExchangeName);
        (
            await passive
                .Should()
                .ThrowAsync<RabbitMQ.Client.Exceptions.OperationInterruptedException>()
        )
            .Which.ShutdownReason!.ReplyCode.Should()
            .Be(
                (ushort)404,
                $"no host without junction events declares their exchange. See {Adr}."
            );
    }
}
