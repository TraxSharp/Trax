using FluentAssertions;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class DagValidatorTests
{
    [Test]
    public void TopologicalSort_LinearChain_ReturnsSortedOrder()
    {
        // Arrange — A -> B -> C
        var nodes = new[] { "A", "B", "C" };
        var edges = new[] { ("A", "B"), ("B", "C") };

        // Act
        var result = DagValidator.TopologicalSort(nodes, edges);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        var sorted = result.Sorted.ToList();
        sorted.Should().HaveCount(3);
        sorted.IndexOf("A").Should().BeLessThan(sorted.IndexOf("B"));
        sorted.IndexOf("B").Should().BeLessThan(sorted.IndexOf("C"));
    }

    [Test]
    public void TopologicalSort_Diamond_IsAcyclic()
    {
        // Arrange — A -> B, A -> C, B -> D, C -> D
        var nodes = new[] { "A", "B", "C", "D" };
        var edges = new[] { ("A", "B"), ("A", "C"), ("B", "D"), ("C", "D") };

        // Act
        var result = DagValidator.TopologicalSort(nodes, edges);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        var sorted = result.Sorted.ToList();
        sorted.Should().HaveCount(4);
        sorted.IndexOf("A").Should().BeLessThan(sorted.IndexOf("B"));
        sorted.IndexOf("A").Should().BeLessThan(sorted.IndexOf("C"));
    }

    [Test]
    public void TopologicalSort_CyclicGraph_DetectsCycle()
    {
        // Arrange — A -> B -> C -> A
        var nodes = new[] { "A", "B", "C" };
        var edges = new[] { ("A", "B"), ("B", "C"), ("C", "A") };

        // Act
        var result = DagValidator.TopologicalSort(nodes, edges);

        // Assert
        result.IsAcyclic.Should().BeFalse();
        result.CycleMembers.Should().Contain("A");
        result.CycleMembers.Should().Contain("B");
        result.CycleMembers.Should().Contain("C");
    }

    [Test]
    public void TopologicalSort_SingleNode_ReturnsIt()
    {
        // Arrange
        var nodes = new[] { "X" };
        var edges = Array.Empty<(string, string)>();

        // Act
        var result = DagValidator.TopologicalSort(nodes, edges);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().ContainSingle().Which.Should().Be("X");
    }

    [Test]
    public void TopologicalSort_DisconnectedNodes_ReturnsAll()
    {
        // Arrange
        var nodes = new[] { "A", "B", "C" };
        var edges = Array.Empty<(string, string)>();

        // Act
        var result = DagValidator.TopologicalSort(nodes, edges);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(3);
        result.Sorted.Should().Contain("A");
        result.Sorted.Should().Contain("B");
        result.Sorted.Should().Contain("C");
    }
}
