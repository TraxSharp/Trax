using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Types;
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
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Confirms the security posture of the admin <c>operations</c> namespace end to end over HTTP.
/// Exposing it does not authenticate it: which of the three postures applies is the deployer's
/// decision, and the host refuses to start until one is chosen.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>RequireAuthorization()</c> gates the whole endpoint, admin surface included, but does
/// not answer for the namespace: it still needs a gate of its own.</item>
/// <item><c>GateOperations(...)</c> gates the namespace alone, which is what a host with public
/// pre-login surfaces needs: the sibling public field on the same endpoint stays reachable.</item>
/// <item><c>AllowAnonymousOperations()</c> publishes the control plane deliberately.</item>
/// </list>
/// </remarks>
[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
[TestFixture]
public class AdminOperationsAuthorizationTests
{
    internal const string AdminApiKey = "admin-ops-key-0123456789abcdefghijklmnop";
    internal const string ReaderApiKey = "reader-ops-key-0123456789abcdefghijklmno";
    private const string OperationsHealthQuery = "{ operations { health { status } } }";
    internal const string PublicQuery = "{ publicPing }";
    internal const string GatedQuery = "{ gatedPing }";
    internal const string AuthenticatedQuery = "{ authenticatedPing }";

    /// <summary>How the host answers for the operations namespace.</summary>
    internal enum Posture
    {
        /// <summary>
        /// RequireAuthorization() with GateOperations(roles: "Admin"): the whole endpoint is gated
        /// and the namespace needs a role on top.
        /// </summary>
        EndpointGated,

        /// <summary>AllowAnonymousOperations(): the control plane is deliberately public.</summary>
        AnonymousAcknowledged,

        /// <summary>
        /// GateOperationsToAuthenticatedUsers(): the namespace is gated, the endpoint stays open.
        /// </summary>
        NamespaceGated,

        /// <summary>GateOperations(roles: "Admin"): the namespace needs a role.</summary>
        NamespaceGatedByRole,
    }

    internal static async Task<IHost> StartHostAsync(Posture posture)
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
                                .Add(ReaderApiKey, id: "reader", "Reader")
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
                            graphql.AddTypeExtension<PublicRootQueryExtension>();

                            switch (posture)
                            {
                                case Posture.EndpointGated:
                                    graphql.RequireAuthorization().GateOperations(roles: "Admin");
                                    break;
                                case Posture.AnonymousAcknowledged:
                                    graphql.AllowAnonymousOperations();
                                    break;
                                case Posture.NamespaceGated:
                                    graphql.GateOperationsToAuthenticatedUsers();
                                    break;
                                case Posture.NamespaceGatedByRole:
                                    graphql.GateOperations(roles: "Admin");
                                    break;
                            }

                            return graphql;
                        });

                        // Registered AFTER AddTraxGraphQL so these stubs win over the real
                        // backing services the stack registers (which would need a database).
                        // Last registration wins for resolution.
                        services.AddScoped(_ => health);
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
                        );
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

        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    internal static async Task<JsonDocument> PostAsync(
        IHost host,
        string? apiKey,
        string query = OperationsHealthQuery
    )
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            req.Headers.Add("X-Api-Key", apiKey);

        var res = await client.SendAsync(req).ConfigureAwait(false);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    internal static bool HasErrorCode(JsonDocument doc, string code) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors
            .EnumerateArray()
            .Any(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var c)
                && string.Equals(c.GetString(), code, StringComparison.Ordinal)
            );

    [Test]
    public async Task Exposed_WithRequireAuthorization_Anonymous_IsRejected()
    {
        using var host = await StartHostAsync(Posture.EndpointGated);

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
        using var host = await StartHostAsync(Posture.EndpointGated);

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
    public async Task Exposed_WithRequireAuthorization_SignedInCallerWithoutTheRole_IsRejected()
    {
        // The endpoint gate admits every user of the application; the namespace gate decides who
        // operates it.
        using var host = await StartHostAsync(Posture.EndpointGated);

        var doc = await PostAsync(host, apiKey: ReaderApiKey);

        HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("passing the endpoint gate does not satisfy the operations gate");

        await host.StopAsync();
    }

    [Test]
    public async Task Exposed_WithoutRequireAuthorization_Anonymous_IsReachable()
    {
        // AllowAnonymousOperations() means what it says. Gating is the deployer's decision;
        // this pins that the acknowledged-anonymous posture really does serve anonymous callers,
        // so it can never be mistaken for a gate.
        using var host = await StartHostAsync(Posture.AnonymousAcknowledged);

        var doc = await PostAsync(host, apiKey: null);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(
                "AllowAnonymousOperations() is an opt-in to a publicly reachable control plane"
            );
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
    public async Task NamespaceGated_Anonymous_IsRejected()
    {
        using var host = await StartHostAsync(Posture.NamespaceGated);

        var doc = await PostAsync(host, apiKey: null);

        HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("GateOperations() puts @authorize on the operations field itself");

        await host.StopAsync();
    }

    [Test]
    public async Task NamespaceGated_Authenticated_IsReachable()
    {
        using var host = await StartHostAsync(Posture.NamespaceGated);

        var doc = await PostAsync(host, apiKey: ReaderApiKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("GateOperationsToAuthenticatedUsers() asks only for an authenticated caller");
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
    public async Task NamespaceGated_PublicSiblingField_StaysReachableAnonymously()
    {
        // The whole point of the namespace gate: an open endpoint keeps its public surfaces.
        // RequireAuthorization() cannot express this, which is why hosts with pre-login pages
        // reached for AllowAnonymousOperations() and published the control plane.
        using var host = await StartHostAsync(Posture.NamespaceGated);

        var doc = await PostAsync(host, apiKey: null, query: PublicQuery);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(
                "gating the namespace must not gate the rest of the endpoint, which is the whole "
                    + "point of "
                    + "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md"
            );
        doc.RootElement.GetProperty("data")
            .GetProperty("publicPing")
            .GetString()
            .Should()
            .Be("pong");

        await host.StopAsync();
    }

    [Test]
    public async Task NamespaceGatedByRole_WrongRole_IsRejected()
    {
        using var host = await StartHostAsync(Posture.NamespaceGatedByRole);

        var doc = await PostAsync(host, apiKey: ReaderApiKey);

        HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("an authenticated caller without the role is still refused");

        await host.StopAsync();
    }

    [Test]
    public async Task NamespaceGatedByRole_RoleHolder_IsReachable()
    {
        using var host = await StartHostAsync(Posture.NamespaceGatedByRole);

        var doc = await PostAsync(host, apiKey: AdminApiKey);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
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
    public async Task EndpointGated_PublicSiblingField_IsAlsoRejected()
    {
        // The contrast that makes the namespace gate worth having: the endpoint gate takes the
        // public field down with it.
        using var host = await StartHostAsync(Posture.EndpointGated);

        var doc = await PostAsync(host, apiKey: null, query: PublicQuery);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeTrue("RequireAuthorization() rejects the whole endpoint, public fields included");

        await host.StopAsync();
    }
}
