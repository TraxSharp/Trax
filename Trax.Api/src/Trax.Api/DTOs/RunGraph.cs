using Trax.Core.Monad;

namespace Trax.Api.DTOs;

/// <summary>
/// One run drawn on its train's declared chain: every node the train declares, each with the steps
/// the run recorded for it and where it stands, and the steps that match no node. Read through
/// <c>RunGraphs</c>, which <c>operations.runGraph</c> and the dashboard's run page both call.
/// </summary>
/// <remarks>
/// The graph is the train's chain as the host declares it now, not as it was when the run ran;
/// <see cref="ChainChanged"/> says when the two differ. A
/// step whose node id is absent (a row recorded before node ids were, or a step on a track whose
/// answer is withheld) or names no node of the current graph (the chain changed since) is listed in
/// <see cref="UnmatchedSteps"/> rather than dropped.
/// </remarks>
/// <param name="MetadataId">The run's id.</param>
/// <param name="Train">The run's train, by its canonical name.</param>
/// <param name="HasGraph">
/// False when the host has no graph for the train: it is not registered here, or its chain cannot
/// be read outside a request. <see cref="Nodes"/> is then empty and every step is unmatched.
/// </param>
/// <param name="Hash">The graph's <see cref="ChainGraph.Hash"/>, or null without a graph.</param>
/// <param name="Nodes">The declared nodes, in the order the chain declares them.</param>
/// <param name="UnmatchedSteps">The run's steps that match no node, in position order.</param>
/// <param name="MoreSteps">
/// True when the run recorded more steps than were read (<c>RunGraphs.MaxSteps</c>), so a node
/// after the last one read can show as not reached when it ran.
/// </param>
public sealed record RunGraph(
    long MetadataId,
    string Train,
    bool HasGraph,
    string? Hash,
    IReadOnlyList<RunGraphNode> Nodes,
    IReadOnlyList<JunctionStep> UnmatchedSteps,
    bool MoreSteps
)
{
    /// <summary>
    /// True when the run can resume after its latest checkpoint: it failed or was cancelled, no
    /// state machine's step started it, and a checkpoint it wrote (or, for a resumed run, one the
    /// run it resumed wrote) lets the steps after it run on what it restores. What the dashboard's
    /// Resume button and <c>resumeExecution</c> without <c>from</c> act on; the operation can still
    /// refuse the run as a whole, for its saved input or a resume already queued, with the reason.
    /// </summary>
    public bool CanResume { get; init; }

    /// <summary>
    /// True when the run recorded the hash of its train's chain when it started, and it differs
    /// from <see cref="Hash"/>: the chain has changed since the run, so the nodes it is drawn on may
    /// not be the ones it ran (a step moved, a track's contents changed) though their ids match.
    /// False when the run recorded no hash (from before it was recorded, or from a host that never
    /// read the chain) and when the host has no graph for the train.
    /// </summary>
    public bool ChainChanged { get; init; }

    /// <summary>
    /// Every node of <see cref="Nodes"/> and of their tracks, at any depth, in one list: depth
    /// first, each node before the nodes on its tracks, and each track's nodes in declared order.
    /// A node's <see cref="RunGraphNode.ParentId"/> and <see cref="RunGraphNode.Track"/> say where
    /// it sits, so a client can rebuild the tree from this list without asking for nested tracks,
    /// whose depth a GraphQL query cannot follow past the server's field-cycle limit.
    /// </summary>
    public IReadOnlyList<RunGraphNode> AllNodes => Flatten(Nodes);

    private static List<RunGraphNode> Flatten(IReadOnlyList<RunGraphNode> nodes)
    {
        var all = new List<RunGraphNode>();
        Walk(nodes);
        return all;

        void Walk(IReadOnlyList<RunGraphNode> level)
        {
            foreach (var node in level)
            {
                all.Add(node);
                foreach (var track in node.Tracks)
                    Walk(track.Nodes);
            }
        }
    }
}
