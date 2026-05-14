using FluentAssertions;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtDispatcherBuilderTests
{
    [Test]
    public void MapIssuer_StoresMapping()
    {
        var builder = new JwtDispatcherBuilder();
        builder.MapIssuer("https://idp-a", "alpha");
        builder.MapIssuer("https://idp-b", "beta");

        builder
            .Mappings.Should()
            .Contain(new KeyValuePair<string, string>("https://idp-a", "alpha"))
            .And.Contain(new KeyValuePair<string, string>("https://idp-b", "beta"));
    }

    [Test]
    public void MapIssuer_DuplicateIssuer_Throws()
    {
        var builder = new JwtDispatcherBuilder();
        builder.MapIssuer("https://idp", "alpha");

        Action act = () => builder.MapIssuer("https://idp", "beta");

        act.Should().Throw<InvalidOperationException>().WithMessage("*already mapped*alpha*");
    }

    [Test]
    public void MapIssuer_EmptyIssuer_Throws()
    {
        var builder = new JwtDispatcherBuilder();

        Action act = () => builder.MapIssuer("", "alpha");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MapIssuer_EmptyScheme_Throws()
    {
        var builder = new JwtDispatcherBuilder();

        Action act = () => builder.MapIssuer("https://idp", "");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MapIssuer_NullArguments_Throw()
    {
        var builder = new JwtDispatcherBuilder();
        Action a = () => builder.MapIssuer(null!, "alpha");
        Action b = () => builder.MapIssuer("https://idp", null!);

        a.Should().Throw<ArgumentException>();
        b.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MapIssuer_IsOrdinalCaseSensitive()
    {
        var builder = new JwtDispatcherBuilder();
        builder.MapIssuer("https://idp", "alpha");
        builder.MapIssuer("https://IDP", "beta");

        builder.Mappings.Should().HaveCount(2);
    }

    [Test]
    public void WithSchemeName_Overrides()
    {
        var builder = new JwtDispatcherBuilder();
        builder.SchemeName.Should().Be(JwtDefaults.DispatcherSchemeName);

        builder.WithSchemeName("CustomDispatcher");

        builder.SchemeName.Should().Be("CustomDispatcher");
    }

    [Test]
    public void WithSchemeName_Empty_Throws()
    {
        var builder = new JwtDispatcherBuilder();

        Action act = () => builder.WithSchemeName("");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void FallbackToScheme_Sets()
    {
        var builder = new JwtDispatcherBuilder();
        builder.FallbackSchemeName.Should().BeNull();

        builder.FallbackToScheme("alpha");

        builder.FallbackSchemeName.Should().Be("alpha");
    }

    [Test]
    public void FallbackToScheme_Empty_Throws()
    {
        var builder = new JwtDispatcherBuilder();

        Action act = () => builder.FallbackToScheme("");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Validate_NoMappings_Throws()
    {
        var builder = new JwtDispatcherBuilder();

        Action act = () => builder.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MapIssuer*");
    }

    [Test]
    public void Validate_WithMappings_DoesNotThrow()
    {
        var builder = new JwtDispatcherBuilder();
        builder.MapIssuer("https://idp", "alpha");

        Action act = () => builder.Validate();

        act.Should().NotThrow();
    }

    [Test]
    public void Builder_IsFluent()
    {
        var builder = new JwtDispatcherBuilder()
            .MapIssuer("https://idp", "alpha")
            .WithSchemeName("Dispatcher")
            .FallbackToScheme("alpha");

        builder.SchemeName.Should().Be("Dispatcher");
        builder.FallbackSchemeName.Should().Be("alpha");
        builder.Mappings.Should().HaveCount(1);
    }
}
