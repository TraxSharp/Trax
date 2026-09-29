using System.ComponentModel;
using System.Globalization;
using Trax.Dashboard.Models;
using Trax.Scheduler.Utilities;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// A <see cref="DagNode"/> with its computed position, in SVG user units. Output of
/// <see cref="DagLayoutEngine.ComputeLayout"/>; public only because <c>DagGraph.Layout</c>
/// exposes it, and not intended for use outside this package.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class PositionedNode
{
    /// <summary>The source node's <see cref="DagNode.Id"/>.</summary>
    public long Id { get; init; }

    /// <summary>The source node's <see cref="DagNode.Label"/>.</summary>
    public string Label { get; init; } = "";

    /// <summary>The source node's <see cref="DagNode.IsHighlighted"/>.</summary>
    public bool IsHighlighted { get; init; }

    /// <summary>Left edge of the node. Nodes in the same layer share it; layers run left to right.</summary>
    public double X { get; init; }

    /// <summary>Top edge of the node. Each layer is centred vertically against the tallest one.</summary>
    public double Y { get; init; }

    /// <summary>Node width; always 180.</summary>
    public double Width { get; init; }

    /// <summary>Node height; always 40.</summary>
    public double Height { get; init; }
}

/// <summary>
/// A <see cref="DagEdge"/> with the curve that draws it. Output of
/// <see cref="DagLayoutEngine.ComputeLayout"/>; public only because <c>DagGraph.Layout</c>
/// exposes it, and not intended for use outside this package.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class PositionedEdge
{
    /// <summary>The upstream node's id; the curve starts at that node's right edge.</summary>
    public long FromId { get; init; }

    /// <summary>The downstream node's id; the curve ends at that node's left edge.</summary>
    public long ToId { get; init; }

    /// <summary>
    /// SVG cubic bezier path data (e.g., "M x1,y1 C cx1,cy1 cx2,cy2 x2,y2").
    /// </summary>
    public string PathData { get; init; } = "";
}

/// <summary>
/// A laid-out dependency graph ready for <c>DagGraph</c> to draw as SVG. Output of
/// <see cref="DagLayoutEngine.ComputeLayout"/>; public only because <c>DagGraph.Layout</c>
/// exposes it, and not intended for use outside this package.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class DagLayout
{
    /// <summary>Every input node, positioned. Empty when the input had no nodes.</summary>
    public IReadOnlyList<PositionedNode> Nodes { get; init; } = [];

    /// <summary>
    /// The input edges whose two ends are both nodes of the graph; edges naming an unknown node
    /// are dropped.
    /// </summary>
    public IReadOnlyList<PositionedEdge> Edges { get; init; } = [];

    /// <summary>Width of the drawing including its 40-unit padding, for the SVG view box. Zero for an empty graph.</summary>
    public double Width { get; init; }

    /// <summary>Height of the drawing including its 40-unit padding, for the SVG view box. Zero for an empty graph.</summary>
    public double Height { get; init; }
}

/// <summary>
/// Lays out a dependency graph left to right in layers, for the manifest group graphs.
/// Infrastructure for the dashboard's own pages; not intended to be called directly.
/// </summary>
internal static class DagLayoutEngine
{
    private const double NodeWidth = 180;
    private const double NodeHeight = 40;
    private const double LayerGap = 120;
    private const double NodeGap = 24;
    private const double Padding = 40;

    /// <summary>
    /// Computes node positions and edge curves. Each node's layer is the length of the longest
    /// path to it from a node with no predecessors; nodes with no edges at all go in one extra
    /// layer on the far right. Within a layer, nodes are ordered by label and then by two
    /// barycenter sweeps to reduce edge crossings. The result is deterministic for the same input.
    /// </summary>
    /// <param name="nodes">The nodes. Ids must be unique, or the method throws.</param>
    /// <param name="edges">
    /// The dependencies, upstream to downstream. Edges naming an id not in
    /// <paramref name="nodes"/> are ignored.
    /// </param>
    /// <returns>The layout, or an empty <see cref="DagLayout"/> when there are no nodes.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// The edges contain a cycle and a node in it has no predecessor earlier in
    /// <paramref name="nodes"/>. For a cyclic graph the input order replaces the topological
    /// order, so layering needs a predecessor already placed.
    /// </exception>
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
