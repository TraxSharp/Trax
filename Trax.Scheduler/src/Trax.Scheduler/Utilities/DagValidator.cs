namespace Trax.Scheduler.Utilities;

/// <summary>
/// Result of a topological sort operation on a directed graph.
/// </summary>
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

/// <summary>
/// Generic DAG validation and topological sort using Kahn's algorithm.
/// </summary>
public static class DagValidator
{
    /// <summary>
    /// Performs a topological sort and validates that the graph is acyclic.
    /// </summary>
    /// <param name="nodes">All node keys in the graph.</param>
    /// <param name="edges">Directed edges as (From, To) pairs. From must be processed before To.</param>
    /// <returns>A result containing the sorted order or the nodes participating in cycles.</returns>
    public static TopologicalSortResult<TKey> TopologicalSort<TKey>(
        IEnumerable<TKey> nodes,
        IEnumerable<(TKey From, TKey To)> edges
    )
        where TKey : notnull
    {
        var nodeSet = new HashSet<TKey>(nodes);
        var successors = new Dictionary<TKey, List<TKey>>();
        var inDegree = new Dictionary<TKey, int>();

        foreach (var node in nodeSet)
        {
            successors[node] = [];
            inDegree[node] = 0;
        }

        foreach (var (from, to) in edges)
        {
            if (!nodeSet.Contains(from) || !nodeSet.Contains(to))
                continue;

            successors[from].Add(to);
            inDegree[to]++;
        }

        // Kahn's algorithm
        var queue = new Queue<TKey>(nodeSet.Where(n => inDegree[n] == 0));
        var sorted = new List<TKey>(nodeSet.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            foreach (var succ in successors[current])
            {
                inDegree[succ]--;
                if (inDegree[succ] == 0)
                    queue.Enqueue(succ);
            }
        }

        if (sorted.Count == nodeSet.Count)
        {
            return new TopologicalSortResult<TKey> { IsAcyclic = true, Sorted = sorted, };
        }

        // Nodes with remaining in-degree > 0 are part of or downstream of a cycle
        var cycleMembers = new HashSet<TKey>(nodeSet.Where(n => inDegree[n] > 0));

        return new TopologicalSortResult<TKey>
        {
            IsAcyclic = false,
            Sorted = sorted,
            CycleMembers = cycleMembers,
        };
    }
}
