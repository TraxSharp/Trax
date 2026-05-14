using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
public class JwtMultiSchemeTests
{
    private const string IssuerAlpha = "https://idp-alpha";
    private const string IssuerBeta = "https://idp-beta";
    private const string AudienceAlpha = "alpha-aud";
    private const string AudienceBeta = "beta-aud";
    private static readonly byte[] KeyAlpha = Encoding.UTF8.GetBytes(new string('a', 32));
    private static readonly byte[] KeyBeta = Encoding.UTF8.GetBytes(new string('b', 32));

    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        return services;
    }

    [Test]
    public void NamedScheme_RegistersScheme()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        using var sp = services.BuildServiceProvider();

        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        schemes.GetSchemeAsync("alpha").GetAwaiter().GetResult().Should().NotBeNull();
    }

    [Test]
    public void NamedScheme_RegistersPerSchemePolicy()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = policyProvider.GetPolicyAsync("alpha-JwtPolicy").GetAwaiter().GetResult();

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain("alpha");
    }

    [Test]
    public void DefaultScheme_RegistersStandardPolicyName()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha));
        using var sp = services.BuildServiceProvider();

        var policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = policyProvider.GetPolicyAsync(JwtDefaults.PolicyName).GetAwaiter().GetResult();

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtDefaults.SchemeName);
    }

    [Test]
    public void MultipleSchemes_AllJoinTraxAuthPolicy()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtAuth(
            "beta",
            jwt => jwt.UseSymmetricKey(IssuerBeta, AudienceBeta, KeyBeta)
        );
        using var sp = services.BuildServiceProvider();

        var policy = sp.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(TraxAuthClaimTypes.TraxAuthPolicy)
            .GetAwaiter()
            .GetResult();

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(new[] { "alpha", "beta" });
    }

    [Test]
    public void NamedScheme_DoesNotPolluteDefaultResolverRegistration()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );

        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeFalse();
    }

    [Test]
    public void NamedScheme_WithCustomResolver_RegistersOnlyKeyedInRegistry()
    {
        var services = NewServices();

        services.AddTraxJwtAuth<AlphaResolver>(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );

        // Should not register ITraxPrincipalResolver<JwtTokenInput> in DI;
        // back-compat is for the default scheme only.
        services
            .Any(sd => sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>))
            .Should()
            .BeFalse();

        // But the resolver type itself is registered scoped.
        services
            .Any(sd =>
                sd.ServiceType == typeof(AlphaResolver) && sd.Lifetime == ServiceLifetime.Scoped
            )
            .Should()
            .BeTrue();
    }

    [Test]
    public void EmptySchemeName_Throws()
    {
        var services = NewServices();

        Action act = () =>
            services.AddTraxJwtAuth(
                "",
                jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
            );

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void NullSchemeName_Throws()
    {
        var services = NewServices();

        Action act = () =>
            services.AddTraxJwtAuth(
                (string)null!,
                jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
            );

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void NullConfigure_OnNamedScheme_Throws()
    {
        var services = NewServices();

        Action act = () => services.AddTraxJwtAuth("alpha", (Action<JwtBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GenericOverload_EmptySchemeName_Throws()
    {
        var services = NewServices();

        Action act = () =>
            services.AddTraxJwtAuth<AlphaResolver>(
                "",
                jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
            );

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void GenericOverload_NullConfigure_Throws()
    {
        var services = NewServices();

        Action act = () =>
            services.AddTraxJwtAuth<AlphaResolver>("alpha", (Action<JwtBuilder>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public async Task TwoSchemes_TokenForAlpha_ValidatesUnderAlphaOnly()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<EndpointResponse>("/protected-alpha");
        response.Should().NotBeNull();
        response!.PrincipalId.Should().Be("alice");
    }

    [Test]
    public async Task TwoSchemes_TokenForAlpha_RejectedByBetaPolicy()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected-beta");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TwoSchemes_DifferentResolvers_ProducedifferentPrincipals()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        var alpha = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        var beta = Sign(IssuerBeta, AudienceBeta, KeyBeta, "bob");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", alpha);
        var aResp = await client.GetFromJsonAsync<EndpointResponse>("/protected-alpha");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", beta);
        var bResp = await client.GetFromJsonAsync<EndpointResponse>("/protected-beta");

        aResp!.PrincipalType.Should().Be("alpha");
        bResp!.PrincipalType.Should().Be("beta");
    }

    [Test]
    public async Task TwoSchemes_TokenSignedWithBetaKey_PresentingAlphaIssuer_Returns401()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        // Sign with beta key but claim alpha issuer.
        var token = Sign(IssuerAlpha, AudienceAlpha, KeyBeta, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/protected-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TraxAuthPolicy_AcceptsAlphaToken_WhenTwoSchemesRegistered()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/trax-policy");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TraxAuthPolicy_AcceptsBetaToken_WhenTwoSchemesRegistered()
    {
        using var host = await BuildHostWithTwoSchemes();
        var client = host.GetTestClient();

        var token = Sign(IssuerBeta, AudienceBeta, KeyBeta, "bob");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/trax-policy");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task NamedScheme_DefaultResolver_RoundTripsToTraxPrincipal()
    {
        using var host = await BuildHostWithTwoSchemes(useCustomResolver: false);
        var client = host.GetTestClient();

        var token = Sign(IssuerAlpha, AudienceAlpha, KeyAlpha, "alice");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetFromJsonAsync<EndpointResponse>("/protected-alpha");

        response!.PrincipalId.Should().Be("alice");
        response.PrincipalType.Should().Be(JwtDefaults.PrincipalType);
    }

    [Test]
    public void NamedScheme_DefaultResolver_RegistersDefaultJwtPrincipalResolverInDi()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );

        // The concrete DefaultJwtPrincipalResolver type is registered as a
        // singleton so the registry factory can resolve it from DI.
        services
            .Any(sd => sd.ServiceType == typeof(DefaultJwtPrincipalResolver))
            .Should()
            .BeTrue();
    }

    [Test]
    public void NamedScheme_DefaultResolver_NotDuplicatedOnSecondNamedRegistration()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(
            "alpha",
            jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
        );
        services.AddTraxJwtAuth(
            "beta",
            jwt => jwt.UseSymmetricKey(IssuerBeta, AudienceBeta, KeyBeta)
        );

        services.Count(sd => sd.ServiceType == typeof(DefaultJwtPrincipalResolver)).Should().Be(1);
    }

    [Test]
    public void Default_PlusNamed_ProvidesBackCompatBindingAndRegistryEntries()
    {
        var services = NewServices();

        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha));
        services.AddTraxJwtAuth(
            "named",
            jwt => jwt.UseSymmetricKey(IssuerBeta, AudienceBeta, KeyBeta)
        );

        // Back-compat: ITraxPrincipalResolver<JwtTokenInput> registered (default scheme).
        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
                && sd.ImplementationType == typeof(DefaultJwtPrincipalResolver)
            );
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

    private static async Task<IHost> BuildHostWithTwoSchemes(bool useCustomResolver = true)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLogging();
                        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

                        if (useCustomResolver)
                        {
                            services.AddTraxJwtAuth<AlphaResolver>(
                                "alpha",
                                jwt =>
                                    jwt.UseSymmetricKey(IssuerAlpha, AudienceAlpha, KeyAlpha)
                                        .WithClockSkew(TimeSpan.Zero)
                            );
                            services.AddTraxJwtAuth<BetaResolver>(
                                "beta",
                                jwt =>
                                    jwt.UseSymmetricKey(IssuerBeta, AudienceBeta, KeyBeta)
                                        .WithClockSkew(TimeSpan.Zero)
                            );
                        }
                        else
                        {
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
                                    "/protected-alpha",
                                    (ClaimsPrincipal user) => Results.Ok(MakeResponse(user))
                                )
                                .RequireAuthorization("alpha-JwtPolicy");
                            endpoints
                                .MapGet(
                                    "/protected-beta",
                                    (ClaimsPrincipal user) => Results.Ok(MakeResponse(user))
                                )
                                .RequireAuthorization("beta-JwtPolicy");
                            endpoints
                                .MapGet(
                                    "/trax-policy",
                                    (ClaimsPrincipal user) => Results.Ok(MakeResponse(user))
                                )
                                .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static EndpointResponse MakeResponse(ClaimsPrincipal user) =>
        new()
        {
            PrincipalId = user.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value,
            PrincipalType = user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value,
        };

    private sealed class EndpointResponse
    {
        public string? PrincipalId { get; set; }
        public string? PrincipalType { get; set; }
    }

    private sealed class AlphaResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            new(BuildPrincipal(input.Principal, "alpha"));
    }

    private sealed class BetaResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            new(BuildPrincipal(input.Principal, "beta"));
    }

    private static TraxPrincipal? BuildPrincipal(ClaimsPrincipal principal, string discriminator)
    {
        // JwtBearer maps "sub" to ClaimTypes.NameIdentifier by default; check both.
        var sub =
            principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null)
            return null;
        return new TraxPrincipal(sub, sub, [], null, PrincipalType: discriminator);
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
