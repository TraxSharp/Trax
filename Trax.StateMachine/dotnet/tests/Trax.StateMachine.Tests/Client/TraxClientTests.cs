using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Trax.Client;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What <see cref="TraxHttpTransport"/> puts on the wire and how <see cref="TraxClient"/> reads it back: a document by
/// its id or its text, <see cref="Optional{T}"/> left out or sent as null, every refusal and failure as a value, and a
/// stream that is subscribed again after the socket drops it.
/// </summary>
public class TraxClientTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly TraxDocument<RunsVariables, RunsResult> Runs = new(
        "Runs",
        TraxOperationKind.Query,
        "trax_console_Runs_abc",
        "query Runs($take: Int!, $state: String) { operations { runs(take: $take, state: $state) { id } } }"
    );

    private static readonly TraxDocument<TraxNoVariables, ChangedResult> Changed = new(
        "Changed",
        TraxOperationKind.Subscription,
        "trax_console_Changed_abc",
        "subscription Changed { onDataChanged { domain } }"
    );

    public sealed record RunsVariables
    {
        [JsonPropertyName("take")]
        public required int Take { get; init; }

        [JsonPropertyName("state")]
        public Optional<string?> State { get; init; }
    }

    public sealed record RunsResult
    {
        [JsonPropertyName("operations")]
        public RunsOperations? Operations { get; init; }
    }

    public sealed record RunsOperations
    {
        [JsonPropertyName("runs")]
        public required IReadOnlyList<RunsRun> Runs { get; init; }
    }

    public sealed record RunsRun
    {
        [JsonPropertyName("id")]
        public required long Id { get; init; }
    }

    public sealed record ChangedResult
    {
        [JsonPropertyName("onDataChanged")]
        public required ChangedDomain OnDataChanged { get; init; }
    }

    public sealed record ChangedDomain
    {
        [JsonPropertyName("domain")]
        public required string Domain { get; init; }
    }

    [Test]
    public async Task A_document_with_an_id_goes_by_its_id_with_no_text()
    {
        var host = new Host("""{"data":{"operations":{"runs":[{"id":7}]}}}""");

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 5 });

        host.Bodies.Should().ContainSingle();
        host.Bodies[0]
            .ToJsonString()
            .Should()
            .Be("""{"id":"trax_console_Runs_abc","operationName":"Runs","variables":{"take":5}}""");
        answer.Succeeded.Should().BeTrue();
        answer.Data!.Operations!.Runs.Should().ContainSingle().Which.Id.Should().Be(7);
    }

    [Test]
    public async Task A_document_the_host_did_not_register_goes_by_its_text()
    {
        var host = new Host("""{"data":{"operations":{"runs":[]}}}""");

        await Client(host, byId: _ => false).SendAsync(Runs, new RunsVariables { Take = 5 });

        host.Bodies[0]["query"]!.GetValue<string>().Should().Be(Runs.Text);
        host.Bodies[0].ContainsKey("id").Should().BeFalse();
        host.Bodies[0]["operationName"]!.GetValue<string>().Should().Be("Runs");
    }

    [Test]
    public async Task An_optional_left_out_is_not_sent_and_one_set_to_null_is()
    {
        var host = new Host("""{"data":{"operations":{"runs":[]}}}""");
        var client = Client(host);

        await client.SendAsync(Runs, new RunsVariables { Take = 1 });
        await client.SendAsync(Runs, new RunsVariables { Take = 1, State = null });
        await client.SendAsync(Runs, new RunsVariables { Take = 1, State = "FAILED" });

        host.Bodies.Select(b => b["variables"]!.ToJsonString())
            .Should()
            .Equal(
                """{"take":1}""",
                """{"take":1,"state":null}""",
                """{"take":1,"state":"FAILED"}"""
            );
    }

    [Test]
    public async Task A_refusal_is_an_answer_with_its_code_whatever_the_status()
    {
        var host = new Host(
            """{"errors":[{"message":"Not registered here.","extensions":{"code":"CLIENT_OUTDATED"}}]}""",
            HttpStatusCode.BadRequest
        );

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 1 });

        answer.Succeeded.Should().BeFalse();
        answer.Failure.Should().BeNull();
        answer.Code.Should().Be("CLIENT_OUTDATED");
        answer.Message.Should().Be("Not registered here.");
    }

    [Test]
    public async Task A_response_with_no_GraphQL_answer_is_a_transport_failure()
    {
        var host = new Host("<html>bad gateway</html>", HttpStatusCode.BadGateway);

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 1 });

        answer.Failure.Should().NotBeNull();
        answer.Failure!.Status.Should().Be(502);
        answer.Code.Should().Be(TraxTransportFailure.Code);
    }

    [Test]
    public async Task A_status_that_is_not_success_with_data_and_no_errors_is_a_transport_failure()
    {
        var host = new Host(
            """{"data":{"operations":{"runs":[]}}}""",
            HttpStatusCode.InternalServerError
        );

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 1 });

        answer.Failure!.Status.Should().Be(500, "the data of a failed response is not an answer");
        answer.Data.Should().BeNull();
    }

    [Test]
    public async Task A_host_that_cannot_be_reached_is_a_transport_failure_not_an_exception()
    {
        var host = new Host(new HttpRequestException("Connection refused"));

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 1 });

        answer.Failure!.Message.Should().Contain("Connection refused");
    }

    [Test]
    public async Task Data_that_does_not_fit_the_result_is_a_failure_not_an_exception()
    {
        var host = new Host("""{"data":{"operations":{"runs":[{"id":"not a number"}]}}}""");

        var answer = await Client(host).SendAsync(Runs, new RunsVariables { Take = 1 });

        answer.Failure!.Message.Should().Contain("does not fit");
    }

    [Test]
    public async Task A_stream_the_socket_drops_is_subscribed_again_and_says_so()
    {
        var socket = new HandSocket();
        var time = new FakeTimeProvider();
        var events = new EventLog<ChangedResult>();
        var client = new TraxClient(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new Host("{}")),
                    Endpoint = new Uri("https://host/trax/graphql"),
                    Socket = socket,
                }
            ),
            new TraxClientOptions { Time = time }
        );

        using var stream = client.Subscribe(Changed, TraxNoVariables.Instance, events.Add);
        var first = await socket.Subscriptions.Take(Wait);
        first
            .Payload.ToJsonString()
            .Should()
            .Be("""{"id":"trax_console_Changed_abc","operationName":"Changed","variables":{}}""");
        first.Sink.Started();
        first.Sink.Next(Frame("WORK_QUEUE"));
        first.Sink.Error(
            new SocketClosedException("The socket was closed while subscribed.", 1006)
        );

        time.Advance(TimeSpan.FromSeconds(1));
        var second = await socket.Subscriptions.Take(Wait);
        second.Sink.Started();
        second.Sink.Next(Frame("DEAD_LETTER"));

        (await events.Take(4))
            .Should()
            .SatisfyRespectively(
                e =>
                    e.Should()
                        .BeOfType<TraxStreamEvent<ChangedResult>.Next>()
                        .Which.Data.OnDataChanged.Domain.Should()
                        .Be("WORK_QUEUE"),
                e => e.Should().BeOfType<TraxStreamEvent<ChangedResult>.Interrupted>(),
                e => e.Should().BeOfType<TraxStreamEvent<ChangedResult>.Resumed>(),
                e =>
                    e.Should()
                        .BeOfType<TraxStreamEvent<ChangedResult>.Next>()
                        .Which.Data.OnDataChanged.Domain.Should()
                        .Be("DEAD_LETTER")
            );
    }

    [Test]
    public async Task A_connection_the_host_refuses_ends_the_stream_instead_of_retrying()
    {
        var socket = new HandSocket();
        var events = new EventLog<ChangedResult>();
        var client = new TraxClient(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new Host("{}")),
                    Endpoint = new Uri("https://host/trax/graphql"),
                    Socket = socket,
                }
            )
        );

        using var stream = client.Subscribe(Changed, TraxNoVariables.Instance, events.Add);
        var opened = await socket.Subscriptions.Take(Wait);
        opened.Sink.Error(new SocketClosedException("The socket was closed: 4403 Forbidden", 4403));

        var ended = (await events.Take(1)).Single();
        ended
            .Should()
            .BeOfType<TraxStreamEvent<ChangedResult>.Ended>()
            .Which.Failure!.Message.Should()
            .Contain("4403");
        opened.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task A_frame_with_errors_ends_the_stream_with_them()
    {
        var socket = new HandSocket();
        var events = new EventLog<ChangedResult>();
        var client = new TraxClient(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new Host("{}")),
                    Endpoint = new Uri("https://host/trax/graphql"),
                    Socket = socket,
                }
            )
        );

        using var stream = client.Subscribe(Changed, TraxNoVariables.Instance, events.Add);
        var opened = await socket.Subscriptions.Take(Wait);
        opened.Sink.Next(
            (JsonObject)
                JsonNode.Parse(
                    """{"errors":[{"message":"No.","extensions":{"code":"AUTH_NOT_AUTHORIZED"}}]}"""
                )!
        );

        var ended = (await events.Take(1)).Single();
        ended
            .Should()
            .BeOfType<TraxStreamEvent<ChangedResult>.Ended>()
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be("AUTH_NOT_AUTHORIZED");
        opened.Disposed.Should().BeTrue();
    }

    private static JsonObject Frame(string domain) =>
        new()
        {
            ["data"] = new JsonObject
            {
                ["onDataChanged"] = new JsonObject { ["domain"] = domain },
            },
        };

    private static TraxClient Client(Host host, Func<TraxDocument, bool>? byId = null) =>
        new(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(host),
                    Endpoint = new Uri("https://host/trax/graphql"),
                    ById = byId,
                }
            )
        );

    // A host that answers every POST with one body, and keeps what it was sent.
    private sealed class Host : HttpMessageHandler
    {
        private readonly string? _answer;
        private readonly HttpStatusCode _status;
        private readonly Exception? _error;

        public Host(string answer, HttpStatusCode status = HttpStatusCode.OK)
        {
            _answer = answer;
            _status = status;
        }

        public Host(Exception error) => _error = error;

        public List<JsonObject> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Bodies.Add(
                (JsonObject)
                    JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!
            );
            if (_error is not null)
                throw _error;
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_answer!, Encoding.UTF8, "application/json"),
            };
        }
    }

    // A socket each subscription is handed to the test through.
    private sealed class HandSocket : ISubscriptionSocket
    {
        public Signals<Opened> Subscriptions { get; } = new();

        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink)
        {
            var opened = new Opened(payload, sink);
            Subscriptions.Add(opened);
            return opened;
        }
    }

    private sealed class Opened(JsonObject payload, ISubscriptionSink sink) : IDisposable
    {
        public JsonObject Payload { get; } = payload;
        public ISubscriptionSink Sink { get; } = sink;
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    // Items a test awaits items from, so every wait is on a signal.
    private sealed class Signals<T>
    {
        private readonly System.Threading.Channels.Channel<T> _items =
            System.Threading.Channels.Channel.CreateUnbounded<T>();

        public void Add(T item) => _items.Writer.TryWrite(item);

        public async Task<T> Take(TimeSpan wait)
        {
            using var timeout = new CancellationTokenSource(wait);
            return await _items.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class EventLog<T>
    {
        private readonly Signals<TraxStreamEvent<T>> _events = new();

        public void Add(TraxStreamEvent<T> e) => _events.Add(e);

        public async Task<IReadOnlyList<TraxStreamEvent<T>>> Take(int count)
        {
            var taken = new List<TraxStreamEvent<T>>();
            for (var i = 0; i < count; i++)
                taken.Add(await _events.Take(Wait));
            return taken;
        }
    }
}
