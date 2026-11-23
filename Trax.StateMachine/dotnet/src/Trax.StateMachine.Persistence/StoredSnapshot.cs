using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A stored draft as read back: its canonical JSON, the concurrency token to write against, the
/// idempotency key of the last applied advance (if any), and when the row was last written (the sliding
/// window the draft-TTL expiry checks against).
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#storedsnapshot">SDK reference</seealso>
public sealed record StoredSnapshot(
    string Json,
    Guid Token,
    string? LastRequestId,
    DateTimeOffset UpdatedAt
)
{
    /// <summary>
    /// The trigger the last applied advance fired, or <see langword="null"/> when none was recorded (no request id, or
    /// a store that does not record it). A request id replays only for the trigger recorded with it.
    /// </summary>
    public string? LastRequestTrigger { get; init; }

    /// <summary>The state the last applied advance fired from, or <see langword="null"/> when none was recorded.</summary>
    public string? LastRequestFromState { get; init; }

    /// <summary>
    /// The correlation token of the train run the draft's current state invoked, or <see langword="null"/> when no run is
    /// live (or the store does not record one). Server-only: it is never part of <see cref="Json"/>. Deleting a
    /// draft that holds one cancels the run first.
    /// </summary>
    public string? InvokeToken { get; init; }

    /// <summary>
    /// The instance's revision: 1 when the row was created and one more for every write since, bumped by the
    /// database. <see langword="null"/> from a store that does not record one. It travels beside the snapshot, never inside
    /// <see cref="Json"/>, and orders the writes of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one, and its revisions start again at 1. <see langword="null"/> from a store that does not record
    /// one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <see langword="null"/> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// The client requests the row recorded most recently, oldest first, at most <see cref="RequestIdRing.Capacity"/>,
    /// each with the result it answered; <see langword="null"/> where none are recorded yet, or the store does not record them.
    /// A retry of one of them is answered with its recorded result. Writes the server makes never change it.
    /// </summary>
    public IReadOnlyList<RecordedRequest>? RecentRequests { get; init; }

    /// <summary>
    /// The effect pending on the row, or <see langword="null"/> when none is, or the store does not record one. While one is
    /// pending, no other write changes the row.
    /// </summary>
    internal PendingEffect? Effect { get; init; }

    /// <summary>
    /// Whether the server put the row in its state, as the row records it: <see langword="null"/> on a row written before it was
    /// recorded, or by host code through the store, or by a store that does not record it.
    /// </summary>
    internal bool? ServerRecord { get; init; }

    /// <summary>
    /// True when another machine's state spawned this instance and still waits on it: the parent removes it, by
    /// leaving that state, and its owner may not discard it. False for an instance nothing spawned, or whose parent
    /// has moved on. Internal: never part of the snapshot.
    /// </summary>
    internal bool ParentWaits { get; init; }

    /// <summary>The last applied request as one value, or <see langword="null"/> when there is no request id.</summary>
    public AppliedRequest? LastRequest =>
        LastRequestId is { } requestId
            ? new AppliedRequest(requestId, LastRequestTrigger, LastRequestFromState)
            : null;
}
