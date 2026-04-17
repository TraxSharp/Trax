using System.Security.Claims;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class DefaultJwtPrincipalResolverTests
{
    private static JwtTokenInput Input(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        return new JwtTokenInput(principal, new FakeSecurityToken());
    }

    [Test]
    public async Task NoSubject_ReturnsNull()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(Input(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task SubClaim_MapsToId()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "user-42")),
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Id.Should().Be("user-42");
    }

    [Test]
    public async Task NameIdentifier_MapsToId_WhenSubAbsent()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim(ClaimTypes.NameIdentifier, "user-77")),
            CancellationToken.None
        );

        result!.Id.Should().Be("user-77");
    }

    [Test]
    public async Task NameClaim_PrefersNameOverPreferredUsernameOverSub()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("preferred_username", "alice.liddell"),
                new Claim("name", "Alice Liddell")
            ),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("Alice Liddell");
    }

    [Test]
    public async Task DisplayName_FallsBackToPreferredUsername()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim("preferred_username", "alice.liddell")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("alice.liddell");
    }

    [Test]
    public async Task DisplayName_FallsBackToSub()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "user-99")),
            CancellationToken.None
        );

        result!.DisplayName.Should().Be("user-99");
    }

    [Test]
    public async Task Roles_MergeFromAllKnownRoleClaims()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("role", "Editor"),
                new Claim("roles", "Viewer")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("Admin", "Editor", "Viewer");
    }

    [Test]
    public async Task DuplicateRoles_Deduped()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("role", "Admin")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Test]
    public async Task CustomClaims_PassThrough()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u"), new Claim("tenant", "acme")),
            CancellationToken.None
        );

        result!.Claims.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
    }

    [Test]
    public async Task PrincipalType_IsJwt()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(new Claim("sub", "u")),
            CancellationToken.None
        );

        result!.PrincipalType.Should().Be(JwtDefaults.PrincipalType);
    }

    [Test]
    public async Task ReservedClaims_DroppedFromCustomClaims()
    {
        // Role and name claims are consumed into first-class fields; they must
        // not also appear verbatim in the custom claim bag or they would
        // duplicate into ClaimsPrincipal on projection.
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim("name", "Alice"),
                new Claim(ClaimTypes.Role, "Admin")
            ),
            CancellationToken.None
        );

        result!.Claims.Should().BeNull();
    }

    [Test]
    public async Task WhitespaceRoles_Filtered()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var result = await resolver.ResolveAsync(
            Input(
                new Claim("sub", "u"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, " ")
            ),
            CancellationToken.None
        );

        result!.Roles.Should().BeEquivalentTo("Admin");
    }

    [Test]
    public async Task Input_Null_Throws()
    {
        var resolver = new DefaultJwtPrincipalResolver();

        var act = async () => await resolver.ResolveAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
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
