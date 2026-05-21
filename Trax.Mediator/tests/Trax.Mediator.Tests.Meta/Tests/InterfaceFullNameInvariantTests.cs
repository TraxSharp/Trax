using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;

namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// Verifies the load-bearing CLAUDE.md > Train Type Naming Rule: the canonical identifier for
/// a train is the INTERFACE FullName, set during DI registration via AddScopedTraxRoute. Every
/// downstream layer (metadata.Name, work_queue.train_name, manifest.Name, GraphQL hooks,
/// dashboard requeue, scheduler exclusions) compares against this value. Drift here corrupts
/// all of them silently.
/// </summary>
[TestFixture]
public class InterfaceFullNameInvariantTests
{
    public interface IFakeRoute
    {
        string? CanonicalName { get; set; }
    }

    public class FakeRoute : IFakeRoute
    {
        public string? CanonicalName { get; set; }
    }

    public interface IFakeRouteWithDifferentNamespace
    {
        string? CanonicalName { get; set; }
    }

    public class FakeRouteWithDifferentNamespace : IFakeRouteWithDifferentNamespace
    {
        public string? CanonicalName { get; set; }
    }

    [Test]
    public void AddScopedTraxRoute_Generic_Sets_CanonicalName_To_InterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IFakeRoute, FakeRoute>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var route = scope.ServiceProvider.GetRequiredService<IFakeRoute>();

        route
            .CanonicalName.Should()
            .Be(
                typeof(IFakeRoute).FullName,
                "AddScopedTraxRoute<TService, TImpl> sets CanonicalName = typeof(TService).FullName. "
                    + "This is the train's canonical identifier across the entire system (metadata, "
                    + "work_queue, manifest, GraphQL hooks). Any drift would silently break lookups."
            );
    }

    [Test]
    public void AddScopedTraxRoute_NonGeneric_Sets_CanonicalName_To_InterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute(
            typeof(IFakeRouteWithDifferentNamespace),
            typeof(FakeRouteWithDifferentNamespace)
        );

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var route = (IFakeRouteWithDifferentNamespace)
            scope.ServiceProvider.GetRequiredService(typeof(IFakeRouteWithDifferentNamespace));

        route
            .CanonicalName.Should()
            .Be(
                typeof(IFakeRouteWithDifferentNamespace).FullName,
                "the runtime-types overload of AddScopedTraxRoute must use the same canonical-name "
                    + "rule as the generic overload."
            );
    }

    [Test]
    public void Resolving_TwoScopes_Each_Gets_SameCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IFakeRoute, FakeRoute>();

        using var sp = services.BuildServiceProvider();
        string? first;
        string? second;

        using (var scope1 = sp.CreateScope())
            first = scope1.ServiceProvider.GetRequiredService<IFakeRoute>().CanonicalName;
        using (var scope2 = sp.CreateScope())
            second = scope2.ServiceProvider.GetRequiredService<IFakeRoute>().CanonicalName;

        first
            .Should()
            .Be(
                second,
                "CanonicalName is stamped per resolution (not cached on the type). Every resolution "
                    + "must produce the same value: the interface FullName."
            );
        first.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void InterfaceFullName_Format_DoesNotContainNullableQuestionMark()
    {
        // Reflection regression guard: if someone accidentally puts a string?-style annotation
        // in CanonicalName by treating it as a non-nullable string, we want to know.
        var name = typeof(IFakeRoute).FullName;
        name.Should().NotBeNull();
        name!
            .Should()
            .NotContain(
                "?",
                "FullName must never contain '?'; that would indicate a nullable annotation leak."
            );
        name.Should().StartWith("Trax.Mediator.Tests.Meta.Tests");
        name.Should().Contain("IFakeRoute");
    }
}
