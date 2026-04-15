using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// End-to-end tests that exercise the DI wiring from
/// <c>AddTraxApiKeyAuth</c> all the way through to a scoped consumer
/// (simulating a junction) injecting <see cref="TraxPrincipal"/> directly.
/// </summary>
[TestFixture]
public class TraxPrincipalInjectionTests
{
    /// <summary>
    /// Stand-in for a junction. The point of the design under test is that
    /// consumers can inject <see cref="TraxPrincipal"/> directly with no
    /// knowledge of <c>IHttpContextAccessor</c>.
    /// </summary>
    private sealed class JunctionLikeService(TraxPrincipal user)
    {
        public string Greet() => $"{user.DisplayName} ({user.Id})";

        public string[] RoleSnapshot() => user.Roles.ToArray();

        public string? Tenant() => user.Claims?.GetValueOrDefault("tenant");
    }

    private static async Task<IHost> CreateHost(
        Func<string, CancellationToken, ValueTask<TraxPrincipal?>> resolver,
        bool requireAuthOnJunctionEndpoint = true
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddTraxApiKeyAuthWithInstance(new DelegateResolver(resolver));
                        services.AddScoped<JunctionLikeService>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            // Junction-like: resolves TraxPrincipal through DI.
                            var junction = endpoints.MapGet(
                                "/junction",
                                (JunctionLikeService svc) =>
                                    Results.Ok(
                                        new JunctionResponse
                                        {
                                            Greeting = svc.Greet(),
                                            Roles = svc.RoleSnapshot(),
                                            Tenant = svc.Tenant(),
                                        }
                                    )
                            );
                            if (requireAuthOnJunctionEndpoint)
                                junction.RequireAuthorization(ApiKeyDefaults.PolicyName);

                            // Direct resolve (no endpoint auth) to simulate accidental
                            // anonymous access to an ungated junction.
                            endpoints.MapGet(
                                "/direct",
                                (TraxPrincipal user) =>
                                    Results.Ok(
                                        new JunctionResponse
                                        {
                                            Greeting = $"{user.DisplayName} ({user.Id})",
                                            Roles = [.. user.Roles],
                                        }
                                    )
                            );
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    [Test]
    public async Task AuthenticatedRequest_InjectsTraxPrincipal_WithAllFields()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal(
                        "alice",
                        "Alice Liddell",
                        ["User", "Tenant.Admin"],
                        Claims: new Dictionary<string, string> { ["tenant"] = "acme" },
                        PrincipalType: "apikey"
                    )
                )
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<JunctionResponse>("/junction");

        response.Should().NotBeNull();
        response!.Greeting.Should().Be("Alice Liddell (alice)");
        response.Roles.Should().BeEquivalentTo(["User", "Tenant.Admin"]);
        response.Tenant.Should().Be("acme");
    }

    [Test]
    public async Task AuthenticatedRequest_DirectInjection_SeesSamePrincipal()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal("bob", "Bob", ["Admin"]))
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<JunctionResponse>("/direct");

        response!.Greeting.Should().Be("Bob (bob)");
        response.Roles.Should().BeEquivalentTo(["Admin"]);
    }

    [Test]
    public async Task TwoConcurrentRequests_EachSeeOwnPrincipal()
    {
        var principals = new Dictionary<string, TraxPrincipal>
        {
            ["alice-key"] = new("alice", "Alice", ["User"]),
            ["bob-key"] = new("bob", "Bob", ["Admin"]),
            ["charlie-key"] = new("charlie", "Charlie", ["User", "Beta"]),
        };

        using var host = await CreateHost(
            (key, _) =>
                principals.TryGetValue(key, out var p)
                    ? ValueTask.FromResult<TraxPrincipal?>(p)
                    : ValueTask.FromResult<TraxPrincipal?>(null)
        );

        var tasks = Enumerable
            .Range(0, 60)
            .Select(async i =>
            {
                var key = (i % 3) switch
                {
                    0 => "alice-key",
                    1 => "bob-key",
                    _ => "charlie-key",
                };
                var expected = (i % 3) switch
                {
                    0 => "alice",
                    1 => "bob",
                    _ => "charlie",
                };
                var client = host.GetTestClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
                var response = await client.GetFromJsonAsync<JunctionResponse>("/junction");
                return (expected, actual: response?.Greeting);
            });

        var results = await Task.WhenAll(tasks);

        foreach (var (expected, actual) in results)
            actual.Should().Contain($"({expected})");
    }

    [Test]
    public async Task AnonymousRequest_ToGatedJunctionEndpoint_Returns401()
    {
        // RequireAuthorization upstream should block the request before
        // the junction even tries to resolve TraxPrincipal. Reaching the
        // junction without auth would be a routing misconfiguration.
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();

        var response = await client.GetAsync("/junction");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AnonymousRequest_ToUngatedEndpoint_InjectingPrincipal_PropagatesException()
    {
        // The misconfiguration case: consumer forgets RequireAuthorization,
        // forgets [TraxAuthorize], and injects TraxPrincipal directly.
        // TestServer propagates the exception directly so the test can verify
        // that the resolver throws the intended diagnostic (not a NullReferenceException
        // or a silent 200). In production, this surfaces as a 500 with a clear
        // stack trace pointing at the misconfiguration.
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();

        var act = async () => await client.GetAsync("/direct");

        await act.Should().ThrowAsync<TraxPrincipalNotAvailableException>();
    }

    [Test]
    public async Task InvalidApiKey_ToGatedEndpoint_Returns401_PrincipalNeverResolved()
    {
        var resolverCalls = 0;
        using var host = await CreateHost(
            (_, _) =>
            {
                resolverCalls++;
                return ValueTask.FromResult<TraxPrincipal?>(null);
            }
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "definitely-invalid");

        var response = await client.GetAsync("/junction");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resolverCalls.Should().Be(1); // scheme called the resolver; rejection short-circuited auth
    }

    private sealed class JunctionResponse
    {
        public string Greeting { get; set; } = string.Empty;
        public string[] Roles { get; set; } = [];
        public string? Tenant { get; set; }
    }

    private sealed class DelegateResolver(
        Func<string, CancellationToken, ValueTask<TraxPrincipal?>> resolver
    ) : ITraxPrincipalResolver<string>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct) =>
            resolver(input, ct);
    }
}
