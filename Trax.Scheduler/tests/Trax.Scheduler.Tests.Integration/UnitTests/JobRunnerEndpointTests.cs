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
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Exercises the route handler bodies registered by <c>UseTraxJobRunner()</c> and
/// <c>UseTraxRunEndpoint()</c> end-to-end through TestServer. Both happy-path and
/// exception-path branches are covered against an NSubstitute <see cref="ITraxRequestHandler"/>.
/// </summary>
[TestFixture]
public class JobRunnerEndpointTests
{
    private static IHost BuildHost(ITraxRequestHandler handler)
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(web =>
            web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton(handler);
                })
                .Configure(app =>
                {
                    app.UseRouting();
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
        body.ErrorMessage.Should().Contain("downstream sink unavailable");
        body.ExceptionType.Should().Be(nameof(InvalidOperationException));
    }

    #endregion

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
        body.ErrorMessage.Should().Contain("downstream slow");
        body.ExceptionType.Should().Be(nameof(TimeoutException));
    }

    #endregion
}
