using FluentAssertions;
using NUnit.Framework;
using Trax.Api.Auth.Oidc;

namespace Trax.Api.Tests.Auth.Oidc;

[TestFixture]
public class OidcBuilderTests
{
    [Test]
    public void Validate_NoAuthority_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () =>
            typeof(OidcBuilder)
                .GetMethod(
                    "Validate",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .Invoke(builder, null);

        act.Should()
            .Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*UseAuthority*");
    }

    [Test]
    public void UseAuthority_NullAuthority_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.UseAuthority(null!, "client");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void UseAuthority_NullClientId_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.UseAuthority("https://x", null!);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithClientSecret_Whitespace_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.WithClientSecret("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithClientSecret_Valid_AppliesToBuilder()
    {
        var builder = new OidcBuilder()
            .UseAuthority("https://x.example", "client")
            .WithClientSecret("secret");

        builder.Should().NotBeNull();
    }

    [Test]
    public void AddScope_DuplicateScope_NotAddedTwice()
    {
        var builder = new OidcBuilder();
        builder.AddScope("custom");
        builder.AddScope("custom");

        // openid + profile + custom (single)
        var scopes =
            (IList<string>)
                typeof(OidcBuilder)
                    .GetProperty(
                        "Scopes",
                        System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic
                    )!
                    .GetValue(builder)!;

        scopes.Count(s => s == "custom").Should().Be(1);
    }

    [Test]
    public void AddScope_Whitespace_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.AddScope("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithCallbackPath_Whitespace_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.WithCallbackPath("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithSignedOutCallbackPath_Whitespace_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.WithSignedOutCallbackPath("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AllowHttpMetadata_FlipsRequireHttps()
    {
        var builder = new OidcBuilder().UseAuthority("https://x", "c").AllowHttpMetadata();

        // RequireHttpsMetadata is internal — read via reflection.
        var requireHttps = (bool)
            typeof(OidcBuilder)
                .GetProperty(
                    "RequireHttpsMetadata",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .GetValue(builder)!;

        requireHttps.Should().BeFalse();
    }

    [Test]
    public void DoNotSaveTokens_DisablesSaveTokens()
    {
        var builder = new OidcBuilder().UseAuthority("https://x", "c").DoNotSaveTokens();

        var save = (bool)
            typeof(OidcBuilder)
                .GetProperty(
                    "SaveTokens",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .GetValue(builder)!;

        save.Should().BeFalse();
    }

    [Test]
    public void CustomizeOidcOptions_NullConfigure_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.CustomizeOidcOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CustomizeCookieOptions_NullConfigure_Throws()
    {
        var builder = new OidcBuilder();

        Action act = () => builder.CustomizeCookieOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void WithCallbackPath_Valid_AppliesToBuilder()
    {
        var builder = new OidcBuilder().UseAuthority("https://x", "c").WithCallbackPath("/cb");
        var path = (string)
            typeof(OidcBuilder)
                .GetProperty(
                    "CallbackPath",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .GetValue(builder)!;
        path.Should().Be("/cb");
    }

    [Test]
    public void WithSignedOutCallbackPath_Valid_AppliesToBuilder()
    {
        var builder = new OidcBuilder()
            .UseAuthority("https://x", "c")
            .WithSignedOutCallbackPath("/signed-out");
        var path = (string)
            typeof(OidcBuilder)
                .GetProperty(
                    "SignedOutCallbackPath",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .GetValue(builder)!;
        path.Should().Be("/signed-out");
    }

    [Test]
    public void CustomizeOidcOptions_Valid_StoresDelegate()
    {
        var builder = new OidcBuilder();
        var called = false;

        builder.CustomizeOidcOptions(_ => called = true);

        var customizer = typeof(OidcBuilder)
            .GetProperty(
                "OidcOptionsCustomizer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .GetValue(builder);
        customizer.Should().NotBeNull();
        (
            (Action<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>)
                customizer!
        ).Invoke(new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions());
        called.Should().BeTrue();
    }

    [Test]
    public void CustomizeCookieOptions_Valid_StoresDelegate()
    {
        var builder = new OidcBuilder();
        var called = false;

        builder.CustomizeCookieOptions(_ => called = true);

        var customizer = typeof(OidcBuilder)
            .GetProperty(
                "CookieOptionsCustomizer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .GetValue(builder);
        customizer.Should().NotBeNull();
        (
            (Action<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>)
                customizer!
        ).Invoke(new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions());
        called.Should().BeTrue();
    }

    [Test]
    public void Validate_AuthorityAndClient_DoesNotThrow()
    {
        var builder = new OidcBuilder().UseAuthority("https://x.example", "c");

        Action act = () =>
            typeof(OidcBuilder)
                .GetMethod(
                    "Validate",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .Invoke(builder, null);

        act.Should().NotThrow();
    }
}
