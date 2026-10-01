using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Exercises the route handler bodies registered by <c>UseTraxJobRunner()</c> and
/// <c>UseTraxRunEndpoint()</c> end-to-end through TestServer. Both happy-path and
/// exception-path branches are covered against an NSubstitute <see cref="ITraxRequestHandler"/>.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class JobRunnerEndpointTests
{
    private static IHost BuildHost(
        ITraxRequestHandler handler,
        Action<TraxJobRunnerOptions>? runner = null,
        bool registerRunner = true,
        ILoggerProvider? logs = null
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(web =>
            web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging =>
                    {
                        if (logs is not null)
                            logging.AddProvider(logs);
                    });
                    services.AddRouting();
                    services.AddAuthorization(o =>
                        o.AddPolicy("scheduler", p => p.RequireClaim("role", "scheduler"))
                    );
                    services.AddSingleton(handler);
                    if (registerRunner)
                    {
                        var options = new TraxJobRunnerOptions();
                        (runner ?? (o => o.AllowUnsignedRequests()))(options);
                        services.AddSingleton(options);
                        services.AddSingleton<INonceStore, InMemoryNonceStore>();
                        services.AddSingleton<RunnerRequestVerifier>();
                    }
                })
                .Configure(app =>
                {
                    // A request marked for it gets a body that fails the test if anything reads it.
                    app.Use(
                        (context, next) =>
                        {
                            if (context.Request.Headers.ContainsKey(UnreadableBodyHeader))
                                context.Request.Body = new UnreadableStream();
                            return next(context);
                        }
                    );
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.UseTraxJobRunner();
                        endpoints.UseTraxRunEndpoint();
                    });
                })
        );

        var host = hostBuilder.Start();
        return host;
    }

    #region UseTraxJobRunner — POST /trax/execute

    [Test]
    public async Task ExecuteJob_HappyPath_ReturnsMetadataId()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        handler
            .ExecuteJobAsync(Arg.Any<RemoteJobRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExecuteJobResult(MetadataId: 42));

        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/trax/execute",
            new RemoteJobRequest(MetadataId: 1, Input: null, InputType: null)
        );
        response.IsSuccessStatusCode.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<RemoteJobResponse>();
        body!.MetadataId.Should().Be(42);
        body.IsError.Should().BeFalse();
        body.ErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task ExecuteJob_HandlerThrows_ReturnsStructuredError()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        handler
            .ExecuteJobAsync(Arg.Any<RemoteJobRequest>(), Arg.Any<CancellationToken>())
            .Returns<ExecuteJobResult>(_ =>
                throw new InvalidOperationException("downstream sink unavailable")
            );

        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/trax/execute",
            new RemoteJobRequest(MetadataId: 7)
        );
        response.IsSuccessStatusCode.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<RemoteJobResponse>();
        body!.MetadataId.Should().Be(7);
        body.IsError.Should().BeTrue();
        body.ErrorMessage.Should().NotContain("downstream sink unavailable");
        body.StackTrace.Should().BeNull();
        body.ExceptionType.Should().Be(nameof(InvalidOperationException));
    }

    #endregion

    [TestCase("{\"metadataId\":1,\"MetadataId\":2}")]
    [TestCase("{\"metadataId\":1,\"metadataId\":2}")]
    public async Task ExecuteJob_BodyWithARepeatedProperty_IsRefused(string body)
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsync(
            "/trax/execute",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        await handler
            .DidNotReceive()
            .ExecuteJobAsync(Arg.Any<RemoteJobRequest>(), Arg.Any<CancellationToken>());
    }

    #region UseTraxRunEndpoint — POST /trax/run

    [Test]
    public async Task RunTrain_HappyPath_ReturnsHandlerResponseVerbatim()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        var expected = new RemoteRunResponse(
            MetadataId: 99,
            ExternalId: "ext-99",
            OutputJson: "{\"k\":1}",
            OutputType: "Foo"
        );
        handler
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/trax/run",
            new RemoteRunRequest(TrainName: "MyTrain", InputJson: "{}", InputType: "Bar")
        );
        response.IsSuccessStatusCode.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<RemoteRunResponse>();
        body!.MetadataId.Should().Be(99);
        body.ExternalId.Should().Be("ext-99");
        body.OutputJson.Should().Be("{\"k\":1}");
        body.IsError.Should().BeFalse();
    }

    [Test]
    public async Task RunTrain_HandlerThrows_BuildsErrorResponse()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        handler
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>())
            .Returns<RemoteRunResponse>(_ => throw new TimeoutException("downstream slow"));

        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/trax/run",
            new RemoteRunRequest(TrainName: "T", InputJson: "{}", InputType: "I")
        );
        response.IsSuccessStatusCode.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<RemoteRunResponse>();
        body!.IsError.Should().BeTrue();
        body.ErrorMessage.Should().NotContain("downstream slow");
        body.StackTrace.Should().BeNull();
        body.ExceptionType.Should().Be(nameof(TimeoutException));
    }

    #endregion

    [TestCase("{\"trainName\":\"A\",\"TrainName\":\"B\",\"inputJson\":\"{}\",\"inputType\":\"T\"}")]
    [TestCase("{\"trainName\":\"A\",\"trainName\":\"B\",\"inputJson\":\"{}\",\"inputType\":\"T\"}")]
    public async Task RunTrain_BodyWithARepeatedProperty_IsRefused(string body)
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsync(
            "/trax/run",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        await handler
            .DidNotReceive()
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>());
    }

    #region Posture

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static HttpRequestMessage SignedPost(
        string path,
        object envelope,
        RunnerRequestPurpose purpose,
        string? signature = null
    )
    {
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        );
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new("application/json") },
            },
        };
        request.Headers.Add(
            RunnerRequestSignature.HeaderName,
            signature ?? RunnerRequestSignature.Create(Key, purpose, body)
        );
        return request;
    }

    [Test]
    public void Mapping_WithoutAddTraxJobRunner_FailsAtStartup()
    {
        var act = () => BuildHost(Substitute.For<ITraxRequestHandler>(), registerRunner: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddTraxJobRunner*");
    }

    [Test]
    public void Mapping_WithoutAPosture_FailsAtStartup()
    {
        var act = () => BuildHost(Substitute.For<ITraxRequestHandler>(), _ => { });

        act.Should()
            .Throw<InvalidOperationException>(
                "mapping a runner endpoint with no posture fails at startup (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            )
            .WithMessage("*no authorization posture*");
    }

    [Test]
    public async Task AuthorizationPolicy_IsAppliedToBothEndpoints()
    {
        using var host = BuildHost(
            Substitute.For<ITraxRequestHandler>(),
            o => o.AuthorizationPolicy = "scheduler"
        );

        var endpoints = host
            .Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
            .Endpoints;
        foreach (var route in new[] { "/trax/execute", "/trax/run" })
            endpoints
                .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                .Single(e => e.RoutePattern.RawText == route)
                .Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Should()
                .Contain(a => a.Policy == "scheduler");

        // No authentication handler is registered, so an anonymous caller is refused before the
        // handler runs.
        var client = host.GetTestServer().CreateClient();
        var act = async () =>
            await client.PostAsJsonAsync("/trax/run", new RemoteRunRequest("T", "{}", "I"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task SigningKey_UnsignedRequest_Is401WithoutRunning()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync(
            "/trax/run",
            new RemoteRunRequest("T", "{}", "I")
        );

        response
            .StatusCode.Should()
            .Be(
                System.Net.HttpStatusCode.Unauthorized,
                "a runner with a signing key refuses an unsigned request (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
        await handler
            .DidNotReceive()
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SigningKey_SignedRequest_Runs()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        handler
            .ExecuteJobAsync(Arg.Any<RemoteJobRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ExecuteJobResult(MetadataId: 5));
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();

        var response = await client.SendAsync(
            SignedPost("/trax/execute", new RemoteJobRequest(5), RunnerRequestPurpose.Execute)
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await handler
            .Received(1)
            .ExecuteJobAsync(
                Arg.Is<RemoteJobRequest>(r => r.MetadataId == 5),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task SigningKey_ReplayedSignedRequest_IsRefused()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        handler
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RemoteRunResponse(MetadataId: 1));
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();
        var envelope = new RemoteRunRequest("T", "{}", "I");
        var first = SignedPost("/trax/run", envelope, RunnerRequestPurpose.Run);
        var signature = first.Headers.GetValues(RunnerRequestSignature.HeaderName).Single();

        var accepted = await client.SendAsync(first);
        var replayed = await client.SendAsync(
            SignedPost("/trax/run", envelope, RunnerRequestPurpose.Run, signature)
        );

        accepted.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        replayed.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        await handler
            .Received(1)
            .RunTrainAsync(Arg.Any<RemoteRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SigningKey_ExecuteSignatureSentToRun_IsRefused()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();

        var response = await client.SendAsync(
            SignedPost(
                "/trax/run",
                new RemoteRunRequest("T", "{}", "I"),
                RunnerRequestPurpose.Execute
            )
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Reading the request

    private const string UnreadableBodyHeader = "X-Test-Unreadable-Body";

    /// <summary>A POST whose body stream throws when read, announcing a 10 MB body.</summary>
    private static HttpRequestMessage PostWithUnreadableBody(string path, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(new byte[16])
            {
                Headers = { ContentType = new("application/json") },
            },
        };
        request.Headers.Add(UnreadableBodyHeader, "1");
        if (signature is not null)
            request.Headers.Add(RunnerRequestSignature.HeaderName, signature);
        return request;
    }

    [TestCase("/trax/execute")]
    [TestCase("/trax/run")]
    public async Task SigningKey_UnsignedRequest_Is401WithoutTheBodyBeingRead(string path)
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();

        var response = await client.SendAsync(PostWithUnreadableBody(path, signature: null));

        response
            .StatusCode.Should()
            .Be(
                System.Net.HttpStatusCode.Unauthorized,
                "the signature header is checked before the body is read (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
        handler.ReceivedCalls().Should().BeEmpty();
    }

    [TestCase("/trax/execute", "not-a-signature")]
    [TestCase("/trax/run", "v1,t=1,n=zz,s=AAAA")]
    public async Task SigningKey_MalformedSignature_Is401WithoutTheBodyBeingRead(
        string path,
        string signature
    )
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();

        var response = await client.SendAsync(PostWithUnreadableBody(path, signature));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        handler.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task SigningKey_StaleSignature_Is401WithoutTheBodyBeingRead()
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(handler, o => o.SigningKey = Key);
        var client = host.GetTestServer().CreateClient();
        var stale = RunnerRequestSignature.Create(
            Key,
            RunnerRequestPurpose.Run,
            new byte[16],
            DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            "00112233445566778899aabbccddeeff"
        );

        var response = await client.SendAsync(PostWithUnreadableBody("/trax/run", stale));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [TestCase("/trax/execute", true)]
    [TestCase("/trax/run", true)]
    [TestCase("/trax/execute", false)]
    [TestCase("/trax/run", false)]
    public async Task A_body_over_the_limit_is_413_without_running(string path, bool announced)
    {
        var handler = Substitute.For<ITraxRequestHandler>();
        using var host = BuildHost(
            handler,
            o =>
            {
                o.AllowUnsignedRequests();
                o.MaxRequestBodyBytes = 1024;
            }
        );
        var client = host.GetTestServer().CreateClient();
        var body = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"metadataId\":1,\"input\":\"{new string('x', 4096)}\"}}"
        );
        HttpContent content = announced
            ? new ByteArrayContent(body)
            : new StreamContent(new MemoryStream(body));
        content.Headers.ContentType = new("application/json");
        if (!announced)
            content.Headers.ContentLength = null;

        var response = await client.PostAsync(path, content);

        response
            .StatusCode.Should()
            .Be(
                System.Net.HttpStatusCode.RequestEntityTooLarge,
                "each runner endpoint holds its body to MaxRequestBodyBytes"
            );
        handler.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public void The_request_body_limit_must_be_positive()
    {
        var act = () => new TraxJobRunnerOptions { MaxRequestBodyBytes = 0 }.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Refusals_in_quick_succession_are_logged_as_one_warning()
    {
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(
            Substitute.For<ITraxRequestHandler>(),
            o => o.SigningKey = Key,
            logs: logs
        );
        var client = host.GetTestServer().CreateClient();

        for (var i = 0; i < 5; i++)
            await client.SendAsync(PostWithUnreadableBody("/trax/run", signature: null));

        logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Refused"))
            .Should()
            .Be(1, "refusals are summarised rather than logged one warning each");
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 10 * 1024 * 1024;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The request body was read.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("The request body was read.");

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("The request body was read.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<(
            LogLevel Level,
            string Message
        )> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Entries);

        public void Dispose() { }

        private sealed class Logger(
            System.Collections.Concurrent.ConcurrentQueue<(LogLevel, string)> entries
        ) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    #endregion
}
