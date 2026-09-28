---
layout: default
title: ISqlDialect
parent: Configuration
grand_parent: SDK Reference
nav_order: 16
---

# ISqlDialect

The SQL that differs between database providers, kept in one place so that nothing above the data layer writes provider SQL of its own. `UsePostgres` registers the Postgres dialect and `UseSqlite` the SQLite one; the InMemory provider registers none, because it never runs raw SQL. Resolve it from the container:

```csharp
var dialect = serviceProvider.GetRequiredService<ISqlDialect>();
```

## Signature

```csharp
namespace Trax.Effect.Data.Services.SqlDialect;

public interface ISqlDialect
{
    FormattableString TryAcquireLeaderLock(string lockName);
    string ClaimWorkQueueEntry();
    string LockSubject() => "SELECT {0}";
    string DequeueBackgroundJobs();
    string? EstimateRowCount() => null;
    string LoadGroupFairQueuedJobs();
}
```

Each member returns SQL for the caller to run, with EF-style `{0}` parameter placeholders. The dispatch members are what the scheduler's claim and load run; this page documents the one meant for other callers.

## EstimateRowCount

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

An estimate is for a number shown to a person, such as the total over millions of log rows, where an exact `COUNT(*)` would scan the whole table. It is as stale as the table's last analyze, so never branch on it. When the method returns `null` or the query returns no row, count exactly:

```csharp
using var db = await dataContextFactory.CreateDbContextAsync(ct);

var sql = dialect.EstimateRowCount();
var estimate = sql is null
    ? new List<long>()
    : await ((DbContext)db).Database.SqlQueryRaw<long>(sql, "log").ToListAsync(ct);

var (total, isEstimate) = estimate is [var rows] && rows >= 10_000
    ? (rows, true)
    : (await db.Logs.LongCountAsync(ct), false);
```

## Package

```
dotnet add package Trax.Effect.Data
```
