using Trax.Effect.Enums;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The owner a draft row is looked up under. Every lookup names the owner kind as well as the id, because a user
/// may hold a draft under the same id as a system instance and must never reach it.
/// </summary>
/// <param name="Kind">Whether a user or the system owns the row.</param>
/// <param name="UserKey">The user's key on a user row; null on a system row.</param>
internal readonly record struct DraftOwner(SnapshotOwnerKind Kind, string? UserKey)
{
    /// <summary>The system, which owns instances created from code and has no user key.</summary>
    public static DraftOwner System { get; } = new(SnapshotOwnerKind.System, UserKey: null);

    /// <summary>The user <paramref name="userKey"/> names.</summary>
    public static DraftOwner User(string userKey) => new(SnapshotOwnerKind.User, userKey);
}
