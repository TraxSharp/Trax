using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// A stored draft as read back: its canonical JSON, the concurrency token to write against, and the
/// idempotency key of the last applied advance (if any).
/// </summary>
public sealed record StoredSnapshot(string Json, Guid Token, string? LastRequestId);

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
/// through <see cref="IdempotentEffect"/> so it fires exactly once per intent.
/// </summary>
public interface IEffect
{
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);
}
