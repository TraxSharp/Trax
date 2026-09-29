using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The Postgres-backed <see cref="ISnapshotStore"/>: user-scoped reads/writes of a
/// <see cref="SnapshotRecord"/> with the context in a real <c>jsonb</c> column and optimistic
/// concurrency via <see cref="SnapshotRecord.ConcurrencyToken"/>. Only EXPECTED races (an optimistic
/// conflict or a concurrent-create unique violation) are caught and returned as <c>false</c>; a genuine
/// constraint violation from a real bug still propagates, so it can't masquerade as a benign conflict.
/// </summary>
public sealed class EfSnapshotStore(SnapshotDbContext db) : ISnapshotStore
{
    /// <inheritdoc/>
    public async Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var record = await db
            .SnapshotDrafts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserKey == userKey, cancellationToken);

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

    /// <summary>
    /// Inserts the draft or overwrites its machine, version, state and context, with a fresh concurrency token
    /// and <c>updated_at</c>. It does not compare tokens and leaves the last-request columns untouched. Returns
    /// <c>false</c> when a tracked write turned stale or a concurrent insert of the same <c>(user_key, id)</c>
    /// won (recognised only as a Postgres unique violation); any other database error propagates.
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
        var record = await db.SnapshotDrafts.FirstOrDefaultAsync(
            x => x.Id == id && x.UserKey == userKey,
            cancellationToken
        );
        if (record is null)
        {
            record = new SnapshotRecord { Id = id, UserKey = userKey };
            db.SnapshotDrafts.Add(record);
        }

        Apply(record, snapshot);
        return await TrySave(cancellationToken);
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
    /// One atomic <c>UPDATE ... WHERE concurrency_token = expectedToken</c> that bypasses the change tracker. It
    /// writes the snapshot, the request's id, trigger and from-state (all null when <paramref name="request"/> is
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
        // Optimistic update as a single atomic statement that bypasses the change tracker (so it can't
        // collide with an entity a prior Upsert tracked on this same context). The token guard is in the
        // WHERE, so a write that lost the race updates 0 rows — no lost update, no exception.
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
                x.Id == id && x.UserKey == userKey && x.ConcurrencyToken == expectedToken
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Machine, machineId)
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

    private static void Apply(SnapshotRecord record, Snapshot snapshot)
    {
        record.Machine = snapshot.Machine;
        record.Version = snapshot.Version;
        record.State = snapshot.State;
        record.Context = snapshot.Context.ToJsonString();
        record.ConcurrencyToken = Guid.NewGuid();
        record.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> TrySave(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A stale optimistic write — someone else changed the row since we read it.
            return false;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent create of the same (user_key, id) — the other writer got there first.
            return false;
        }
        // Any other DbUpdateException (a NOT NULL / check-constraint violation from a real bug) is NOT
        // swallowed — it must surface, not masquerade as a benign "reload and retry" conflict.
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
