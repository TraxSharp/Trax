using AwesomeAssertions;
using RabbitMQ.Client;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// The broadcaster is often first resolved on a train run's flow, and its sender outlives that
/// run, so the sender must not carry the ambient state of the flow that created it.
/// </summary>
[TestFixture]
public class RabbitMqBroadcasterFlowTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    [Test]
    public async Task The_sender_does_not_carry_the_flow_that_created_the_broadcaster()
    {
        var seen = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        Ambient.Value = "the creating run";
        await using var broadcaster = new RabbitMqTrainEventBroadcaster(
            new RabbitMqBroadcasterOptions { ConnectionString = "amqp://unused/" },
            logger: null,
            RabbitMqTrainEventBroadcaster.DefaultQueueCapacity,
            connect: _ =>
            {
                seen.TrySetResult(Ambient.Value);
                return Task.FromException<IConnection>(new InvalidOperationException("no broker"));
            },
            firstRetryDelay: TimeSpan.FromMilliseconds(10)
        );
        Ambient.Value = null;

        await broadcaster.PublishAsync(
            new TrainLifecycleEventMessage(
                MetadataId: 1,
                ExternalId: "e",
                TrainName: "Flow.ITrain",
                TrainState: "InProgress",
                Timestamp: DateTime.UtcNow,
                FailureJunction: null,
                FailureReason: null,
                EventType: "Started",
                Executor: null,
                Output: null
            ),
            CancellationToken.None
        );

        (await seen.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull();
    }
}
