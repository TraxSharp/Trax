using System.ComponentModel;

namespace Trax.Scheduler.Utilities;

/// <summary>
/// Result of a topological sort operation on a directed graph.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#infrastructure-types">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public class TopologicalSortResult<TKey>
    where TKey : notnull
{
    /// <summary>
    /// True if the graph is a valid DAG (no cycles).
    /// </summary>
    public bool IsAcyclic { get; init; }

    /// <summary>
    /// Nodes in topological order. Only meaningful when <see cref="IsAcyclic"/> is true.
    /// </summary>
    public IReadOnlyList<TKey> Sorted { get; init; } = [];

    /// <summary>
    /// Nodes involved in at least one cycle. Only populated when <see cref="IsAcyclic"/> is false.
    /// </summary>
    public IReadOnlySet<TKey> CycleMembers { get; init; } = new HashSet<TKey>();
}
