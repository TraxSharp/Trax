using Trax.Core.Monad;

namespace Trax.Api.DTOs;

/// <summary>
/// One node of a train's declared chain with where it sits, as <c>declaredChain.allNodes</c> lists
/// every node of the graph at any depth in one flat list. A client rebuilds the tree from each
/// node's <see cref="ParentId"/> and <see cref="Track"/>, so it never has to follow the nested
/// <c>tracks { nodes }</c> fields, whose depth a GraphQL query cannot follow past the server's
/// field-cycle limit.
/// </summary>
/// <param name="Node">The declared node.</param>
/// <param name="ParentId">
/// The id of the routing or <c>Parallel</c> step whose track the node sits on, or null at the
/// chain's top level.
/// </param>
/// <param name="Track">The name of that track or branch, or null at the chain's top level.</param>
/// <param name="Depth">How many tracks deep the node sits: 0 at the chain's top level.</param>
public sealed record DeclaredNode(ChainGraphNode Node, string? ParentId, string? Track, int Depth)
{
    /// <summary>
    /// Every node of <paramref name="graph"/> with where it sits: depth first, each node before the
    /// nodes on its tracks, and each track's nodes in declared order.
    /// </summary>
    /// <param name="graph">The declared chain.</param>
    public static IReadOnlyList<DeclaredNode> Flatten(ChainGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var all = new List<DeclaredNode>();
        Walk(graph.Nodes, null, null, 0);
        return all;

        void Walk(IReadOnlyList<ChainGraphNode> nodes, string? parentId, string? track, int depth)
        {
            foreach (var node in nodes)
            {
                all.Add(new DeclaredNode(node, parentId, track, depth));
                foreach (var t in node.Tracks)
                    Walk(t.Nodes, node.Id, t.Name, depth + 1);
            }
        }
    }
}

/// <summary>
/// A track of a routing step, or a branch of a <c>Parallel</c> step, as a <see cref="DeclaredNode"/>
/// names it: its name and what it is for, without its nodes, which the flat list carries itself.
/// </summary>
/// <param name="Name">The track's or branch's name.</param>
/// <param name="Description">What the track is for, as offered to the decider; null for a branch.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track; never for a branch.</param>
public sealed record DeclaredTrack(string Name, string? Description, bool IsFallback);
