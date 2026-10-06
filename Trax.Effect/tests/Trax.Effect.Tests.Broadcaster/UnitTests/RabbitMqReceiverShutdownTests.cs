using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Trax.Effect.Broadcaster.RabbitMQ;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// A broker can close the receiver's connection (a broker restart, another host tearing down, the
/// management plugin) between the receiver reading <c>IsOpen</c> and acting on it. Stopping and
/// disposing must tolerate that, because the host calls both on its way down.
/// </summary>
[TestFixture]
public class RabbitMqReceiverShutdownTests
{
    private static AlreadyClosedException ClosedByPeer() =>
        new(
            new ShutdownEventArgs(
                ShutdownInitiator.Peer,
                320,
                "CONNECTION_FORCED - Closed via management plugin"
            )
        );

    private static RabbitMqTrainEventReceiver ReceiverWhoseConnectionIsClosedUnderIt()
    {
        var receiver = new RabbitMqTrainEventReceiver(
            new RabbitMqBroadcasterOptions { ConnectionString = "amqp://localhost/" },
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );

        // Both still report open: the close from the broker lands after the check.
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        channel
            .QueueDeleteAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());
        channel
            .CloseAsync(
                Arg.Any<ushort>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());

        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);
        connection
            .CloseAsync(
                Arg.Any<ushort>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ClosedByPeer());

        Set(receiver, "_channel", channel);
        Set(receiver, "_connection", connection);
        Set(receiver, "_queueName", "amq.gen-closed");
        return receiver;
    }

    private static void Set(object target, string field, object value) =>
        typeof(RabbitMqTrainEventReceiver)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    [Test]
    public async Task StopAsync_WhenTheBrokerClosedTheConnection_DoesNotThrow()
    {
        var receiver = ReceiverWhoseConnectionIsClosedUnderIt();

        var stop = async () => await receiver.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync();
    }

    [Test]
    public async Task DisposeAsync_WhenTheBrokerClosedTheConnection_DoesNotThrow()
    {
        var receiver = ReceiverWhoseConnectionIsClosedUnderIt();

        var dispose = async () => await receiver.DisposeAsync();

        await dispose.Should().NotThrowAsync();
    }
}
