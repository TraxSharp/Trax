using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using HotChocolate.AspNetCore.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// The composite socket interceptor routes each <c>connection_init</c> to the API-key or JWT
/// strategy from the credential it carries, reading the registered schemes from a real
/// container. Every branch is exercised together because a composite regresses one branch
/// silently.
///
/// <para>Enforces <c>docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md")]
[TestFixture]
public class TraxCompositeSocketInterceptorTests
{
    private const string Adr =
        "docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md";
    private const string Issuer = "https://composite-test";
    private const string OtherIssuer = "https://composite-test-other";
    private const string Audience = "composite";
    internal const string ApiKey = "composite-api-key";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(new string('c', 32));
    private static readonly byte[] OtherKey = Encoding.UTF8.GetBytes(new string('o', 32));

    [Flags]
    public enum Schemes
    {
        None = 0,
        ApiKey = 1,
        Jwt = 2,
        Dispatcher = 4,
    }

    internal static TraxCompositeSocketInterceptor Build(Schemes schemes)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (schemes.HasFlag(Schemes.ApiKey))
            services.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "key-user", "Player"));

        if (schemes.HasFlag(Schemes.Jwt))
            services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(Issuer, Audience, Key));

        if (schemes.HasFlag(Schemes.Dispatcher))
        {
            services.AddTraxJwtAuth("primary", jwt => jwt.UseSymmetricKey(Issuer, Audience, Key));
            services.AddTraxJwtAuth(
                "other",
                jwt => jwt.UseSymmetricKey(OtherIssuer, Audience, OtherKey)
            );
            services.AddTraxJwtDispatcher(d =>
                d.MapIssuer(Issuer, "primary").MapIssuer(OtherIssuer, "other")
            );
        }

        return new TraxCompositeSocketInterceptor(
            new TraxApplicationServices(services.BuildServiceProvider())
        );
    }

    private static string SignJwt(string sub, string issuer = Issuer, byte[]? key = null)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(key ?? Key),
            SecurityAlgorithms.HmacSha256
        );
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                issuer: issuer,
                audience: Audience,
                claims: [new Claim("sub", sub)],
                notBefore: now.AddMinutes(-1),
                expires: now.AddMinutes(10),
                signingCredentials: creds
            )
        );
    }

    private static async Task<(Status Status, string? Principal)> ConnectAsync(
        TraxCompositeSocketInterceptor interceptor,
        object payload
    )
    {
        var (session, http) = NewSession();
        var status = await interceptor.OnConnectAsync(session, Payload(payload));
        return (
            new Status(status.Accepted),
            http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null
        );
    }

    #region Both API key and JWT registered

    [Test]
    public async Task Both_ApiKeyInAuthToken_RoutesToApiKey()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { authToken = ApiKey }
        );

        status.Accepted.Should().BeTrue("an opaque authToken is an API key, per " + Adr);
    }

    [Test]
    public async Task Both_ApiKeyInApiKey_RoutesToApiKey()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { apiKey = ApiKey }
        );

        status.Accepted.Should().BeTrue("an apiKey field is an API key, per " + Adr);
    }

    [Test]
    public async Task Both_JwtInAuthToken_RoutesToJwt()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { authToken = SignJwt("alice") }
        );

        status.Accepted.Should().BeTrue("a JWT-shaped authToken is a JWT, per " + Adr);
    }

    [Test]
    public async Task Both_JwtInBearer_RoutesToJwt()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { bearer = SignJwt("alice") }
        );

        status.Accepted.Should().BeTrue("a bearer field is a JWT, per " + Adr);
    }

    [Test]
    public async Task Both_NoCredential_Rejected()
    {
        var (status, principal) = await ConnectAsync(Build(Schemes.ApiKey | Schemes.Jwt), new { });

        status.Accepted.Should().BeFalse();
        principal.Should().BeNull();
    }

    [Test]
    public async Task Both_UnknownApiKey_Rejected()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { authToken = "not-a-key" }
        );

        status.Accepted.Should().BeFalse();
    }

    [Test]
    public async Task Both_JwtWithWrongKey_RejectedAndNotRetriedAsApiKey()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { authToken = SignJwt("alice", key: OtherKey) }
        );

        status
            .Accepted.Should()
            .BeFalse("a credential is validated by one strategy only, per " + Adr);
    }

    [Test]
    public async Task Both_AuthTokenWinsOverApiKeyField()
    {
        // authToken is what each strategy reads first, so routing on it keeps the credential
        // routed on and the credential validated the same.
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey | Schemes.Jwt),
            new { authToken = SignJwt("alice"), apiKey = "not-a-key" }
        );

        status.Accepted.Should().BeTrue();
    }

    [Test]
    public async Task Both_DottedApiKey_StaysOnTheApiKeyPath()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxApiKeyAuth(keys => keys.Add("abc.def.ghi", id: "dotted"));
        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(Issuer, Audience, Key));
        var interceptor = new TraxCompositeSocketInterceptor(
            new TraxApplicationServices(services.BuildServiceProvider())
        );

        var (status, _) = await ConnectAsync(interceptor, new { authToken = "abc.def.ghi" });

        status
            .Accepted.Should()
            .BeTrue("three segments are not a JWT unless the header parses, per " + Adr);
    }

    #endregion

    #region Dispatcher with API key

    [Test]
    public async Task DispatcherAndApiKey_EachMappedIssuerAndTheApiKeyAreAccepted()
    {
        var interceptor = Build(Schemes.ApiKey | Schemes.Dispatcher);

        (await ConnectAsync(interceptor, new { authToken = SignJwt("alice") }))
            .Status.Accepted.Should()
            .BeTrue();
        (await ConnectAsync(interceptor, new { authToken = SignJwt("bob", OtherIssuer, OtherKey) }))
            .Status.Accepted.Should()
            .BeTrue();
        (await ConnectAsync(interceptor, new { authToken = ApiKey }))
            .Status.Accepted.Should()
            .BeTrue();
    }

    #endregion

    #region One scheme, and none

    [Test]
    public async Task ApiKeyOnly_JwtShapedToken_GoesToTheApiKeyResolver()
    {
        // With one scheme there is nothing to route: the composite behaves as that scheme's own
        // interceptor did, so a JWT is simply an unknown API key.
        var (status, _) = await ConnectAsync(
            Build(Schemes.ApiKey),
            new { authToken = SignJwt("alice") }
        );

        status.Accepted.Should().BeFalse();
    }

    [Test]
    public async Task ApiKeyOnly_ValidKey_Accepted()
    {
        var (status, _) = await ConnectAsync(Build(Schemes.ApiKey), new { authToken = ApiKey });

        status.Accepted.Should().BeTrue();
    }

    [Test]
    public async Task JwtOnly_ValidToken_Accepted()
    {
        var (status, _) = await ConnectAsync(
            Build(Schemes.Jwt),
            new { authToken = SignJwt("alice") }
        );

        status.Accepted.Should().BeTrue();
    }

    [Test]
    public async Task JwtOnly_ApiKeyField_Rejected()
    {
        var (status, _) = await ConnectAsync(Build(Schemes.Jwt), new { apiKey = ApiKey });

        status.Accepted.Should().BeFalse();
    }

    [Test]
    public async Task NoScheme_EmptyPayload_Accepted()
    {
        var (status, _) = await ConnectAsync(Build(Schemes.None), new { });

        status
            .Accepted.Should()
            .BeTrue("a host with no token scheme accepts every connection, per " + Adr);
    }

    #endregion

    [TestCase("abc", false)]
    [TestCase("abc.def.ghi", false)]
    [TestCase("Zm9v.YmFy.YmF6", false)]
    [TestCase("", false)]
    public void LooksLikeJwt_RejectsNonJwtShapes(string token, bool expected) =>
        TraxCompositeSocketInterceptor.LooksLikeJwt(token).Should().Be(expected);

    [Test]
    public void LooksLikeJwt_AcceptsASignedToken() =>
        TraxCompositeSocketInterceptor.LooksLikeJwt(SignJwt("alice")).Should().BeTrue();

    private sealed record Status(bool Accepted);
}
