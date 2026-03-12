using FluentAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TrainRegistryUnitTests
{
    [Test]
    public void Constructor_WithNoAssemblies_ReturnsEmptyMapping()
    {
        // Act
        var registry = new TrainRegistry();

        // Assert
        registry.InputTypeToTrain.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WithAssemblyContainingTrains_FindsTrains()
    {
        // Arrange — use the test assembly which has MemoryTestTrain registered
        var assembly = typeof(AssemblyMarker).Assembly;

        // Act
        var registry = new TrainRegistry(assembly);

        // Assert
        registry.InputTypeToTrain.Should().NotBeEmpty();
    }

    [Test]
    public void InputTypeToTrain_IsNotNull()
    {
        // Act
        var registry = new TrainRegistry();

        // Assert
        registry.InputTypeToTrain.Should().NotBeNull();
    }

    #region DiscoveredTrains

    [Test]
    public void Constructor_WithAssemblyContainingTrains_PopulatesDiscoveredTrains()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry.DiscoveredTrains.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_WithNoAssemblies_ReturnsEmptyDiscoveredTrains()
    {
        var registry = new TrainRegistry();

        registry.DiscoveredTrains.Should().BeEmpty();
    }

    [Test]
    public void DiscoveredTrains_ContainsExpectedTrainCount()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        // Every discovered train should have a corresponding InputTypeToTrain entry
        // (though InputTypeToTrain may have fewer entries due to duplicate input types)
        registry
            .DiscoveredTrains.Count.Should()
            .BeGreaterThanOrEqualTo(registry.InputTypeToTrain.Count);
    }

    [Test]
    public void DiscoveredTrains_ServiceType_IsInterfaceNotConcrete()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        foreach (var (serviceType, _) in registry.DiscoveredTrains)
        {
            serviceType
                .IsInterface.Should()
                .BeTrue($"ServiceType {serviceType.Name} should be an interface");
        }
    }

    [Test]
    public void DiscoveredTrains_ImplementationType_IsConcreteClass()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        foreach (var (_, implementationType) in registry.DiscoveredTrains)
        {
            implementationType
                .IsClass.Should()
                .BeTrue($"ImplementationType {implementationType.Name} should be a class");
            implementationType
                .IsAbstract.Should()
                .BeFalse($"ImplementationType {implementationType.Name} should not be abstract");
        }
    }

    [Test]
    public void DiscoveredTrains_ContainsMemoryTestTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry
            .DiscoveredTrains.Should()
            .Contain(t =>
                t.ServiceType == typeof(IMemoryTestTrain)
                && t.ImplementationType == typeof(MemoryTestTrain)
            );
    }

    [Test]
    public void DiscoveredTrains_ContainsFailingTestTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry
            .DiscoveredTrains.Should()
            .Contain(t =>
                t.ServiceType == typeof(IFailingTestTrain)
                && t.ImplementationType == typeof(FailingTestTrain)
            );
    }

    [Test]
    public void DiscoveredTrains_ContainsNestedTestTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry
            .DiscoveredTrains.Should()
            .Contain(t =>
                t.ServiceType == typeof(INestedTestTrain)
                && t.ImplementationType == typeof(NestedTestTrain)
            );
    }

    [Test]
    public void DiscoveredTrains_MatchesInputTypeToTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        // Every value in InputTypeToTrain should appear as a ServiceType in DiscoveredTrains
        foreach (var (_, trainType) in registry.InputTypeToTrain)
        {
            registry
                .DiscoveredTrains.Should()
                .Contain(
                    t => t.ServiceType == trainType,
                    $"InputTypeToTrain value {trainType.Name} should appear in DiscoveredTrains"
                );
        }
    }

    [Test]
    public void DiscoveredTrains_DuplicateAssembly_DoesNotProduceDuplicates()
    {
        var assembly = typeof(AssemblyMarker).Assembly;

        // Pass the same assembly twice
        var registry = new TrainRegistry(assembly, assembly);

        // Each concrete type should appear exactly once
        var concreteTypes = registry.DiscoveredTrains.Select(t => t.ImplementationType).ToList();
        concreteTypes.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Trains Without Dedicated Interface

    [Test]
    public void DiscoveredTrains_ContainsInterfacelessTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry
            .DiscoveredTrains.Should()
            .Contain(t => t.ImplementationType == typeof(InterfacelessTestTrain));
    }

    [Test]
    public void DiscoveredTrains_InterfacelessTrain_ServiceTypeIsGenericInterface()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        var entry = registry.DiscoveredTrains.First(t =>
            t.ImplementationType == typeof(InterfacelessTestTrain)
        );

        // Service type should be the closed generic IServiceTrain<TIn, TOut>,
        // NOT the concrete class
        entry
            .ServiceType.Should()
            .Be(typeof(IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>));
        entry.ServiceType.IsInterface.Should().BeTrue();
        entry.ServiceType.IsClass.Should().BeFalse();
    }

    [Test]
    public void InputTypeToTrain_InterfacelessTrain_MapsInputTypeCorrectly()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        registry.InputTypeToTrain.Should().ContainKey(typeof(InterfacelessTestInput));

        registry
            .InputTypeToTrain[typeof(InterfacelessTestInput)]
            .Should()
            .Be(typeof(IServiceTrain<InterfacelessTestInput, InterfacelessTestOutput>));
    }

    [Test]
    public void DiscoveredTrains_InterfacelessTrain_MatchesInputTypeToTrain()
    {
        var registry = new TrainRegistry(typeof(AssemblyMarker).Assembly);

        var entry = registry.DiscoveredTrains.First(t =>
            t.ImplementationType == typeof(InterfacelessTestTrain)
        );

        // The service type from DiscoveredTrains should be the same value
        // stored in InputTypeToTrain for this train's input type
        registry.InputTypeToTrain[typeof(InterfacelessTestInput)].Should().Be(entry.ServiceType);
    }

    #endregion
}
