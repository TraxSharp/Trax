namespace Trax.Dashboard.Models;

/// <summary>
/// A node handed to <see cref="Utilities.DagLayoutEngine.ComputeLayout"/>: one manifest group
/// in the manifest group dependency graph. Infrastructure for the dashboard's dependency graphs;
/// not intended to be used directly.
/// </summary>
internal class DagNode
{
    /// <summary>
    /// The database id of the manifest group the node stands for. Edges refer to
    /// nodes by this value, so it must be unique within one graph.
    /// </summary>
    public long Id { get; init; }

    /// <summary>The text drawn in the node. Nodes in the same layer are ordered by it when nothing else decides.</summary>
    public string Label { get; init; } = "";

    /// <summary><see langword="true"/> for the node the current page is about, which the graph draws emphasised.</summary>
    public bool IsHighlighted { get; init; }
}

/// <summary>
/// A dependency between two <see cref="DagNode"/>s. Infrastructure for the dashboard's
/// dependency graphs; not intended to be used directly.
/// </summary>
internal class DagEdge
{
    /// <summary>
    /// Upstream node ID (the dependency/parent — rendered on the left).
    /// </summary>
    public long FromId { get; init; }

    /// <summary>
    /// Downstream node ID (the dependent — rendered on the right).
    /// </summary>
    public long ToId { get; init; }
}
