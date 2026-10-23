using System.Net.WebSockets;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;

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
                taken.Add(await _calls.Reader.ReadAsync(timeout.Token));
            return taken;
        }
    }

    [Test]
    public async Task A_sink_that_throws_ends_its_own_subscription_and_no_other()
    {
        var completed = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await using var host = new WireHost(async peer =>
        {
            await peer.AckAsync();
            var first = await peer.SubscribedAsync();
            var second = await peer.SubscribedAsync();
            await peer.NextAsync(first, Frame("A"));
            await peer.NextAsync(second, Frame("B"));
            completed.TrySetResult((await peer.ExpectAsync("complete"))["id"]!.GetValue<string>());
            await peer.NextAsync(second, Frame("C"));
            await peer.GoneAsync();
        });
        await using var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
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
        (await completed.Task.WaitAsync(Wait)).Should().Be("1");
        (await healthy.Take(3)).Should().Equal("started", "next", "next");
    }

    [Test]
    public async Task Started_comes_before_the_first_event_however_fast_the_host_sends_it()
    {
        // The host answers the subscribe at once, racing the socket's own Started.
        await using var host = new WireHost(async peer =>
        {
            await peer.AckAsync();
            for (var i = 0; i < 20; i++)
                await peer.NextAsync(await peer.SubscribedAsync(), Frame("X"));
            await peer.GoneAsync();
        });
        await using var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );

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
        await using var host = new WireHost(async peer =>
        {
            await peer.AckAsync();
            await peer.StallAsync();
        });
        await using var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = host.Endpoint,
                KeepAliveInterval = TimeSpan.FromMilliseconds(100),
                KeepAliveTimeout = TimeSpan.FromMilliseconds(300),
            }
        );
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait)).Should().NotBeOfType<SocketClosedException>();
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
    public async Task A_malformed_message_fails_the_connection_and_closes_it(string message)
    {
        var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new WireHost(async peer =>
        {
            await peer.AckAsync();
            await peer.SubscribedAsync();
            await peer.SendAsync(message);
            await peer.GoneAsync();
            gone.TrySetResult();
        });
        await using var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait)).Should().BeOfType<WebSocketException>();
        await gone.Task.WaitAsync(Wait);
    }

    [Test]
    public async Task An_error_with_no_errors_in_it_reaches_the_sink()
    {
        await using var host = new WireHost(async peer =>
        {
            await peer.AckAsync();
            var id = await peer.SubscribedAsync();
            await peer.SendAsync($$"""{"type":"error","id":"{{id}}","payload":[]}""");
            await peer.GoneAsync();
        });
        await using var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = host.Endpoint }
        );
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        var error = await sink.Failed.Task.WaitAsync(Wait);
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

    [Test]
    public async Task A_disposed_socket_fails_a_new_subscription_as_unavailable()
    {
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("ws://127.0.0.1:1/graphql") }
        );
        await socket.DisposeAsync();
        var sink = new Sink();

        using var subscription = socket.Subscribe(Payload, sink);

        (await sink.Failed.Task.WaitAsync(Wait)).Should().BeOfType<SocketUnavailableException>();
    }
}
