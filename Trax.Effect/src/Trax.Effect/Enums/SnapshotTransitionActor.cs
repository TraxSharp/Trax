namespace Trax.Effect.Enums;

/// <summary>
/// Who made one transition of a state-machine instance. Stored in <c>trax.snapshot_transition.actor</c>. It names a
/// kind of actor, never a user: a user-owned instance's history does not say whose it is.
/// </summary>
/// <remarks>
/// The values are pinned because SQLite stores the integer.
/// </remarks>
public enum SnapshotTransitionActor
{
    /// <summary>A client acting for the owner of a user's instance, host code acting as one included.</summary>
    Owner = 0,

    /// <summary>Host code driving a system-owned instance (<c>IMachineInstances</c>).</summary>
    Host = 1,

    /// <summary>Trax moving the instance itself: a timer, a run's outcome, or a parent spawning it.</summary>
    Machine = 2,
}
