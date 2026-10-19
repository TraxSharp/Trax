namespace Trax.Effect.Enums;

/// <summary>
/// What wrote one row of a state-machine instance's history. Stored in <c>trax.snapshot_transition.kind</c>.
/// </summary>
/// <remarks>
/// The values are pinned because SQLite stores the integer.
/// </remarks>
public enum SnapshotTransitionKind
{
    /// <summary>The instance was created: a <c>start</c>, a save that created a draft, or a parent spawning it.</summary>
    Start = 0,

    /// <summary>A command (a machine event) moved it, sent by a client or by host code, or an effect it bound ran.</summary>
    Command = 1,

    /// <summary>A save wrote the snapshot without a command: a client's autosave, or host code writing through the store.</summary>
    Save = 2,

    /// <summary>A timer of its state fired.</summary>
    Timer = 3,

    /// <summary>A train run it invoked ended, and its outcome was applied, or could not be and left it stranded.</summary>
    Outcome = 4,
}
