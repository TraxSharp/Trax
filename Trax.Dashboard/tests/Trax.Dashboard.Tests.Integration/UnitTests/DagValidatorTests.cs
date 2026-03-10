using FluentAssertions;
using Trax.Scheduler.Utilities;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class DagValidatorTests
{
    /// <summary>
    /// Helper: returns the index of a value in a sorted result list.
    /// </summary>
    private static int IndexOf<T>(IReadOnlyList<T> list, T value)
        where T : notnull
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], value))
                return i;
        }

        return -1;
    }

    #region Acyclic Graphs

    [Test]
    public void TopologicalSort_EmptyGraph_ReturnsAcyclic()
    {
        // Arrange & Act
        var result = DagValidator.TopologicalSort<int>([], []);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().BeEmpty();
        result.CycleMembers.Should().BeEmpty();
    }

    [Test]
    public void TopologicalSort_SingleNode_ReturnsAcyclic()
    {
        // Arrange & Act
        var result = DagValidator.TopologicalSort([1], Array.Empty<(int, int)>());

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().Equal(1);
    }

    [Test]
    public void TopologicalSort_LinearChain_ReturnsCorrectOrder()
    {
        // Arrange: A → B → C
        var result = DagValidator.TopologicalSort([1, 2, 3], [(1, 2), (2, 3)]);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(3);

        // 1 must come before 2, and 2 before 3
        IndexOf(result.Sorted, 1).Should().BeLessThan(IndexOf(result.Sorted, 2));
        IndexOf(result.Sorted, 2).Should().BeLessThan(IndexOf(result.Sorted, 3));
    }

    [Test]
    public void TopologicalSort_DiamondDag_ReturnsAcyclic()
    {
        // Arrange: A → B, A → C, B → D, C → D
        var result = DagValidator.TopologicalSort([1, 2, 3, 4], [(1, 2), (1, 3), (2, 4), (3, 4)]);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(4);

        // A before B and C, both B and C before D
        IndexOf(result.Sorted, 1).Should().BeLessThan(IndexOf(result.Sorted, 2));
        IndexOf(result.Sorted, 1).Should().BeLessThan(IndexOf(result.Sorted, 3));
        IndexOf(result.Sorted, 2).Should().BeLessThan(IndexOf(result.Sorted, 4));
        IndexOf(result.Sorted, 3).Should().BeLessThan(IndexOf(result.Sorted, 4));
    }

    [Test]
    public void TopologicalSort_DisconnectedGraph_ReturnsAcyclic()
    {
        // Arrange: Two separate chains — A→B and C→D
        var result = DagValidator.TopologicalSort([1, 2, 3, 4], [(1, 2), (3, 4)]);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(4);
        IndexOf(result.Sorted, 1).Should().BeLessThan(IndexOf(result.Sorted, 2));
        IndexOf(result.Sorted, 3).Should().BeLessThan(IndexOf(result.Sorted, 4));
    }

    [Test]
    public void TopologicalSort_IsolatedNodes_ReturnsAcyclic()
    {
        // Arrange: Multiple nodes with no edges
        var result = DagValidator.TopologicalSort([1, 2, 3], Array.Empty<(int, int)>());

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(3);
        result.Sorted.Should().Contain([1, 2, 3]);
    }

    [Test]
    public void TopologicalSort_StringKeys_WorksCorrectly()
    {
        // Arrange: String-keyed graph — "extract" → "transform" → "load"
        var result = DagValidator.TopologicalSort(
            ["extract", "transform", "load"],
            [("extract", "transform"), ("transform", "load")]
        );

        // Assert
        result.IsAcyclic.Should().BeTrue();
        IndexOf(result.Sorted, "extract").Should().BeLessThan(IndexOf(result.Sorted, "transform"));
        IndexOf(result.Sorted, "transform").Should().BeLessThan(IndexOf(result.Sorted, "load"));
    }

    #endregion

    #region Cyclic Graphs

    [Test]
    public void TopologicalSort_SimpleCycle_DetectsCycle()
    {
        // Arrange: A → B → A
        var result = DagValidator.TopologicalSort([1, 2], [(1, 2), (2, 1)]);

        // Assert
        result.IsAcyclic.Should().BeFalse();
        result.CycleMembers.Should().Contain(1);
        result.CycleMembers.Should().Contain(2);
    }

    [Test]
    public void TopologicalSort_SelfLoop_DetectsCycle()
    {
        // Arrange: A → A
        var result = DagValidator.TopologicalSort([1], [(1, 1)]);

        // Assert
        result.IsAcyclic.Should().BeFalse();
        result.CycleMembers.Should().Contain(1);
    }

    [Test]
    public void TopologicalSort_ThreeNodeCycle_DetectsCycle()
    {
        // Arrange: A → B → C → A
        var result = DagValidator.TopologicalSort([1, 2, 3], [(1, 2), (2, 3), (3, 1)]);

        // Assert
        result.IsAcyclic.Should().BeFalse();
        result.CycleMembers.Should().HaveCount(3);
        result.CycleMembers.Should().Contain([1, 2, 3]);
    }

    [Test]
    public void TopologicalSort_CycleWithTail_IdentifiesCycleMembersOnly()
    {
        // Arrange: A → B → C → B  (A is not in cycle, B and C are)
        var result = DagValidator.TopologicalSort([1, 2, 3], [(1, 2), (2, 3), (3, 2)]);

        // Assert
        result.IsAcyclic.Should().BeFalse();

        // A (1) should be in the sorted portion (processed before the cycle is hit)
        result.Sorted.Should().Contain(1);

        // B (2) and C (3) are in the cycle
        result.CycleMembers.Should().Contain(2);
        result.CycleMembers.Should().Contain(3);
        result.CycleMembers.Should().NotContain(1);
    }

    [Test]
    public void TopologicalSort_CycleWithDownstreamNode_IdentifiesAffectedNodes()
    {
        // Arrange: A → B → A, B → C (C is downstream of cycle)
        var result = DagValidator.TopologicalSort([1, 2, 3], [(1, 2), (2, 1), (2, 3)]);

        // Assert
        result.IsAcyclic.Should().BeFalse();

        // All three should be cycle members (A and B in cycle, C depends on cycle)
        result.CycleMembers.Should().Contain(1);
        result.CycleMembers.Should().Contain(2);
        // C has non-zero in-degree because B (which is stuck) feeds it
        result.CycleMembers.Should().Contain(3);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void TopologicalSort_EdgesReferencingUnknownNodes_AreIgnored()
    {
        // Arrange: Nodes [1,2] but edge references node 99
        var result = DagValidator.TopologicalSort([1, 2], [(1, 2), (2, 99), (99, 1)]);

        // Assert
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(2);
        IndexOf(result.Sorted, 1).Should().BeLessThan(IndexOf(result.Sorted, 2));
    }

    [Test]
    public void TopologicalSort_DuplicateEdges_HandledCorrectly()
    {
        // Arrange: Same edge repeated — A → B with duplicate
        var result = DagValidator.TopologicalSort([1, 2], [(1, 2), (1, 2)]);

        // Assert — still a valid DAG (duplicates just add redundant in-degree)
        result.IsAcyclic.Should().BeTrue();
        result.Sorted.Should().HaveCount(2);
    }

    #endregion
}
