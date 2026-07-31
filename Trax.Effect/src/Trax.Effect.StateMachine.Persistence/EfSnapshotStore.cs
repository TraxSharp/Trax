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
            record.LastRequestId
        );
    }

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

    public async Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
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
