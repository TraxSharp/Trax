using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What <see cref="GraphQLSubscriptionSocket"/> does with a host on the other end of a real socket: a sink that throws
/// ends only its own subscription, a broken connection is found and closed, and Started always comes first.
/// </summary>
public class GraphQLSubscriptionSocketTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static JsonObject Payload =>
        new() { ["id"] = "changed-1", ["variables"] = new JsonObject() };

    private static JsonObject Frame(string domain) =>
        new()
        {
            ["data"] = new JsonObject
            {
                ["onDataChanged"] = new JsonObject { ["domain"] = domain },
            },
        };

    // Records what a subscription was told, in order.
    private sealed class Sink(Action<JsonObject>? onNext = null) : ISubscriptionSink
    {
        private readonly System.Threading.Channels.Channel<string> _calls =
            System.Threading.Channels.Channel.CreateUnbounded<string>();

        public TaskCompletionSource<Exception> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Started() => _calls.Writer.TryWrite("started");

        public void Next(JsonObject value)
        {
            _calls.Writer.TryWrite("next");
            onNext?.Invoke(value);
        }

        public void Error(Exception error)
        {
            _calls.Writer.TryWrite("error");
            Failed.TrySetResult(error);
        }

        public void Complete() => _calls.Writer.TryWrite("complete");

        public async Task<IReadOnlyList<string>> Take(int count)
        {
            using var timeout = new CancellationTokenSource(Wait);
            var taken = new List<string>();
            for (var i = 0; i < count; i++)
                taken.Add(await _calls.Reader.ReadAsync(timeout.Token).ConfigureAwait(false));
            return taken;
        }
    }

    [Test]
    public async Task A_sink_that_throws_ends_its_own_subscription_and_no_other()
    {
        var completed = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            var first = await peer.SubscribedAsync().ConfigureAwait(false);
            var second = await peer.SubscribedAsync().ConfigureAwait(false);
            await peer.NextAsync(first, Frame("A")).ConfigureAwait(false);
            await peer.NextAsync(second, Frame("B")).ConfigureAwait(false);
            completed.TrySetResult(
                (await peer.ExpectAsync("complete").ConfigureAwait(false))["id"]
                    .NotNull()
                    .GetValue<string>()
            );
            await peer.NextAsync(second, Frame("C")).ConfigureAwait(false);
            await peer.GoneAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var throwing = new Sink(_ => throw new InvalidOperationException("handler bug"));
        var healthy = new Sink();

        using var a = socket.Subscribe(Payload, throwing);
        // The second subscribe goes out after the first, so the host reads them in order.
        (await throwing.Take(1))
            .Should()
            .Equal("started");
        using var b = socket.Subscribe(Payload, healthy);

        (await throwing.Take(2)).Should().Equal("next", "error");
        (await throwing.Failed.Task).Message.Should().Be("handler bug");
        (await completed.Task.WaitAsync(Wait, TimeProvider.System)).Should().Be("1");
        (await healthy.Take(3)).Should().Equal("started", "next", "next");
    }

    [Test]
    public async Task A_redirected_upgrade_is_not_followed()
    {
        // connection_init carries the credential, so it must not go to whatever host a redirect names.
        using var elsewhere = new TcpListener(IPAddress.Loopback, 0);
        elsewhere.Start();
        using var host = new TcpListener(IPAddress.Loopback, 0);
        host.Start();
        var redirected = Task.Run(async () =>
        {
            using var client = await host.AcceptTcpClientAsync().ConfigureAwait(false);
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var head = new StringBuilder();
            while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    break;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            var port = ((IPEndPoint)elsewhere.LocalEndpoint).Port;
            await stream
                .WriteAsync(
                    Encoding.ASCII.GetBytes(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"HTTP/1.1 302 Found\r\nLocation: ws://127.0.0.1:{port}/graphql\r\n"
                        ) + "Content-Length: 0\r\nConnection: close\r\n\r\n"
                    )
                )
                .ConfigureAwait(false);
        });
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = new Uri(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"ws://127.0.0.1:{((IPEndPoint)host.LocalEndpoint).Port}/graphql"
                    )
                ),
            }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .BeAssignableTo<WebSocketException>();
        await redirected.WaitAsync(Wait, TimeProvider.System);
        // negative-wait: a followed redirect would connect to elsewhere within this window.
        await Task.Delay(200);
        elsewhere.Pending().Should().BeFalse();
    }

    [Test]
    public async Task Started_comes_before_the_first_event_however_fast_the_host_sends_it()
    {
        // The host answers the subscribe at once, racing the socket's own Started.
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            for (var i = 0; i < 20; i++)
                await peer.NextAsync(await peer.SubscribedAsync().ConfigureAwait(false), Frame("X"))
                    .ConfigureAwait(false);
            await peer.GoneAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);

        for (var i = 0; i < 20; i++)
        {
            var sink = new Sink();
            using var subscription = socket.Subscribe(Payload, sink);
            (await sink.Take(2)).Should().Equal("started", "next");
        }
    }

    [Test]
    public async Task A_host_that_stops_answering_pings_fails_the_connection()
    {
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            await peer.StallAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = host.Endpoint,
                KeepAliveInterval = TimeSpan.FromMilliseconds(100),
                KeepAliveTimeout = TimeSpan.FromMilliseconds(300),
            }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .NotBeOfType<SocketClosedException>();
    }

    [Test]
    public async Task A_connection_init_the_host_never_reads_fails_within_the_connect_timeout()
    {
        var host = new WireHost(async peer =>
        {
            await peer.UpgradeAsync().ConfigureAwait(false);
            await peer.StallAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        // Larger than the loopback buffers hold, so the send stalls until the host reads it.
        var large = new string('x', 32 * 1024 * 1024);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = host.Endpoint,
                ConnectTimeout = TimeSpan.FromMilliseconds(500),
                ConnectionParams = _ =>
                    ValueTask.FromResult<JsonObject?>(new JsonObject { ["token"] = large }),
            }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System)).Should().NotBeNull();
    }

    [Test]
    public async Task A_close_the_host_never_answers_drops_the_connection_within_the_connect_timeout()
    {
        var dropped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            await peer.SubscribedAsync().ConfigureAwait(false);
            await peer.LeaveCloseUnansweredAsync().ConfigureAwait(false);
            dropped.TrySetResult();
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = host.Endpoint,
                ConnectTimeout = TimeSpan.FromMilliseconds(500),
            }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();
        var subscription = socket.Subscribe(Payload, sink);
        (await sink.Take(1)).Should().Equal("started");

        subscription.Dispose();

        await dropped.Task.WaitAsync(Wait, TimeProvider.System);
    }

    [Test]
    public void The_keep_alive_is_fifteen_seconds_each_way_by_default()
    {
        var options = new GraphQLSubscriptionSocketOptions
        {
            Endpoint = new Uri("wss://h/graphql"),
        };

        options.KeepAliveInterval.Should().Be(TimeSpan.FromSeconds(15));
        options.KeepAliveTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [TestCase("""{"type":5}""")]
    [TestCase("""{"type":"next","id":7,"payload":{}}""")]
    [TestCase("""{"id":"1"}""")]
    [TestCase("""[]""")]
    [TestCase("""not json""")]
    [TestCase("""{"type":"next","type":"complete"}""")]
    public async Task A_malformed_message_fails_the_connection_and_closes_it_with_4400(
        string message
    )
    {
        var closed = new TaskCompletionSource<WebSocketCloseStatus?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            await peer.SubscribedAsync().ConfigureAwait(false);
            await peer.SendAsync(message).ConfigureAwait(false);
            closed.TrySetResult(await peer.ClosedWithAsync().ConfigureAwait(false));
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .BeAssignableTo<WebSocketException>();
        (await closed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .Be((WebSocketCloseStatus)4400);
    }

    [Test]
    public async Task An_error_with_no_errors_in_it_reaches_the_sink()
    {
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            var id = await peer.SubscribedAsync().ConfigureAwait(false);
            await peer.SendAsync($$"""{"type":"error","id":"{{id}}","payload":[]}""")
                .ConfigureAwait(false);
            await peer.GoneAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        var error = await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System);
        error.Should().BeOfType<SubscriptionErrorException>().Which.Errors.Should().BeEmpty();
        error.Message.Should().Contain("ended the subscription with an error");
    }

    [Test]
    public void The_errors_message_is_read_only_from_an_error_object()
    {
        new SubscriptionErrorException(new JsonArray("not an object"))
            .Message.Should()
            .Contain("ended the subscription with an error");
        new SubscriptionErrorException(new JsonArray(new JsonObject { ["message"] = "No." }))
            .Message.Should()
            .Be("No.");
    }

    [TestCase(401)]
    [TestCase(403)]
    public async Task An_upgrade_the_host_refuses_is_a_refused_close(int status)
    {
        var host = new WireHost(peer => peer.RefuseAsync(status, "No"));
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        var closed = (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .BeOfType<SocketClosedException>()
            .Subject;
        closed.HttpStatus.Should().Be(status);
        closed.Refused.Should().BeTrue();
    }

    [Test]
    public async Task An_upgrade_answered_with_another_status_is_not_refused()
    {
        var host = new WireHost(peer => peer.RefuseAsync(502, "Bad Gateway"));
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .NotBeOfType<SocketClosedException>("a 502 is a host that may be back soon");
    }

    [TestCase(4401, null, true)]
    [TestCase(4403, null, true)]
    [TestCase(null, 401, true)]
    [TestCase(null, 403, true)]
    [TestCase(4400, null, false)]
    [TestCase(4408, null, false)]
    [TestCase(4429, null, false)]
    [TestCase(4499, null, false)]
    [TestCase(1011, null, false)]
    public void Only_4401_and_4403_and_an_upgrade_answered_401_or_403_are_refusals(
        int? closeStatus,
        int? httpStatus,
        bool refused
    ) => new SocketClosedException("closed", closeStatus, httpStatus).Refused.Should().Be(refused);

    [Test]
    public async Task A_subscription_disposed_before_the_connection_is_acknowledged_is_never_sent()
    {
        var acknowledge = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var received = new TaskCompletionSource<IReadOnlyList<JsonObject>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new WireHost(async peer =>
        {
            await peer.UpgradeAsync().ConfigureAwait(false);
            await peer.ReceiveAsync().ConfigureAwait(false);
            await acknowledge.Task.WaitAsync(peer.Stopping).ConfigureAwait(false);
            await peer.SendAsync("""{"type":"connection_ack"}""").ConfigureAwait(false);
            var messages = new List<JsonObject>();
            try
            {
                while (await peer.ReceiveAsync().ConfigureAwait(false) is { } message)
                    messages.Add(message);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException)
            {
                // Dropped rather than closed.
            }
            received.TrySetResult(messages);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        await using var socketDisposal = socket.ConfigureAwait(false);
        var gone = new Sink();
        var kept = new Sink();

        socket.Subscribe(Payload, gone).Dispose();
        // A second subscription holds the connection open past the first one's end.
        var held = socket.Subscribe(Payload, kept);
        acknowledge.SetResult();
        (await kept.Take(1)).Should().Equal("started");
        held.Dispose();

        var messages = await received.Task.WaitAsync(Wait, TimeProvider.System);
        messages
            .Where(m =>
                string.Equals(
                    m["type"].NotNull().GetValue<string>(),
                    "subscribe",
                    StringComparison.Ordinal
                )
            )
            .Select(m => m["id"].NotNull().GetValue<string>())
            .Should()
            .Equal("2");
    }

    [Test]
    public async Task Disposing_the_socket_closes_the_connection_normally()
    {
        var closed = new TaskCompletionSource<WebSocketCloseStatus?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            await peer.SubscribedAsync().ConfigureAwait(false);
            closed.TrySetResult(await peer.ClosedWithAsync().ConfigureAwait(false));
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        var sink = new Sink();
        using var subscription = socket.Subscribe(Payload, sink);
        (await sink.Take(1)).Should().Equal("started");

        await socket.DisposeAsync();

        (await closed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .Be(WebSocketCloseStatus.NormalClosure);
        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .BeOfType<SocketUnavailableException>();
    }

    [Test]
    public async Task Disposing_the_socket_lets_go_of_the_connections_stop_and_send_lock()
    {
        var host = new WireHost(async peer =>
        {
            await peer.AckAsync().ConfigureAwait(false);
            await peer.SubscribedAsync().ConfigureAwait(false);
            await peer.ClosedWithAsync().ConfigureAwait(false);
        });
        await using var hostDisposal = host.ConfigureAwait(false);
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        var sink = new Sink();
        using var subscription = socket.Subscribe(Payload, sink);
        (await sink.Take(1)).Should().Equal("started");
        // The connection, and what it holds, read from the socket's private fields.
        var connection = typeof(GraphQLSubscriptionSocket)
            .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)
            .NotNull()
            .GetValue(socket)
            .NotNull();
        var stop = (CancellationTokenSource)
            connection
                .GetType()
                .GetField("_stop", BindingFlags.NonPublic | BindingFlags.Instance)
                .NotNull()
                .GetValue(connection)
                .NotNull();
        var sending = (SemaphoreSlim)
            connection.GetType().GetProperty("Sending").NotNull().GetValue(connection).NotNull();

        await socket.DisposeAsync();

        ((Action)(() => _ = stop.Token)).Should().Throw<ObjectDisposedException>();
        ((Action)(() => _ = sending.AvailableWaitHandle)).Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public async Task A_disposed_socket_fails_a_new_subscription_as_unavailable()
    {
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("ws://127.0.0.1:1/graphql") }
        );
        await socket.DisposeAsync();
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait, TimeProvider.System))
            .Should()
            .BeOfType<SocketUnavailableException>();
    }
}
