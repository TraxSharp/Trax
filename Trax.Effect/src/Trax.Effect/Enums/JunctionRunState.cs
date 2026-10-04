namespace Trax.Effect.Enums;

/// <summary>
/// Where one step of a run's timeline stands. Carried by junction events and stored in
/// <c>trax.junction_run.state</c>.
/// </summary>
/// <remarks>
/// The values are pinned because SQLite stores the integer.
/// </remarks>
public enum JunctionRunState
{
    /// <summary>The junction has started and not yet returned.</summary>
    InProgress = 0,

    /// <summary>The junction returned a result, or the question was answered, or the track taken.</summary>
    Completed = 1,

    /// <summary>
    /// The junction failed, or a decider's answer was refused and the routing step failed on it.
    /// </summary>
    Failed = 2,

    /// <summary>The junction stopped because the run was asked to cancel.</summary>
    Cancelled = 3,
}
