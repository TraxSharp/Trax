using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Confirms the security posture of the admin <c>operations</c> namespace end to end over HTTP:
/// it is NOT auto-authenticated by being exposed (auth stays the deployer's decision), and when
/// the endpoint opts into <c>RequireAuthorization()</c> the admin surface is gated by the exact
/// same endpoint policy as everything else on that schema. There is no admin-specific auth built
/// in: separation between an admin surface and a client surface is achieved by exposing operations
/// on a distinct, gated host, not by a per-namespace policy.
/// </summary>
[TestFixture]
public class AdminOperationsAuthorizationTests
{
    private const string AdminApiKey = "admin-ops-key";
    private const string OperationsHealthQuery = "{ operations { health { status } } }";

    private static async Task<IHost> StartHostAsync(bool requireAuthorization)
    {
        var health = Substitute.For<ITraxHealthService>();
        health
            .GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new HealthStatus("Healthy", "ok", 0, 0, 0, 0));

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();

                        // API-key scheme registers the combined TraxAuthPolicy that
                        // the builder's RequireAuthorization() gates on by default.
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminApiKey, id: "admin", "Admin")
                        );

                        // Minimal graph the GraphQL builder needs, plus the backing
                        // services the exposed operations resolvers require.
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());

                        services.AddTraxGraphQL(graphql =>
                        {
                            graphql.ExposeOperationQueries();
                            if (requireAuthorization)
                                graphql.RequireAuthorization();
                            return graphql;
                        });

                        // Registered AFTER AddTraxGraphQL so these stubs win over the real
                        // backing services the stack registers (which would need a database).
                        // Last registration wins for resolution.
                        services.AddScoped(_ => health);
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<JsonDocument> PostAsync(IHost host, string? apiKey)
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query = OperationsHealthQuery }),
        };
        if (apiKey is not null)
            req.Headers.Add("X-Api-Key", apiKey);

        var res = await client.SendAsync(req);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    private static bool HasErrorCode(JsonDocument doc, string code) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors
            .EnumerateArray()
            .Any(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var c)
                && c.GetString() == code
            );

    [Test]
    public async Task Exposed_WithRequireAuthorization_Anonymous_IsRejected()
    {
        using var host = await StartHostAsync(requireAuthorization: true);

        var doc = await PostAsync(host, apiKey: null);

        HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "a gated endpoint must reject an unauthenticated call into the admin namespace"
            );
        var dataIsEmpty =
            !doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null;
        dataIsEmpty.Should().BeTrue("a rejected request must not return any admin data");

        await host.StopAsync();
    }

    [Test]
    public async Task Exposed_WithRequireAuthorization_AuthenticatedAdmin_Succeeds()
    {
        using var host = await StartHostAsync(requireAuthorization: true);

        var doc = await PostAsync(host, apiKey: AdminApiKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("an authenticated admin caller passes the endpoint gate");
        doc.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("health")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");

        await host.StopAsync();
    }

    [Test]
    public async Task Exposed_WithoutRequireAuthorization_Anonymous_IsReachable()
    {
        // Exposing the admin surface does NOT force authentication. Gating is the deployer's
        // decision; this test pins that so a future change can't silently start rejecting or,
        // worse, be assumed to gate when it does not.
        using var host = await StartHostAsync(requireAuthorization: false);

        var doc = await PostAsync(host, apiKey: null);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("without RequireAuthorization the operations surface is publicly reachable");
        doc.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("health")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");

        await host.StopAsync();
    }
}
