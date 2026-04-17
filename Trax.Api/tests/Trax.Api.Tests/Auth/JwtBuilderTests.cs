using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtBuilderTests
{
    [Test]
    public void Validate_NoKeySource_Throws()
    {
        var builder = new JwtBuilder();

        var act = () => builder.Validate();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseAuthority*UseSigningKey*UseSymmetricKey*");
    }

    [Test]
    public void Validate_BothAuthorityAndSigningKey_Throws()
    {
        var builder = new JwtBuilder();
        builder.UseAuthority("https://login.example.com", "api");
        builder.UseSymmetricKey("iss", "aud", new byte[32]);

        var act = () => builder.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot mix*");
    }

    [Test]
    public void UseSymmetricKey_ShortKey_Throws()
    {
        var builder = new JwtBuilder();

        var act = () => builder.UseSymmetricKey("iss", "aud", new byte[16]);

        act.Should().Throw<ArgumentException>().WithMessage("*at least 32 bytes*");
    }

    [Test]
    public void UseSymmetricKey_SetsIssuerAudienceAndKey()
    {
        var builder = new JwtBuilder();
        var keyBytes = Encoding.UTF8.GetBytes(new string('k', 32));

        builder.UseSymmetricKey("my-iss", "my-aud", keyBytes);
        builder.Validate();

        builder.Issuer.Should().Be("my-iss");
        builder.Audience.Should().Be("my-aud");
        builder.SigningKey.Should().BeOfType<SymmetricSecurityKey>();
    }

    [Test]
    public void UseAuthority_SetsAuthorityAndAudience()
    {
        var builder = new JwtBuilder();

        builder.UseAuthority("https://login.example.com", "my-aud");
        builder.Validate();

        builder.Authority.Should().Be("https://login.example.com");
        builder.Audience.Should().Be("my-aud");
        builder.SigningKey.Should().BeNull();
    }

    [Test]
    public void WithClockSkew_Negative_Throws()
    {
        var builder = new JwtBuilder();

        var act = () => builder.WithClockSkew(TimeSpan.FromSeconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void AllowHttpMetadata_TogglesFlag()
    {
        var builder = new JwtBuilder();

        builder.RequireHttpsMetadata.Should().BeTrue();
        builder.AllowHttpMetadata();

        builder.RequireHttpsMetadata.Should().BeFalse();
    }

    [Test]
    public void UseAuthority_Empty_Throws()
    {
        var builder = new JwtBuilder();

        var act = () => builder.UseAuthority("", "aud");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void UseSigningKey_Null_Throws()
    {
        var builder = new JwtBuilder();

        var act = () => builder.UseSigningKey("iss", "aud", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
