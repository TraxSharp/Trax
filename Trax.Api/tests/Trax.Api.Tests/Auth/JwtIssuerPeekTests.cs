using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtIssuerPeekTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(new string('k', 32));

    private static string MakeToken(string? issuer = "https://issuer.example.com")
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Key),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "aud",
            claims: new[] { new Claim("sub", "alice") },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── TryReadIssuer ────────────────────────────────────────────────────

    [Test]
    public void TryReadIssuer_ValidToken_ReturnsIssuer()
    {
        var token = MakeToken("https://issuer.example.com");

        JwtIssuerPeek.TryReadIssuer(token).Should().Be("https://issuer.example.com");
    }

    [Test]
    public void TryReadIssuer_Null_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer(null).Should().BeNull();

    [Test]
    public void TryReadIssuer_Empty_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer(string.Empty).Should().BeNull();

    [Test]
    public void TryReadIssuer_Whitespace_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer("   ").Should().BeNull();

    [Test]
    public void TryReadIssuer_Malformed_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer("not-a-token").Should().BeNull();

    [Test]
    public void TryReadIssuer_PartialToken_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer("only.two").Should().BeNull();

    [Test]
    public void TryReadIssuer_InvalidBase64_ReturnsNull() =>
        JwtIssuerPeek.TryReadIssuer("!!!.!!!.!!!").Should().BeNull();

    [Test]
    public void TryReadIssuer_TokenWithoutIssClaim_ReturnsNull()
    {
        // Build a token directly with no iss.
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Key),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: null,
            audience: "aud",
            claims: new[] { new Claim("sub", "alice") },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds
        );
        var raw = new JwtSecurityTokenHandler().WriteToken(token);

        JwtIssuerPeek.TryReadIssuer(raw).Should().BeNull();
    }

    [Test]
    public void TryReadIssuer_DoesNotValidateSignature()
    {
        // Mint with one key, peek doesn't care about signature.
        var token = MakeToken("https://anyone-can-claim-this");
        JwtIssuerPeek.TryReadIssuer(token).Should().Be("https://anyone-can-claim-this");
    }

    // ── TryReadBearerToken ───────────────────────────────────────────────

    [Test]
    public void TryReadBearerToken_StandardCase_ReturnsToken() =>
        JwtIssuerPeek.TryReadBearerToken("Bearer abc123").Should().Be("abc123");

    [Test]
    public void TryReadBearerToken_LowercasePrefix_StillStrips() =>
        JwtIssuerPeek.TryReadBearerToken("bearer abc123").Should().Be("abc123");

    [Test]
    public void TryReadBearerToken_MixedCasePrefix_StillStrips() =>
        JwtIssuerPeek.TryReadBearerToken("BeArEr abc123").Should().Be("abc123");

    [Test]
    public void TryReadBearerToken_ExtraWhitespace_Trimmed() =>
        JwtIssuerPeek.TryReadBearerToken("Bearer    abc123   ").Should().Be("abc123");

    [Test]
    public void TryReadBearerToken_Null_ReturnsNull() =>
        JwtIssuerPeek.TryReadBearerToken(null).Should().BeNull();

    [Test]
    public void TryReadBearerToken_Empty_ReturnsNull() =>
        JwtIssuerPeek.TryReadBearerToken(string.Empty).Should().BeNull();

    [Test]
    public void TryReadBearerToken_NotBearer_ReturnsNull() =>
        JwtIssuerPeek.TryReadBearerToken("Basic abc123").Should().BeNull();

    [Test]
    public void TryReadBearerToken_BearerButEmpty_ReturnsNull() =>
        JwtIssuerPeek.TryReadBearerToken("Bearer ").Should().BeNull();

    [Test]
    public void TryReadBearerToken_BearerSpacesOnly_ReturnsNull() =>
        JwtIssuerPeek.TryReadBearerToken("Bearer    ").Should().BeNull();
}
