using FluentAssertions;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class OidcBuilderTests
{
    [Test]
    public void Validate_MissingAuthority_Throws()
    {
        var builder = new OidcBuilder();

        var act = () => builder.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseAuthority*clientId*");
    }

    [Test]
    public void UseAuthority_EmptyClientId_Throws()
    {
        var builder = new OidcBuilder();

        var act = () => builder.UseAuthority("https://id.example.com", "");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddScope_DefaultsIncludeOpenIdAndProfile()
    {
        var builder = new OidcBuilder();

        builder.Scopes.Should().Contain(new[] { "openid", "profile" });
    }

    [Test]
    public void AddScope_Dedupes()
    {
        var builder = new OidcBuilder();

        builder.AddScope("email");
        builder.AddScope("email");

        builder.Scopes.Count(s => s == "email").Should().Be(1);
    }

    [Test]
    public void AddScope_AppendsNewScopes()
    {
        var builder = new OidcBuilder();

        builder.AddScope("email");
        builder.AddScope("offline_access");

        builder.Scopes.Should().Contain("email").And.Contain("offline_access");
    }

    [Test]
    public void WithCallbackPath_Empty_Throws()
    {
        var builder = new OidcBuilder();

        var act = () => builder.WithCallbackPath("");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Validate_WithValidConfig_Succeeds()
    {
        var builder = new OidcBuilder();

        builder.UseAuthority("https://id.example.com", "my-client");

        builder.Invoking(b => b.Validate()).Should().NotThrow();
    }

    [Test]
    public void AllowHttpMetadata_TogglesFlag()
    {
        var builder = new OidcBuilder();

        builder.RequireHttpsMetadata.Should().BeTrue();
        builder.AllowHttpMetadata();

        builder.RequireHttpsMetadata.Should().BeFalse();
    }

    [Test]
    public void WithClientSecret_Persists()
    {
        var builder = new OidcBuilder();

        builder.WithClientSecret("shh");

        builder.ClientSecret.Should().Be("shh");
    }

    [Test]
    public void DoNotSaveTokens_TogglesFlag()
    {
        var builder = new OidcBuilder();

        builder.SaveTokens.Should().BeTrue();
        builder.DoNotSaveTokens();

        builder.SaveTokens.Should().BeFalse();
    }
}
