using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Oidc;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A scheme policy (<c>ApiKeyPolicy</c>, <c>JwtPolicy</c>, <c>{scheme}-JwtPolicy</c>, the
/// dispatcher's policy, <c>OidcPolicy</c>) is satisfied only by a principal that scheme
/// authenticated, wherever it is evaluated. And a GraphQL HTTP request on a host with several
/// schemes and no default one is authenticated before the endpoint policy is checked.
///
/// <para>Enforces <c>docs/adr/0010-a-scheme-policy-requires-its-scheme.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0010-a-scheme-policy-requires-its-scheme.md")]
[TestFixture]
[NonParallelizable]
public class SchemePolicyRequiresItsSchemeTests
{
    private const string Adr = "docs/adr/0010-a-scheme-policy-requires-its-scheme.md";
    private const string ApiKey = "scheme-policy-key";
    private const string Issuer = "https://scheme-policy.example";
    private const string Audience = "scheme-policy";
    private static readonly byte[] JwtKey = Encoding.UTF8.GetBytes(new string('j', 32));
    private const string HealthQuery = "{ operations { health { status } } }";

    #region The policies themselves

    private static IAuthorizationService AllSchemes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "key-user"));
        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(Issuer, Audience, JwtKey));
        services.AddTraxJwtAuth("Customer", jwt => jwt.UseSymmetricKey(Issuer, Audience, JwtKey));
        services.AddTraxOidcAuth(o => o.UseAuthority("https://idp.invalid", "c"));
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal AuthenticatedBy(string scheme) =>
        new TraxPrincipal("u-1", "User", [], null).ToClaimsPrincipal(scheme);

    [TestCase(ApiKeyDefaults.PolicyName, ApiKeyDefaults.SchemeName, true)]
    [TestCase(ApiKeyDefaults.PolicyName, JwtDefaults.SchemeName, false)]
    [TestCase(ApiKeyDefaults.PolicyName, OidcDefaults.SchemeName, false)]
    [TestCase(JwtDefaults.PolicyName, JwtDefaults.SchemeName, true)]
    [TestCase(JwtDefaults.PolicyName, ApiKeyDefaults.SchemeName, false)]
    [TestCase(JwtDefaults.PolicyName, "Customer", false)]
    [TestCase("Customer-JwtPolicy", "Customer", true)]
    [TestCase("Customer-JwtPolicy", JwtDefaults.SchemeName, false)]
    [TestCase(OidcDefaults.PolicyName, OidcDefaults.SchemeName, true)]
    [TestCase(OidcDefaults.PolicyName, ApiKeyDefaults.SchemeName, false)]
    public async Task SchemePolicy_IsSatisfiedOnlyByItsScheme(
        string policy,
        string authenticatedBy,
        bool expected
    )
    {
        var result = await AllSchemes().AuthorizeAsync(AuthenticatedBy(authenticatedBy), policy);

        result
            .Succeeded.Should()
            .Be(
                expected,
                $"{policy} requires a principal its own scheme authenticated, per " + Adr
            );
    }

    [Test]
    public async Task DispatcherPolicy_IsSatisfiedByAMappedScheme_AndNotByAnother()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "key-user"));
        services.AddTraxJwtAuth("Customer", jwt => jwt.UseSymmetricKey(Issuer, Audience, JwtKey));
        services.AddTraxJwtDispatcher(d => d.MapIssuer(Issuer, "Customer"));
        var authorization = services
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();
        var policy = JwtDefaults.DispatcherSchemeName + "-JwtPolicy";

        (await authorization.AuthorizeAsync(AuthenticatedBy("Customer"), policy))
            .Succeeded.Should()
            .BeTrue();
        (await authorization.AuthorizeAsync(AuthenticatedBy(ApiKeyDefaults.SchemeName), policy))
            .Succeeded.Should()
            .BeFalse();
    }

    #endregion

    #region Over HTTP, on a host with an API-key and a JWT scheme and no default scheme

    [Test]
    public async Task EndpointGatedByTheCombinedPolicy_AdmitsAnApiKeyCaller()
    {
        using var host = await StartAsync(g => g.RequireAuthorization());

        var doc = await PostAsync(host, HealthQuery, apiKey: ApiKey);

        Errors(doc)
            .Should()
            .BeEmpty(
                "a request on a multi-scheme host is authenticated before the endpoint policy, per "
                    + Adr
            );
    }

    [Test]
    public async Task EndpointGatedByTheCombinedPolicy_AdmitsAJwtCaller()
    {
        using var host = await StartAsync(g => g.RequireAuthorization());

        var doc = await PostAsync(host, HealthQuery, bearer: SignJwt());

        Errors(doc).Should().BeEmpty();
    }

    [Test]
    public async Task EndpointGatedByTheCombinedPolicy_RefusesAnAnonymousCaller()
    {
        using var host = await StartAsync(g => g.RequireAuthorization());

        var doc = await PostAsync(host, HealthQuery);

        Errors(doc).Should().Equal("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task EndpointGatedByApiKeyPolicy_RefusesAJwtCaller()
    {
        using var host = await StartAsync(g => g.RequireAuthorization(ApiKeyDefaults.PolicyName));

        var jwt = await PostAsync(host, HealthQuery, bearer: SignJwt());
        var key = await PostAsync(host, HealthQuery, apiKey: ApiKey);

        Errors(jwt).Should().Equal(new[] { "TRAX_AUTHORIZATION" }, "per " + Adr);
        Errors(key).Should().BeEmpty();
    }

    [Test]
    public async Task OperationsGatedByApiKeyPolicy_RefusesAJwtCaller()
    {
        using var host = await StartAsync(g => g.GateOperations(ApiKeyDefaults.PolicyName));

        var jwt = await PostAsync(host, HealthQuery, bearer: SignJwt());
        var key = await PostAsync(host, HealthQuery, apiKey: ApiKey);

        Errors(jwt)
            .Should()
            .Contain(
                c =>
                    c != null && c.StartsWith("AUTH_", StringComparison.Ordinal)
                    || c == "TRAX_AUTHORIZATION",
                "GateOperations(ApiKeyPolicy) is not satisfied by a JWT caller, per " + Adr
            );
        Errors(key).Should().BeEmpty();
    }

    #endregion

    private static string SignJwt()
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                Issuer,
                Audience,
                [new Claim("sub", "jwt-user")],
                now.AddMinutes(-1),
                now.AddMinutes(10),
                new SigningCredentials(
                    new SymmetricSecurityKey(JwtKey),
                    SecurityAlgorithms.HmacSha256
                )
            )
        );
    }

    private static async Task<IHost> StartAsync(Func<TraxGraphQLBuilder, TraxGraphQLBuilder> gate)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "key-user"));
                        services.AddTraxJwtAuth(jwt =>
                            jwt.UseSymmetricKey(Issuer, Audience, JwtKey)
                        );
                        services.AddAuthorization();
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(g => gate(g.ExposeOperationQueries()));

                        var health = Substitute.For<ITraxHealthService>();
                        health
                            .GetHealthAsync(Arg.Any<CancellationToken>())
                            .Returns(new Trax.Api.DTOs.HealthStatus("Healthy", "ok", 0, 0, 0, 0));
                        services.AddScoped(_ => health);
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
                        );
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

    private static async Task<JsonDocument> PostAsync(
        IHost host,
        string query,
        string? apiKey = null,
        string? bearer = null
    )
    {
        using var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);
        if (bearer is not null)
            request.Headers.Authorization = new("Bearer", bearer);

        using var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static IReadOnlyList<string?> Errors(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                        ? code.GetString()
                        : e.GetProperty("message").GetString()
                )
                .ToList()
            : [];
}
