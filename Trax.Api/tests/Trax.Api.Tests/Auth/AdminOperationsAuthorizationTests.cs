using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
/// <item><c>RequireAuthorization()</c> gates the whole endpoint, admin surface included.</item>
/// <item><c>GateOperations(...)</c> gates the namespace alone, which is what a host with public
/// pre-login surfaces needs: the sibling public field on the same endpoint stays reachable.</item>
/// <item><c>AllowAnonymousOperations()</c> publishes the control plane deliberately.</item>
/// </list>
/// </remarks>
[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
[TestFixture]
public class AdminOperationsAuthorizationTests
{
    internal const string AdminApiKey = "admin-ops-key";
    internal const string ReaderApiKey = "reader-ops-key";
    private const string OperationsHealthQuery = "{ operations { health { status } } }";
    internal const string PublicQuery = "{ publicPing }";
    internal const string GatedQuery = "{ gatedPing }";
    internal const string AuthenticatedQuery = "{ authenticatedPing }";

    /// <summary>How the host answers for the operations namespace.</summary>
    internal enum Posture
    {
        /// <summary>RequireAuthorization(): the whole endpoint is gated.</summary>
        EndpointGated,

        /// <summary>AllowAnonymousOperations(): the control plane is deliberately public.</summary>
        AnonymousAcknowledged,

        /// <summary>GateOperations(): the namespace is gated, the endpoint stays open.</summary>
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
                                    graphql.RequireAuthorization();
                                    break;
                                case Posture.AnonymousAcknowledged:
                                    graphql.AllowAnonymousOperations();
                                    break;
                                case Posture.NamespaceGated:
                                    graphql.GateOperations();
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

        var res = await client.SendAsync(req);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    internal static bool HasErrorCode(JsonDocument doc, string code) =>
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
            .BeFalse("a bare GateOperations() asks only for an authenticated caller");
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

/// <summary>
/// <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c> on a resolver, end to end over HTTP.
/// The census refuses a field that declares nothing; these prove the other half, that a field
/// which does declare is actually enforced rather than merely annotated.
///
/// <para>Enforces <c>docs/adr/0003-a-type-extension-field-declares-its-own-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class ResolverAuthorizationTests
{
    private const string Adr = "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md";

    // ── [TraxAuthorize] on a resolver is a real gate ─────────────────────

    /// <summary>
    /// The whole point of owning the vocabulary: Trax reads its own attribute off the resolver
    /// and emits the server's directive, so the field is enforced, not merely annotated.
    /// </summary>
    [Test]
    public async Task TraxAuthorizeOnAResolver_RefusesAnAnonymousCaller()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "[TraxAuthorize] on a resolver method emits @authorize on that field, per " + Adr
            );

        await host.StopAsync();
    }

    [Test]
    public async Task TraxAuthorizeOnAResolver_RefusesAnAuthenticatedCallerWithoutTheRole()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.ReaderApiKey,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("Roles on the attribute reach the directive, not just the census");

        await host.StopAsync();
    }

    [Test]
    public async Task TraxAuthorizeOnAResolver_ServesTheRoleHolder()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.AdminApiKey,
            query: AdminOperationsAuthorizationTests.GatedQuery
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        doc.RootElement.GetProperty("data")
            .GetProperty("gatedPing")
            .GetString()
            .Should()
            .Be("pong");

        await host.StopAsync();
    }

    /// <summary>
    /// A bare [TraxAuthorize] with no policy and no roles asks only for an authenticated caller,
    /// the same as it does on a train or an entity.
    /// </summary>
    [Test]
    public async Task BareTraxAuthorizeOnAResolver_AsksOnlyForAuthentication()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var anonymous = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.AuthenticatedQuery
        );
        AdminOperationsAuthorizationTests
            .HasErrorCode(anonymous, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue();

        var reader = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: AdminOperationsAuthorizationTests.ReaderApiKey,
            query: AdminOperationsAuthorizationTests.AuthenticatedQuery
        );
        reader
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("any authenticated caller satisfies a bare [TraxAuthorize]");

        await host.StopAsync();
    }

    /// <summary>
    /// The sibling stays open. Gating one field on a root type must not gate the root.
    /// </summary>
    [Test]
    public async Task TraxAllowAnonymousSibling_StaysOpenBesideAGatedField()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGated
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            query: AdminOperationsAuthorizationTests.PublicQuery
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();

        await host.StopAsync();
    }
}

/// <summary>
/// Two root-type extension fields, one of each posture. Both declare, because a root-type
/// extension field inherits nothing. <c>publicPing</c> stands in for the pre-login surfaces a
/// host cannot gate; <c>gatedPing</c> is what proves Trax's own attribute produces a working
/// gate and not just a schema annotation.
/// </summary>
[ExtendObjectType("RootQuery")]
public sealed class PublicRootQueryExtension
{
    [TraxAllowAnonymous]
    public string PublicPing() => "pong";

    [TraxAuthorize(Roles = "Admin")]
    public string GatedPing() => "pong";

    [TraxAuthorize]
    public string AuthenticatedPing() => "pong";
}
