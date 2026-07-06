using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Covers JWKS / Authority-backed JWT validation over WebSockets. The stock
/// interceptor previously validated against TokenValidationParameters that carry
/// no signing keys until the OIDC discovery document is fetched, so every
/// JWKS-backed token (Cognito, Google, any OIDC provider) was rejected. These
/// tests run against a real in-process JWKS server.
/// </summary>
[TestFixture]
public class TraxJwtSocketInterceptorJwksTests
{
    private const string Audience = "trax-ws-jwks";

    /// <summary>
    /// Wires an Authority (JWKS) JWT scheme against <paramref name="authority"/> and
    /// returns a live options monitor plus the default principal resolver, exactly
    /// as a host would after <c>AddTraxJwtAuth(authority, audience)</c>.
    /// </summary>
    private static (
        ServiceProvider Provider,
        IOptionsMonitor<JwtBearerOptions> Monitor,
        ITraxPrincipalResolver<JwtTokenInput> Resolver
    ) WireAuthorityScheme(string authority)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth(jwt => jwt.UseAuthority(authority, Audience).AllowHttpMetadata());
        var sp = services.BuildServiceProvider();
        return (
            sp,
            sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
            sp.GetRequiredService<ITraxPrincipalResolver<JwtTokenInput>>()
        );
    }

    private static TraxJwtSocketInterceptor NewInterceptor(
        IOptionsMonitor<JwtBearerOptions> monitor,
        ITraxPrincipalResolver<JwtTokenInput> resolver
    ) => new(monitor, resolver, NullLogger<TraxJwtSocketInterceptor>.Instance);

    [Test]
    public async Task JwksScheme_ValidToken_Accepts_AttachesPrincipal()
    {
        await using var server = await TestJwksServer.StartAsync();
        var (sp, monitor, resolver) = WireAuthorityScheme(server.Issuer);
        await using var _ = sp;

        var token = server
            .CreateIssuer(Audience)
            .Mint(b => b.WithSubject("alice").WithClaim("name", "Alice").WithRole("Player"));

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, http) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeTrue();
        http.User.Identity!.IsAuthenticated.Should().BeTrue();
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
        http.User.IsInRole("Player").Should().BeTrue();
    }

    [Test]
    public async Task JwksScheme_KeyNotInJwks_Rejects()
    {
        // Token carries the validating server's issuer/audience but is signed by
        // a different server's key, so the kid is absent from the JWKS.
        await using var validatingServer = await TestJwksServer.StartAsync();
        await using var foreignServer = await TestJwksServer.StartAsync();
        var (sp, monitor, resolver) = WireAuthorityScheme(validatingServer.Issuer);
        await using var _ = sp;

        var token = foreignServer
            .CreateIssuer(Audience)
            .Mint(b => b.WithSubject("alice").WithIssuer(validatingServer.Issuer));

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, _sess) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task JwksScheme_Expired_Rejects()
    {
        await using var server = await TestJwksServer.StartAsync();
        var (sp, monitor, resolver) = WireAuthorityScheme(server.Issuer);
        await using var _ = sp;

        var token = server
            .CreateIssuer(Audience)
            .Mint(b =>
                b.WithSubject("alice")
                    .WithNotBefore(DateTime.UtcNow.AddMinutes(-10))
                    .WithExpires(DateTime.UtcNow.AddMinutes(-5))
            );

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, _sess) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task JwksScheme_WrongAudience_Rejects()
    {
        await using var server = await TestJwksServer.StartAsync();
        var (sp, monitor, resolver) = WireAuthorityScheme(server.Issuer);
        await using var _ = sp;

        var token = server
            .CreateIssuer(Audience)
            .Mint(b => b.WithSubject("alice").WithAudience("some-other-audience"));

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, _sess) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task JwksScheme_WrongIssuer_Rejects()
    {
        await using var server = await TestJwksServer.StartAsync();
        var (sp, monitor, resolver) = WireAuthorityScheme(server.Issuer);
        await using var _ = sp;

        // Signed by the server's real key, but the iss claim is a stranger.
        var token = server
            .CreateIssuer(Audience)
            .Mint(b => b.WithSubject("alice").WithIssuer("https://rogue-issuer"));

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, _sess) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task JwksScheme_DiscoveryUnreachable_Rejects_DoesNotThrow()
    {
        // Authority points at a closed loopback port: the discovery fetch fails.
        // The interceptor must turn that into a reject, not let it bubble.
        var (sp, monitor, resolver) = WireAuthorityScheme("http://127.0.0.1:1");
        await using var _ = sp;

        var token = TestTokenIssuer
            .Symmetric(
                "http://127.0.0.1:1",
                Audience,
                System.Text.Encoding.UTF8.GetBytes(new string('k', 32))
            )
            .Mint(b => b.WithSubject("alice"));

        var interceptor = NewInterceptor(monitor, resolver);
        var (session, _sess) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("JWT validation failed");
    }
}
