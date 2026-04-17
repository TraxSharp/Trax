using System.Security.Claims;
using FluentAssertions;
using Trax.Api.Auth;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class DefaultOidcPrincipalResolverTests
{
    private static OidcTokenInput Input(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "OIDC");
        var principal = new ClaimsPrincipal(identity);
        return new OidcTokenInput(principal, IdToken: "id-token", AccessToken: "access-token");
    }

    [Test]
    public async Task NoSubject_ReturnsNull()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(Input(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task Sub_MapsToId()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "user-42")),
            CancellationToken.None
        );

        result!.Id.Should().Be("user-42");
    }

    [Test]
    public async Task DisplayName_PrefersName()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("name", "Alice"),
                new Claim("preferred_username", "alice.l"),
                new Claim("email", "alice@example.com")
            ),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task DisplayName_FallsBackToEmail()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim("email", "alice@example.com")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("alice@example.com");
    }

    [Test]
    public async Task Groups_MapToRoles()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("groups", "admins"),
                new Claim("groups", "engineers")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("admins", "engineers");
    }

    [Test]
    public async Task PrincipalType_IsOidc()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u")),
            CancellationToken.None
        );

        result!.PrincipalType.Should().Be(OidcDefaults.PrincipalType);
    }

    [Test]
    public async Task CustomClaims_PassThrough()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim("tenant_id", "acme")),
            CancellationToken.None
        );

        result!.Claims.Should().ContainKey("tenant_id").WhoseValue.Should().Be("acme");
    }

    [Test]
    public async Task GroupsClaim_NotDuplicatedIntoCustomClaimBag()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim("groups", "admins")),
            CancellationToken.None
        );

        (result!.Claims?.ContainsKey("groups") ?? false).Should().BeFalse();
    }

    [Test]
    public async Task Input_Null_Throws()
    {
        var resolver = new DefaultOidcPrincipalResolver();

        var act = async () => await resolver.ResolveAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
