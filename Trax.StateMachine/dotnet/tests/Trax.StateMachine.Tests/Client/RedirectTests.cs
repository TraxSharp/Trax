using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// A client whose handler follows redirects is refused before it sends anything, so neither the transport nor the
/// machine server ever lets a request leave for another host; an answer from another host, through a handler the
/// transport cannot see into, fails the request.
/// </summary>
public class RedirectTests
{
    private const string Answer = """{"data":{"x":1}}""";

    private static readonly TraxDocument<TraxNoVariables, JsonObject> Query = new(
        "Q",
        TraxOperationKind.Query,
        "q-1",
        "query Q { x }"
    );

    private static TraxHttpTransport Transport(HttpClient http, Uri endpoint) =>
        new(new TraxHttpTransportOptions { Http = http, Endpoint = endpoint });

    private static HttpClient NoRedirects() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false });

    [Test]
    public async Task A_default_client_is_refused_and_never_reaches_the_other_host()
    {
        await using var other = LoopbackHttpHost.Answering(Answer);
        await using var endpoint = LoopbackHttpHost.RedirectingTo(other.Endpoint);

        var build = () => Transport(new HttpClient(), endpoint.Endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*follows redirects*");
        endpoint.Received.Should().Be(0);
        other.Received.Should().Be(0);
    }

    [Test]
    public void A_handler_that_follows_redirects_behind_a_delegating_handler_is_refused()
    {
        using var http = new HttpClient(
            new PassThrough { InnerHandler = new SocketsHttpHandler() }
        );

        var build = () => Transport(http, new Uri("https://api.example.com/graphql"));

        build.Should().Throw<ArgumentException>().WithMessage("*follows redirects*");
    }

    [Test]
    public async Task An_answer_from_another_host_through_a_handler_that_followed_a_redirect_is_a_failure()
    {
        await using var other = LoopbackHttpHost.Answering(Answer);
        await using var endpoint = LoopbackHttpHost.RedirectingTo(other.Endpoint);
        using var http = new HttpClient(new FollowsItself());

        var answer = await Transport(http, endpoint.Endpoint)
            .ExecuteAsync(new TraxRequest(Query, new JsonObject()));

        answer.Failure.Should().NotBeNull();
        answer.Failure!.Message.Should().Contain("redirected");
        answer.Data.Should().BeNull();
    }

    [Test]
    public async Task A_client_with_redirects_off_never_reaches_the_other_host()
    {
        await using var other = LoopbackHttpHost.Answering(Answer);
        await using var endpoint = LoopbackHttpHost.RedirectingTo(other.Endpoint);
        using var http = NoRedirects();

        var answer = await Transport(http, endpoint.Endpoint)
            .ExecuteAsync(new TraxRequest(Query, new JsonObject()));

        answer.Failure.Should().NotBeNull();
        answer.Failure!.Status.Should().Be(307);
        other.Received.Should().Be(0);
    }

    [Test]
    public async Task An_answer_from_the_endpoints_own_host_is_read()
    {
        await using var endpoint = LoopbackHttpHost.Answering(Answer);
        using var http = NoRedirects();

        var answer = await Transport(http, endpoint.Endpoint)
            .ExecuteAsync(new TraxRequest(Query, new JsonObject()));

        answer.Failure.Should().BeNull();
        answer.Data!["x"]!.GetValue<int>().Should().Be(1);
    }

    [Test]
    public async Task The_machine_server_refuses_a_default_client_and_never_reaches_the_other_host()
    {
        await using var other = LoopbackHttpHost.Answering(Answer);
        await using var endpoint = LoopbackHttpHost.RedirectingTo(other.Endpoint);

        var build = () => MachineServer(new HttpClient(), endpoint.Endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*follows redirects*");
        endpoint.Received.Should().Be(0);
        other.Received.Should().Be(0);
    }

    [Test]
    public async Task The_machine_server_fails_a_request_answered_after_a_redirect()
    {
        await using var other = LoopbackHttpHost.Answering(
            """{"data":{"machines":{"turnstile":{"get":{"problem":{"code":"NOT_FOUND","message":"m"}}}}}}"""
        );
        await using var endpoint = LoopbackHttpHost.RedirectingTo(other.Endpoint);
        using var http = new HttpClient(new FollowsItself());
        var server = MachineServer(http, endpoint.Endpoint);

        var get = () => server.GetAsync("i-1");

        (await get.Should().ThrowAsync<HttpRequestException>()).WithMessage("*redirected*");
    }

    private static GraphQLMachineServer MachineServer(HttpClient http, Uri endpoint) =>
        new(
            new GraphQLMachineServerOptions
            {
                Http = http,
                Endpoint = endpoint,
                Machine = new ClientManifestMachine
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
                },
                Socket = new NoSocket(),
            }
        );

    private sealed class PassThrough : DelegatingHandler { }

    // Follows a 307 itself, as a platform's own handler might: the transport cannot tell that it does.
    private sealed class FollowsItself : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(
            new SocketsHttpHandler { AllowAutoRedirect = false }
        );

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = await _inner.SendAsync(request, cancellationToken);
            if (
                response.StatusCode != HttpStatusCode.TemporaryRedirect
                || response.Headers.Location is not { } location
            )
                return response;
            response.Dispose();
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var again = new HttpRequestMessage(request.Method, location)
            {
                Content = new ByteArrayContent(body),
            };
            return await _inner.SendAsync(again, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NoSocket : ISubscriptionSocket
    {
        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink) =>
            throw new NotSupportedException();
    }

    /// <summary>A loopback HTTP host that answers every request with one fixed response and counts the requests.</summary>
    private sealed class LoopbackHttpHost : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _response;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private int _received;

        private LoopbackHttpHost(string response)
        {
            _response = Encoding.UTF8.GetBytes(response);
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public Uri Endpoint =>
            new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/graphql");

        public int Received => Volatile.Read(ref _received);

        public static LoopbackHttpHost Answering(string json) =>
            new(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}"
            );

        public static LoopbackHttpHost RedirectingTo(Uri target) =>
            new(
                $"HTTP/1.1 307 Temporary Redirect\r\nLocation: {target}\r\nContent-Length: 0\r\n"
                    + "Connection: close\r\n\r\n"
            );

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex)
                    when (ex
                            is OperationCanceledException
                                or SocketException
                                or ObjectDisposedException
                    )
                {
                    return;
                }
                using (client)
                {
                    var stream = client.GetStream();
                    await ReadRequestAsync(stream, _stop.Token);
                    Interlocked.Increment(ref _received);
                    await stream.WriteAsync(_response, _stop.Token);
                }
            }
        }

        // Reads the head and as much body as its Content-Length says.
        private static async Task ReadRequestAsync(NetworkStream stream, CancellationToken token)
        {
            var read = new List<byte>();
            var buffer = new byte[4096];
            int end;
            while ((end = HeadEnd(read)) < 0)
            {
                var n = await stream.ReadAsync(buffer, token);
                if (n == 0)
                    return;
                read.AddRange(buffer.AsSpan(0, n));
            }
            var head = Encoding.ASCII.GetString(read.ToArray(), 0, end);
            var length = head.Split("\r\n")
                .Select(line => line.Split(':', 2))
                .Where(parts =>
                    parts.Length == 2
                    && parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                )
                .Select(parts => int.Parse(parts[1].Trim()))
                .FirstOrDefault();
            var body = read.Count - (end + 4);
            while (body < length)
            {
                var n = await stream.ReadAsync(buffer, token);
                if (n == 0)
                    return;
                body += n;
            }
        }

        private static int HeadEnd(List<byte> read)
        {
            for (var i = 0; i + 3 < read.Count; i++)
                if (
                    read[i] == '\r'
                    && read[i + 1] == '\n'
                    && read[i + 2] == '\r'
                    && read[i + 3] == '\n'
                )
                    return i;
            return -1;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // The host was stopped mid-request.
            }
            _stop.Dispose();
        }
    }
}
