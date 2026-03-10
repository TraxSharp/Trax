using FluentAssertions;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class DagLayoutEngineTests
{
    [Test]
    public void ComputeLayout_EmptyNodes_ReturnsEmptyLayout()
    {
        // Arrange
        var nodes = Array.Empty<DagNode>();
        var edges = Array.Empty<DagEdge>();

        // Act
        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        // Assert
        layout.Nodes.Should().BeEmpty();
        layout.Edges.Should().BeEmpty();
    }

    [Test]
    public void ComputeLayout_SingleNode_PositionsIt()
    {
        // Arrange
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
        };
        var edges = Array.Empty<DagEdge>();

        // Act
        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        // Assert
        layout.Nodes.Should().HaveCount(1);
        layout.Nodes[0].Id.Should().Be(1);
    }

    [Test]
    public void ComputeLayout_LinearChain_ProducesMultipleLayers()
    {
        // Arrange — A -> B -> C
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
            new DagNode { Id = 2, Label = "B" },
            new DagNode { Id = 3, Label = "C" },
        };
        var edges = new[]
        {
            new DagEdge { FromId = 1, ToId = 2 },
            new DagEdge { FromId = 2, ToId = 3 },
        };

        // Act
        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        // Assert
        layout.Nodes.Should().HaveCount(3);
        layout.Edges.Should().HaveCount(2);
        // Nodes should have different X positions (different layers)
        var xs = layout.Nodes.Select(n => n.X).Distinct().ToList();
        xs.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public void ComputeLayout_Diamond_ProducesValidLayout()
    {
        // Arrange — A -> B, A -> C, B -> D, C -> D
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
            new DagNode { Id = 2, Label = "B" },
            new DagNode { Id = 3, Label = "C" },
            new DagNode { Id = 4, Label = "D" },
        };
        var edges = new[]
        {
            new DagEdge { FromId = 1, ToId = 2 },
            new DagEdge { FromId = 1, ToId = 3 },
            new DagEdge { FromId = 2, ToId = 4 },
            new DagEdge { FromId = 3, ToId = 4 },
        };

        // Act
        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        // Assert
        layout.Nodes.Should().HaveCount(4);
        layout.Edges.Should().HaveCount(4);
        layout.Width.Should().BeGreaterThan(0);
        layout.Height.Should().BeGreaterThan(0);
    }

    [Test]
    public void ComputeLayout_Edges_ProduceValidPathData()
    {
        // Arrange
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
            new DagNode { Id = 2, Label = "B" },
        };
        var edges = new[]
        {
            new DagEdge { FromId = 1, ToId = 2 },
        };

        // Act
        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        // Assert — SVG path data should start with M (moveto) and contain C (cubic bezier)
        layout.Edges.Should().HaveCount(1);
        layout.Edges[0].PathData.Should().StartWith("M");
        layout.Edges[0].PathData.Should().Contain("C");
    }
}
