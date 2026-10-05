using AwesomeAssertions;
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

    [Test]
    public void ComputeLayout_CyclicGraph_LaysOutEveryNodeAndEdge()
    {
        // Group graphs can be cyclic even when the manifest dependencies under them are not:
        // A -> B -> A, with C feeding A.
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
            new DagNode { Id = 2, Label = "B" },
            new DagNode { Id = 3, Label = "C" },
        };
        var edges = new[]
        {
            new DagEdge { FromId = 1, ToId = 2 },
            new DagEdge { FromId = 2, ToId = 1 },
            new DagEdge { FromId = 3, ToId = 1 },
        };

        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        layout.Nodes.Select(n => n.Id).Should().BeEquivalentTo([1L, 2L, 3L]);
        layout.Edges.Should().HaveCount(3, "an edge that closes a cycle is still drawn");
        var x = layout.Nodes.ToDictionary(n => n.Id, n => n.X);
        x[3].Should().BeLessThan(x[1], "C feeds A, so it is laid out to A's left");
        x[1].Should().BeLessThan(x[2], "the cycle is entered at A, so B follows it");
    }

    [Test]
    public void ComputeLayout_SelfLoop_LaysOutTheNode()
    {
        var nodes = new[]
        {
            new DagNode { Id = 1, Label = "A" },
            new DagNode { Id = 2, Label = "B" },
        };
        var edges = new[]
        {
            new DagEdge { FromId = 1, ToId = 1 },
            new DagEdge { FromId = 1, ToId = 2 },
        };

        var layout = DagLayoutEngine.ComputeLayout(nodes, edges);

        layout.Nodes.Should().HaveCount(2);
        layout.Edges.Should().HaveCount(2);
    }
}
