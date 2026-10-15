using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

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
    /// The trigger the last applied advance fired, or <c>null</c> when none was recorded (no request id, or
    /// a store that does not record it). A request id replays only for the trigger recorded with it.
    /// </summary>
    public string? LastRequestTrigger { get; init; }

    /// <summary>The state the last applied advance fired from, or <c>null</c> when none was recorded.</summary>
    public string? LastRequestFromState { get; init; }

    /// <summary>
    /// The correlation token of the train run the draft's current state invoked, or <c>null</c> when no run is
    /// live (or the store does not record one). Server-only: it is never part of <see cref="Json"/>. Deleting a
    /// draft that holds one cancels the run first.
    /// </summary>
    public string? InvokeToken { get; init; }

    /// <summary>
    /// The instance's revision: 1 when the row was created and one more for every write since, bumped by the
    /// database. <c>null</c> from a store that does not record one. It travels beside the snapshot, never inside
    /// <see cref="Json"/>, and orders the writes of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one, and its revisions start again at 1. <c>null</c> from a store that does not record
    /// one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <c>null</c> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// The client requests the row recorded most recently, oldest first, at most <see cref="RequestIdRing.Capacity"/>,
    /// each with the result it answered; <c>null</c> where none are recorded yet, or the store does not record them.
    /// A retry of one of them is answered with its recorded result. Writes the server makes never change it.
    /// </summary>
    public IReadOnlyList<RecordedRequest>? RecentRequests { get; init; }

    /// <summary>
    /// The effect pending on the row, or <c>null</c> when none is, or the store does not record one. While one is
    /// pending, no other write changes the row.
    /// </summary>
    internal PendingEffect? Effect { get; init; }

    /// <summary>
    /// Whether the server put the row in its state, as the row records it: <c>null</c> on a row written before it was
    /// recorded, or by host code through the store, or by a store that does not record it.
    /// </summary>
    internal bool? ServerRecord { get; init; }

    /// <summary>The last applied request as one value, or <c>null</c> when there is no request id.</summary>
    public AppliedRequest? LastRequest =>
        LastRequestId is { } requestId
            ? new AppliedRequest(requestId, LastRequestTrigger, LastRequestFromState)
            : null;
}

/// <summary>
/// The effect pending on a row: its claimant, the key it runs under, its event, the request that sent it, and when the
/// lease ends.
/// </summary>
/// <param name="Token">The claimant's token, which guards the writes that finish or end the effect.</param>
/// <param name="Key">The key the effect runs under, the same for every run of it.</param>
/// <param name="Trigger">The effect-bound event.</param>
/// <param name="RequestId">The client request that sent it, or null when none was recorded.</param>
/// <param name="LeaseUntil">When the claimant's lease ends, on the database's clock.</param>
internal sealed record PendingEffect(
    Guid Token,
    string? Key,
    string? Trigger,
    string? RequestId,
    DateTimeOffset? LeaseUntil
);

/// <summary>
/// The advance a draft records so a retry can be recognised: the client's idempotency key, the trigger it
/// fired, and the state it fired from. <see cref="Trigger"/> and <see cref="FromState"/> are <c>null</c>
/// only on a row written before they were recorded, and such a request is never replayed.
/// </summary>
public sealed record AppliedRequest(string RequestId, string? Trigger, string? FromState)
{
    /// <summary>
    /// The row's ring of recent client requests with this request recorded as the newest, which the store writes
    /// with it; null leaves the stored ring as it is.
    /// </summary>
    internal IReadOnlyList<RecordedRequest>? Ring { get; init; }
}

