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
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

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
        response.ErrorMessage.Should().Be("boom");
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
        parsed.ErrorMessage.Should().Be("inner-fail");
    }

    #endregion

    #region Helpers

    private static TestLambdaContext CreateContext() =>
        new() { RemainingTime = TimeSpan.FromMinutes(5) };

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

    private sealed class TestFunction : TraxLambdaFunction
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
