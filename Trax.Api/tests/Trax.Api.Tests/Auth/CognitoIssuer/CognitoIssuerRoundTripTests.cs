using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Cognito;
using Trax.Api.Auth.Jwt.Cognito.Issuer;
using Trax.Api.Auth.Jwt.Testing;

namespace Trax.Api.Tests.Auth.CognitoIssuer;

/// <summary>
/// Acceptance tests for the Cognito issuer feature: tokens minted by
/// CognitoTokenIssuer must round-trip through CognitoJwtPrincipalResolver
/// (via UseCognito on the JWT bearer middleware) and produce a fully-formed
/// <see cref="TraxPrincipal"/> with the same shape a real Cognito-issued
/// token would.
/// </summary>
[TestFixture]
public class CognitoIssuerRoundTripTests
{
    private const string Region = "us-east-1";
    private const string UserPoolId = "us-east-1_LocalTestPool";
    private const string ClientId = "abc123clientid";

    [Test]
    public async Task AccessToken_RoundTripsToTraxPrincipal_WithGroupsAsRoles()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var sub = Guid.NewGuid();
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Username = "alice",
                Scopes = new[] { "openid", "profile" },
                Groups = new[] { "admin", "editor" },
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.Access);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var ctx = await ReadPrincipalSnapshot(resp);
        ctx.Id.Should().Be(sub.ToString());
        ctx.PrincipalType.Should().Be(CognitoDefaults.PrincipalType);
        ctx.Roles.Should().BeEquivalentTo(new[] { "admin", "editor" });
        ctx.IdentityProvider.Should()
            .Be(CognitoDefaults.PrincipalType, "no `identities` claim means native cognito user");
    }

    [Test]
    public async Task IdToken_RoundTripsToTraxPrincipal_WithEmail()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var sub = Guid.NewGuid();
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "alice@example.com",
                EmailVerified = true,
                GivenName = "Alice",
                FamilyName = "Smith",
                Username = "alice",
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.Id);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var ctx = await ReadPrincipalSnapshot(resp);
        ctx.Id.Should().Be(sub.ToString());
        ctx.DisplayName.Should().Be("alice");
        ctx.Email.Should().Be("alice@example.com");
    }

    [Test]
    public async Task IdToken_FederatedIdentity_SurfaceAsIdentityProviderClaim()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "alice@gmail.com",
                EmailVerified = true,
                Identities = new[]
                {
                    new FederatedIdentity(
                        UserId: "108222222222",
                        ProviderName: "Google",
                        ProviderType: "Google",
                        Primary: true,
                        DateCreated: DateTimeOffset.UtcNow
                    ),
                },
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.Id);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var ctx = await ReadPrincipalSnapshot(resp);
        ctx.IdentityProvider.Should().Be("Google");
    }

    [Test]
    public async Task AccessToken_RejectedByIdOnlyValidator()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );

        // Validator configured to accept ID tokens only must reject the
        // access token: both the missing `aud` claim and the `token_use=access`
        // claim should trip it.
        using var host = await BuildCognitoHost(server, CognitoTokenUse.Id);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task IdToken_RejectedByAccessOnlyValidator()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.Access);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task IdAndAccessValidator_AcceptsBothShapes()
    {
        await using var server = await TestJwksServer.StartAsync();

        var issuer = server.CreateCognitoIssuer();
        var sub = Guid.NewGuid();
        var accessToken = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );
        var idToken = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.IdAndAccess);
        var client = host.GetTestClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken
        );
        (await client.GetAsync("/protected")).StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            idToken
        );
        (await client.GetAsync("/protected")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task RotatedSigningKey_NewTokenValidatedAfterJwksRefresh()
    {
        await using var server = await TestJwksServer.StartAsync();

        // Build the host (and let the JwtBearer handler prime its JWKS cache)
        // before we add a second key. The handler is expected to refresh on
        // kid miss.
        using var host = await BuildCognitoHost(server, CognitoTokenUse.IdAndAccess);
        var client = host.GetTestClient();

        // Prime: mint and use a token signed by the original key.
        var primingIssuer = server.CreateCognitoIssuer();
        var primingToken = primingIssuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            primingToken
        );
        (await client.GetAsync("/protected")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Rotate: publish a new signing key, mint a token with it.
        server.AddSigningKey();
        var rotatedIssuer = server.CreateCognitoIssuer();
        var rotatedToken = rotatedIssuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            rotatedToken
        );
        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should()
            .Be(
                HttpStatusCode.OK,
                "the JWT bearer handler refreshes JWKS on kid miss and finds the new key"
            );
    }

    [Test]
    public async Task RefreshFlow_RotatedToken_NewAccessTokenValidates()
    {
        await using var server = await TestJwksServer.StartAsync();
        var issuer = server.CreateCognitoIssuer();
        var store = new InMemoryRefreshTokenStore();
        var sub = Guid.NewGuid();

        // Initial sign-in: issue refresh + access pair.
        var refresh = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromDays(30),
            CancellationToken.None
        );

        // Time passes. The client comes back with the refresh token to get a new access token.
        var rotated = await store.RotateAsync(refresh.Token, CancellationToken.None);
        rotated.Should().NotBeNull();

        var claims = await store.ValidateAsync(rotated!.Token, CancellationToken.None);
        claims.Should().NotBeNull();
        claims!.Sub.Should().Be(sub);

        var newAccess = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = claims.Sub,
                ClientId = claims.ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );

        using var host = await BuildCognitoHost(server, CognitoTokenUse.Access);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            newAccess
        );
        (await client.GetAsync("/protected")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<PrincipalSnapshot> ReadPrincipalSnapshot(HttpResponseMessage resp)
    {
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new PrincipalSnapshot
        {
            Id = root.GetProperty("id").GetString() ?? "",
            DisplayName = root.GetProperty("displayName").GetString() ?? "",
            PrincipalType = root.GetProperty("principalType").GetString() ?? "",
            Email = root.TryGetProperty("email", out var e) ? e.GetString() : null,
            IdentityProvider = root.TryGetProperty("identityProvider", out var ip)
                ? ip.GetString()
                : null,
            Roles = root.GetProperty("roles")
                .EnumerateArray()
                .Select(x => x.GetString()!)
                .ToArray(),
        };
    }

    private static async Task<IHost> BuildCognitoHost(
        TestJwksServer server,
        CognitoTokenUse tokenUse
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLogging();
                        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
                        services.AddTraxJwtAuth<CognitoJwtPrincipalResolver>(jwt =>
                        {
                            jwt.UseCognito(Region, UserPoolId, ClientId, tokenUse)
                                .AllowHttpMetadata();
                            // The JwtBearer middleware uses Authority to fetch JWKS. Override
                            // it to our test JWKS server so signature validation works.
                            jwt.CustomizeBearerOptions(opts =>
                            {
                                opts.Authority = server.Issuer;
                                opts.RequireHttpsMetadata = false;
                            });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints
                                .MapGet(
                                    "/protected",
                                    (ClaimsPrincipal user) =>
                                    {
                                        var id =
                                            user.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value
                                            ?? "";
                                        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                                        var type =
                                            user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value
                                            ?? "";
                                        var email =
                                            user.FindFirst("email")?.Value
                                            ?? user.FindFirst(ClaimTypes.Email)?.Value;
                                        var idp = user.FindFirst(
                                            CognitoDefaults.IdentityProvider
                                        )?.Value;
                                        var roles = user.FindAll(ClaimTypes.Role)
                                            .Select(c => c.Value)
                                            .ToArray();
                                        return Results.Json(
                                            new
                                            {
                                                id,
                                                displayName = name,
                                                principalType = type,
                                                email,
                                                identityProvider = idp,
                                                roles,
                                            }
                                        );
                                    }
                                )
                                .RequireAuthorization(JwtDefaults.PolicyName);
                        });
                    })
            )
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class PrincipalSnapshot
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string PrincipalType { get; init; } = "";
        public string? Email { get; init; }
        public string? IdentityProvider { get; init; }
        public string[] Roles { get; init; } = Array.Empty<string>();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
