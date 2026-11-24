using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Tailscale;
using Trax.Effect.Configuration.Deploy;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// The tailnet sign-in scheme against the real Trax GraphQL endpoint. A caller is the login in
/// <c>Tailscale-User-Login</c> only when the request carries the proxy's secret; every other request is anonymous,
/// whatever address it came from or claims to come from.
///
/// <para>Guard for <c>Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md</c>.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md"
)]
public class TailscaleAuthE2ETests
{
    private const string Database = "trax_api_auth_tailscale";

    private const string WhoAmIQuery = """
        { whoAmI { isAuthenticated principalId principalRoles } }
        """;

    private static Task<IHost> StartAsync(
        (Action<IServiceCollection>, Action<IApplicationBuilder>)? before = null
    ) => AuthE2EHost.StartAsync(Schemes.Tailscale, Database, beforeAuthentication: before);

    private static Action<HttpRequestMessage> Headers(
        params (string Name, string Value)[] headers
    ) =>
        req =>
        {
            foreach (var (name, value) in headers)
                req.Headers.TryAddWithoutValidation(name, value);
        };

    private static (bool Authenticated, string? Id, string[] Roles) Caller(JsonDocument doc)
    {
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.ToString());
        var who = doc.RootElement.GetProperty("data").GetProperty("whoAmI");
        return (
            who.GetProperty("isAuthenticated").GetBoolean(),
            who.GetProperty("principalId").GetString(),
            [.. who.GetProperty("principalRoles").EnumerateArray().Select(r => r.GetString() ?? "")]
        );
    }

    [Test]
    public async Task From_the_proxy_the_caller_is_the_login_with_its_mapped_roles()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(
                (DeployExposure.ProxySecretHeader, TailnetProxySecret),
                (TailscaleDefaults.LoginHeader, TailnetAdmin),
                (TailscaleDefaults.NameHeader, "Admin")
            )
        );

        var caller = Caller(doc);
        caller.Authenticated.Should().BeTrue();
        caller.Id.Should().Be($"{TailscaleDefaults.SchemeName}:{TailnetAdmin}");
        caller.Roles.Should().BeEquivalentTo("Admin", "Player");
    }

    [Test]
    public async Task A_login_header_from_a_sender_without_the_secret_is_anonymous()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers((TailscaleDefaults.LoginHeader, TailnetAdmin))
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    [Test]
    public async Task A_wrong_secret_is_anonymous()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(
                (DeployExposure.ProxySecretHeader, TailnetProxySecret[..^1] + "X"),
                (TailscaleDefaults.LoginHeader, TailnetAdmin)
            )
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    [Test]
    public async Task The_secret_twice_is_anonymous()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(
                (DeployExposure.ProxySecretHeader, TailnetProxySecret),
                (DeployExposure.ProxySecretHeader, TailnetProxySecret),
                (TailscaleDefaults.LoginHeader, TailnetAdmin)
            )
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    /// <summary>
    /// A sender inside the network the host trusts forwarded headers from claims to be the proxy. The forwarded-for
    /// header moves the request's address, which the scheme never reads; without the secret it stays anonymous.
    /// </summary>
    [Test]
    public async Task A_spoofed_forwarded_for_from_inside_the_known_networks_is_anonymous()
    {
        using var host = await StartAsync(
            (
                services =>
                    services.Configure<ForwardedHeadersOptions>(o =>
                    {
                        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                        o.KnownIPNetworks.Clear();
                        o.KnownProxies.Clear();
                        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
                    }),
                app =>
                {
                    app.Use(
                        (context, next) =>
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
                            return next(context);
                        }
                    );
                    app.UseForwardedHeaders();
                }
            )
        );

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(("X-Forwarded-For", "10.0.0.5"), (TailscaleDefaults.LoginHeader, TailnetAdmin))
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    [Test]
    public async Task From_the_proxy_with_no_login_header_a_tagged_device_is_anonymous()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers((DeployExposure.ProxySecretHeader, TailnetProxySecret))
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    [Test]
    public async Task A_duplicate_login_header_is_refused()
    {
        using var host = await StartAsync();

        var status = await ProtectedStatusAsync(
            host,
            (DeployExposure.ProxySecretHeader, TailnetProxySecret),
            (TailscaleDefaults.LoginHeader, TailnetAdmin),
            (TailscaleDefaults.LoginHeader, TailnetPlayer)
        );

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_comma_joined_login_header_is_refused()
    {
        using var host = await StartAsync();

        var status = await ProtectedStatusAsync(
            host,
            (DeployExposure.ProxySecretHeader, TailnetProxySecret),
            (TailscaleDefaults.LoginHeader, $"{TailnetPlayer}, {TailnetAdmin}")
        );

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_login_the_host_does_not_admit_is_refused()
    {
        using var host = await StartAsync();

        var status = await ProtectedStatusAsync(
            host,
            (DeployExposure.ProxySecretHeader, TailnetProxySecret),
            (TailscaleDefaults.LoginHeader, "stranger@example.com")
        );

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task An_admitted_login_passes_the_protected_endpoint()
    {
        using var host = await StartAsync();

        var status = await ProtectedStatusAsync(
            host,
            (DeployExposure.ProxySecretHeader, TailnetProxySecret),
            (TailscaleDefaults.LoginHeader, TailnetPlayer)
        );

        status.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_login_maps_only_to_its_own_roles()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(
                (DeployExposure.ProxySecretHeader, TailnetProxySecret),
                (TailscaleDefaults.LoginHeader, TailnetPlayer.ToUpperInvariant())
            )
        );

        var caller = Caller(doc);
        caller.Authenticated.Should().BeTrue();
        caller.Roles.Should().Equal("Player");
    }

    [Test]
    public async Task A_funnel_request_is_anonymous()
    {
        using var host = await StartAsync();

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            Headers(
                (DeployExposure.ProxySecretHeader, TailnetProxySecret),
                (TailscaleDefaults.FunnelHeader, "?1"),
                (TailscaleDefaults.LoginHeader, TailnetAdmin)
            )
        );

        Caller(doc).Authenticated.Should().BeFalse();
    }

    private static async Task<HttpStatusCode> ProtectedStatusAsync(
        IHost host,
        params (string Name, string Value)[] headers
    )
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = WhoAmIQuery }),
        };
        Headers(headers)(req);
        using var res = await client.SendAsync(req).ConfigureAwait(false);
        return res.StatusCode;
    }
}
