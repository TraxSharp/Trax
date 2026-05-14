using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt.Testing;

namespace Trax.Api.Tests.Auth.Testing;

[TestFixture]
public class TestTokenIssuerTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(new string('k', 32));

    [Test]
    public void Constructor_NullIssuer_Throws()
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Key),
            SecurityAlgorithms.HmacSha256
        );
        Action act = () => new TestTokenIssuer(null!, "aud", creds);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Constructor_NullAudience_Throws()
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Key),
            SecurityAlgorithms.HmacSha256
        );
        Action act = () => new TestTokenIssuer("iss", null!, creds);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Constructor_NullCredentials_Throws()
    {
        Action act = () => new TestTokenIssuer("iss", "aud", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Symmetric_ShortKey_Throws()
    {
        Action act = () => TestTokenIssuer.Symmetric("iss", "aud", new byte[16]);

        act.Should().Throw<ArgumentException>().WithMessage("*32 bytes*");
    }

    [Test]
    public void Symmetric_NullKey_Throws()
    {
        Action act = () => TestTokenIssuer.Symmetric("iss", "aud", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Symmetric_BuildsValidIssuer()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        issuer.Issuer.Should().Be("iss");
        issuer.DefaultAudience.Should().Be("aud");
    }

    [Test]
    public void Mint_NoConfigure_ProducesValidToken()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var token = issuer.Mint();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be("iss");
        jwt.Audiences.Should().Contain("aud");
    }

    [Test]
    public void Mint_DefaultExpiry_FiveMinutesFromNow()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(10));
    }

    [Test]
    public void Mint_DefaultNotBefore_OneMinuteAgo()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.ValidFrom.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromSeconds(10));
    }

    // ── Builder ──────────────────────────────────────────────────────────

    [Test]
    public void Builder_WithSubject_SetsSubClaim()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithSubject("alice"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == "alice");
    }

    [Test]
    public void Builder_WithSubject_Empty_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithSubject(""));

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Builder_WithSubject_Replaces_NotAppends()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithSubject("a").WithSubject("b"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Claims.Count(c => c.Type == "sub").Should().Be(1);
        jwt.Claims.Single(c => c.Type == "sub").Value.Should().Be("b");
    }

    [Test]
    public void Builder_WithClaim_AppendsMultiple()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b =>
            b.WithSubject("u").WithClaim("scope", "read").WithClaim("scope", "write")
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Claims.Where(c => c.Type == "scope")
            .Select(c => c.Value)
            .Should()
            .BeEquivalentTo("read", "write");
    }

    [Test]
    public void Builder_WithClaim_EmptyType_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithClaim("", "x"));

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Builder_WithClaim_NullValue_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithClaim("t", null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Builder_WithRole_AddsRoleClaim()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithSubject("u").WithRole("admin"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "admin");
    }

    [Test]
    public void Builder_WithRole_Empty_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithRole(""));

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Builder_WithAudience_Overrides()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithAudience("other-aud"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Audiences.Should().Contain("other-aud").And.NotContain("aud");
    }

    [Test]
    public void Builder_WithAudience_Empty_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithAudience(""));

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Builder_WithIssuer_Overrides()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithIssuer("other-iss"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Issuer.Should().Be("other-iss");
    }

    [Test]
    public void Builder_WithIssuer_Empty_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithIssuer(""));

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Builder_WithNotBeforeAndExpires_HonoredOnToken()
    {
        var nbf = DateTime.UtcNow.AddDays(-1);
        var exp = DateTime.UtcNow.AddDays(1);
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithNotBefore(nbf).WithExpires(exp));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.ValidFrom.Should().BeCloseTo(nbf, TimeSpan.FromSeconds(2));
        jwt.ValidTo.Should().BeCloseTo(exp, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Builder_WithLifetime_OverridesBothTimestamps()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);
        var raw = issuer.Mint(b => b.WithLifetime(TimeSpan.FromHours(2)));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddHours(2), TimeSpan.FromSeconds(10));
        jwt.ValidFrom.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromSeconds(10));
    }

    [Test]
    public void Builder_WithLifetime_NonPositive_Throws()
    {
        var issuer = TestTokenIssuer.Symmetric("iss", "aud", Key);

        Action act = () => issuer.Mint(b => b.WithLifetime(TimeSpan.Zero));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
