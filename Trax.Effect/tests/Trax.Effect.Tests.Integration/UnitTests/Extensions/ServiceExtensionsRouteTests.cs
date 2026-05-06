using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class ServiceExtensionsRouteTests
{
    [Test]
    public void AddTransientTraxRoute_Generic_ResolvesAndSetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransientTraxRoute<IFakeRoute, FakeRoute>();
        using var provider = services.BuildServiceProvider();

        var instance = provider.GetRequiredService<IFakeRoute>();
        instance.Should().BeOfType<FakeRoute>();
        ((FakeRoute)instance).CanonicalName.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void AddTransientTraxRoute_RuntimeTypes_ResolvesAndSetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransientTraxRoute(typeof(IFakeRoute), typeof(FakeRoute));
        using var provider = services.BuildServiceProvider();

        var instance = (FakeRoute)provider.GetRequiredService<IFakeRoute>();
        instance.CanonicalName.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void AddScopedTraxRoute_RuntimeTypes_ResolvesAndSetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScopedTraxRoute(typeof(IFakeRoute), typeof(FakeRoute));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var instance = (FakeRoute)scope.ServiceProvider.GetRequiredService<IFakeRoute>();
        instance.CanonicalName.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void AddSingletonTraxRoute_Generic_ResolvesAndSetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingletonTraxRoute<IFakeRoute, FakeRoute>();
        using var provider = services.BuildServiceProvider();

        var instance = (FakeRoute)provider.GetRequiredService<IFakeRoute>();
        instance.CanonicalName.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void AddSingletonTraxRoute_RuntimeTypes_ResolvesAndSetsCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingletonTraxRoute(typeof(IFakeRoute), typeof(FakeRoute));
        using var provider = services.BuildServiceProvider();

        var instance = (FakeRoute)provider.GetRequiredService<IFakeRoute>();
        instance.CanonicalName.Should().Be(typeof(IFakeRoute).FullName);
    }

    [Test]
    public void InjectProperties_FillsNullPropertiesFromProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, ConcreteDependency>();
        using var provider = services.BuildServiceProvider();

        var target = new HasInject();
        provider.InjectProperties(target);

        target.Dep.Should().NotBeNull().And.BeOfType<ConcreteDependency>();
    }

    [Test]
    public void InjectProperties_DoesNotOverwritePreSetProperty()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, ConcreteDependency>();
        using var provider = services.BuildServiceProvider();

        var existing = new ConcreteDependency();
        var target = new HasInject { Dep = existing };
        provider.InjectProperties(target);

        target.Dep.Should().BeSameAs(existing);
    }

    [Test]
    public void InjectProperties_IEnumerableProperty_FillsFromProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDependency, ConcreteDependency>();
        services.AddSingleton<IDependency, ConcreteDependency>();
        using var provider = services.BuildServiceProvider();

        var target = new HasEnumerableInject();
        provider.InjectProperties(target);

        target.Deps.Should().NotBeNull();
        target.Deps!.Should().HaveCount(2);
    }

    public class HasEnumerableInject
    {
        [Inject]
        public IEnumerable<IDependency>? Deps { get; set; }
    }

    public interface IFakeRoute
    {
        string? CanonicalName { get; set; }
    }

    public class FakeRoute : IFakeRoute
    {
        public string? CanonicalName { get; set; }
    }

    public interface IDependency { }

    public class ConcreteDependency : IDependency { }

    public class HasInject
    {
        [Inject]
        public IDependency? Dep { get; set; }
    }
}