/// <summary>
/// Raw, user-scoped persistence of a snapshot. Engine-agnostic: it moves the four snapshot fields
/// (context as jsonb) and enforces optimistic concurrency, but does NOT validate — validation lives in
/// <see cref="SnapshotDraftService{TState,TTrigger}"/>. Every write is total: a concurrency conflict or
/// a unique-key race returns <c>false</c> rather than throwing (genuine infrastructure failures still
/// propagate).
///
/// <para>A draft is keyed by its user, its machine and its id: two machines may give one user a draft under
/// the same id. The writes take the machine from the snapshot, and the draft service reads and deletes through
/// the overloads that name it.</para>
///
/// <para>Every member is a user's: it reads, writes and deletes only drafts a user owns. A system-owned instance
/// (created by <see cref="IMachineInstances.Start{TMachine}"/>) is never reached through this port, even under the
/// same id, so an implementation must not return or touch a row a user does not own.</para>
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotstore">SDK reference</seealso>
public interface ISnapshotStore
{
    /// <summary>
    /// Reads the caller's draft, or <c>null</c> if there is no such draft for that user. A store that keys drafts
    /// by machine returns one of that user's drafts under <paramref name="id"/>, whichever machine it belongs to;
    /// read through <see cref="Get(string, string, Guid, CancellationToken)"/> instead.
    /// </summary>
    Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the caller's draft for <paramref name="machine"/>, or <c>null</c> if that user has no draft of that
    /// machine under <paramref name="id"/>. A store that does not override this reads through
    /// <see cref="Get(string, Guid, CancellationToken)"/> and treats a draft that names another machine as absent.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="machine">The machine id the draft belongs to.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    async Task<StoredSnapshot?> Get(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        // Only a draft that positively names another machine is absent: one whose machine cannot be read is
        // returned, so rehydration refuses it rather than a save silently overwriting it.
        await Get(userKey, id, cancellationToken) is { } stored
        && StoredMachine(stored.Json) is var owner
        && (owner is null || owner == machine)
            ? stored
            : null;

    /// <summary>
    /// Deletes the caller's draft (the expiry / start-over path). Idempotent: deleting a row that is
    /// already gone is a no-op, never a throw. A store that keys drafts by machine deletes every machine's draft
    /// under <paramref name="id"/>; delete through <see cref="Delete(string, string, Guid, CancellationToken)"/>
    /// instead.
    /// </summary>
    Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the caller's draft for <paramref name="machine"/>, leaving any other machine's draft under the same
    /// id. Idempotent. A store that does not override this deletes through
    /// <see cref="Delete(string, Guid, CancellationToken)"/> only when the draft there is this machine's.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="machine">The machine id the draft belongs to.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    async Task Delete(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (await Get(userKey, machine, id, cancellationToken) is not null)
            await Delete(userKey, id, cancellationToken);
    }

    private static string? StoredMachine(string json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json)?["machine"]?.GetValue<string>();
        }
        catch (Exception ex)
            when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

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
    /// Creates the caller's draft, and only creates it: returns <c>false</c> when a draft already exists under
    /// <paramref name="id"/>, which is how a writer that read no draft finds out one was created since. The draft
    /// service creates every draft through this, so a save never overwrites a draft it did not read. A store that
    /// does not override it checks <see cref="Get(string, Guid, CancellationToken)"/> and then writes through
    /// <see cref="Upsert"/>: it refuses while that user has any draft under the id, whichever machine it belongs
    /// to, but the check and the write are two steps, so a store that can make them one should.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    async Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) =>
        await Get(userKey, id, cancellationToken) is null
        && await Upsert(userKey, id, snapshot, cancellationToken);

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

