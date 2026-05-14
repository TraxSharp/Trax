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
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtDispatcherTests
{
    private const string IssuerAlpha = "https://idp-alpha";
    private const string IssuerBeta = "https://idp-beta";
    private const string AudienceAlpha = "alpha-aud";
    private const string AudienceBeta = "beta-aud";
    private static readonly byte[] KeyAlpha = Encoding.UTF8.GetBytes(new string('a', 32));
    private static readonly byte[] KeyBeta = Encoding.UTF8.GetBytes(new string('b', 32));

    [Test]
    public void AddDispatcher_EmptyMappings_Throws()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );

        Action act = () => services.AddTraxJwtDispatcher(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*MapIssuer*");
    }

    [Test]
    public void AddDispatcher_NullConfigure_Throws()
    {
        var services = NewServices();

        Action act = () => services.AddTraxJwtDispatcher(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddDispatcher_RegistersSchemeUnderDefaultName()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes
            .GetSchemeAsync(JwtDefaults.DispatcherSchemeName)
            .GetAwaiter()
            .GetResult()
            .Should()
            .NotBeNull();
    }

    [Test]
    public void AddDispatcher_AlsoRegistersRejectScheme()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes
            .GetSchemeAsync(JwtDefaults.RejectSchemeName)
            .GetAwaiter()
            .GetResult()
            .Should()
            .NotBeNull();
    }

    [Test]
    public void AddDispatcher_Twice_Throws()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));

        Action act = () => services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Test]
    public void AddDispatcher_WithCustomSchemeName_RegistersUnderIt()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d =>
            d.WithSchemeName("CustomDispatcher").MapIssuer(IssuerAlpha, "alpha")
        );
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes.GetSchemeAsync("CustomDispatcher").GetAwaiter().GetResult().Should().NotBeNull();
    }

    [Test]
    public async Task AddDispatcher_RegistersPerSchemePolicy()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));
        using var sp = services.BuildServiceProvider();

        var policy =
            await sp.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>()
                .GetPolicyAsync(JwtDefaults.DispatcherSchemeName + "-JwtPolicy");

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtDefaults.DispatcherSchemeName);
    }

    [Test]
    public void AddDispatcher_DispatcherSchemeName_AddedToTraxAuthPolicy()
    {
        var services = NewServices();
        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerAlpha, "alpha"));
        using var sp = services.BuildServiceProvider();

        var policy =
            sp.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>()
                .GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy)
                .GetAwaiter()
                .GetResult();

        policy!.AuthenticationSchemes.Should().Contain(JwtDefaults.DispatcherSchemeName);
    }

    [Test]
    public async Task Dispatcher_RoutesAlphaIssuer_ToAlphaScheme()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<EndpointResponse>("/dispatched");
        response!.PrincipalId.Should().Be("alice");
    }

    [Test]
    public async Task Dispatcher_RoutesBetaIssuer_ToBetaScheme()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var token = Sign(IssuerBeta, AudienceBeta, KeyBeta, "bob");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<EndpointResponse>("/dispatched");
        response!.PrincipalId.Should().Be("bob");
    }

    [Test]
    public async Task Dispatcher_UnknownIssuer_Returns401_DefaultReject()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var token = Sign("https://idp-unknown", AudienceAlpha, KeyAlpha, "x");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_MissingHeader_Returns401_DefaultReject()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_MalformedToken_Returns401()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "not-a-real-token"
        );

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_NonBearerScheme_Returns401()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "abc");

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_WithFallback_RoutesUnknownIssuerToFallbackScheme()
    {
        using var host = await BuildHost(fallback: "alpha");
        var client = host.GetTestClient();

        // Unknown issuer falls back to alpha, which then rejects on its own issuer check.
        var token = Sign("https://idp-unknown", AudienceAlpha, KeyAlpha, "x");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dispatched");

        // Token claims a wrong issuer relative to alpha's signing scheme, so still 401.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_PolicyForwardSelectorPicksRejectByDefault()
    {
        using var host = await BuildHost();
        var client = host.GetTestClient();

        // Make a token whose signature CAN'T be peeked (no parseable structure).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "...");

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Dispatcher_RouteRejectsTokenForOtherSchemesValidationFails()
    {
        // Send a token whose iss matches alpha but signature is from beta key.
        using var host = await BuildHost();
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyBeta, "intruder");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dispatched");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        return services;
    }

    private static string Sign(string issuer, string audience, byte[] key, string sub)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[] { new Claim("sub", sub) },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<IHost> BuildHost(string? fallback = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLogging();
                        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
                        services.AddTraxJwtAuth(
                            "alpha",
                            jwt =>
                                jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
                                    .WithClockSkew(TimeSpan.Zero)
                        );
                        services.AddTraxJwtAuth(
                            "beta",
                            jwt =>
                                jwt.UseSymmetricKey(IssuerBeta, AudienceBeta, KeyBeta)
                                    .WithClockSkew(TimeSpan.Zero)
                        );
                        services.AddTraxJwtDispatcher(d =>
                        {
                            d.MapIssuer(IssuerAlpha, "alpha");
                            d.MapIssuer(IssuerBeta, "beta");
                            if (fallback is not null)
                                d.FallbackToScheme(fallback);
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
                                    "/dispatched",
                                    (ClaimsPrincipal user) =>
                                        Results.Ok(
                                            new EndpointResponse
                                            {
                                                PrincipalId = user.FindFirst(
                                                    TraxAuthClaimTypes.PrincipalId
                                                )?.Value,
                                            }
                                        )
                                )
                                .RequireAuthorization(
                                    JwtDefaults.DispatcherSchemeName + "-JwtPolicy"
                                );
                        });
                    })
            )
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class EndpointResponse
    {
        public string? PrincipalId { get; set; }
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
