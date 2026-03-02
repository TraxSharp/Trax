using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class MediatorServiceExtensionsTests
{
    [Test]
    public void AddServiceTrainBus_RegistersITrainBus()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options =>
            options.AddServiceTrainBus(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        // Act
        var trainBus = provider.GetService<ITrainBus>();

        // Assert
        trainBus.Should().NotBeNull();
    }

    [Test]
    public void AddServiceTrainBus_RegistersITrainRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options =>
            options.AddServiceTrainBus(assemblies: [typeof(AssemblyMarker).Assembly])
        );
        using var provider = services.BuildServiceProvider();

        // Act
        var registry = provider.GetService<ITrainRegistry>();

        // Assert
        registry.Should().NotBeNull();
    }

    [Test]
    public void RegisterServiceTrains_WithNoAssemblies_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.RegisterServiceTrains();
        act.Should().NotThrow();
    }
}
