using System.Text.Json.Nodes;
using System.Threading.Channels;
using AwesomeAssertions;
using Trax.Client;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// How <see cref="GraphQLMachineServer"/> follows an instance: a socket the host refused (4401, 4403, or an upgrade
/// answered 401 or 403) ends the stream for good, and any other close is subscribed again.
/// </summary>
public class MachineStreamTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly ClientManifestMachine Turnstile = new()
    {
        Id = "turnstile",
        IrHash = MachinePredictor.For(DeclarativeTurnstile.Built).IrHash,
        Ir = (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!,
        Documents =
        [
            new ClientManifestDocument
            {
                Id = "changed-1",
                Operation = "changed",
                Document = "subscription Changed($id: UUID!) { x }",
            },
        ],
    };

    private static (HandSocket Socket, Channel<StreamMessage> Messages, IDisposable Stream) Follow()
    {
        var socket = new HandSocket();
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = new HttpClient(),
                Endpoint = new Uri("https://host/trax/graphql"),
                Machine = Turnstile,
                Socket = socket,
                ResubscribeDelays = [TimeSpan.FromMilliseconds(10)],
            }
        );
        var messages = Channel.CreateUnbounded<StreamMessage>();
        var stream = server.Subscribe("i-1", m => messages.Writer.TryWrite(m));
        return (socket, messages, stream);
    }

    [TestCase(4401, null, "UNAUTHENTICATED")]
    [TestCase(4403, null, "FORBIDDEN")]
    [TestCase(null, 401, "UNAUTHENTICATED")]
    [TestCase(null, 403, "FORBIDDEN")]
    public async Task A_socket_the_host_refused_ends_the_stream_and_is_not_subscribed_again(
        int? closeStatus,
        int? httpStatus,
        string code
    )
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        var refusal = new SocketClosedException(
            $"The host refused the socket: {closeStatus ?? httpStatus}",
            closeStatus,
            httpStatus
        );

        first.Sink.Error(refusal);

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().Be(new StreamMessage.Refused(code, refusal.Message));
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [TestCase(1006)]
    [TestCase(1011)]
    [TestCase(4500)]
    public async Task A_socket_closed_any_other_way_is_subscribed_again(int closeStatus)
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Error(new SocketClosedException("The socket was closed.", closeStatus));

        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Should().NotBeSameAs(first);
        first.Disposed.Should().BeTrue();
        messages.Reader.TryRead(out _).Should().BeFalse();
    }

    // A socket each subscription is handed to the test through.
    private sealed class HandSocket : ISubscriptionSocket
    {
        public Channel<Opened> Subscriptions { get; } = Channel.CreateUnbounded<Opened>();

        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink)
        {
            var opened = new Opened(sink);
            Subscriptions.Writer.TryWrite(opened);
            return opened;
        }
    }

    private sealed class Opened(ISubscriptionSink sink) : IDisposable
    {
        public ISubscriptionSink Sink { get; } = sink;
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
