using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What <see cref="TraxHttpTransport"/> accepts as an endpoint, and how it reads a body that is slow, broken or too
/// large: every one is a failure, never a throw or a hang, and the response is let go either way.
/// </summary>
public class TraxHttpTransportTests
{
    private static readonly TraxDocument<TraxNoVariables, JsonObject> Query = new(
        "Q",
        TraxOperationKind.Query,
        "q-1",
        "query Q { x }"
    );

    private static TraxRequest Request => new(Query, new JsonObject());

    private static TraxHttpTransport Transport(
        HttpMessageHandler handler,
        TimeSpan? timeout = null,
        long max = TraxHttpTransportOptions.DefaultMaxResponseSize
    ) =>
        new(
            new TraxHttpTransportOptions
            {
                Http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(100) },
                Endpoint = new Uri("https://api.example.com/trax/graphql"),
                MaxResponseSize = max,
            }
        );

    [TestCase("http://api.example.com/trax/graphql")]
    [TestCase("http://10.0.0.5/trax/graphql")]
    public void A_cleartext_endpoint_on_another_host_is_refused(string endpoint)
    {
        var build = () =>
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
                    Endpoint = new Uri(endpoint),
                }
            );

        build.Should().Throw<ArgumentException>().WithMessage("*not over TLS*");
    }

    [Test]
    public void A_relative_endpoint_is_checked_against_the_clients_base_address()
    {
        TraxHttpTransport Build(string baseAddress) =>
            new(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                    {
                        BaseAddress = new Uri(baseAddress),
                    },
                    Endpoint = new Uri("trax/graphql", UriKind.Relative),
                }
            );

        ((Action)(() => Build("http://api.example.com/")))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*not over TLS*");
        ((Action)(() => Build("https://api.example.com/"))).Should().NotThrow();
        ((Action)(() => Build("http://localhost:5000/"))).Should().NotThrow();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void A_relative_endpoint_with_no_base_address_is_refused(bool allowInsecure)
    {
        var build = () =>
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
                    Endpoint = new Uri("trax/graphql", UriKind.Relative),
                    AllowInsecureTransport = allowInsecure,
                }
            );

        build.Should().Throw<ArgumentException>().WithMessage("*relative*base address*");
    }

    [Test]
    public async Task A_cancellation_the_caller_asked_for_throws()
    {
        using var cancel = new CancellationTokenSource();
        var answering = Transport(new Never()).ExecuteAsync(Request, cancel.Token);

        await cancel.CancelAsync();

        await ((Func<Task>)(() => answering)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task A_host_that_never_answers_is_a_failure_once_the_timeout_passes()
    {
        var answer = await Transport(new Never(), TimeSpan.FromMilliseconds(200))
            .ExecuteAsync(Request)
            .WaitAsync(TimeSpan.FromSeconds(10));

        answer.Failure!.Message.Should().Contain("did not answer");
    }

    [Test]
    public async Task A_body_that_trickles_in_is_a_failure_once_the_timeout_passes()
    {
        var body = new Trickle();

        var answer = await Transport(new Answers(body), TimeSpan.FromMilliseconds(500))
            .ExecuteAsync(Request)
            .WaitAsync(TimeSpan.FromSeconds(10));

        answer.Failure!.Message.Should().Contain("did not answer");
        body.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task A_body_that_breaks_off_is_a_failure_and_the_response_is_let_go()
    {
        var body = new BreaksOff(Encoding.UTF8.GetBytes("""{"data":{"x":"""));

        var answer = await Transport(new Answers(body)).ExecuteAsync(Request);

        answer.Failure!.Message.Should().Contain("could not be reached");
        body.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task A_body_over_the_limit_lets_the_response_go_unread()
    {
        var body = new BreaksOff(new byte[4096]);

        var answer = await Transport(new Answers(body, length: 4096), max: 1024)
            .ExecuteAsync(Request);

        answer.Failure!.Message.Should().Contain("larger than 1024");
        body.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task An_answer_with_a_member_twice_is_a_failure_not_an_exception()
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes("""{"data":{},"data":{}}"""));

        var answer = await Transport(new Answers(body)).ExecuteAsync(Request);

        answer.Failure.Should().NotBeNull();
    }

    // Never answers: waits for the request to be cancelled.
    private sealed class Never : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // allowed-delay: ends only when the request is cancelled, which is what the test waits for.
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    // Answers 200 at once, with the body read from a stream.
    private sealed class Answers(Stream body, long? length = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = length;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
            );
        }
    }

    // A body that cannot say its length, and remembers being let go.
    private abstract class Body : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    // One space every 50 ms, forever: always valid JSON so far, never done.
    private sealed class Trickle : Body
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            // allowed-delay: the trickle itself; the test waits on the transport's timeout, not on this.
            await Task.Delay(50, cancellationToken);
            buffer.Span[0] = (byte)' ';
            return 1;
        }
    }

    // Some bytes, then the connection breaks.
    private sealed class BreaksOff(byte[] first) : Body
    {
        private bool _sent;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_sent)
                throw new HttpIOException(
                    HttpRequestError.ResponseEnded,
                    "The response ended early."
                );
            _sent = true;
            var n = Math.Min(buffer.Length, first.Length);
            first.AsSpan(0, n).CopyTo(buffer.Span);
            return ValueTask.FromResult(n);
        }
    }
}
