using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class AddTraxPrincipalAccessorTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Test]
    public void RegistersHttpContextAccessor()
    {
        var services = NewServices();

        services.AddTraxPrincipalAccessor();

        services.Should().Contain(sd => sd.ServiceType == typeof(IHttpContextAccessor));
    }

    [Test]
    public void RegistersTraxPrincipalAsScoped()
    {
        var services = NewServices();

        services.AddTraxPrincipalAccessor();

        services
            .Should()
            .ContainSingle(sd => sd.ServiceType == typeof(TraxPrincipal))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Test]
    public void CalledTwice_RegistersTraxPrincipalOnlyOnce()
    {
        var services = NewServices();

        services.AddTraxPrincipalAccessor();
        services.AddTraxPrincipalAccessor();

        services.Count(sd => sd.ServiceType == typeof(TraxPrincipal)).Should().Be(1);
    }

    [Test]
    public void Resolve_WithAuthenticatedHttpContext_ReturnsTraxPrincipal()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var accessor = new HttpContextAccessor
        {
            HttpContext = MakeContextWith(
                new TraxPrincipal("alice", "Alice", ["User"]).ToClaimsPrincipal("TraxApiKey")
            ),
        };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var principal = scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        principal.Id.Should().Be("alice");
        principal.DisplayName.Should().Be("Alice");
        principal.Roles.Should().BeEquivalentTo(["User"]);
    }

    [Test]
    public void Resolve_WithAnonymousHttpContext_Throws()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity()),
            },
        };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        act.Should().Throw<TraxPrincipalNotAvailableException>();
    }

    [Test]
    public void Resolve_WithNoHttpContext_Throws()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var accessor = new HttpContextAccessor { HttpContext = null };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        act.Should().Throw<TraxPrincipalNotAvailableException>();
    }

    [Test]
    public void Resolve_WithNonTraxClaimsPrincipal_Throws()
    {
        // A ClaimsPrincipal from some other scheme (no trax:principal-id claim)
        // should be rejected: we can't safely present a ClaimsPrincipal of unknown
        // origin as a TraxPrincipal.
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "ForeignScheme");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        act.Should().Throw<TraxPrincipalNotAvailableException>();
    }

    [Test]
    public void Resolve_SameScope_ReturnsSameInstance()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var accessor = new HttpContextAccessor
        {
            HttpContext = MakeContextWith(
                new TraxPrincipal("alice", "Alice", ["User"]).ToClaimsPrincipal("TraxApiKey")
            ),
        };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<TraxPrincipal>();
        var b = scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        a.Should().BeSameAs(b);
    }

    [Test]
    public void Resolve_DifferentScopes_CanReturnDifferentPrincipals()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var accessor = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();

        accessor.HttpContext = MakeContextWith(
            new TraxPrincipal("alice", "Alice", ["User"]).ToClaimsPrincipal("TraxApiKey")
        );
        using var scope1 = sp.CreateScope();
        var first = scope1.ServiceProvider.GetRequiredService<TraxPrincipal>();

        accessor.HttpContext = MakeContextWith(
            new TraxPrincipal("bob", "Bob", ["User", "Admin"]).ToClaimsPrincipal("TraxApiKey")
        );
        using var scope2 = sp.CreateScope();
        var second = scope2.ServiceProvider.GetRequiredService<TraxPrincipal>();

        first.Id.Should().Be("alice");
        second.Id.Should().Be("bob");
        second.Roles.Should().BeEquivalentTo(["User", "Admin"]);
    }

    [Test]
    public void Resolve_CustomClaimsRoundtrip()
    {
        var services = NewServices();
        services.AddTraxPrincipalAccessor();

        var original = new TraxPrincipal(
            "alice",
            "Alice",
            ["Admin"],
            Claims: new Dictionary<string, string> { ["tenant"] = "acme", ["tier"] = "enterprise" },
            PrincipalType: "apikey"
        );

        var accessor = new HttpContextAccessor
        {
            HttpContext = MakeContextWith(original.ToClaimsPrincipal("TraxApiKey")),
        };
        services.AddSingleton<IHttpContextAccessor>(accessor);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        resolved.Id.Should().Be("alice");
        resolved.DisplayName.Should().Be("Alice");
        resolved.Roles.Should().BeEquivalentTo(["Admin"]);
        resolved.PrincipalType.Should().Be("apikey");
        resolved.Claims.Should().NotBeNull();
        resolved.Claims!["tenant"].Should().Be("acme");
        resolved.Claims!["tier"].Should().Be("enterprise");
    }

    [Test]
    public void Resolve_FromRootProvider_Throws()
    {
        // TraxPrincipal is scoped; resolving from the root provider should
        // either fail with the standard scoped-validation error or our own
        // not-available exception. Either way, it should NOT silently succeed.
        var services = NewServices();
        services.AddTraxPrincipalAccessor();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());

        using var sp = services.BuildServiceProvider(validateScopes: true);

        var act = () => sp.GetRequiredService<TraxPrincipal>();

        act.Should().Throw<InvalidOperationException>();
    }

    private static DefaultHttpContext MakeContextWith(ClaimsPrincipal user) =>
        new() { User = user };
}
