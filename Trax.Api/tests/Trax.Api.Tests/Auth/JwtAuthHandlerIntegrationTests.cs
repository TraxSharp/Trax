using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtAuthHandlerIntegrationTests
{
    private const string Issuer = "https://trax-test";
    private const string Audience = "trax-api";
    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes(new string('k', 32));

    private static async Task<IHost> CreateHost(
        ITraxPrincipalResolver<JwtTokenInput>? customResolver = null,
        Action<JwtBuilder>? extraConfig = null
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddTraxJwtAuth(b =>
                        {
                            b.UseSymmetricKey(Issuer, Audience, KeyBytes);
                            b.WithClockSkew(TimeSpan.Zero);
                            extraConfig?.Invoke(b);
                        });
                        if (customResolver is not null)
                        {
                            // Override the default resolver registered by AddTraxJwtAuth.
                            services.RemoveAll<ITraxPrincipalResolver<JwtTokenInput>>();
                            services.AddSingleton<ITraxPrincipalResolver<JwtTokenInput>>(
                                customResolver
                            );
                        }
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
                                        Results.Ok(
                                            new ProtectedResponse
                                            {
                                                Name = user.Identity?.Name,
                                                PrincipalId = user.FindFirst(
                                                    TraxAuthClaimTypes.PrincipalId
                                                )?.Value,
                                                PrincipalType = user.FindFirst(
                                                    TraxAuthClaimTypes.PrincipalType
                                                )?.Value,
                                                Roles = user.FindAll(ClaimTypes.Role)
                                                    .Select(c => c.Value)
                                                    .ToArray(),
                                            }
                                        )
                                )
                                .RequireAuthorization(JwtDefaults.PolicyName);

                            endpoints.MapGet("/anonymous", () => Results.Ok("ok")).AllowAnonymous();

                            endpoints.MapGet(
                                "/authresult",
                                async (HttpContext ctx) =>
                                {
                                    var result = await ctx.AuthenticateAsync(
                                        JwtDefaults.SchemeName
                                    );
                                    return Results.Ok(
                                        new AuthResultResponse
                                        {
                                            Succeeded = result.Succeeded,
                                            None = result.None,
                                            FailureMessage = result.Failure?.Message,
                                        }
                                    );
                                }
                            );
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static string SignToken(
        IEnumerable<Claim> claims,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string? audience = null,
        string? issuer = null
    )
    {
        var key = new SymmetricSecurityKey(KeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Test]
    public async Task MissingHeader_Returns401OnProtected()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task MissingHeader_AnonymousEndpointWorks()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/anonymous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ValidToken_AuthenticatesAndExposesTraxPrincipalClaims()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var token = SignToken(
            new[]
            {
                new Claim("sub", "alice"),
                new Claim("name", "Alice Liddell"),
                new Claim(ClaimTypes.Role, "Admin"),
            }
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response.Should().NotBeNull();
        response!.PrincipalId.Should().Be("alice");
        response.Name.Should().Be("Alice Liddell");
        response.PrincipalType.Should().Be(JwtDefaults.PrincipalType);
        response.Roles.Should().Contain("Admin");
    }

    [Test]
    public async Task ExpiredToken_Returns401()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var token = SignToken(
            new[] { new Claim("sub", "alice") },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1)
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WrongIssuer_Returns401()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var token = SignToken(new[] { new Claim("sub", "alice") }, issuer: "https://rogue-issuer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task WrongAudience_Returns401()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var token = SignToken(new[] { new Claim("sub", "alice") }, audience: "wrong-aud");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TokenSignedWithDifferentKey_Returns401()
    {
        using var host = await CreateHost();
        var client = host.GetTestClient();

        var otherKey = Encoding.UTF8.GetBytes(new string('z', 32));
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(otherKey),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("sub", "alice") },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );
        var signed = new JwtSecurityTokenHandler().WriteToken(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            signed
        );

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ResolverReturnsNull_Returns401()
    {
        using var host = await CreateHost(new NullResolver());
        var client = host.GetTestClient();

        var token = SignToken(new[] { new Claim("sub", "alice") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ResolverThrows_Returns401_NotServerError()
    {
        using var host = await CreateHost(new ThrowingResolver());
        var client = host.GetTestClient();

        var token = SignToken(new[] { new Claim("sub", "alice") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CustomResolver_CanIssueTraxPrincipal()
    {
        using var host = await CreateHost(new FixedPrincipalResolver());
        var client = host.GetTestClient();

        var token = SignToken(new[] { new Claim("sub", "ignored") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response!.PrincipalId.Should().Be("override-id");
        response.Name.Should().Be("Overridden");
    }

    private sealed class NullResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            new((TraxPrincipal?)null);
    }

    private sealed class ThrowingResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FixedPrincipalResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            new(
                new TraxPrincipal(
                    "override-id",
                    "Overridden",
                    ["Admin"],
                    PrincipalType: JwtDefaults.PrincipalType
                )
            );
    }

    private sealed class ProtectedResponse
    {
        public string? Name { get; set; }
        public string? PrincipalId { get; set; }
        public string? PrincipalType { get; set; }
        public string[] Roles { get; set; } = [];
    }

    private sealed class AuthResultResponse
    {
        public bool Succeeded { get; set; }
        public bool None { get; set; }
        public string? FailureMessage { get; set; }
    }
}
