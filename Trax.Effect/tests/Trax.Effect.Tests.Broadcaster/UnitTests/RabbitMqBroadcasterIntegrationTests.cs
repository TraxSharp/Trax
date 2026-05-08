using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// Integration tests that exercise <see cref="RabbitMqTrainEventBroadcaster"/> and
/// <see cref="RabbitMqTrainEventReceiver"/> against a real RabbitMQ broker.
/// CI provisions a rabbitmq:4-management service container with a dedicated
/// 'trax' user (default 'guest' is restricted to localhost in RabbitMQ, and CI
/// service containers route through port forwarding so the broker sees
/// non-localhost connections). The Trax.Samples docker-compose broker is
/// configured the same way for local parity.
/// </summary>
[TestFixture]
public class RabbitMqBroadcasterIntegrationTests
{
    private const string AmqpUri = "amqp://trax:trax123@localhost:5672/";

    private static RabbitMqBroadcasterOptions Options(string suffix) =>
        new()
        {
            ConnectionString = AmqpUri,
            ExchangeName = $"trax.test.{suffix}.{Guid.NewGuid():N}",
        };

    private static TrainLifecycleEventMessage SampleMessage(string trainName) =>
        new(
            MetadataId: 1,
            ExternalId: "ext-1",
            TrainName: trainName,
            TrainState: "InProgress",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Started",
            Executor: null,
            Output: null
        );

    [Test]
    public async Task PublishAsync_DeliversMessage_ToReceiver()
    {
        var opts = Options("publish");
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            opts,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await using var receiver = new RabbitMqTrainEventReceiver(
            opts,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );

        var received = new TaskCompletionSource<TrainLifecycleEventMessage>();
        await receiver.StartAsync(
            (msg, _) =>
            {
                received.TrySetResult(msg);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        await broadcaster.PublishAsync(SampleMessage("RoundTrip.Train"), CancellationToken.None);

        var awaited = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        awaited.Should().Be(received.Task);
        received.Task.Result.TrainName.Should().Be("RoundTrip.Train");

        await receiver.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task PublishAsync_TwiceOnSameBroadcaster_ReusesChannel()
    {
        var opts = Options("reuse");
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            opts,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );

        // First publish opens the connection + declares the exchange. The second
        // publish should hit the already-open channel and skip the exchange-declare.
        await broadcaster.PublishAsync(SampleMessage("First"), CancellationToken.None);
        await broadcaster.PublishAsync(SampleMessage("Second"), CancellationToken.None);
    }

    [Test]
    public async Task Receiver_HandlerThrows_NacksAndKeepsConsuming()
    {
        var opts = Options("handler-throws");
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            opts,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );
        await using var receiver = new RabbitMqTrainEventReceiver(
            opts,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );

        var calls = 0;
        var second = new TaskCompletionSource();
        await receiver.StartAsync(
            (msg, _) =>
            {
                calls++;
                if (calls == 1)
                    throw new InvalidOperationException("handler down");
                second.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        await broadcaster.PublishAsync(SampleMessage("first"), CancellationToken.None);
        await broadcaster.PublishAsync(SampleMessage("second"), CancellationToken.None);

        var awaited = await Task.WhenAny(second.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        awaited.Should().Be(second.Task);
        calls.Should().BeGreaterThanOrEqualTo(2);

        await receiver.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Receiver_StopAsync_ThenDisposeAsync_DoesNotThrow()
    {
        var opts = Options("stop-dispose");
        var receiver = new RabbitMqTrainEventReceiver(
            opts,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        await receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);

        await receiver.StopAsync(CancellationToken.None);
        await receiver.DisposeAsync();
    }

    [Test]
    public async Task Broadcaster_DisposeAsync_WithNoPublishes_DoesNotThrow()
    {
        var opts = Options("nop-dispose");
        var broadcaster = new RabbitMqTrainEventBroadcaster(
            opts,
            NullLogger<RabbitMqTrainEventBroadcaster>.Instance
        );

        await broadcaster.DisposeAsync();
    }
}