/// <summary>The row a write left, as the writer knows it without reading the row back.</summary>
/// <param name="RowId">The row's surrogate key, which names the incarnation it wrote.</param>
/// <param name="Revision">The revision the write gave the row.</param>
internal readonly record struct WrittenRow(long RowId, long Revision)
{
    /// <summary>The due time the write left on the row: when its state's timer fires, or null when it has none.</summary>
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>
/// An <see cref="ISnapshotStore"/> whose blind writes report the row they left, so the draft service can answer a
/// save with its revision without a second read, which another write could already have overtaken. Each returns
/// null where the <see cref="ISnapshotStore"/> member it mirrors returns false.
/// </summary>
internal interface IRowReportingSnapshotStore
{
    /// <summary><see cref="ISnapshotStore.Upsert"/>, reporting the row it wrote.</summary>
    Task<WrittenRow?> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    );

    /// <summary><see cref="ISnapshotStore.Insert"/>, reporting the row it created.</summary>
    Task<WrittenRow?> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// <see cref="ISnapshotStore.UpdateWithRequest"/> guarded by the token <paramref name="read"/> was read with,
    /// reporting the row it left: <paramref name="read"/>'s next revision, and the due time the write gave it.
    /// </summary>
    Task<WrittenRow?> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        StoredSnapshot read,
        AppliedRequest? request,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// A store that deletes a user's draft only while it is still the row and revision a caller read, and runs the
/// caller's follow-up writes in the same transaction. The draft service discards through it, so a draft written
/// between its check and its delete is kept, and the claims it releases go with the row or not at all.
/// </summary>
internal interface IConditionalDraftDelete
{
    /// <summary>
    /// Deletes <paramref name="userKey"/>'s draft of <paramref name="machine"/> under <paramref name="id"/> only while
    /// it is the row <paramref name="read"/> names (<see cref="StoredSnapshot.RowId"/>), at the revision and invoke
    /// token it was read with, then runs <paramref name="afterDelete"/> before committing. False, with nothing written
    /// and <paramref name="afterDelete"/> not run, when the row is gone or has changed since.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="machine">The machine id the draft belongs to.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="read">The row as the caller read it; it must carry its row id and revision.</param>
    /// <param name="afterDelete">The writes that commit with the delete.</param>
    /// <remarks>Every write runs on a token nothing cancels: a delete that has started finishes.</remarks>
    Task<bool> DeleteAsRead(
        string userKey,
        string machine,
        Guid id,
        StoredSnapshot read,
        Func<Task> afterDelete
    );
}

/// <summary>
/// The authenticated user behind a request. The draft operations read <see cref="CurrentUserKey"/> to
/// scope every draft to its owner, so the draft id is NOT a bearer capability. In an HTTP host this is
/// backed by the request's principal (e.g. a Trax principal claim); in unit tests it is a fake.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotprincipal">SDK reference</seealso>
public interface ISnapshotPrincipal
{
    /// <summary>
    /// The current user's key, or <c>null</c> if the request is unauthenticated. An empty or whitespace key is
    /// treated as unauthenticated too.
    /// </summary>
    string? CurrentUserKey { get; }
}

/// <summary>
/// The single irreversible side effect bound to a consequential transition (send a letter, charge a
/// card, provision a resource). It returns a receipt (a downstream id) recorded in the snapshot. Run
/// through <see cref="IdempotentEffect"/> so it fires exactly once per intent. The receipt must be non-empty:
/// a null or empty one is treated as a failed delivery, and the send reports <c>DELIVERY_FAILED</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/i-snapshot-effect">SDK reference</seealso>
public interface ISnapshotEffect
{
    /// <summary>
    /// Performs the side effect for <paramref name="snapshot"/> (the draft as loaded, in the effect's from-state)
    /// and returns its receipt, which is written into the terminal snapshot's context. Throw if the effect did not
    /// happen: the draft is left as it was and a retry runs it again. An <see cref="OperationCanceledException"/> (an
    /// outbound call's timeout included) means the outcome is unknown instead: the effect stays pending on the draft
    /// until its lease passes, and is then run again under the same key, by the next send or by the reconciler.
    /// </summary>
    /// <param name="snapshot">The draft the effect acts on.</param>
    /// <param name="cancellationToken">
    /// The effect runner passes <see cref="CancellationToken.None"/>: a request that goes away once the effect has
    /// started does not stop it. Bound a slow downstream call with its own timeout, never after the irreversible
    /// step.
    /// </param>
    /// <returns>A non-empty receipt, typically the downstream system's id for what was done.</returns>
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the side effect as <see cref="Run(Snapshot, CancellationToken)"/> does, given the effect's key: minted
    /// when a send marks the draft, and the same for every run of that send, a run again after its claimant stopped
    /// without recording a result included; the next send of the event, after a reset for instance, has a new one. Pass
    /// it to the system the effect calls as its idempotency key, so a repeat is recognised there. The default ignores
    /// the key and calls <see cref="Run(Snapshot, CancellationToken)"/>.
    /// </summary>
    /// <param name="snapshot">The draft the effect acts on.</param>
    /// <param name="effectKey">The effect's key, stable across every run of one intent.</param>
    /// <param name="cancellationToken">As for <see cref="Run(Snapshot, CancellationToken)"/>.</param>
    /// <returns>A non-empty receipt, typically the downstream system's id for what was done.</returns>
    Task<string> Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken) =>
        Run(snapshot, cancellationToken);
}
