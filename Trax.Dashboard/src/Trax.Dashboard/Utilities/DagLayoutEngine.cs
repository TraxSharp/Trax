using System.Globalization;
using Trax.Dashboard.Models;
using Trax.Scheduler.Utilities;

namespace Trax.Dashboard.Utilities;

public class PositionedNode
{
    public long Id { get; init; }
    public string Label { get; init; } = "";
    public bool IsHighlighted { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public class PositionedEdge
{
    public long FromId { get; init; }
    public long ToId { get; init; }

    /// <summary>
    /// SVG cubic bezier path data (e.g., "M x1,y1 C cx1,cy1 cx2,cy2 x2,y2").
    /// </summary>
    public string PathData { get; init; } = "";
}

public class DagLayout
{
    public IReadOnlyList<PositionedNode> Nodes { get; init; } = [];
    public IReadOnlyList<PositionedEdge> Edges { get; init; } = [];
    public double Width { get; init; }
    public double Height { get; init; }
}

public static class DagLayoutEngine
{
    private const double NodeWidth = 180;
    private const double NodeHeight = 40;
    private const double LayerGap = 120;
    private const double NodeGap = 24;
    private const double Padding = 40;

    public static DagLayout ComputeLayout(
        IReadOnlyList<DagNode> nodes,
        IReadOnlyList<DagEdge> edges
    )
    {
        if (nodes.Count == 0)
            return new DagLayout();

        // Build adjacency lists (needed for layer assignment + barycenter)
        var nodeIds = new HashSet<long>(nodes.Select(n => n.Id));
        var successors = nodes.ToDictionary(n => n.Id, _ => new List<long>());
        var predecessors = nodes.ToDictionary(n => n.Id, _ => new List<long>());

        var validEdges = edges
            .Where(e => nodeIds.Contains(e.FromId) && nodeIds.Contains(e.ToId))
            .ToList();

        foreach (var edge in validEdges)
        {
            successors[edge.FromId].Add(edge.ToId);
            predecessors[edge.ToId].Add(edge.FromId);
        }

        // Topological sort via shared DagValidator
        var sortResult = DagValidator.TopologicalSort(
            nodeIds,
            validEdges.Select(e => (e.FromId, e.ToId))
        );

        // Use sorted order if acyclic, fall back to original order for resilience
        var sorted = sortResult.IsAcyclic ? sortResult.Sorted : nodes.Select(n => n.Id).ToList();

        // Layer assignment (longest path from roots)
        var layer = new Dictionary<long, int>();
        foreach (var id in sorted)
        {
            if (predecessors[id].Count == 0)
            {
                layer[id] = 0;
            }
            else
            {
                layer[id] = predecessors[id].Where(layer.ContainsKey).Max(p => layer[p]) + 1;
            }
        }

        // Separate isolated nodes (no edges at all) into their own rightmost layer
        var isolatedIds = new HashSet<long>(
            nodes
                .Where(n => successors[n.Id].Count == 0 && predecessors[n.Id].Count == 0)
                .Select(n => n.Id)
        );

        var maxConnectedLayer = layer
            .Where(kv => !isolatedIds.Contains(kv.Key))
            .Select(kv => kv.Value)
            .DefaultIfEmpty(-1)
            .Max();

        if (isolatedIds.Count > 0)
        {
            var isolatedLayer = maxConnectedLayer + 1;
            foreach (var id in isolatedIds)
                layer[id] = isolatedLayer;
        }

        // Group nodes by layer, initial alphabetical order
        var nodeMap = nodes.ToDictionary(n => n.Id);
        var layerGroups = nodes
            .GroupBy(n => layer[n.Id])
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Label).ToList());

        // Barycenter heuristic to minimize edge crossings (2 full sweeps)
        var layerKeys = layerGroups.Keys.OrderBy(k => k).ToList();
        for (var sweep = 0; sweep < 2; sweep++)
        {
            // Forward pass: order each layer by average position of predecessors
            for (var li = 1; li < layerKeys.Count; li++)
            {
                var key = layerKeys[li];
                var prevKey = layerKeys[li - 1];
                var prevOrder = BuildPositionIndex(layerGroups[prevKey]);

                layerGroups[key] = layerGroups[key]
                    .OrderBy(n => Barycenter(n.Id, predecessors, prevOrder))
                    .ThenBy(n => n.Label)
                    .ToList();
            }

            // Backward pass: order each layer by average position of successors
            for (var li = layerKeys.Count - 2; li >= 0; li--)
            {
                var key = layerKeys[li];
                var nextKey = layerKeys[li + 1];
                var nextOrder = BuildPositionIndex(layerGroups[nextKey]);

                layerGroups[key] = layerGroups[key]
                    .OrderBy(n => Barycenter(n.Id, successors, nextOrder))
                    .ThenBy(n => n.Label)
                    .ToList();
            }
        }

        // Compute max layer height for vertical centering
        var maxNodesInLayer = layerGroups.Values.Max(g => g.Count);
        var maxLayerHeight = maxNodesInLayer * (NodeHeight + NodeGap) - NodeGap;

        // Position nodes
        var positioned = new Dictionary<long, PositionedNode>();

        foreach (var (layerIndex, nodesInLayer) in layerGroups)
        {
            var layerHeight = nodesInLayer.Count * (NodeHeight + NodeGap) - NodeGap;
            var yOffset = (maxLayerHeight - layerHeight) / 2;

            for (var i = 0; i < nodesInLayer.Count; i++)
            {
                var n = nodesInLayer[i];
                positioned[n.Id] = new PositionedNode
                {
                    Id = n.Id,
                    Label = n.Label,
                    IsHighlighted = n.IsHighlighted,
                    X = Padding + layerIndex * (NodeWidth + LayerGap),
                    Y = Padding + yOffset + i * (NodeHeight + NodeGap),
                    Width = NodeWidth,
                    Height = NodeHeight,
                };
            }
        }

        // Compute edge paths (cubic bezier)
        var positionedEdges = validEdges
            .Select(e =>
            {
                var from = positioned[e.FromId];
                var to = positioned[e.ToId];

                var startX = from.X + NodeWidth;
                var startY = from.Y + NodeHeight / 2;
                var endX = to.X;
                var endY = to.Y + NodeHeight / 2;

                var dx = endX - startX;
                var cp1X = startX + dx * 0.4;
                var cp2X = endX - dx * 0.4;

                var path = string.Format(
                    CultureInfo.InvariantCulture,
                    "M {0:F1},{1:F1} C {2:F1},{3:F1} {4:F1},{5:F1} {6:F1},{7:F1}",
                    startX,
                    startY,
                    cp1X,
                    startY,
                    cp2X,
                    endY,
                    endX,
                    endY
                );

                return new PositionedEdge
                {
                    FromId = e.FromId,
                    ToId = e.ToId,
                    PathData = path,
                };
            })
            .ToList();

        // Compute viewport
        var maxLayer = layerGroups.Keys.Max();
        var totalWidth = Padding * 2 + (maxLayer + 1) * NodeWidth + maxLayer * LayerGap;
        var totalHeight = Padding * 2 + maxLayerHeight;

        return new DagLayout
        {
            Nodes = positioned.Values.ToList(),
            Edges = positionedEdges,
            Width = totalWidth,
            Height = totalHeight,
        };
    }

    /// <summary>
    /// Builds a map from node ID to its index position within the layer.
    /// </summary>
    private static Dictionary<long, int> BuildPositionIndex(List<DagNode> layerNodes)
    {
        var index = new Dictionary<long, int>();
        for (var i = 0; i < layerNodes.Count; i++)
            index[layerNodes[i].Id] = i;
        return index;
    }

    /// <summary>
    /// Computes the barycenter (average position) of a node's neighbors in an adjacent layer.
    /// Returns double.MaxValue for nodes with no neighbors so they sort to the end.
    /// </summary>
    private static double Barycenter(
        long nodeId,
        Dictionary<long, List<long>> adjacency,
        Dictionary<long, int> neighborPositions
    )
    {
        var neighbors = adjacency[nodeId];
        if (neighbors.Count == 0)
            return double.MaxValue;

        var sum = 0.0;
        var count = 0;
        foreach (var n in neighbors)
        {
            if (neighborPositions.TryGetValue(n, out var pos))
            {
                sum += pos;
                count++;
            }
        }

        return count > 0 ? sum / count : double.MaxValue;
    }
}
