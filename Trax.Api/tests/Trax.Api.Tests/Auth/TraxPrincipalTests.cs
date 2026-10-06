using System.Security.Claims;
using AwesomeAssertions;
using Trax.Api.Auth;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxPrincipalTests
{
    private const string Scheme = "TestScheme";

    #region ToClaimsPrincipal

    [Test]
    public void ToClaimsPrincipal_SetsPrincipalIdClaim()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal
            .FindFirst(TraxAuthClaimTypes.PrincipalId)
            ?.Value.Should()
            .Be("TestScheme:alice");
    }

    [Test]
    public void ToClaimsPrincipal_SetsNameClaim()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.Identity!.Name.Should().Be("Alice");
    }

    [Test]
    public void ToClaimsPrincipal_WithRoles_EmitsOneRoleClaimEach()
    {
        var principal = new TraxPrincipal("admin", "admin", ["Admin", "Player", "Auditor"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Should()
            .BeEquivalentTo(["Admin", "Player", "Auditor"]);
    }

    [Test]
    public void ToClaimsPrincipal_WithEmptyRoles_EmitsNoRoleClaims()
    {
        var principal = new TraxPrincipal("alice", "Alice", []);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Test]
    public void ToClaimsPrincipal_WithNullClaims_DoesNotThrow()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"], Claims: null);

        var act = () => principal.ToClaimsPrincipal(Scheme);

        act.Should().NotThrow();
    }

    [Test]
    public void ToClaimsPrincipal_WithCustomClaims_PreservesKeyValuePairs()
    {
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { ["tenant"] = "acme", ["tier"] = "enterprise" }
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.FindFirst("tenant")?.Value.Should().Be("acme");
        claimsPrincipal.FindFirst("tier")?.Value.Should().Be("enterprise");
    }

    [Test]
    public void ToClaimsPrincipal_WithPrincipalType_SetsPrincipalTypeClaim()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"], PrincipalType: "apikey");

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value.Should().Be("apikey");
    }

    [Test]
    public void ToClaimsPrincipal_WithoutPrincipalType_DoesNotEmitClaim()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.FindFirst(TraxAuthClaimTypes.PrincipalType).Should().BeNull();
    }

    [Test]
    public void ToClaimsPrincipal_SetsAuthenticationType()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.Identity!.AuthenticationType.Should().Be(Scheme);
    }

    [Test]
    public void ToClaimsPrincipal_IsAuthenticated()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Test]
    public void ToClaimsPrincipal_IsInRole_MatchesSuppliedRole()
    {
        var principal = new TraxPrincipal("admin", "admin", ["Admin", "Player"]);

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.IsInRole("Admin").Should().BeTrue();
        claimsPrincipal.IsInRole("Player").Should().BeTrue();
        claimsPrincipal.IsInRole("Auditor").Should().BeFalse();
    }

    #endregion

    #region FromClaimsPrincipal / TryGetTraxPrincipal

    [Test]
    public void TryGetTraxPrincipal_Roundtrip_PreservesAllFields()
    {
        var original = new TraxPrincipal(
            "alice",
            "Alice Liddell",
            ["User", "Admin"],
            Claims: new Dictionary<string, string> { ["tenant"] = "acme" },
            PrincipalType: "apikey"
        );

        var roundtripped = original.ToClaimsPrincipal(Scheme).TryGetTraxPrincipal(out var result);

        roundtripped.Should().BeTrue();
        result!.Id.Should().Be("TestScheme:alice");
        result.DisplayName.Should().Be("Alice Liddell");
        result.Roles.Should().BeEquivalentTo(["User", "Admin"]);
        result.Claims.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
        result.PrincipalType.Should().Be("apikey");
    }

    [Test]
    public void TryGetTraxPrincipal_WithoutPrincipalIdClaim_ReturnsFalse()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], Scheme);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        var success = claimsPrincipal.TryGetTraxPrincipal(out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Test]
    public void TryGetTraxPrincipal_WithMissingName_FallsBackToPrincipalId()
    {
        var identity = new ClaimsIdentity(
            [new Claim(TraxAuthClaimTypes.PrincipalId, "alice")],
            Scheme
        );
        var claimsPrincipal = new ClaimsPrincipal(identity);

        claimsPrincipal.TryGetTraxPrincipal(out var result).Should().BeTrue();
        result!.DisplayName.Should().Be("alice");
    }

    [Test]
    public void TryGetPrincipalId_WithMissingClaim_ReturnsFalse()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], Scheme);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        var success = claimsPrincipal.TryGetPrincipalId(out var id);

        success.Should().BeFalse();
        id.Should().BeNull();
    }

    [Test]
    public void TryGetPrincipalId_WithPresentClaim_ReturnsTrueAndValue()
    {
        var identity = new ClaimsIdentity(
            [new Claim(TraxAuthClaimTypes.PrincipalId, "bob")],
            Scheme
        );
        var claimsPrincipal = new ClaimsPrincipal(identity);

        claimsPrincipal.TryGetPrincipalId(out var id).Should().BeTrue();
        id.Should().Be("bob");
    }

    #endregion

    #region Validation

    [Test]
    public void Ctor_NullId_Throws()
    {
        var act = () => new TraxPrincipal(null!, "Alice", []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("Id");
    }

    [Test]
    public void Ctor_EmptyId_Throws()
    {
        var act = () => new TraxPrincipal("", "Alice", []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("Id");
    }

    [Test]
    public void Ctor_WhitespaceId_Throws()
    {
        var act = () => new TraxPrincipal("   ", "Alice", []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("Id");
    }

    [Test]
    public void Ctor_NullDisplayName_Throws()
    {
        var act = () => new TraxPrincipal("alice", null!, []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("DisplayName");
    }

    [Test]
    public void Ctor_EmptyDisplayName_Throws()
    {
        var act = () => new TraxPrincipal("alice", "", []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("DisplayName");
    }

    [Test]
    public void Ctor_WhitespaceDisplayName_Throws()
    {
        var act = () => new TraxPrincipal("alice", "\t", []);

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("DisplayName");
    }

    [Test]
    public void Ctor_EmptyRoles_Allowed()
    {
        var act = () => new TraxPrincipal("alice", "Alice", []);

        act.Should().NotThrow();
    }

    [Test]
    public void WithExpression_ResetsIdToEmpty_Throws()
    {
        var principal = new TraxPrincipal("alice", "Alice", ["User"]);

        var act = () => principal with { Id = "" };

        act.Should().Throw<ArgumentException>().And.ParamName.Should().Be("Id");
    }

    #endregion

    #region ReservedClaimFiltering

    [Test]
    public void ToClaimsPrincipal_ClaimsBagRole_Ignored()
    {
        // Roles must come through the Roles list. A resolver-emitted Role claim
        // in the custom bag is dropped so it can't grant roles out-of-band.
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { [ClaimTypes.Role] = "Admin" }
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        var roles = claimsPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().BeEquivalentTo(["User"]);
        claimsPrincipal.IsInRole("Admin").Should().BeFalse();
    }

    [Test]
    public void ToClaimsPrincipal_ClaimsBagPrincipalId_Ignored()
    {
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string>
            {
                [TraxAuthClaimTypes.PrincipalId] = "forged-id",
            }
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        var idClaims = claimsPrincipal
            .FindAll(TraxAuthClaimTypes.PrincipalId)
            .Select(c => c.Value)
            .ToList();
        idClaims.Should().BeEquivalentTo(["TestScheme:alice"]);
    }

    [Test]
    public void ToClaimsPrincipal_ClaimsBagPrincipalType_Ignored()
    {
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string>
            {
                [TraxAuthClaimTypes.PrincipalType] = "forged-scheme",
            },
            PrincipalType: "apikey"
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        var types = claimsPrincipal
            .FindAll(TraxAuthClaimTypes.PrincipalType)
            .Select(c => c.Value)
            .ToList();
        types.Should().BeEquivalentTo(["apikey"]);
    }

    [Test]
    public void ToClaimsPrincipal_ClaimsBagName_Ignored()
    {
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { [ClaimTypes.Name] = "Mallory" }
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        var names = claimsPrincipal.FindAll(ClaimTypes.Name).Select(c => c.Value).ToList();
        names.Should().BeEquivalentTo(["Alice"]);
        claimsPrincipal.Identity!.Name.Should().Be("Alice");
    }

    [Test]
    public void ToClaimsPrincipal_ArbitraryCustomClaim_PassesThrough()
    {
        var principal = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { ["tenant"] = "acme", ["region"] = "us-west-2" }
        );

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme);

        claimsPrincipal.FindFirst("tenant")?.Value.Should().Be("acme");
        claimsPrincipal.FindFirst("region")?.Value.Should().Be("us-west-2");
    }

    [Test]
    public void RoundTrip_ReservedClaimsDoNotAppearInRecoveredBag()
    {
        var original = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string>
            {
                // Reserved, should be stripped on the way out.
                [ClaimTypes.Role] = "Admin",
                [ClaimTypes.Name] = "Mallory",
                [TraxAuthClaimTypes.PrincipalId] = "forged-id",
                [TraxAuthClaimTypes.PrincipalType] = "forged-scheme",
                // Non-reserved, should survive.
                ["tenant"] = "acme",
            },
            PrincipalType: "apikey"
        );

        var roundtripped = original.ToClaimsPrincipal(Scheme).TryGetTraxPrincipal(out var result);

        roundtripped.Should().BeTrue();
        result!.Id.Should().Be("TestScheme:alice");
        result.DisplayName.Should().Be("Alice");
        result.Roles.Should().BeEquivalentTo(["User"]);
        result.PrincipalType.Should().Be("apikey");
        result.Claims.Should().NotBeNull();
        result.Claims!.Keys.Should().NotContain(ClaimTypes.Role);
        result.Claims.Keys.Should().NotContain(ClaimTypes.Name);
        result.Claims.Keys.Should().NotContain(TraxAuthClaimTypes.PrincipalId);
        result.Claims.Keys.Should().NotContain(TraxAuthClaimTypes.PrincipalType);
        result.Claims.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
    }

    #endregion

    #region Repeated claim types

    [Test]
    public void TryGetTraxPrincipal_ClaimTypeRepeated_ByAClaimsTransformation_DoesNotThrow()
    {
        // ASP.NET Core runs IClaimsTransformation after every successful authentication, and a
        // transformation that adds one claim per permission is the usual shape.
        var claimsPrincipal = new TraxPrincipal("alice", "Alice", ["User"]).ToClaimsPrincipal(
            Scheme
        );
        var identity = (ClaimsIdentity)claimsPrincipal.Identity!;
        identity.AddClaim(new Claim("permission", "orders:read"));
        identity.AddClaim(new Claim("permission", "orders:write"));

        var read = () => claimsPrincipal.TryGetTraxPrincipal(out _);

        read.Should().NotThrow();
        claimsPrincipal.TryGetTraxPrincipal(out var principal).Should().BeTrue();
        principal!.Id.Should().Be($"{Scheme}:alice");
        principal
            .Claims!["permission"]
            .Should()
            .Be("orders:read", "the first value of a type wins, as FindFirst does");
    }

    [Test]
    public void TryGetTraxPrincipal_TwoSchemesAuthenticatedTheRequest_DoesNotThrow()
    {
        // A policy naming several schemes authenticates each and merges the identities into one
        // principal (AuthorizationPolicy's PolicyEvaluator does this for the combined Trax policy).
        // Two JWT-shaped identities both carry the token's registered claims.
        var first = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { ["iss"] = "https://a.example" }
        ).ToClaimsPrincipal("SchemeA");
        var second = new TraxPrincipal(
            "alice",
            "Alice",
            ["User"],
            Claims: new Dictionary<string, string> { ["iss"] = "https://b.example" }
        ).ToClaimsPrincipal("SchemeB");
        var merged = new ClaimsPrincipal(first.Identities.Concat(second.Identities));

        var read = () => merged.TryGetTraxPrincipal(out _);

        read.Should().NotThrow();
        merged.TryGetTraxPrincipal(out var principal).Should().BeTrue();
        principal!
            .Id.Should()
            .Be("SchemeA:alice", "the id and the claims come from the first identity");
        principal.Claims!["iss"].Should().Be("https://a.example");
    }

    #endregion
}
