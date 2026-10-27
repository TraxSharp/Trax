using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// What the scheduler requires of the way to a runner, since a runner's status codes and error
/// bodies are not signed: <c>https://</c> off loopback unless the host sets
/// <c>AllowInsecureTransport</c>, and a run's answer read only up to
/// <see cref="RemoteRunOptions.MaxResponseBodyBytes"/>, before its signature is checked.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RemoteRunnerTransportTests
{
    private const string Adr = "docs/adr/0006-a-runner-requires-an-authorization-posture.md";

    #region Base URL

    [TestCase("http://runner.internal/trax/run", false)]
    [TestCase("http://10.0.0.4:8080/trax/run", false)]
    [TestCase("https://runner.internal/trax/run", true)]
    [TestCase("http://localhost:5205/trax/run", true)]
    [TestCase("http://127.0.0.1:5205/trax/run", true)]
    [TestCase("http://[::1]:5205/trax/run", true)]
    public void UseRemoteRun_PlainHttpToAnotherHost_RefusesToBuild(string baseUrl, bool builds)
    {
        var act = BuildWith(s => s.UseRemoteRun(o => o.BaseUrl = baseUrl));

        if (builds)
            act.Should().NotThrow();
        else
            act.Should()
                .Throw<InvalidOperationException>(
                    "status codes and error bodies are not signed, so the way to a runner is TLS ({0})",
                    Adr
                )
                .WithMessage("*not reached over TLS*RemoteRunOptions.AllowInsecureTransport*");
    }

    [Test]
    public void UseRemoteRun_PlainHttpWithAllowInsecureTransport_Builds()
    {
        var act = BuildWith(s =>
            s.UseRemoteRun(o =>
            {
                o.BaseUrl = "http://runner.internal/trax/run";
                o.AllowInsecureTransport = true;
            })
        );

        act.Should().NotThrow("the host opted in by name ({0})", Adr);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void UseRemoteRun_AResponseLimitThatIsNotPositive_RefusesToBuild(long limit)
    {
        var act = BuildWith(s =>
            s.UseRemoteRun(o =>
            {
                o.BaseUrl = "https://runner.internal/trax/run";
                o.MaxResponseBodyBytes = limit;
            })
        );

        act.Should()
            .Throw<InvalidOperationException>("an answer is read up to a positive limit ({0})", Adr)
            .WithMessage("*MaxResponseBodyBytes*");
    }

    [TestCase("http://runner.internal/trax/execute", false)]
    [TestCase("https://runner.internal/trax/execute", true)]
    [TestCase("http://localhost:5205/trax/execute", true)]
    public void UseRemoteWorkers_PlainHttpToAnotherHost_RefusesToBuild(string baseUrl, bool builds)
    {
        var act = BuildWith(s =>
            s.UseRemoteWorkers(o => o.BaseUrl = baseUrl, routing => routing.ForTrain<ITestTrain>())
        );

        if (builds)
            act.Should().NotThrow();
        else
            act.Should()
                .Throw<InvalidOperationException>(
                    "a queued job's answer is not signed at all, so the way to a runner is TLS ({0})",
                    Adr
                )
                .WithMessage("*not reached over TLS*RemoteWorkerOptions.AllowInsecureTransport*");
    }

    [Test]
    public void UseRemoteWorkers_PlainHttpWithAllowInsecureTransport_Builds()
    {
        var act = BuildWith(s =>
            s.UseRemoteWorkers(
                o =>
                {
                    o.BaseUrl = "http://runner.internal/trax/execute";
                    o.AllowInsecureTransport = true;
                },
                routing => routing.ForTrain<ITestTrain>()
            )
        );

        act.Should().NotThrow("the host opted in by name ({0})", Adr);
    }

    private static Action BuildWith(
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> scheduler
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler)
            );
    }

    #endregion

    #region Response size

    [Test]
    public async Task A_response_declared_larger_than_the_limit_fails_the_run()
    {
        var body = JsonSerializer.Serialize(
            new RemoteRunResponse(MetadataId: 1, OutputJson: new string('x', 4096))
        );
        var executor = Executor(new StringHandler(HttpStatusCode.OK, body), limit: 1024);

        var act = () => Run(executor);

        await act.Should()
            .ThrowAsync<RemoteRunException>("an answer over the limit is not read ({0})", Adr)
            .WithMessage("*MaxResponseBodyBytes*");
    }

    [Test]
    public async Task A_response_that_never_ends_is_read_only_to_the_limit()
    {
        var stream = new EndlessStream();
        var executor = Executor(new StreamingHandler(HttpStatusCode.OK, stream), limit: 64 * 1024);

        var act = () => Run(executor);

        await act.Should()
            .ThrowAsync<RemoteRunException>("an answer over the limit is not read ({0})", Adr)
            .WithMessage("*MaxResponseBodyBytes*");
        stream
            .Served.Should()
            .BeLessThan(
                64 * 1024 + 2 * 81920,
                "a body with no declared length is abandoned at the limit ({0})",
                Adr
            );
    }

    [Test]
    public async Task An_error_response_that_never_ends_has_only_its_start_read()
    {
        var stream = new EndlessStream();
        var executor = Executor(
            new StreamingHandler(HttpStatusCode.InternalServerError, stream),
            limit: RemoteRunOptions.DefaultMaxResponseBodyBytes
        );

        var act = () => Run(executor);

        var ex = (await act.Should().ThrowAsync<RemoteRunException>()).Which;
        ex.Message.Should().Contain("HTTP 500").And.EndWith("... (truncated)");
        stream
            .Served.Should()
            .BeLessThan(2 * 81920, "an unauthenticated error body is only logged ({0})", Adr);
    }

    [Test]
    public async Task A_response_within_the_limit_is_read()
    {
        var body = JsonSerializer.Serialize(new RemoteRunResponse(MetadataId: 9));
        var executor = Executor(new StringHandler(HttpStatusCode.OK, body), limit: 1024);

        var result = await Run(executor);

        result.MetadataId.Should().Be(9, "an answer within the limit is read ({0})", Adr);
    }

    private static HttpRunExecutor Executor(HttpMessageHandler handler, long limit) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            new RemoteRunOptions
            {
                Retry = new HttpRetryOptions { MaxRetries = 0 },
                MaxResponseBodyBytes = limit,
            },
            NullLogger<HttpRunExecutor>.Instance
        );

    private static Task<Trax.Mediator.Services.TrainExecution.RunTrainResult> Run(
        HttpRunExecutor executor
    ) => executor.ExecuteAsync("My.Train", new RunInput(), typeof(RunInput));

    private sealed record RunInput;

    /// <summary>A body with no declared length that never ends, counting what it served.</summary>
    private sealed class EndlessStream : Stream
    {
        public long Served { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)'x');
            Served += count;
            return count;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class StreamingHandler(HttpStatusCode status, Stream body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(body) });
    }

    private sealed class StringHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        body,
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    #endregion
}
