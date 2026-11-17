using Trax.Core.Decisions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>The codes a refused resume carries, each with its own reason.</summary>
internal static class ResumeRefusals
{
    /// <summary>The run wrote no checkpoint that covers the point.</summary>
    public const string NoCheckpoint = "no-checkpoint";

    /// <summary>The chain declares no step with that id.</summary>
    public const string UnknownStep = "unknown-step";

    /// <summary>The id names a step a run cannot start at: an extraction, a seed or the end.</summary>
    public const string NotAStep = "not-a-step";

    /// <summary>The point is inside a <c>Parallel</c> branch; a branch resumes from its own checkpoint.</summary>
    public const string InsideABranch = "inside-a-branch";

    /// <summary>The point is in a track of a routing step after the checkpoint, which the run would not ask.</summary>
    public const string OffThePath = "off-the-path";

    /// <summary>A step from the point on needs a type nothing restores or produces.</summary>
    public const string MissingInput = "missing-input";

    /// <summary>
    /// A step from the point on reads a type a skipped step overwrote, which the checkpoint does
    /// not hold: a resumed run would read an older value.
    /// </summary>
    public const string StaleValue = "stale-value";

    /// <summary>The chain changed since the checkpoint was written.</summary>
    public const string ChainChanged = "chain-changed";

    /// <summary>The checkpoint's state type changed shape since it was written.</summary>
    public const string StateChanged = "state-changed";
}
