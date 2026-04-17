using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxJwtSocketInterceptorTests
{
    private const string Issuer = "https://trax-ws-test";
    private const string Audience = "trax-ws";
    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes(new string('k', 32));

    private static IOptionsMonitor<JwtBearerOptions> OptionsMonitor(
        Action<JwtBearerOptions>? customize = null
    )
    {
        var opts = new JwtBearerOptions
        {
            TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = new SymmetricSecurityKey(KeyBytes),
                ClockSkew = TimeSpan.Zero,
            },
        };
        customize?.Invoke(opts);

        var monitor = Substitute.For<IOptionsMonitor<JwtBearerOptions>>();
        monitor.Get(JwtDefaults.SchemeName).Returns(opts);
        return monitor;
    }

    private static string SignToken(
        IEnumerable<Claim> claims,
        DateTime? expires = null,
        string? issuer = null,
        string? audience = null,
        byte[]? keyBytes = null
    )
    {
        var key = new SymmetricSecurityKey(keyBytes ?? KeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var effectiveExpires = expires ?? DateTime.UtcNow.AddMinutes(5);
        // notBefore must be <= expires; back it off so expired-token tests pass.
        var notBefore = effectiveExpires.AddMinutes(-10);
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: effectiveExpires,
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TraxJwtSocketInterceptor NewInterceptor(
        ITraxPrincipalResolver<JwtTokenInput>? resolver = null,
        Action<JwtBearerOptions>? customize = null
    ) =>
        new(
            OptionsMonitor(customize),
            resolver ?? new DefaultJwtPrincipalResolver(),
            NullLogger<TraxJwtSocketInterceptor>.Instance
        );

    [Test]
    public async Task EmptyPayload_Rejects()
    {
        var interceptor = NewInterceptor();
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
    public async Task MissingAuthToken_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var payload = Payload(
            new TraxJwtSocketInterceptor.ConnectionInitPayload(AuthToken: null, Bearer: null)
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Missing auth token");
    }

    [Test]
    public async Task WhitespaceToken_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload("   ", null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Missing auth token");
    }

    [Test]
    public async Task MalformedToken_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var payload = Payload(
            new TraxJwtSocketInterceptor.ConnectionInitPayload("not.a.jwt", null)
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task WrongSignature_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var token = SignToken(
            [new Claim("sub", "alice")],
            keyBytes: Encoding.UTF8.GetBytes(new string('z', 32))
        );
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task ExpiredToken_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice")], expires: DateTime.UtcNow.AddMinutes(-5));
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task WrongIssuer_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice")], issuer: "https://rogue");
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task WrongAudience_Rejects()
    {
        var interceptor = NewInterceptor();
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice")], audience: "wrong-aud");
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid JWT");
    }

    [Test]
    public async Task ResolverReturnsNull_Rejects()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<JwtTokenInput>>();
        resolver
            .ResolveAsync(Arg.Any<JwtTokenInput>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>((TraxPrincipal?)null));

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("did not map");
    }

    [Test]
    public async Task ResolverThrows_Rejects_DoesNotBubble()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<JwtTokenInput>>();
        resolver
            .ResolveAsync(Arg.Any<JwtTokenInput>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<TraxPrincipal?>>(_ => throw new InvalidOperationException("boom"));

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("resolver failed");
    }

    [Test]
    public async Task ValidToken_AuthTokenKey_Accepts_AttachesPrincipal()
    {
        var interceptor = NewInterceptor();
        var (session, http) = NewSession();

        var token = SignToken([
            new Claim("sub", "alice"),
            new Claim("name", "Alice"),
            new Claim(ClaimTypes.Role, "Player"),
        ]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeTrue();
        http.User.Should().NotBeNull();
        http.User.Identity!.IsAuthenticated.Should().BeTrue();
        http.User.Identity.AuthenticationType.Should().Be(JwtDefaults.SchemeName);
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
        http.User.IsInRole("Player").Should().BeTrue();
    }

    [Test]
    public async Task ValidToken_BearerKey_AlsoAccepts()
    {
        // The interceptor accepts either 'authToken' (graphql-transport-ws
        // convention) or 'bearer' (mirrors the HTTP header name).
        var interceptor = NewInterceptor();
        var (session, http) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(
            new TraxJwtSocketInterceptor.ConnectionInitPayload(AuthToken: null, Bearer: token)
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeTrue();
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
    }

    [Test]
    public async Task ValidToken_PrincipalType_IsJwt()
    {
        var interceptor = NewInterceptor();
        var (session, http) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        http.User.FindFirst(TraxAuthClaimTypes.PrincipalType)!
            .Value.Should()
            .Be(JwtDefaults.PrincipalType);
    }

    [Test]
    public async Task CustomResolver_CanRemapPrincipal()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<JwtTokenInput>>();
        resolver
            .ResolveAsync(Arg.Any<JwtTokenInput>(), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<TraxPrincipal?>(
                    new TraxPrincipal(
                        "override-id",
                        "Overridden",
                        ["Admin"],
                        PrincipalType: JwtDefaults.PrincipalType
                    )
                )
            );

        var interceptor = NewInterceptor(resolver);
        var (session, http) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("override-id");
        http.User.IsInRole("Admin").Should().BeTrue();
    }

    [Test]
    public async Task ResolverReceivesValidatedPrincipalAndToken()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<JwtTokenInput>>();
        resolver
            .ResolveAsync(Arg.Any<JwtTokenInput>(), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<TraxPrincipal?>(
                    new TraxPrincipal(
                        "alice",
                        "Alice",
                        [],
                        PrincipalType: JwtDefaults.PrincipalType
                    )
                )
            );

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        var token = SignToken([new Claim("sub", "alice"), new Claim("name", "Alice")]);
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        await resolver
            .Received(1)
            .ResolveAsync(
                Arg.Is<JwtTokenInput>(i =>
                    i.Principal.FindFirst("sub")!.Value == "alice" && i.SecurityToken != null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task TokenValidationFailsOutsideClockSkew()
    {
        // Tight skew — a token 10 seconds expired must be rejected when
        // clockSkew is zero.
        var interceptor = NewInterceptor(customize: opts =>
            opts.TokenValidationParameters.ClockSkew = TimeSpan.Zero
        );
        var (session, _) = NewSession();

        var token = SignToken(
            [new Claim("sub", "alice"), new Claim("name", "Alice")],
            expires: DateTime.UtcNow.AddSeconds(-10)
        );
        var payload = Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
    }
}
