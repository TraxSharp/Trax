using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// Exercises the public <see cref="ServiceExtensions.RegisterServiceTrains"/> overload
/// that scans assemblies and applies a service lifetime. Distinct from the internal
/// pre-scanned overload exercised by <see cref="ServiceExtensions.AddServiceTrainBus"/>.
/// </summary>
[TestFixture]
public class RegisterServiceTrainsPublicTests
{
    [Test]
    public void RegisterServiceTrains_TransientLifetime_RegistersDiscoveredTrains()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterServiceTrains(ServiceLifetime.Transient, typeof(AssemblyMarker).Assembly);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IMemoryTestTrain>().Should().NotBeNull();
    }

    [Test]
    public void RegisterServiceTrains_ScopedLifetime_RegistersDiscoveredTrains()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterServiceTrains(ServiceLifetime.Scoped, typeof(AssemblyMarker).Assembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<IMemoryTestTrain>().Should().NotBeNull();
    }

    [Test]
    public void RegisterServiceTrains_SingletonLifetime_RegistersDiscoveredTrains()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterServiceTrains(ServiceLifetime.Singleton, typeof(AssemblyMarker).Assembly);

        using var provider = services.BuildServiceProvider();
        provider.GetService<IMemoryTestTrain>().Should().NotBeNull();
    }

    [Test]
    public void RegisterServiceTrains_InvalidLifetime_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Action act = () =>
            services.RegisterServiceTrains((ServiceLifetime)999, typeof(AssemblyMarker).Assembly);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void RegisterServiceTrains_AssemblyWithNoTrains_DoesNotThrow()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.RegisterServiceTrains(ServiceLifetime.Transient, typeof(string).Assembly);

        act.Should().NotThrow();
    }
}
