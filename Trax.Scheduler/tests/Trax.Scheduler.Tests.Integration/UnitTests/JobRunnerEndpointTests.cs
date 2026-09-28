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
        bool registerRunner = true
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(web =>
            web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
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
}
