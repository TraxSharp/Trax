using System.Security.Claims;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Cognito;

namespace Trax.Api.Tests.Auth.Cognito;

[TestFixture]
public class CognitoJwtPrincipalResolverTests
{
    private static JwtTokenInput Input(params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")), new FakeSecurityToken());

    [Test]
    public async Task NoSub_ReturnsNull()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(Input(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task Sub_MapsToId()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "e4b8f1a2-9d4b-4f3c-a1e2-7d8b9c3d1a2e")),
            CancellationToken.None
        );

        result!.Id.Should().Be("e4b8f1a2-9d4b-4f3c-a1e2-7d8b9c3d1a2e");
    }

    [Test]
    public async Task NameIdentifier_FallsBack_WhenNoSub()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim(ClaimTypes.NameIdentifier, "alt-sub")),
            CancellationToken.None
        );

        result!.Id.Should().Be("alt-sub");
    }

    [Test]
    public async Task PrincipalType_IsCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u")),
            CancellationToken.None
        );

        result!.PrincipalType.Should().Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task CognitoGroups_FlowIntoRoles()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.CognitoGroups, "admin"),
                new Claim(CognitoDefaults.CognitoGroups, "billing")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("admin", "billing");
    }

    [Test]
    public async Task RolesAndCognitoGroups_MergedAndDeduped()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim(CognitoDefaults.CognitoGroups, "admin"),
                new Claim("role", "editor")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("admin", "editor");
    }

    [Test]
    public async Task DisplayName_PrefersName_OverCognitoUsername_OverEmail()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("name", "Alice"),
                new Claim(CognitoDefaults.CognitoUsername, "alice123"),
                new Claim(CognitoDefaults.Email, "alice@example.com")
            ),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task DisplayName_FallsBackToCognitoUsername()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.CognitoUsername, "alice123")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("alice123");
    }

    [Test]
    public async Task DisplayName_FallsBackToEmail()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Email, "alice@example.com")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("alice@example.com");
    }

    [Test]
    public async Task DisplayName_FallsBackToSub_WhenNothingElse()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u-final")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("u-final");
    }

    [Test]
    public async Task NativeUser_NoIdentitiesClaim_IdentityProviderIsCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Email, "alice@example.com")),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task FederatedGoogle_IdentitiesParsed_IdentityProviderSet()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """
            [
              {
                "providerName": "Google",
                "providerType": "Google",
                "userId": "1080987654321",
                "primary": "true",
                "dateCreated": "1731540000000"
              }
            ]
            """;

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Email, "alice@gmail.com"),
                new Claim(CognitoDefaults.Identities, json)
            ),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("Google");
    }

    [Test]
    public async Task FederatedApple_PrivateRelay_IdentityProviderSet()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """
            [
              {
                "providerName": "SignInWithApple",
                "userId": "001234.abc.7890",
                "primary": "true"
              }
            ]
            """;

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Email, "x7k9q2@privaterelay.appleid.com"),
                new Claim(CognitoDefaults.Identities, json)
            ),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("SignInWithApple");
        result.Claims["email"].Should().Be("x7k9q2@privaterelay.appleid.com");
    }

    [Test]
    public async Task AppleSecondLogin_NoEmail_StillResolvesViaSub()
    {
        var resolver = new CognitoJwtPrincipalResolver();
        var json = """[{"providerName":"SignInWithApple","userId":"001"}]""";

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u-stable"), new Claim(CognitoDefaults.Identities, json)),
            CancellationToken.None
        );

        result!.Id.Should().Be("u-stable");
        result.DisplayName.Should().Be("u-stable");
        result.Claims![CognitoDefaults.IdentityProvider].Should().Be("SignInWithApple");
    }

    [Test]
    public async Task IdentitiesArray_PrimaryWins_WhenMultipleEntries()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """
            [
              {"providerName":"Google","primary":"false","userId":"g-1"},
              {"providerName":"SignInWithApple","primary":"true","userId":"a-1"}
            ]
            """;

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, json)),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("SignInWithApple");
    }

    [Test]
    public async Task IdentitiesArray_NoPrimary_FirstEntryWins()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """
            [
              {"providerName":"Google","userId":"g-1"},
              {"providerName":"SignInWithApple","userId":"a-1"}
            ]
            """;

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, json)),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("Google");
    }

    [Test]
    public async Task IdentitiesArray_BooleanPrimary_Honored()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """
            [
              {"providerName":"Google","primary":false,"userId":"g-1"},
              {"providerName":"SignInWithApple","primary":true,"userId":"a-1"}
            ]
            """;

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, json)),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("SignInWithApple");
    }

    [Test]
    public async Task IdentitiesArray_NonObjectEntries_SkippedSafely()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var json = """["not-an-object", {"providerName":"Google","userId":"g"}]""";

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, json)),
            CancellationToken.None
        );

        result!.Claims![CognitoDefaults.IdentityProvider].Should().Be("Google");
    }

    [Test]
    public async Task IdentitiesClaim_MalformedJson_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, "not json")),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task IdentitiesClaim_EmptyArray_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.Identities, "[]")),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task IdentitiesClaim_NotArray_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Identities, "{\"key\":\"value\"}")
            ),
            CancellationToken.None
        );

        // Wrapping logic wraps in [...], producing an array of one object
        // that has no providerName, so it defaults.
        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task IdentitiesClaim_MissingProviderName_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Identities, """[{"userId":"x"}]""")
            ),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task IdentitiesClaim_EmptyProviderName_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Identities, """[{"providerName":""}]""")
            ),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task IdentitiesClaim_ProviderNameNotString_DefaultsToCognito()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.Identities, """[{"providerName":123}]""")
            ),
            CancellationToken.None
        );

        result!
            .Claims![CognitoDefaults.IdentityProvider]
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    [Test]
    public async Task CustomClaims_PassThrough_ButReservedTypesFiltered()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("name", "Alice"),
                new Claim(CognitoDefaults.CognitoUsername, "alice123"),
                new Claim(CognitoDefaults.CognitoGroups, "admin"),
                new Claim("tenant", "acme")
            ),
            CancellationToken.None
        );

        result!.Claims.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
        // identity_provider is always synthesized.
        result.Claims.Should().ContainKey(CognitoDefaults.IdentityProvider);
        result.Claims.Should().NotContainKey("name");
        result.Claims.Should().NotContainKey(CognitoDefaults.CognitoUsername);
        result.Claims.Should().NotContainKey(CognitoDefaults.CognitoGroups);
        result.Claims.Should().NotContainKey("sub");
    }

    [Test]
    public async Task CustomClaims_DuplicateTypes_FirstWriteWins_NoThrow()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("custom", "first"),
                new Claim("custom", "second")
            ),
            CancellationToken.None
        );

        result!.Claims!["custom"].Should().Be("first");
    }

    [Test]
    public async Task WhitespaceRoles_Filtered()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.CognitoGroups, "admin"),
                new Claim(CognitoDefaults.CognitoGroups, " ")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("admin");
    }

    [Test]
    public async Task EmailVerifiedClaim_PassesThrough()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim(CognitoDefaults.EmailVerified, "true")),
            CancellationToken.None
        );

        result!
            .Claims!.Should()
            .Contain(new KeyValuePair<string, string>("email_verified", "true"));
    }

    [Test]
    public async Task TokenUseClaim_PassesThrough()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(CognitoDefaults.TokenUse, CognitoDefaults.TokenUseAccess)
            ),
            CancellationToken.None
        );

        result!
            .Claims!.Should()
            .Contain(
                new KeyValuePair<string, string>(
                    CognitoDefaults.TokenUse,
                    CognitoDefaults.TokenUseAccess
                )
            );
    }

    [Test]
    public async Task InputNull_Throws()
    {
        var resolver = new CognitoJwtPrincipalResolver();

        var act = async () => await resolver.ResolveAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public void ExtractIdentityProvider_NoClaim_ReturnsCognito()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        CognitoJwtPrincipalResolver
            .ExtractIdentityProvider(principal)
            .Should()
            .Be(CognitoDefaults.PrincipalType);
    }

    private sealed class FakeSecurityToken : SecurityToken
    {
        public override string Id { get; } = Guid.NewGuid().ToString();
        public override string Issuer => "fake";
        public override SecurityKey SecurityKey => null!;
        public override SecurityKey SigningKey { get; set; } = null!;
        public override DateTime ValidFrom => DateTime.UtcNow;
        public override DateTime ValidTo => DateTime.UtcNow.AddHours(1);
    }
}
