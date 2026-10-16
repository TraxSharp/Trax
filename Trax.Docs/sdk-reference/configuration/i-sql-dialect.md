---
title: ISqlDialect
description: Reference for ISqlDialect, the SQL that differs between Postgres and SQLite, transient errors, and whether the database takes one writer.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 42
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Services.SqlDialect
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/SqlDialect/ISqlDialect.cs
---

# ISqlDialect

The SQL that differs between database providers, kept in one place so that nothing above the data layer writes provider SQL of its own. `UsePostgres` registers the Postgres dialect and `UseSqlite` the SQLite one; the InMemory provider registers none, because it never runs raw SQL. It is resolved from the container.

## Signature

```csharp fragment
namespace Trax.Effect.Data.Services.SqlDialect;

public interface ISqlDialect
{
    FormattableString TryAcquireLeaderLock(string lockName);
    bool HasSingleWriter => false;
    string ClaimWorkQueueEntry();
    string LockSubject() => "SELECT {0}";
    string DequeueBackgroundJobs();
    string? EstimateRowCount() => null;
    bool IsUniqueViolation(DbUpdateException exception) => false;
    bool IsTransient(Exception exception) => false;
    string LoadGroupFairQueuedJobs();
}
```

## Behavior

The SQL members return SQL for the caller to run, with EF-style `{0}` parameter placeholders. The dispatch members are what the scheduler's claim and load run. Both pass over an entry whose manifest is disabled unless the entry is an explicit trigger (`WorkQueue.IsExplicitTrigger`); see [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher#loadqueuedjobsjunction). `IsUniqueViolation` and `IsTransient` classify a failure instead of returning SQL. This page documents the members meant for other callers.

### HasSingleWriter

Whether the database takes one writer at a time across every connection. A transaction held open on such a database blocks every other connection's write until it ends, and a waiting writer fails after the busy timeout.

| Provider | Value |
|----------|-------|
| Postgres | `false` |
| SQLite | `true`: writes are serialized at the file |
| A custom implementation that does not override it | `false` (the interface default) |

The scheduler reads it twice. Its manifest manager runs with no outer transaction and no leader lock, since one process is the only writer anyway, and the [local worker pool](/docs/sdk-reference/scheduler-api/configure-local-workers) starts one worker unless `WorkerCount` is set.

### EstimateRowCount

Returns SQL that reads the database's own estimate of how many rows a Trax table holds, or `null` when the provider keeps no estimate.

| Parameter | Description |
|-----------|-------------|
| `{0}` | The table's unqualified name, for example `log` or `metadata` |

The query returns one `bigint` column aliased as `"Value"`, and **no row** when there is no estimate to give.

| Provider | Returns |
|----------|---------|
| Postgres | SQL reading `pg_class.reltuples` for the table in the `trax` schema. No row when the table does not exist, or has never been analyzed (Postgres 14+ records `-1` for that, meaning unknown) |
| SQLite | `null` |
| A custom implementation that does not override it | `null` (the interface default) |

An estimate is for a number shown to a person, such as the total over millions of log rows, where an exact `COUNT(*)` would scan the whole table. It is as stale as the table's last analyze, so it is for display. When the method returns `null` or the query returns no row, the caller counts exactly.


### IsTransient

Whether a failure may succeed if the same work is tried again. The exception and everything it wraps are examined, so a `DbUpdateException`, or the `InvalidOperationException` EF's retry strategy throws around the last failure, is classified by the database error inside it.

| Provider | Transient |
|----------|-----------|
| Postgres | Any `NpgsqlException` that Npgsql reports as transient (`IsTransient`): a connection that broke or could not be opened, a timeout, and the server errors Npgsql lists, among them serialization failure (`40001`), deadlock (`40P01`), too many connections (`53300`) and a server not yet accepting connections (`57P03`). Also a `TimeoutException` |
| SQLite | A `SqliteException` whose primary code is `SQLITE_BUSY` or `SQLITE_LOCKED`, which another connection's write causes and which clears when it commits. Also a `TimeoutException` |
| A custom implementation that does not override it | Nothing (the interface default) |

Everything else is not transient: a constraint violation, a missing table or a syntax error fails the same way on every try. Matching the provider's exception types by name instead would treat every database error as transient.


## Examples

A row count shown to a person, estimated over large tables:

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;

public class LogTotal(IDataContextProviderFactory dataContextFactory, ISqlDialect dialect)
{
    public async Task<(long Total, bool IsEstimate)> GetAsync(CancellationToken ct)
    {
        await using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var sql = dialect.EstimateRowCount();
        var estimate = sql is null
            ? new List<long>()
            : await ((DbContext)db).Database.SqlQueryRaw<long>(sql, "log").ToListAsync(ct);

        return estimate is [var rows] && rows >= 10_000
            ? (rows, true)
            : (await db.Logs.LongCountAsync(ct), false);
    }
}
```

A retry of transient failures only:

```csharp compile
using Trax.Effect.Data.Services.SqlDialect;

public class Seeder(ISqlDialect dialect)
{
    public async Task SeedWithRetryAsync(Func<CancellationToken, Task> seed, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await seed(ct);
                return;
            }
            catch (Exception ex) when (attempt < 5 && dialect.IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
    }
}
```

## See also

- [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite)
