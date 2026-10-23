using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// The credential goes to the endpoint the operator named and nowhere else: a host that answers with a redirect gets a
/// failure naming where it pointed, and the HTTP client does not follow it.
/// </summary>
[TestFixture]
public class RedirectTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    [Test]
    public async Task A_redirect_is_a_failure_and_the_key_goes_no_further()
    {
        using var elsewhere = new Listener(_ => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        using var host = new Listener(_ =>
            $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{elsewhere.Port}/trax/graphql\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n"
        );
        await using var session = ConsoleSession.Open(Target(host.Port));

        var version = await session.CheckVersionAsync().WaitAsync(Deadline);

        version
            .Should()
            .BeOfType<VersionCheck.Failed>()
            .Which.Message.Should()
            .Contain($"redirected to http://127.0.0.1:{elsewhere.Port}/trax/graphql")
            .And.Contain("follows no redirect");
        host.Requests.Should().ContainSingle().Which.Should().Contain("X-Api-Key: sk_live_1");
        await Task.Delay(200);
        elsewhere.Requests.Should().BeEmpty();
    }

    // ClientWebSocket follows a redirect of the upgrade request, and GraphQLSubscriptionSocket connects through its
    // own handler, so connection_init goes wherever the host points. The fix belongs in Trax.Client.
    [Test]
    [Ignore("GraphQLSubscriptionSocket follows redirects; needs a Trax.Client change")]
    public async Task The_socket_follows_no_redirect_either()
    {
        using var elsewhere = new Listener(_ =>
            "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"
        );
        using var host = new Listener(_ =>
            $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{elsewhere.Port}/trax/graphql\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n"
        );
        await using var session = ConsoleSession.Open(Target(host.Port));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var stream = session.Client.OnDataChanged(e =>
        {
            if (
                e
                is TraxStreamEvent<OnDataChangedResult>.Interrupted
                    or TraxStreamEvent<OnDataChangedResult>.Ended
            )
                stopped.TrySetResult();
        });
        await stopped.Task.WaitAsync(Deadline);

        host.Requests.Should().NotBeEmpty();
        await Task.Delay(200);
        elsewhere.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task The_location_is_shown_on_one_line_without_its_userinfo()
    {
        var handler = new Answering(
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers =
                {
                    Location = new Uri("https://me:secret@evil.example/x\u001b]0;t\u0007"),
                },
            }
        );
        using var http = new HttpClient(new NoRedirects { InnerHandler = handler });

        var thrown = await FluentActions
            .Awaiting(() => http.GetAsync("https://ops.example.com/trax/graphql"))
            .Should()
            .ThrowAsync<HttpRequestException>();

        thrown
            .Which.Message.Should()
            .Contain("https://evil.example/")
            .And.NotContain("secret")
            .And.NotContain("\u001b");
    }

    private static ConsoleTarget Target(int port) =>
        ConsoleTarget.Resolve(
            $"http://127.0.0.1:{port}",
            ConsoleTarget.DefaultGraphQLPath,
            apiKeyFile: null,
            tokenFile: null,
            sendText: false,
            n => n == ConsoleTarget.ApiKeyVariable ? "sk_live_1" : null
        );

    private sealed class Answering(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(response);
    }

    // A loopback port that keeps the head of every request it is sent and answers each with one fixed response.
    private sealed class Listener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<string> _requests = [];
        private readonly CancellationTokenSource _stop = new();

        public Listener(Func<string, string> answer)
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            var stream = client.GetStream();
                            var head = new StringBuilder();
                            var buffer = new byte[4096];
                            while (!head.ToString().Contains("\r\n\r\n"))
                            {
                                var read = await stream.ReadAsync(buffer, _stop.Token);
                                if (read == 0)
                                    break;
                                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                            }
                            lock (_requests)
                                _requests.Add(head.ToString());
                            await stream.WriteAsync(
                                Encoding.ASCII.GetBytes(answer(head.ToString()))
                            );
                        }
                    });
                }
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                    return _requests.ToList();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
