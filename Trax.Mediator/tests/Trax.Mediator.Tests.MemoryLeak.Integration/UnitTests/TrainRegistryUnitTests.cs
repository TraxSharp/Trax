using FluentAssertions;
using Trax.Mediator.Services.TrainRegistry;

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
}
