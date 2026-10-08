using Trax.Core.Monad;

namespace Trax.Api.DTOs;

/// <summary>
/// One run drawn on its train's declared chain: every node the train declares, each with the steps
/// the run recorded for it and where it stands, and the steps that match no node. Read through
/// <c>RunGraphs</c>, which <c>operations.runGraph</c> and the dashboard's run page both call.
/// </summary>
/// <remarks>
/// The graph is the train's chain as the host declares it now, not as it was when the run ran. A
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
}

/// <summary>One declared node of a <see cref="RunGraph"/>, with what the run did there.</summary>
/// <param name="Id">The node's id; see <see cref="ChainGraph"/>.</param>
/// <param name="Kind">Which chain primitive declared the step.</param>
/// <param name="Junction">The junction or decider type, or null for a step that names none.</param>
/// <param name="In">The type the step consumes from Memory, or null.</param>
/// <param name="Out">The type the step contributes to Memory, or null.</param>
/// <param name="Opaque">True when what runs is decided only at run time (<see cref="ChainGraphNode.Opaque"/>).</param>
/// <param name="State">Where the node stands in this run.</param>
/// <param name="Replayed">True when an answer the node acted on came from an earlier run.</param>
/// <param name="TrackTaken">
/// For a routing step, the track the run took: the answer its route recorded, or the one track
/// holding steps the run recorded when the answer is withheld. Null when it took none or it cannot
/// be told, and always null for a <c>Parallel</c> step, which runs every branch.
/// </param>
/// <param name="Steps">The run's steps recorded for this node, in position order.</param>
/// <param name="Tracks">
/// The tracks of a routing step, or the branches of a <c>Parallel</c> step, in declared order;
/// empty for any other step.
/// </param>
/// <remarks>
/// A <c>Parallel</c> step records no step of its own, so its <paramref name="State"/> is read from
/// its branches: failed when any branch failed, cancelled when one was stopped (by a sibling's
/// failure or by the run's cancel), in progress while any branch has a node running or still to
/// reach, completed once every branch has finished, and not reached when none has started.
/// </remarks>
public sealed record RunGraphNode(
    string Id,
    ChainStepKind Kind,
    string? Junction,
    string? In,
    string? Out,
    bool Opaque,
    RunNodeState State,
    bool Replayed,
    string? TrackTaken,
    IReadOnlyList<JunctionStep> Steps,
    IReadOnlyList<RunGraphTrack> Tracks
)
{
    /// <summary>
    /// True when the run can resume at this node, as <see cref="RunGraph.CanResume"/> says for its
    /// latest checkpoint: the resume check over the declared chain allows it from a checkpoint
    /// before it. The dashboard offers "Resume from here" on such a node, and
    /// <c>resumeExecution(id, from)</c> takes its id.
    /// </summary>
    public bool CanResume { get; init; }

    /// <summary>
    /// True when a checkpoint the run can resume from is stored at this node: one the run wrote,
    /// or for a resumed run, one the run it resumed wrote before the point it resumed at. Only
    /// that it exists: what it holds is never on an operator surface.
    /// </summary>
    public bool Checkpointed { get; init; }
}

/// <summary>
/// One track of a routing node, or one branch of a <c>Parallel</c> node, in a <see cref="RunGraph"/>.
/// </summary>
/// <param name="Name">The track's or branch's name.</param>
/// <param name="Description">What the track is for, as offered to the decider; null for a branch.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track; never for a branch.</param>
/// <param name="Taken">
/// True when the run took this track. Every branch of a <c>Parallel</c> step runs, so each is taken
/// once the step has started.
/// </param>
/// <param name="Nodes">The track's nodes, in declared order.</param>
public sealed record RunGraphTrack(
    string Name,
    string? Description,
    bool IsFallback,
    bool Taken,
    IReadOnlyList<RunGraphNode> Nodes
);

/// <summary>Where one declared node stands in a run.</summary>
public enum RunNodeState
{
    /// <summary>No step was recorded for it, and nothing says it was passed over.</summary>
    NotReached,

    /// <summary>Its junction has started and not yet returned.</summary>
    InProgress,

    /// <summary>Every step recorded for it completed.</summary>
    Completed,

    /// <summary>A step recorded for it failed: the junction threw, or a decider's answer was refused.</summary>
    Failed,

    /// <summary>A step recorded for it stopped because the run was asked to cancel.</summary>
    Cancelled,

    /// <summary>
    /// It sits on a track the run did not take. A branch of a <c>Parallel</c> step is never passed
    /// over, so a node there is skipped only when the step itself sits on such a track.
    /// </summary>
    Skipped,

    /// <summary>
    /// A kind of step that records nothing when it runs (<c>Extract</c>, <c>Seed</c>, <c>Resolve</c>),
    /// so the run cannot say whether it ran.
    /// </summary>
    NotRecorded,

    /// <summary>
    /// It comes after a route whose answer is withheld (a question about a <c>[TraxSensitive]</c>
    /// type), so the steps the run recorded there name no node: which ran would give the answer away.
    /// Those steps are in <see cref="RunGraph.UnmatchedSteps"/>.
    /// </summary>
    Withheld,

    /// <summary>
    /// The run resumed from a checkpoint, and this node comes before the point it resumed at, so
    /// the run skipped it and recorded no step for it: what the steps before the checkpoint
    /// produced was restored from it instead.
    /// </summary>
    Restored,
}
