using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Raw, user-scoped persistence of a snapshot. Engine-agnostic: it moves the four snapshot fields
/// (context as jsonb) and enforces optimistic concurrency, but does NOT validate — validation lives in
/// <see cref="SnapshotDraftService{TState,TTrigger}"/>. Every write is total: a concurrency conflict or
/// a unique-key race returns <see langword="false"/> rather than throwing (genuine infrastructure failures still
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
    /// Reads the caller's draft, or <see langword="null"/> if there is no such draft for that user. A store that keys drafts
    /// by machine returns one of that user's drafts under <paramref name="id"/>, whichever machine it belongs to;
    /// read through <see cref="Get(string, string, Guid, CancellationToken)"/> instead.
    /// </summary>
    Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the caller's draft for <paramref name="machine"/>, or <see langword="null"/> if that user has no draft of that
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
        await Get(userKey, id, cancellationToken).ConfigureAwait(false) is { } stored
        && StoredMachine(stored.Json) is var owner
        && (owner is null || string.Equals(owner, machine, StringComparison.Ordinal))
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
        if (await Get(userKey, machine, id, cancellationToken).ConfigureAwait(false) is not null)
            await Delete(userKey, id, cancellationToken).ConfigureAwait(false);
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
    /// Insert-or-update (the autosave path). Returns <see langword="false"/> on a concurrent-write conflict
    /// (the draft changed elsewhere), <see langword="true"/> otherwise.
    /// </summary>
    Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates the caller's draft, and only creates it: returns <see langword="false"/> when a draft already exists under
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
        await Get(userKey, id, cancellationToken).ConfigureAwait(false) is null
        && await Upsert(userKey, id, snapshot, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Conditional update used by the authoritative path: writes only if the row still carries
    /// <paramref name="expectedToken"/>, and records <paramref name="requestId"/> as the last applied
    /// idempotency key. Returns <see langword="false"/> if the row changed since it was read.
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
    /// or clears it when <paramref name="request"/> is <see langword="null"/>. The draft service writes through this,
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
