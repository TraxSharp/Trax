using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Trax.Effect.Broadcaster.RabbitMQ;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// <c>TrainEventReceiverService</c> retries a receiver that failed to start by stopping it and
/// starting it again on the same instance. Each start must release the connection the previous
/// one opened, and a start that fails partway must not leave its connection open. Runs against the
/// same broker as <see cref="RabbitMqBroadcasterIntegrationTests"/>.
/// </summary>
[TestFixture]
public class RabbitMqReceiverRestartTests
{
    private static readonly string AmqpUri =
        $"amqp://trax:trax123@localhost:{PortOrDefault(Environment.GetEnvironmentVariable("TRAX_TEST_RABBITMQ_PORT"))}/";

    private static string PortOrDefault(string? port) =>
        string.IsNullOrWhiteSpace(port) ? "5672" : port;

    private static RabbitMqBroadcasterOptions Options() =>
        new()
        {
            ConnectionString = AmqpUri,
            ExchangeName = $"trax.test.restart.{Guid.NewGuid():N}",
        };

    private static IConnection? ConnectionOf(RabbitMqTrainEventReceiver receiver) =>
        (IConnection?)
            typeof(RabbitMqTrainEventReceiver)
                .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(receiver);

    [Test]
    public async Task StartAsync_CalledAgain_ClosesTheConnectionTheFirstStartOpened()
    {
        await using var receiver = new RabbitMqTrainEventReceiver(
            Options(),
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        await receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);
        var first = ConnectionOf(receiver)!;

        await receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);

        first
            .IsOpen.Should()
            .BeFalse("a restart replaces the receiver's connection rather than adding one");
        ConnectionOf(receiver)!.IsOpen.Should().BeTrue();
    }

    [Test]
    public async Task StartAsync_WhenItFailsPartway_LeavesNoConnectionOpen()
    {
        var options = Options();
        // An exchange of another type under the same name makes the receiver's declare fail
        // after its connection is open.
        var factory = new ConnectionFactory { Uri = new Uri(AmqpUri) };
        await using (var setup = await factory.CreateConnectionAsync())
        await using (var channel = await setup.CreateChannelAsync())
        {
            await channel.ExchangeDeclareAsync(
                options.ExchangeName,
                ExchangeType.Direct,
                durable: false,
                autoDelete: true
            );
        }

        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );

        var start = async () =>
            await receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);

        await start.Should().ThrowAsync<Exception>();
        var connection = ConnectionOf(receiver);
        (connection is null || !connection.IsOpen)
            .Should()
            .BeTrue("a failed start releases the connection it opened");
    }
}
