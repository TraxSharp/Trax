using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
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
        IReadOnlyList<TimeSpan>? delays = null,
        TimeProvider? time = null,
        Func<double>? random = null,
        Func<StreamMessage, bool>? throwOn = null
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
                Time = time,
                Random = random,
            }
        );
        var messages = Channel.CreateUnbounded<StreamMessage>();
        var stream = server.Subscribe(
            "i-1",
            m =>
            {
                messages.Writer.TryWrite(m);
                if (throwOn?.Invoke(m) == true)
                    throw new InvalidOperationException("handler bug");
            }
        );
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
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(time: time);
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
        // Subscribing again would wait on a timer first: none was started.
        time.Delays.Should().BeEmpty();
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_socket_that_follows_no_more_subscriptions_ends_the_stream_and_is_not_subscribed_again()
    {
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(time: time);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        var disposed = new SocketUnavailableException(
            "The socket was disposed: it follows no more subscriptions."
        );

        first.Sink.Error(disposed);

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message
            .Should()
            .Be(new StreamMessage.Refused(ClientProblemCodes.TransportError, disposed.Message));
        // Subscribing again would wait on a timer first: none was started.
        time.Delays.Should().BeEmpty();
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

    // Each kit transcript malformed-push-* holds one of these through the controller; here the server's own message is
    // held, and that the stream goes on after it.
    [TestCase("""{"turnstileChanged":{"gone":"yes"}}""")]
    [TestCase("""{"turnstileChanged":{"reload":1}}""")]
    [TestCase("""{"turnstileChanged":"Locked"}""")]
    [TestCase("""{"turnstileChanged":null}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":"one","context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":-1,"context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":1.5,"context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":1,"rowId":"7","context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"state":"Locked","revision":1,"rowId":7.5,"context":{}}}""")]
    [TestCase("""{"turnstileChanged":{"problem":{"message":"no code"}}}""")]
    [TestCase(
        """{"turnstileChanged":{"state":"Locked","revision":1,"dueAt":"soon","context":{}}}"""
    )]
    [TestCase(
        """{"turnstileChanged":{"state":"Locked","revision":1,"dueAt":"2026-10-22T10:10:00","context":{}}}"""
    )]
    [TestCase(
        """{"turnstileChanged":{"state":"Locked","state":"Unlocked","revision":1,"context":{}}}"""
    )]
    public async Task A_push_the_client_cannot_read_is_dropped_and_the_instance_read_again(
        string data
    )
    {
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(time: time);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push((JsonObject)JsonNode.Parse($$"""{"data":{{data}}}""")!);
        first.Push(Changed(2));

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Reload>();
        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>()
            .Which.Snapshot.Revision.Should()
            .Be(2);
        // The stream goes on: it was neither ended nor subscribed again.
        first.Disposed.Should().BeFalse();
        time.Delays.Should().BeEmpty();
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_revision_a_javascript_host_writes_as_a_whole_number_with_a_fraction_of_zero_is_read()
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(
            (JsonObject)
                JsonNode.Parse(
                    """{"data":{"turnstileChanged":{"state":"Locked","revision":2.0,"context":{},"dueAt":"2026-10-22T12:10:00.5+02:00"}}}"""
                )!
        );

        var changed = (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>()
            .Which;
        changed.Snapshot.Revision.Should().Be(2);
        changed
            .Snapshot.DueAt.Should()
            .Be(new DateTimeOffset(2026, 10, 22, 10, 10, 0, 500, TimeSpan.Zero));
    }

    // Trax.Api types rowId as a Long, so a host writes it as a number.
    [TestCase("7")]
    [TestCase("7.0")]
    public async Task A_row_id_written_as_a_whole_number_is_read_as_its_decimal_text(
        string rowId
    )
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(
            (JsonObject)
                JsonNode.Parse(
                    """{"data":{"turnstileChanged":{"state":"Locked","revision":1,"rowId":ROW,"context":{}}}}""".Replace(
                        "ROW",
                        rowId
                    )
                )!
        );

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>()
            .Which.Snapshot.RowId.Should()
            .Be("7");
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
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(time: time);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Errors(error));

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().BeOfType<StreamMessage.Refused>().Which.Code.Should().Be(code);
        // Subscribing again would wait on a timer first: none was started.
        time.Delays.Should().BeEmpty();
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
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(time: time);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Error(
            new SubscriptionErrorException((JsonArray)Errors(error)["errors"]!.DeepClone())
        );

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().BeOfType<StreamMessage.Refused>().Which.Code.Should().Be(code);
        // Subscribing again would wait on a timer first: none was started.
        time.Delays.Should().BeEmpty();
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task An_error_whose_errors_cannot_be_read_is_subscribed_again()
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Error(
            new SubscriptionErrorException(
                (JsonArray)
                    JsonNode.Parse("""[{"message":"m","extensions":{"code":"A","code":"B"}}]""")!
            )
        );

        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Should().NotBeSameAs(first);
        first.Disposed.Should().BeTrue();
        messages.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_resubscribe_whose_subscribe_throws_ends_the_stream_with_a_transport_error()
    {
        var (socket, messages, stream) = Follow();
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        socket.Throws = new ObjectDisposedException("socket");

        first.Sink.Complete();

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Refused>()
            .Which.Code.Should()
            .Be(ClientProblemCodes.TransportError);
    }

    [Test]
    public async Task A_code_the_caller_named_terminal_ends_the_stream_with_that_code()
    {
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(terminal: ["CONNECTION_CHANGED"], time: time);
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Errors("CONNECTION_CHANGED"));

        var message = await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        message.Should().Be(new StreamMessage.Refused("CONNECTION_CHANGED", "Not allowed."));
        // Subscribing again would wait on a timer first: none was started.
        time.Delays.Should().BeEmpty();
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task A_handler_that_throws_ends_the_stream_with_a_failure_and_is_not_subscribed_again()
    {
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(
            time: time,
            throwOn: m => m is StreamMessage.Changed
        );
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Push(Changed(1));

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>();
        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Refused>()
            .Which.Message.Should()
            .Contain("handler bug");
        first.Disposed.Should().BeTrue();
        time.Delays.Should().BeEmpty();
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
        var time = new TimerLog();
        var (socket, messages, stream) = Follow(
            delays: [TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(1)],
            time: time
        );
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        first.Push(Changed(1));
        first.Sink.Complete();
        time.Advance(TimeSpan.FromMilliseconds(10));
        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        second.Push(Changed(1));
        second.Sink.Complete();

        (await messages.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .BeOfType<StreamMessage.Changed>();
        // The second wait is drawn from the hour's step, at least half of it.
        time.Delays.Should().HaveCount(2);
        time.Delays[1].Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
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

    // Each wait is drawn uniformly between half the step and the whole of it, so instances a host dropped together do
    // not all come back together.
    [TestCase(0.0, 1000)]
    [TestCase(0.5, 750)]
    [TestCase(1.0, 500)]
    public async Task Each_resubscribe_delay_is_drawn_between_half_its_step_and_the_whole(
        double drawn,
        int milliseconds
    )
    {
        var time = new TimerLog();
        var (socket, _, stream) = Follow(
            delays: [TimeSpan.FromSeconds(1)],
            time: time,
            random: () => drawn
        );
        using var following = stream;
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Complete();
        time.Delays.Should().Equal(TimeSpan.FromMilliseconds(milliseconds));
        time.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
        socket.Subscriptions.Reader.TryRead(out _).Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));

        (await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Should()
            .NotBeSameAs(first);
    }

    [Test]
    public async Task A_resubscribe_delay_lets_go_of_its_timer_once_it_ends_and_once_the_stream_is_disposed()
    {
        var time = new FakeTimeProvider();
        var (socket, _, stream) = Follow(delays: [TimeSpan.FromSeconds(1)], time: time);
        var first = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        first.Sink.Complete();
        var ended = PendingTimer(stream)!;
        time.Advance(TimeSpan.FromSeconds(1));
        var second = await socket.Subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Sink.Complete();
        var pending = PendingTimer(stream)!;
        stream.Dispose();

        IsDisposed(ended).Should().BeTrue("its delay ended and the stream was subscribed again");
        IsDisposed(pending).Should().BeTrue("the stream was disposed while it was pending");
    }

    // The source of the resubscribe delay pending now, read from the stream's private field.
    private static CancellationTokenSource? PendingTimer(IDisposable stream) =>
        (CancellationTokenSource?)
            stream
                .GetType()
                .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(stream);

    private static bool IsDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    // A fake clock that keeps the wait of every timer started on it, so a test reads whether, and how long, the stream
    // waits before subscribing again without waiting itself.
    private sealed class TimerLog : FakeTimeProvider
    {
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays)
                    return [.. _delays];
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            lock (_delays)
                _delays.Add(dueTime);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    // A socket each subscription is handed to the test through.
    private sealed class HandSocket : ISubscriptionSocket
    {
        public Channel<Opened> Subscriptions { get; } = Channel.CreateUnbounded<Opened>();

        // Thrown by every subscribe from now on, as a custom socket's bug would be.
        public Exception? Throws { get; set; }

        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink)
        {
            if (Throws is not null)
                throw Throws;
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
