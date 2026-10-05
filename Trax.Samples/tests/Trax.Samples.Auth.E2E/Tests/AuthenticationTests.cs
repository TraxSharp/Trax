using System.Net;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.E2E.Fixtures;
using Trax.Samples.Auth.E2E.Utilities;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// Both schemes authenticate the same person, and the principal id says which one did:
/// <c>TraxApiKey:alice</c> for her key, <c>TraxJwt:alice</c> for her token. A credential either
/// scheme rejects leaves the caller anonymous, and a gated train refuses them.
/// </summary>
[TestFixture]
public class AuthenticationTests : AuthTestFixture
{
    private const string WhoAmI = "{ discover { whoAmI { id displayName roles principalType } } }";

    [Test]
    public async Task WhoAmI_Anonymous_IsRefused()
    {
        var result = await GraphQL.SendAsync(WhoAmI, Anonymous);

        result.IsRefused.Should().BeTrue("a bare [TraxAuthorize] requires a signed-in caller");
    }

    [Test]
    public async Task WhoAmI_ApiKey_CarriesTheApiKeySchemeInTheId()
    {
        var result = await GraphQL.SendAsync(WhoAmI, AliceKey);

        result.HasErrors.Should().BeFalse(result.Raw);
        var me = result.GetData("discover", "whoAmI");
        me.GetProperty("id").GetString().Should().Be("TraxApiKey:alice");
        me.GetProperty("displayName").GetString().Should().Be("Alice");
        me.GetProperty("principalType").GetString().Should().Be("apikey");
        me.GetProperty("roles")
            .EnumerateArray()
            .Select(r => r.GetString())
            .Should()
            .Equal("Editor");
    }

    [Test]
    public async Task WhoAmI_Jwt_CarriesTheJwtSchemeInTheId()
    {
        var result = await GraphQL.SendAsync(WhoAmI, AliceToken);

        result.HasErrors.Should().BeFalse(result.Raw);
        var me = result.GetData("discover", "whoAmI");
        me.GetProperty("id").GetString().Should().Be("TraxJwt:alice");
        me.GetProperty("displayName").GetString().Should().Be("Alice");
        me.GetProperty("principalType").GetString().Should().Be("jwt");
        me.GetProperty("roles")
            .EnumerateArray()
            .Select(r => r.GetString())
            .Should()
            .Equal("Editor");
    }

    [Test]
    public async Task BothCredentials_TheFirstRegisteredSchemeWins()
    {
        // No scheme is the default, so Trax tries each registered scheme in registration order
        // and keeps the first that authenticates. Program.cs registers the API key first.
        var both = new Caller(
            "alice's key and oscar's token",
            ApiKey: DemoCredentials.AliceKey,
            BearerToken: OscarToken.BearerToken
        );

        var result = await GraphQL.SendAsync(WhoAmI, both);

        result.HasErrors.Should().BeFalse(result.Raw);
        result.GetData("discover", "whoAmI", "id").GetString().Should().Be("TraxApiKey:alice");
    }

    [Test]
    public async Task UnknownApiKey_IsRefused()
    {
        var result = await GraphQL.SendAsync(
            WhoAmI,
            Caller.WithApiKey("guess", "mallory-key-do-not-use-in-production")
        );

        result.IsRefused.Should().BeTrue();
    }

    [Test]
    public async Task TokenSignedWithAnotherKey_IsRefused()
    {
        var forger = TestTokenIssuer.Symmetric(
            DemoCredentials.JwtIssuer,
            DemoCredentials.JwtAudience,
            "a-different-key-of-at-least-32-bytes!!"u8.ToArray()
        );
        var token = forger.Mint(b => b.WithSubject("alice").WithRole(AuthRoles.Editor));

        var result = await GraphQL.SendAsync(WhoAmI, Caller.WithToken("forged", token));

        result.IsRefused.Should().BeTrue("the signature does not verify against the scheme's key");
    }

    [Test]
    public async Task ExpiredToken_IsRefused()
    {
        var issuer = TestTokenIssuer.Symmetric(
            DemoCredentials.JwtIssuer,
            DemoCredentials.JwtAudience,
            DemoCredentials.JwtSigningKey
        );
        var token = issuer.Mint(b =>
            b.WithSubject("alice")
                .WithNotBefore(DateTime.UtcNow.AddHours(-2))
                .WithExpires(DateTime.UtcNow.AddHours(-1))
        );

        var result = await GraphQL.SendAsync(WhoAmI, Caller.WithToken("expired", token));

        result.IsRefused.Should().BeTrue();
    }

    [Test]
    public async Task TokenForAnotherAudience_IsRefused()
    {
        var issuer = TestTokenIssuer.Symmetric(
            DemoCredentials.JwtIssuer,
            "some-other-api",
            DemoCredentials.JwtSigningKey
        );
        var token = issuer.Mint(b => b.WithSubject("alice"));

        var result = await GraphQL.SendAsync(WhoAmI, Caller.WithToken("wrong audience", token));

        result.IsRefused.Should().BeTrue();
    }

    [Test]
    public async Task Echo_IsServedToAnonymousAndSignedInCallers()
    {
        foreach (var caller in new[] { Anonymous, BobKey, AliceToken })
        {
            var result = await GraphQL.SendAsync(
                """{ discover { echo(input: { message: "hi" }) { echoed } } }""",
                caller
            );

            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");
            result.GetData("discover", "echo", "echoed").GetString().Should().Be("hi");
        }
    }

    [Test]
    public async Task DevTokenEndpoint_MintsOnlyForDemoUsers()
    {
        var response = await Http.GetAsync("/dev/token/mallory");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
