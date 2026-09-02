using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Covers the dispatcher-aware socket interceptor: subscription tokens are routed
/// to a JWT scheme by their <c>iss</c> claim, and the selected scheme performs
/// full validation. Uses two symmetric schemes so the dispatch logic is exercised
/// without a JWKS server (JWKS resolution is covered by
/// <see cref="TraxJwtSocketInterceptorJwksTests"/> and the dispatcher E2E).
/// </summary>
[TestFixture]
public class TraxJwtDispatcherSocketInterceptorTests
{
    private const string Audience = "trax-ws";
    private const string IssuerA = "https://issuer-a";
    private const string IssuerB = "https://issuer-b";
    private static readonly byte[] KeyA = Encoding.UTF8.GetBytes(new string('a', 32));
    private static readonly byte[] KeyB = Encoding.UTF8.GetBytes(new string('b', 32));

    private static ServiceProvider TwoSchemeDispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth("a", jwt => jwt.UseSymmetricKey(IssuerA, Audience, KeyA));
        services.AddTraxJwtAuth("b", jwt => jwt.UseSymmetricKey(IssuerB, Audience, KeyB));
        services.AddTraxJwtDispatcher(d => d.MapIssuer(IssuerA, "a").MapIssuer(IssuerB, "b"));
        return services.BuildServiceProvider();
    }

    private static TraxJwtDispatcherSocketInterceptor NewInterceptor(IServiceProvider sp) =>
        new(
            sp.GetRequiredService<JwtDispatcherRuntime>(),
            sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
            new TraxApplicationServices(sp),
            NullLogger<TraxJwtDispatcherSocketInterceptor>.Instance
        );

    [Test]
    public async Task MissingToken_Rejects()
    {
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, _) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            EmptyPayload(),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Missing auth token");
    }

    [Test]
    public async Task UnknownIssuer_Rejects_NoFallback()
    {
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, _) = NewSession();

        var token = TestTokenIssuer
            .Symmetric("https://stranger", Audience, KeyA)
            .Mint(b => b.WithSubject("nobody"));

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtDispatcherSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("issuer is not recognized");
    }

    [Test]
    public async Task IssuerA_Token_Accepts_ViaSchemeA()
    {
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, http) = NewSession();

        var token = TestTokenIssuer
            .Symmetric(IssuerA, Audience, KeyA)
            .Mint(b => b.WithSubject("alice").WithClaim("name", "Alice"));

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtDispatcherSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeTrue();
        http.User.Identity!.IsAuthenticated.Should().BeTrue();
        http.User.Identity.AuthenticationType.Should().Be("a");
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
    }

    [Test]
    public async Task IssuerB_Token_Accepts_ViaSchemeB()
    {
        // Dispatch works both ways: the second issuer routes to its own scheme.
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, http) = NewSession();

        var token = TestTokenIssuer
            .Symmetric(IssuerB, Audience, KeyB)
            .Mint(b => b.WithSubject("bob").WithClaim("name", "Bob"));

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtDispatcherSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeTrue();
        http.User.Identity!.AuthenticationType.Should().Be("b");
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("bob");
    }

    [Test]
    public async Task MappedIssuer_ButSignedWithWrongKey_Rejects()
    {
        // iss routes to scheme "a", but the token is signed with scheme "b"'s key.
        // The selected scheme's signature check must reject it: dispatch by iss is
        // routing only, never a validation shortcut.
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, _) = NewSession();

        var token = TestTokenIssuer
            .Symmetric(IssuerA, Audience, KeyB)
            .Mint(b => b.WithSubject("attacker"));

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxJwtDispatcherSocketInterceptor.ConnectionInitPayload(token, null)),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task BearerFieldName_AlsoAccepts()
    {
        await using var sp = TwoSchemeDispatcher();
        var interceptor = NewInterceptor(sp);
        var (session, http) = NewSession();

        var token = TestTokenIssuer
            .Symmetric(IssuerA, Audience, KeyA)
            .Mint(b => b.WithSubject("alice"));

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(
                new TraxJwtDispatcherSocketInterceptor.ConnectionInitPayload(
                    AuthToken: null,
                    Bearer: token
                )
            ),
            CancellationToken.None
        );

        result.Accepted.Should().BeTrue();
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
    }
}
