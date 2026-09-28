using System.Net;
using System.Text.Json;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Runner.Lambda;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The Lambda runner entry point and its local HTTP routes, their posture and signature checks.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class TraxLambdaFunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    #region FunctionHandler — Execute

    [Test]
    public async Task FunctionHandler_ExecuteEnvelope_ReturnsSuccessResponse()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteResult = new ExecuteJobResult(MetadataId: 42);

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 42))
        );

        var result = await fn.FunctionHandler(envelope, CreateContext());

        result.Should().BeOfType<RemoteJobResponse>();
        var response = (RemoteJobResponse)result!;
        response.MetadataId.Should().Be(42);
        response.IsError.Should().BeFalse();
        fn.Handler.ExecuteCalls.Should().HaveCount(1);
        fn.Handler.ExecuteCalls[0].MetadataId.Should().Be(42);
    }

    [Test]
    public async Task FunctionHandler_ExecuteEnvelope_HandlerThrows_ReturnsErrorResponse()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteException = new InvalidOperationException("boom");

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 7))
        );

        var result = await fn.FunctionHandler(envelope, CreateContext());

        var response = (RemoteJobResponse)result!;
        response.MetadataId.Should().Be(7);
        response.IsError.Should().BeTrue();
        response.ErrorMessage.Should().NotContain("boom");
        response.StackTrace.Should().BeNull();
        response.ExceptionType.Should().Be(nameof(InvalidOperationException));
    }

    [Test]
    public async Task FunctionHandler_ExecutePayload_Invalid_Throws()
    {
        var fn = new TestFunction();
        var envelope = new LambdaEnvelope(LambdaRequestType.Execute, "null");

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region FunctionHandler — Run

    [Test]
    public async Task FunctionHandler_RunEnvelope_ReturnsResponse()
    {
        var fn = new TestFunction();
        fn.Handler.RunResult = new RemoteRunResponse(MetadataId: 99);

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Run,
            JsonSerializer.Serialize(
                new RemoteRunRequest(TrainName: "My.Train", InputJson: "{}", InputType: "Foo")
            )
        );

        var result = await fn.FunctionHandler(envelope, CreateContext());

        var response = (RemoteRunResponse)result!;
        response.MetadataId.Should().Be(99);
        fn.Handler.RunCalls.Should().HaveCount(1);
        fn.Handler.RunCalls[0].TrainName.Should().Be("My.Train");
    }

    [Test]
    public async Task FunctionHandler_RunEnvelope_HandlerThrows_Rethrows()
    {
        var fn = new TestFunction();
        fn.Handler.RunException = new InvalidOperationException("run-fail");

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Run,
            JsonSerializer.Serialize(
                new RemoteRunRequest(TrainName: "My.Train", InputJson: "{}", InputType: "Foo")
            )
        );

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("run-fail");
    }

    [Test]
    public async Task FunctionHandler_RunPayload_Invalid_Throws()
    {
        var fn = new TestFunction();
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, "null");

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region FunctionHandler — Duplicate properties

    [TestCase("{\"MetadataId\":1,\"metadataId\":2}")]
    [TestCase("{\"MetadataId\":1,\"MetadataId\":2}")]
    public async Task FunctionHandler_ExecutePayload_WithARepeatedProperty_IsRefused(string payload)
    {
        var fn = new TestFunction();
        var envelope = new LambdaEnvelope(LambdaRequestType.Execute, payload);

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<JsonException>();
        fn.Handler.ExecuteCalls.Should().BeEmpty();
    }

    [TestCase("{\"TrainName\":\"A\",\"trainName\":\"B\",\"InputJson\":\"{}\",\"InputType\":\"T\"}")]
    [TestCase("{\"TrainName\":\"A\",\"TrainName\":\"B\",\"InputJson\":\"{}\",\"InputType\":\"T\"}")]
    public async Task FunctionHandler_RunPayload_WithARepeatedProperty_IsRefused(string payload)
    {
        var fn = new TestFunction();
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, payload);

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<JsonException>();
        fn.Handler.RunCalls.Should().BeEmpty();
    }

    #endregion

    #region Posture and signatures

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static LambdaEnvelope Signed(
        LambdaRequestType type,
        string payload,
        byte[]? key = null
    ) =>
        new(type, payload)
        {
            Signature = RunnerRequestSignature.Create(
                key ?? Key,
                type == LambdaRequestType.Run
                    ? RunnerRequestPurpose.Run
                    : RunnerRequestPurpose.Execute,
                System.Text.Encoding.UTF8.GetBytes(payload)
            ),
        };

    private static string RunPayload() =>
        JsonSerializer.Serialize(new RemoteRunRequest("My.Train", "{}", "Foo"));

    [Test]
    public async Task FunctionHandler_NoPosture_IsRefused()
    {
        var fn = new TestFunction(_ => { });
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, RunPayload());

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no authorization posture*");
        fn.Handler.RunCalls.Should().BeEmpty();
    }

    [Test]
    public async Task FunctionHandler_SigningKey_UnsignedEnvelope_IsRefused()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, RunPayload());

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Missing*");
        fn.Handler.RunCalls.Should().BeEmpty();
    }

    [Test]
    public async Task FunctionHandler_SigningKey_SignedEnvelope_Runs()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);

        await fn.FunctionHandler(Signed(LambdaRequestType.Run, RunPayload()), CreateContext());

        fn.Handler.RunCalls.Should().HaveCount(1);
    }

    [Test]
    public async Task FunctionHandler_SigningKey_EnvelopeSignedWithAnotherKey_IsRefused()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        var otherKey = Enumerable.Repeat((byte)7, 32).ToArray();

        var act = async () =>
            await fn.FunctionHandler(
                Signed(LambdaRequestType.Run, RunPayload(), otherKey),
                CreateContext()
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Invalid*");
        fn.Handler.RunCalls.Should().BeEmpty();
    }

    [Test]
    public async Task FunctionHandler_SigningKey_ExecuteSignatureOnARunEnvelope_IsRefused()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        var payload = RunPayload();
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, payload)
        {
            Signature = Signed(LambdaRequestType.Execute, payload).Signature,
        };

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Invalid*");
    }

    [Test]
    public async Task FunctionHandler_SigningKey_RepeatedRunEnvelope_IsRefused()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        var envelope = Signed(LambdaRequestType.Run, RunPayload());

        await fn.FunctionHandler(envelope, CreateContext());
        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Replayed*");
        fn.Handler.RunCalls.Should()
            .HaveCount(
                1,
                "a synchronous Run is accepted once per nonce (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [Test]
    public async Task FunctionHandler_SigningKey_RepeatedExecuteEnvelope_IsDeliveredAgain()
    {
        // Lambda retries an asynchronous invocation with the same payload; the job's Pending
        // metadata row, not the nonce, stops a second run.
        var fn = new TestFunction(o => o.SigningKey = Key);
        var envelope = Signed(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 5))
        );

        await fn.FunctionHandler(envelope, CreateContext());
        await fn.FunctionHandler(envelope, CreateContext());

        fn.Handler.ExecuteCalls.Should().HaveCount(2);
    }

    [Test]
    public async Task ConfigureRoutes_SigningKey_UnsignedRequest_Is401()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        using var host = await CreateRouteHost(fn);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/trax/run", new StringContent(RunPayload()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fn.Handler.RunCalls.Should().BeEmpty();
    }

    [Test]
    public async Task ConfigureRoutes_SigningKey_SignedRequest_Runs()
    {
        var fn = new TestFunction(o => o.SigningKey = Key);
        using var host = await CreateRouteHost(fn);
        var client = host.GetTestClient();
        var payload = RunPayload();

        var request = new HttpRequestMessage(HttpMethod.Post, "/trax/run")
        {
            Content = new StringContent(payload),
        };
        request.Headers.Add(
            RunnerRequestSignature.HeaderName,
            RunnerRequestSignature.Create(
                Key,
                RunnerRequestPurpose.Run,
                System.Text.Encoding.UTF8.GetBytes(payload)
            )
        );
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fn.Handler.RunCalls.Should().HaveCount(1);
    }

    #endregion

    #region FunctionHandler — Unknown Type

    [Test]
    public async Task FunctionHandler_UnknownType_Throws()
    {
        var fn = new TestFunction();
        var envelope = new LambdaEnvelope((LambdaRequestType)999, "{}");

        var act = async () => await fn.FunctionHandler(envelope, CreateContext());

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown Lambda request type*");
    }

    #endregion

    #region ConfigureRoutes — local HTTP host

    [Test]
    public async Task ConfigureRoutes_PostExecute_ReturnsSerializedJobResponse()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteResult = new ExecuteJobResult(MetadataId: 11);

        using var host = await CreateRouteHost(fn);
        var client = host.GetTestClient();

        var body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 11));
        var response = await client.PostAsync("/trax/execute", new StringContent(body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<RemoteJobResponse>(payload, JsonOptions);
        parsed.Should().NotBeNull();
        parsed!.MetadataId.Should().Be(11);
        parsed.IsError.Should().BeFalse();
    }

    [Test]
    public async Task ConfigureRoutes_PostRun_ReturnsSerializedRunResponse()
    {
        var fn = new TestFunction();
        fn.Handler.RunResult = new RemoteRunResponse(MetadataId: 22);

        using var host = await CreateRouteHost(fn);
        var client = host.GetTestClient();

        var body = JsonSerializer.Serialize(
            new RemoteRunRequest(TrainName: "T", InputJson: "{}", InputType: "X")
        );
        var response = await client.PostAsync("/trax/run", new StringContent(body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<RemoteRunResponse>(payload, JsonOptions);
        parsed!.MetadataId.Should().Be(22);
    }

    [Test]
    public async Task ConfigureRoutes_PostExecute_HandlerThrows_StillReturnsOkWithErrorBody()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteException = new InvalidOperationException("inner-fail");

        using var host = await CreateRouteHost(fn);
        var client = host.GetTestClient();

        var body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 33));
        var response = await client.PostAsync("/trax/execute", new StringContent(body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<RemoteJobResponse>(payload, JsonOptions);
        parsed!.IsError.Should().BeTrue();
        parsed.ErrorMessage.Should().NotContain("inner-fail");
    }

    #endregion

    #region RemainingTime margin

    /// <summary>
    /// With less time left than the outcome write needs, the work is not started.
    /// </summary>
    /// <remarks>
    /// Cancellation used to be derived from <c>RemainingTime</c> itself, which fires at the instant
    /// Lambda freezes or kills the environment, so the uncancellable terminal write had no time to
    /// land. The row stayed InProgress holding its subject until StaleInProgressTimeout, and the
    /// reaper then recorded Failed rather than Cancelled, which a manifest counts toward retries and
    /// dead letters. Holding back a margin is what gives the write somewhere to happen.
    /// </remarks>
    [Test]
    public async Task FunctionHandler_LessTimeLeftThanTheWriteMargin_HandsTheHandlerACancelledToken()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteResult = new ExecuteJobResult(MetadataId: 1);

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 1))
        );

        await fn.FunctionHandler(envelope, CreateContext(TimeSpan.FromSeconds(1)));

        fn.Handler.ExecuteTokenCancelled.Should()
            .Equal(
                [true],
                "one second is less than the margin the outcome write needs, so starting work that "
                    + "cannot be recorded is worse than reporting it cancelled straight away"
            );
    }

    [Test]
    public async Task FunctionHandler_AmpleTimeLeft_HandsTheHandlerALiveToken()
    {
        var fn = new TestFunction();
        fn.Handler.ExecuteResult = new ExecuteJobResult(MetadataId: 2);

        var envelope = new LambdaEnvelope(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 2))
        );

        await fn.FunctionHandler(envelope, CreateContext(TimeSpan.FromMinutes(5)));

        fn.Handler.ExecuteTokenCancelled.Should()
            .Equal([false], "the margin comes off the budget; it does not shrink it to nothing");
    }

    #endregion

    #region Helpers

    private static TestLambdaContext CreateContext() =>
        new() { RemainingTime = TimeSpan.FromMinutes(5) };

    private static TestLambdaContext CreateContext(TimeSpan remaining) =>
        new() { RemainingTime = remaining };

    private static async Task<IHost> CreateRouteHost(TestFunction fn)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services => services.AddRouting());
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(routes => fn.ExposeConfigureRoutes(routes));
            });
        });
        var host = await builder.StartAsync();
        return host;
    }

    #endregion

    #region TestFunction

    private sealed class TestFunction(Action<TraxJobRunnerOptions>? runner = null)
        : TraxLambdaFunction
    {
        public FakeRequestHandler Handler { get; } = new();

        protected override void ConfigureServices(
            IServiceCollection services,
            IConfiguration configuration
        )
        {
            // base.BuildServiceProvider is overridden — this is unused but required by abstract.
        }

        protected override IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddLogging();
            services.AddSingleton<ITraxRequestHandler>(Handler);

            var options = new TraxJobRunnerOptions();
            (runner ?? (o => o.AllowUnsignedRequests()))(options);
            services.AddSingleton(options);
            services.AddSingleton<INonceStore, InMemoryNonceStore>();
            services.AddSingleton<RunnerRequestVerifier>();
            return services.BuildServiceProvider();
        }

        public void ExposeConfigureRoutes(
            Microsoft.AspNetCore.Routing.IEndpointRouteBuilder routes
        ) => ConfigureRoutes(routes);
    }

    private sealed class FakeRequestHandler : ITraxRequestHandler
    {
        public List<RemoteJobRequest> ExecuteCalls { get; } = [];
        public List<RemoteRunRequest> RunCalls { get; } = [];

        /// <summary>Whether the token each Execute call arrived with was already cancelled.</summary>
        public List<bool> ExecuteTokenCancelled { get; } = [];
        public ExecuteJobResult ExecuteResult { get; set; } = new(MetadataId: 0);
        public RemoteRunResponse RunResult { get; set; } = new(MetadataId: 0);
        public Exception? ExecuteException { get; set; }
        public Exception? RunException { get; set; }

        public Task<ExecuteJobResult> ExecuteJobAsync(
            RemoteJobRequest request,
            CancellationToken ct = default
        )
        {
            ExecuteCalls.Add(request);
            ExecuteTokenCancelled.Add(ct.IsCancellationRequested);
            if (ExecuteException is not null)
                throw ExecuteException;
            return Task.FromResult(ExecuteResult);
        }

        public Task<RemoteRunResponse> RunTrainAsync(
            RemoteRunRequest request,
            CancellationToken ct = default
        )
        {
            RunCalls.Add(request);
            if (RunException is not null)
                throw RunException;
            return Task.FromResult(RunResult);
        }
    }

    #endregion
}
