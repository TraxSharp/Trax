using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class JwtResolverRegistryTests
{
    private static readonly ITraxPrincipalResolver<JwtTokenInput> A = new StubResolver("a");
    private static readonly ITraxPrincipalResolver<JwtTokenInput> B = new StubResolver("b");

    [Test]
    public void Register_StoresFactoryAndResolveReturnsIt()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);

        registry
            .Resolve("alpha", new ServiceCollection().BuildServiceProvider())
            .Should()
            .BeSameAs(A);
    }

    [Test]
    public void Register_Replaces_WhenSchemeAlreadyKnown()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);
        registry.Register("alpha", _ => B);

        registry
            .Resolve("alpha", new ServiceCollection().BuildServiceProvider())
            .Should()
            .BeSameAs(B);
    }

    [Test]
    public void Register_NullSchemeName_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () => registry.Register(null!, _ => A);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Register_EmptySchemeName_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () => registry.Register("", _ => A);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Register_NullFactory_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () => registry.Register("alpha", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Resolve_UnknownScheme_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () =>
            registry.Resolve("missing", new ServiceCollection().BuildServiceProvider());

        act.Should().Throw<InvalidOperationException>().WithMessage("*missing*AddTraxJwtAuth*");
    }

    [Test]
    public void Resolve_NullSp_Throws()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);

        Action act = () => registry.Resolve("alpha", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Resolve_NullSchemeName_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () => registry.Resolve(null!, new ServiceCollection().BuildServiceProvider());

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void TryResolve_KnownScheme_ReturnsResolver()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);

        registry
            .TryResolve("alpha", new ServiceCollection().BuildServiceProvider())
            .Should()
            .BeSameAs(A);
    }

    [Test]
    public void TryResolve_UnknownScheme_ReturnsNull()
    {
        var registry = new JwtResolverRegistry();

        registry
            .TryResolve("missing", new ServiceCollection().BuildServiceProvider())
            .Should()
            .BeNull();
    }

    [Test]
    public void TryResolve_NullScheme_Throws()
    {
        var registry = new JwtResolverRegistry();

        Action act = () =>
            registry.TryResolve(null!, new ServiceCollection().BuildServiceProvider());

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void TryResolve_NullSp_Throws()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);

        Action act = () => registry.TryResolve("alpha", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ResolveDefault_RoutesToDefaultScheme()
    {
        var registry = new JwtResolverRegistry();
        registry.Register(JwtDefaults.SchemeName, _ => A);
        registry.Register("other", _ => B);

        registry
            .ResolveDefault(new ServiceCollection().BuildServiceProvider())
            .Should()
            .BeSameAs(A);
    }

    [Test]
    public void ResolveDefault_DefaultNotRegistered_ReturnsNull()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("other", _ => B);

        registry.ResolveDefault(new ServiceCollection().BuildServiceProvider()).Should().BeNull();
    }

    [Test]
    public void SchemeNames_ReflectsRegisteredSchemes()
    {
        var registry = new JwtResolverRegistry();
        registry.Register("alpha", _ => A);
        registry.Register("beta", _ => B);

        registry.SchemeNames.Should().BeEquivalentTo("alpha", "beta");
    }

    [Test]
    public void GetOrAdd_ReusesExistingInstance()
    {
        var services = new ServiceCollection();
        var first = JwtResolverRegistry.GetOrAdd(services);
        var second = JwtResolverRegistry.GetOrAdd(services);

        second.Should().BeSameAs(first);
        services.Count(sd => sd.ServiceType == typeof(JwtResolverRegistry)).Should().Be(1);
    }

    [Test]
    public void GetOrAdd_RegistersAsSingletonInstance()
    {
        var services = new ServiceCollection();
        var registry = JwtResolverRegistry.GetOrAdd(services);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<JwtResolverRegistry>().Should().BeSameAs(registry);
    }

    [Test]
    public void Resolve_FactoryGetsServiceProvider()
    {
        var registry = new JwtResolverRegistry();
        var capturedSp = (IServiceProvider?)null;
        registry.Register(
            "alpha",
            sp =>
            {
                capturedSp = sp;
                return A;
            }
        );

        var services = new ServiceCollection().BuildServiceProvider();
        registry.Resolve("alpha", services);

        capturedSp.Should().BeSameAs(services);
    }

    private sealed class StubResolver(string label) : ITraxPrincipalResolver<JwtTokenInput>
    {
        public string Label => label;

        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct) =>
            new((TraxPrincipal?)null);
    }
}
