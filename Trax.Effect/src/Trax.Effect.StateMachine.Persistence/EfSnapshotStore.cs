using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.StateMachine;
using SnapshotDraft = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The <see cref="ISnapshotStore"/> over <see cref="IDataContext.SnapshotDrafts"/>: user-scoped reads and
/// writes of a <see cref="SnapshotDraft"/> with the context in a real <c>jsonb</c> column (<c>TEXT</c> on
/// SQLite) and optimistic concurrency via its concurrency token. Only EXPECTED races (an optimistic conflict,
/// or a concurrent create the <paramref name="dialect"/> recognises as a unique violation) are returned as
/// <c>false</c>; any other database error propagates, so it cannot masquerade as a benign conflict.
/// </summary>
/// <param name="db">The data context the table is reached through.</param>
/// <param name="dialect">
/// Recognises a unique violation on the configured provider. Without one, a concurrent create of the same draft
/// throws instead of losing the race.
/// </param>
public sealed class EfSnapshotStore(IDataContext db, ISqlDialect? dialect = null) : ISnapshotStore
{
    /// <inheritdoc/>
    public Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    ) => Read(db.SnapshotDrafts.Where(x => x.Id == id && x.UserKey == userKey), cancellationToken);

    /// <inheritdoc/>
    public Task<StoredSnapshot?> Get(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        Read(
            db.SnapshotDrafts.Where(x =>
                x.Id == id && x.UserKey == userKey && x.Machine == machine
            ),
            cancellationToken
        );

    private static async Task<StoredSnapshot?> Read(
        IQueryable<SnapshotDraft> query,
        CancellationToken cancellationToken
    )
    {
        var record = await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (record is null)
            return null;

        var snapshot = new JsonObject
        {
            ["machine"] = record.Machine,
            ["version"] = record.Version,
            ["state"] = record.State,
            ["context"] = JsonNode.Parse(record.Context),
        };
        return new StoredSnapshot(
            snapshot.ToJsonString(),
            record.ConcurrencyToken,
            record.LastRequestId,
            record.UpdatedAt
        )
        {
            LastRequestTrigger = record.LastRequestTrigger,
            LastRequestFromState = record.LastRequestFromState,
        };
    }

    /// <inheritdoc/>
    public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default) =>
        db
            .SnapshotDrafts.Where(x => x.Id == id && x.UserKey == userKey)
            .ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc/>
    public Task Delete(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        db
            .SnapshotDrafts.Where(x => x.Id == id && x.UserKey == userKey && x.Machine == machine)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Inserts the draft of <paramref name="snapshot"/>'s machine, or overwrites its version, state and context,
    /// with a fresh concurrency token and <c>updated_at</c>. It leaves the last-request columns untouched, and
    /// never touches another machine's draft under the same id. Returns <c>false</c> when the row changed between
    /// this call's read and its write, or a concurrent insert of the same <c>(user_key, machine, id)</c> won; any
    /// other database error propagates.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    public async Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var machine = snapshot.Machine;
        var record = await db.SnapshotDrafts.FirstOrDefaultAsync(
            x => x.Id == id && x.UserKey == userKey && x.Machine == machine,
            cancellationToken
        );
        if (record is null)
            return await Insert(userKey, id, snapshot, cancellationToken);

        Apply(record, snapshot);
        return await Save(record, cancellationToken);
    }

    /// <summary>
    /// Inserts a new draft row in one statement. Returns <c>false</c> when a row with the same
    /// <c>(user_key, machine, id)</c> already exists, which is how a writer that lost the race to create a draft
    /// finds out; any other database error propagates.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    public Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var record = new SnapshotDraft { Id = id, UserKey = userKey };
        Apply(record, snapshot);
        db.SnapshotDrafts.Add(record);
        return Save(record, cancellationToken);
    }

    private static void Apply(SnapshotDraft record, Snapshot snapshot)
    {
        record.Machine = snapshot.Machine;
        record.Version = snapshot.Version;
        record.State = snapshot.State;
        record.Context = snapshot.Context.ToJsonString();
        record.ConcurrencyToken = Guid.NewGuid();
        record.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> Save(SnapshotDraft record, CancellationToken cancellationToken)
    {
        try
        {
            await ((DbContext)db).SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A stale optimistic write: someone else changed the row since it was read.
            return false;
        }
        catch (DbUpdateException ex) when (dialect?.IsUniqueViolation(ex) == true)
        {
            // A concurrent create of the same (user_key, id): the other writer got there first. Any other
            // DbUpdateException (a NOT NULL or check-constraint violation from a real bug) is not swallowed.
            return false;
        }
        finally
        {
            // Stop tracking the row whether or not it was written: the context may be shared with the rest of
            // the request, a failed write left tracked would be retried by its next save, and a written one
            // would be served stale from the identity map to this store's next read.
            ((DbContext)db)
                .Entry(record)
                .State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Conditional update that records <paramref name="requestId"/> alone and clears the stored trigger and
    /// from-state, so a retry of that request is refused as a reused id rather than replayed. The draft service
    /// writes through <see cref="UpdateWithRequest"/> instead. Returns <c>false</c> when the row no longer carries
    /// <paramref name="expectedToken"/> or does not exist.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="expectedToken">The concurrency token read with the draft.</param>
    /// <param name="requestId">The idempotency key to record, or null to clear it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    public Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) =>
        UpdateWithRequest(
            userKey,
            id,
            snapshot,
            expectedToken,
            requestId is null ? null : new AppliedRequest(requestId, null, null),
            cancellationToken
        );

    /// <summary>
    /// One atomic <c>UPDATE ... WHERE concurrency_token = expectedToken</c> on the draft of
    /// <paramref name="snapshot"/>'s machine that bypasses the change tracker. It writes the snapshot, the
    /// request's id, trigger and from-state (all null when <paramref name="request"/> is
    /// null), a fresh token and <c>updated_at</c>. A write that lost the race, or targets a missing row, updates
    /// nothing and returns <c>false</c> rather than throwing.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="expectedToken">The concurrency token read with the draft.</param>
    /// <param name="request">The applied request to record, or null to clear the last-request columns.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    public async Task<bool> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        CancellationToken cancellationToken = default
    )
    {
        // Optimistic update as a single atomic statement that bypasses the change tracker. The token guard is
        // in the WHERE, so a write that lost the race updates 0 rows — no lost update, no exception.
        var machineId = snapshot.Machine;
        var version = snapshot.Version;
        var state = snapshot.State;
        var contextJson = snapshot.Context.ToJsonString();
        var newToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var requestId = request?.RequestId;
        var requestTrigger = request?.Trigger;
        var requestFromState = request?.FromState;

        var rows = await db
            .SnapshotDrafts.Where(x =>
                x.Id == id
                && x.UserKey == userKey
                && x.Machine == machineId
                && x.ConcurrencyToken == expectedToken
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Version, version)
                        .SetProperty(x => x.State, state)
                        .SetProperty(x => x.Context, contextJson)
                        .SetProperty(x => x.ConcurrencyToken, newToken)
                        .SetProperty(x => x.LastRequestId, requestId)
                        .SetProperty(x => x.LastRequestTrigger, requestTrigger)
                        .SetProperty(x => x.LastRequestFromState, requestFromState)
                        .SetProperty(x => x.UpdatedAt, now),
                cancellationToken
            );

        return rows == 1;
    }
}
