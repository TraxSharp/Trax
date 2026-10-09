namespace Trax.Effect.Enums;

/// <summary>
/// Who owns a state-machine draft. Stored in <c>trax.snapshot_draft.owner_kind</c>.
/// </summary>
/// <remarks>
/// The values are pinned because SQLite stores the integer.
/// </remarks>
public enum SnapshotOwnerKind
{
    /// <summary>
    /// A user owns the draft. Its <c>user_key</c> names that user, and only that user's requests reach it.
    /// </summary>
    User = 0,

    /// <summary>
    /// The system owns the instance. It has no <c>user_key</c>, it is created only from code, and no user
    /// request reads, writes, lists or expires it.
    /// </summary>
    System = 1,
}
