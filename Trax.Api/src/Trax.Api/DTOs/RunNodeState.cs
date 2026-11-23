using Trax.Core.Monad;

namespace Trax.Api.DTOs;

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

    /// <summary>
    /// Its junction started and never recorded an end, and the run has ended: the host stopped
    /// mid-junction, or the step's end event was dropped. The step's row stays in progress, so the
    /// node is read with the run's state rather than shown as still running.
    /// </summary>
    Interrupted,
}
