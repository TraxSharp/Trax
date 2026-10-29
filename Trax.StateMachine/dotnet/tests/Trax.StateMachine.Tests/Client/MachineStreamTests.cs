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

    private static (HandSocket Socket, Channel<StreamMessage> Messages, IDisposable Stream) Follow(
        IReadOnlyCollection<string>? terminal = null,
        IReadOnlyList<TimeSpan>? delays = null
    )
    {
        var socket = new HandSocket();
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
                Endpoint = new Uri("https://host/trax/graphql"),
                Machine = Turnstile,
                Socket = socket,
                ResubscribeDelays = delays ?? [TimeSpan.FromMilliseconds(10)],
                TerminalStreamErrors = terminal,
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

    [TestCase("""{"turnstileChanged":{"gone":"yes"}}""")]
    [TestCase("""{"turnstileChanged":{"reload":1}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":"one","context":{}}}""")]
    [TestCase(
        """{"turnstileChanged":{"state":"Locked","revision":1,"dueAt":"soon","context":{}}}"""
    )]
    public async Task A_push_the_client_cannot_read_ends_the_stream_once_with_a_failure(string data)
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push((JsonObject)JsonNode.Parse($$"""{"data":{{data}}}""")!);

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message
            .Should()
            .BeOfType<StreamMessage.Refused>()
            .Which.Code.Should()
            .Be(ClientProblemCodes.TransportError);
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_push_with_a_member_given_twice_ends_the_stream_once_with_a_failure()
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(
            (JsonObject)
                JsonNode.Parse(
                    """{"data":{"turnstileChanged":{"state":"Locked","state":"Unlocked","revision":1}}}"""
                )!
        );

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Refused>()
            .Which.Code.Should()
            .Be(ClientProblemCodes.TransportError);
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    private static JsonObject Errors(string code) =>
        (JsonObject)
            JsonNode.Parse(
                $$$"""{"data":null,"errors":[{"message":"Not allowed.","extensions":{"code":"{{{code}}}"}}]}"""
            )!;

    [TestCase("TRAX_AUTHORIZATION", "FORBIDDEN")]
    [TestCase("AUTH_NOT_AUTHORIZED", "FORBIDDEN")]
    [TestCase("AUTH_NOT_AUTHENTICATED", "UNAUTHENTICATED")]
    public async Task A_stream_the_host_refuses_in_a_next_ends_and_is_not_subscribed_again(
        string error,
        string code
    )
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Errors(error));

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().BeOfType<StreamMessage.Refused>().Which.Code.Should().Be(code);
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [TestCase("TRAX_AUTHORIZATION", "FORBIDDEN")]
    [TestCase("AUTH_NOT_AUTHORIZED", "FORBIDDEN")]
    [TestCase("AUTH_NOT_AUTHENTICATED", "UNAUTHENTICATED")]
    public async Task A_stream_the_host_refuses_in_an_error_ends_and_is_not_subscribed_again(
        string error,
        string code
    )
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Error(
            new SubscriptionErrorException((JsonArray)Errors(error)["errors"]!.DeepClone())
        );

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().BeOfType<StreamMessage.Refused>().Which.Code.Should().Be(code);
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_code_the_caller_named_terminal_ends_the_stream_with_that_code()
    {
        var (socket, messages, stream) = Follow(terminal: ["CONNECTION_CHANGED"]);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Errors("CONNECTION_CHANGED"));

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().Be(new StreamMessage.Refused("CONNECTION_CHANGED", "Not allowed."));
        // negative-wait: nothing should subscribe again, so there is no signal to wait on; ten resubscribe delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_stream_error_with_any_other_code_is_subscribed_again()
    {
        var (socket, messages, stream) = Follow(terminal: ["CONNECTION_CHANGED"]);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Errors("INTERNAL_SERVER_ERROR"));

        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Should().NotBeSameAs(first);
        messages.Reader.TryRead(out _).Should().BeFalse();
    }

    private static JsonObject Changed(long revision) =>
        new()
        {
            ["data"] = new JsonObject
            {
                ["turnstileChanged"] = new JsonObject
                {
                    ["state"] = "Locked",
                    ["revision"] = revision,
                    ["context"] = new JsonObject(),
                },
            },
        };

    [Test]
    public async Task A_stream_that_ends_after_its_first_push_waits_the_next_delay_before_subscribing_again()
    {
        // Every new stream's first push is the instance as it is: one that ends after it has not settled down.
        var (socket, messages, stream) = Follow(
            delays: [TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(1)]
        );
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        first.Push(Changed(1));
        first.Sink.Complete();
        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        second.Push(Changed(1));
        second.Sink.Complete();

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>();
        // negative-wait: the second delay is an hour, so nothing should subscribe again; ten first delays pass.
        await Task.Delay(100);
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_stream_that_delivered_a_second_push_starts_the_delays_over()
    {
        var (socket, _, stream) = Follow(
            delays: [TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(1)]
        );
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        first.Sink.Complete();
        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        second.Push(Changed(1));
        second.Push(Changed(2));
        second.Sink.Complete();

        (await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .NotBeSameAs(second);
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

        // Hands the sink one push as GraphQLSubscriptionSocket does: a sink that throws gets its exception as the error.
        public void Push(JsonObject value)
        {
            try
            {
                Sink.Next(value);
            }
            catch (Exception ex)
            {
                Sink.Error(ex);
            }
        }

        public void Dispose() => Disposed = true;
    }
}
