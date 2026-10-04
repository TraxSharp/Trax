using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Oidc;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A policy set with <c>AddTraxGraphQL(g =&gt; g.RequireAuthorization(policy))</c> governs every
/// transport the endpoint serves. Over a socket it is evaluated against the connection's principal
/// when the connection is initialised and again for every operation, and an anonymous connection
/// does not satisfy it.
///
/// <para>Enforces <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md")]
[TestFixture]
[NonParallelizable]
public class EndpointPolicyOnSocketsTests
{
    private const string Adr = "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md";
    private const string AdminKey = "policy-admin-key";
    private const string ReaderKey = "policy-reader-key";
    private const string AdminPolicy = "EndpointPolicyOnSocketsAdmin";
    private const string TriggerMutation =
        """mutation { operations { triggerManifest(externalId: "m-1") { success } } }""";

    #region API-key host gated by a named policy

    [Test]
    public async Task KeyWithoutThePolicy_IsRefusedAtConnectionInit_AndNothingRuns()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        using var host = await StartApiKeyHostAsync(scheduler);

        using var ws = await ConnectAsync(host);
        await SendAsync(ws, new { type = "connection_init", payload = new { apiKey = ReaderKey } });

        (await ReceiveTypeAsync(ws))
            .Should()
            .Be("(closed)", "the endpoint policy is evaluated over a socket too, per " + Adr);
        await scheduler.DidNotReceiveWithAnyArgs().TriggerAsync(default!, default);
    }

    [Test]
    public async Task KeyWithThePolicy_RunsTheMutation()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        using var host = await StartApiKeyHostAsync(scheduler);

        using var ws = await ConnectAsync(host);
        await SendAsync(ws, new { type = "connection_init", payload = new { apiKey = AdminKey } });
        (await ReceiveTypeAsync(ws)).Should().Be("connection_ack");

        await SendAsync(
            ws,
            new
            {
                id = "1",
                type = "subscribe",
                payload = new { query = TriggerMutation },
            }
        );

        (await ReceiveTypeAsync(ws)).Should().Be("next");
        await scheduler.Received(1).TriggerAsync("m-1", Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cookie-only host gated with RequireAuthorization()

    [Test]
    public async Task CookieOnlyHost_AnonymousConnectionInit_IsRefused()
    {
        using var host = await StartOidcHostAsync();

        using var ws = await ConnectAsync(host);
        await SendAsync(ws, new { type = "connection_init", payload = new { } });

        (await ReceiveTypeAsync(ws))
            .Should()
            .Be(
                "(closed)",
                "an anonymous connection does not satisfy the endpoint policy, per " + Adr
            );
    }

    #endregion

    #region Every operation is checked in the request pipeline, whatever carried it

    [Test]
    public async Task Pipeline_PrincipalWithoutThePolicy_IsRefused_AndNothingRuns()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        using var host = await StartApiKeyHostAsync(scheduler);

        var result = await ExecuteAsync(host, Principal("Reader"));

        ErrorCodes(result)
            .Should()
            .Equal(
                new[] { "TRAX_AUTHORIZATION" },
                "the pipeline evaluates the endpoint policy for every operation, per " + Adr
            );
        await scheduler.DidNotReceiveWithAnyArgs().TriggerAsync(default!, default);
    }

    [Test]
    public async Task Pipeline_AnonymousPrincipal_IsRefused()
    {
        using var host = await StartApiKeyHostAsync(Substitute.For<ITraxScheduler>());

        var result = await ExecuteAsync(host, new ClaimsPrincipal(new ClaimsIdentity()));

        ErrorCodes(result).Should().Equal("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task Pipeline_PrincipalWithThePolicy_Runs()
    {
        var scheduler = Substitute.For<ITraxScheduler>();
        using var host = await StartApiKeyHostAsync(scheduler);

        var result = await ExecuteAsync(host, Principal("Admin"));

        ErrorCodes(result).Should().BeEmpty();
        await scheduler.Received(1).TriggerAsync("m-1", Arg.Any<CancellationToken>());
    }

    #endregion

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "TraxApiKey"));

    /// <summary>
    /// Runs the trigger mutation straight through the Trax executor, the way any transport hands
    /// an operation to it, carrying <paramref name="user"/> as the request's principal.
    /// </summary>
    private static async Task<IExecutionResult> ExecuteAsync(IHost host, ClaimsPrincipal user)
    {
        var executor = await host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        await using var scope = host.Services.CreateAsyncScope();
        return await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(TriggerMutation)
                .SetGlobalState("ClaimsPrincipal", user)
                .SetServices(scope.ServiceProvider)
                .Build()
        );
    }

    private static IReadOnlyList<string?> ErrorCodes(IExecutionResult result) =>
        (result as HotChocolate.Execution.OperationResult)?.Errors?.Select(e => e.Code).ToList()
        ?? [];

    private static async Task<IHost> StartApiKeyHostAsync(ITraxScheduler scheduler)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminKey, id: "admin", "Admin")
                                .Add(ReaderKey, id: "reader", "Reader")
                        );
                        services.AddAuthorization(o =>
                            o.AddPolicy(AdminPolicy, p => p.RequireRole("Admin"))
                        );
                        AddGraphQLBase(services);
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .RequireAuthorization(AdminPolicy)
                        );
                        AddOperationStubs(services, scheduler);
                    })
                    .Configure(Pipeline)
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<IHost> StartOidcHostAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxOidcAuth(o => o.UseAuthority("https://idp.invalid", "c"));
                        services.AddAuthorization();
                        AddGraphQLBase(services);
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().RequireAuthorization()
                        );
                        AddOperationStubs(services, Substitute.For<ITraxScheduler>());
                    })
                    .Configure(Pipeline)
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static void AddGraphQLBase(IServiceCollection services)
    {
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
    }

    private static void AddOperationStubs(IServiceCollection services, ITraxScheduler scheduler)
    {
        // Registered after AddTraxGraphQL so they win over the real services, which would need a
        // database.
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<IOperationsService>());
        services.AddScoped(_ =>
            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
        );
        services.AddScoped(_ => scheduler);
        services.AddScoped(_ => Substitute.For<IJobSubmitter>());
        services.AddSingleton(Trax.Api.Tests.Fakes.SeededManifests.With("m-1"));
    }

    private static void Pipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
    }

    private static async Task<WebSocket> ConnectAsync(IHost host)
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectClosingAsync(new Uri("ws://localhost/trax/graphql"), default);
    }

    private static Task SendAsync(WebSocket ws, object message) =>
        ws.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),
            WebSocketMessageType.Text,
            true,
            default
        );

    /// <summary>The type of the next message, or <c>(closed)</c> when the server closes.</summary>
    private static async Task<string> ReceiveTypeAsync(WebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16384];
        try
        {
            while (true)
            {
                var received = await ws.ReceiveAsync(buffer, cts.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                    return "(closed)";
                if (received.Count == 0)
                    continue;
                var type = JsonDocument
                    .Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))
                    .RootElement.GetProperty("type")
                    .GetString()!;
                if (type is "ping" or "pong")
                    continue;
                return type;
            }
        }
        catch (WebSocketException)
        {
            return "(closed)";
        }
    }
}
