using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// A stored draft as read back: its canonical JSON, the concurrency token to write against, the
/// idempotency key of the last applied advance (if any), and when the row was last written (the sliding
/// window the draft-TTL expiry checks against).
/// </summary>
public sealed record StoredSnapshot(
    string Json,
    Guid Token,
    string? LastRequestId,
    DateTimeOffset UpdatedAt
)
{
    /// <summary>
    /// The trigger the last applied advance fired, or <c>null</c> when none was recorded (no request id, or
    /// a store that does not record it). A request id replays only for the trigger recorded with it.
    /// </summary>
    public string? LastRequestTrigger { get; init; }

    /// <summary>The state the last applied advance fired from, or <c>null</c> when none was recorded.</summary>
    public string? LastRequestFromState { get; init; }

    /// <summary>The last applied request as one value, or <c>null</c> when there is no request id.</summary>
    public AppliedRequest? LastRequest =>
        LastRequestId is { } requestId
            ? new AppliedRequest(requestId, LastRequestTrigger, LastRequestFromState)
            : null;
}

/// <summary>
/// The advance a draft records so a retry can be recognised: the client's idempotency key, the trigger it
/// fired, and the state it fired from. <see cref="Trigger"/> and <see cref="FromState"/> are <c>null</c>
/// only on a row written before they were recorded, and such a request is never replayed.
/// </summary>
public sealed record AppliedRequest(string RequestId, string? Trigger, string? FromState);

/// <summary>
/// Raw, user-scoped persistence of a snapshot. Engine-agnostic: it moves the four snapshot fields
/// (context as jsonb) and enforces optimistic concurrency, but does NOT validate — validation lives in
/// <see cref="SnapshotDraftService{TState,TTrigger}"/>. Every write is total: a concurrency conflict or
/// a unique-key race returns <c>false</c> rather than throwing (genuine infrastructure failures still
/// propagate).
/// </summary>
public interface ISnapshotStore
{
    /// <summary>Reads the caller's draft, or <c>null</c> if there is no such draft for that user.</summary>
    Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes the caller's draft (the expiry / start-over path). Idempotent: deleting a row that is
    /// already gone is a no-op, never a throw.
    /// </summary>
    Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert-or-update (the autosave path). Returns <c>false</c> on a concurrent-write conflict
    /// (the draft changed elsewhere), <c>true</c> otherwise.
    /// </summary>
    Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Conditional update used by the authoritative path: writes only if the row still carries
    /// <paramref name="expectedToken"/>, and records <paramref name="requestId"/> as the last applied
    /// idempotency key. Returns <c>false</c> if the row changed since it was read.
    /// </summary>
    Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// <see cref="Update"/> that records the whole <paramref name="request"/> (id, trigger and from-state),
    /// or clears it when <paramref name="request"/> is <c>null</c>. The draft service writes through this,
    /// so a retry replays only the request it repeats. A store that does not override it records the id
    /// alone, and every retry against it is then refused as a reused id rather than replayed.
    /// </summary>
    Task<bool> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        CancellationToken cancellationToken = default
    ) => Update(userKey, id, snapshot, expectedToken, request?.RequestId, cancellationToken);
}

/// <summary>
/// The authenticated user behind a request. The draft operations read <see cref="CurrentUserKey"/> to
/// scope every draft to its owner, so the draft id is NOT a bearer capability. In an HTTP host this is
/// backed by the request's principal (e.g. a Trax principal claim); in unit tests it is a fake.
/// </summary>
public interface ISnapshotPrincipal
{
    /// <summary>The current user's key, or <c>null</c> if the request is unauthenticated.</summary>
    string? CurrentUserKey { get; }
}

/// <summary>
/// The single irreversible side effect bound to a consequential transition (send a letter, charge a
/// card, provision a resource). It returns a receipt (a downstream id) recorded in the snapshot. Run
/// through <see cref="IdempotentEffect"/> so it fires exactly once per intent. The receipt must be non-empty:
/// a null or empty one is treated as a failed delivery, and the send reports <c>delivery-failed</c>.
/// </summary>
public interface ISnapshotEffect
{
    /// <summary>
    /// Performs the side effect for <paramref name="snapshot"/> (the draft as loaded, in the effect's from-state)
    /// and returns its receipt, which is written into the terminal snapshot's context. Throw if the effect did not
    /// happen: the claim is released and a retry runs it again.
    /// </summary>
    /// <param name="snapshot">The draft the effect acts on.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A non-empty receipt, typically the downstream system's id for what was done.</returns>
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);
}
