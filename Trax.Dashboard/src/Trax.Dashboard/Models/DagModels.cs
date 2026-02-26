namespace Trax.Dashboard.Models;

public class DagNode
{
    public long Id { get; init; }
    public string Label { get; init; } = "";
    public bool IsHighlighted { get; init; }
}

public class DagEdge
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
