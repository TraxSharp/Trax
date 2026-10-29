using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What the GraphQL server and socket accept as an endpoint, and what they read from one: a token travels only over
/// TLS unless the host is this machine or the caller says otherwise.
/// </summary>
public class TransportTests
{
    private static readonly ClientManifestMachine Turnstile = new()
    {
        Id = "turnstile",
        IrHash = MachinePredictor.For(DeclarativeTurnstile.Built).IrHash,
        Ir = (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!,
        Documents =
        [
            new ClientManifestDocument
            {
                Id = "get-1",
                Operation = "get",
                Document = "query Get($id: UUID!) { x }",
            },
            new ClientManifestDocument
            {
                Id = "changed-1",
                Operation = "changed",
                Document = "subscription Changed($id: UUID!) { x }",
            },
        ],
    };

    private sealed class NoSocket : ISubscriptionSocket
    {
        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink) =>
            throw new NotSupportedException();
    }

    private static GraphQLMachineServer Server(
        string endpoint,
        bool allowInsecure = false,
        HttpClient? http = null
    ) =>
        new(
            new GraphQLMachineServerOptions
            {
                Http = http ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
                Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute),
                Machine = Turnstile,
                Socket = new NoSocket(),
                AllowInsecureTransport = allowInsecure,
            }
        );

    private static GraphQLSubscriptionSocket Socket(string endpoint, bool allowInsecure = false) =>
        new(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = new Uri(endpoint),
                AllowInsecureTransport = allowInsecure,
            }
        );

    [TestCase("http://api.example.com/trax/graphql")]
    [TestCase("http://10.0.0.5/trax/graphql")]
    public void A_cleartext_endpoint_on_another_host_is_refused(string endpoint)
    {
        var build = () => Server(endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*not over TLS*");
    }

    [TestCase("ws://api.example.com/trax/graphql")]
    [TestCase("ws://[2001:db8::1]/trax/graphql")]
    public void A_cleartext_socket_on_another_host_is_refused(string endpoint)
    {
        var build = () => Socket(endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*not over TLS*");
    }

    [Test]
    public void The_refusal_names_the_host_but_not_the_query()
    {
        var build = () => Server("http://api.example.com/graphql?access_token=SECRET");

        build.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("SECRET");
    }

    [TestCase("https://api.example.com/trax/graphql")]
    [TestCase("http://localhost:5000/trax/graphql")]
    [TestCase("http://127.0.0.1:5000/trax/graphql")]
    [TestCase("http://127.4.5.6:5000/trax/graphql")]
    [TestCase("http://[::1]:5000/trax/graphql")]
    public void Tls_or_a_loopback_host_is_accepted(string endpoint)
    {
        var build = () => Server(endpoint);

        build.Should().NotThrow();
    }

    [TestCase("wss://api.example.com/trax/graphql")]
    [TestCase("ws://localhost:5000/trax/graphql")]
    [TestCase("ws://127.0.0.1:5000/trax/graphql")]
    [TestCase("ws://[::1]:5000/trax/graphql")]
    public void A_socket_over_tls_or_to_a_loopback_host_is_accepted(string endpoint)
    {
        var build = () => Socket(endpoint);

        build.Should().NotThrow();
    }

    [Test]
    public void The_caller_may_accept_cleartext_explicitly()
    {
        var server = () => Server("http://api.example.com/trax/graphql", allowInsecure: true);
        var socket = () => Socket("ws://api.example.com/trax/graphql", allowInsecure: true);

        server.Should().NotThrow();
        socket.Should().NotThrow();
    }

    [Test]
    public void A_relative_endpoint_is_checked_against_the_clients_base_address()
    {
        var cleartext = () =>
            Server(
                "trax/graphql",
                http: new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri("http://api.example.com/"),
                }
            );
        var tls = () =>
            Server(
                "trax/graphql",
                http: new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri("https://api.example.com/"),
                }
            );

        cleartext.Should().Throw<ArgumentException>();
        tls.Should().NotThrow();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void A_relative_endpoint_with_no_base_address_is_refused(bool allowInsecure)
    {
        var server = () => Server("trax/graphql", allowInsecure);
        var socket = () =>
            new GraphQLSubscriptionSocket(
                new GraphQLSubscriptionSocketOptions
                {
                    Endpoint = new Uri("trax/graphql", UriKind.Relative),
                    AllowInsecureTransport = allowInsecure,
                }
            );

        server.Should().Throw<ArgumentException>().WithMessage("*relative*base address*");
        socket.Should().Throw<ArgumentException>().WithMessage("*relative*");
    }

    // Answers every request with a body of `size` bytes, with or without its length up front.
    private sealed class LargeAnswer(int size, bool sayLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = Encoding.UTF8.GetBytes(
                "{\"data\":{\"pad\":\"" + new string('x', size) + "\"}}"
            );
            HttpContent content = sayLength
                ? new ByteArrayContent(body)
                : new StreamContent(new UnseekableStream(body));
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
            );
        }
    }

    // A stream that cannot say its length, so the content goes out without one.
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_response_larger_than_the_limit_fails_the_request(bool sayLength)
    {
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = new HttpClient(new LargeAnswer(64 * 1024, sayLength)),
                Endpoint = new Uri("https://api.example.com/graphql"),
                Machine = Turnstile,
                Socket = new NoSocket(),
                MaxResponseSize = 1024,
            }
        );

        var get = () => server.GetAsync("i-1");

        (await get.Should().ThrowAsync<HttpRequestException>()).WithMessage("*larger than 1024*");
    }

    [Test]
    public async Task A_response_within_the_limit_is_read()
    {
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = new HttpClient(new LargeAnswer(512, sayLength: false)),
                Endpoint = new Uri("https://api.example.com/graphql"),
                Machine = Turnstile,
                Socket = new NoSocket(),
                MaxResponseSize = 1024,
            }
        );

        var answer = await server.GetAsync("i-1");

        answer.Problem!.Code.Should().Be("NOT_FOUND");
    }

    [Test]
    public void The_limits_are_sixteen_mebibytes_by_default()
    {
        new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("wss://h/graphql") }
            .MaxMessageSize.Should()
            .Be(16 * 1024 * 1024);
        new GraphQLMachineServerOptions
        {
            Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
            Endpoint = new Uri("https://h/graphql"),
            Machine = Turnstile,
            Socket = new NoSocket(),
        }
            .MaxResponseSize.Should()
            .Be(16 * 1024 * 1024);
    }

    [Test]
    public void A_request_timeout_that_is_neither_positive_nor_infinite_is_refused()
    {
        var build = () =>
            new GraphQLMachineServer(
                new GraphQLMachineServerOptions
                {
                    Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
                    Endpoint = new Uri("https://h/graphql"),
                    Machine = Turnstile,
                    Socket = new NoSocket(),
                    RequestTimeout = TimeSpan.Zero,
                }
            );
        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class Sink : ISubscriptionSink
    {
        public TaskCompletionSource<Exception> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Next(JsonObject value) { }

        public void Error(Exception error) => Failed.TrySetResult(error);

        public void Complete() { }
    }

    [Test]
    public async Task A_socket_message_larger_than_the_limit_fails_the_connection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var host = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync();
            var stream = tcp.GetStream();
            await HandshakeAsync(stream);
            using var socket = WebSocket.CreateFromStream(
                stream,
                new WebSocketCreationOptions
                {
                    IsServer = true,
                    SubProtocol = "graphql-transport-ws",
                }
            );
            var buffer = new byte[4096];
            await socket.ReceiveAsync(buffer, CancellationToken.None);
            await socket.SendAsync(
                Encoding.UTF8.GetBytes("{\"type\":\"connection_ack\"}"),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
            var large = Encoding.UTF8.GetBytes(
                "{\"type\":\"ping\",\"payload\":\"" + new string('x', 64 * 1024) + "\"}"
            );
            try
            {
                await socket.SendAsync(
                    large,
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
                await socket.ReceiveAsync(buffer, CancellationToken.None);
            }
            catch (Exception)
            {
                // The client gave up on the connection.
            }
        });
        await using var client = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = new Uri($"ws://127.0.0.1:{port}/graphql"),
                MaxMessageSize = 1024,
            }
        );
        var sink = new Sink();

        using var subscription = client.Subscribe(
            new JsonObject { ["id"] = "changed-1", ["variables"] = new JsonObject() },
            sink
        );
        var error = await sink.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        error.Message.Should().Contain("larger than 1024");
        await host.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // The server half of the opening handshake (RFC 6455), so a test needs no web host.
    private static async Task HandshakeAsync(NetworkStream stream)
    {
        var request = new StringBuilder();
        var one = new byte[1];
        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0)
                throw new IOException("The client closed before its handshake ended.");
            request.Append((char)one[0]);
        }
        var key = request
            .ToString()
            .Split("\r\n")
            .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1]
            .Trim();
        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
        );
        var response =
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + $"Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: graphql-transport-ws\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
    }
}
