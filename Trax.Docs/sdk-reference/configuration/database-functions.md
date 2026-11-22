---
title: DatabaseFunctions
description: Reference for DatabaseFunctions, the database's clock and its lower() as methods a LINQ query over a Trax data context translates to SQL.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 45
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Utils
source:
  - Trax.Effect/src/Trax.Effect.Data/Utils/DatabaseFunctions.cs
---

# DatabaseFunctions

Methods a LINQ query hands to the database instead of evaluating in the process: the database's
clock and its lowering of text. Trax uses them where a time is compared with one another process
wrote (the scheduler's due check and its dispatch and success stamps) and in its text filters.

## Signature

```csharp fragment
namespace Trax.Effect.Data.Utils;

public static class DatabaseFunctions
{
    public static DateTime UtcNow();
    public static string? Lower(string? value);
}
```

## Members

| Member | PostgreSQL | SQLite | In memory |
|--------|------------|--------|-----------|
| `UtcNow()` | `now()` | `rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', 'now'), '0'), '.')` | the system clock |
| `Lower(value)` | `lower(value)` | `lower(value)` | `value.ToLowerInvariant()` |

## Behavior

The functions are mapped on the contexts `UsePostgres` and `UseSqlite` register, and on every
[DomainDataContext](/docs/sdk-reference/configuration/domain-data-context). A plain `DbContext`
of your own does not map them, and a query over one fails to translate them.

`UtcNow()` is the database's time, so every process sharing the database reads the same clock
however far apart their own clocks are. On PostgreSQL inside a transaction, `now()` is the
transaction's start. The SQL is the same EF Core writes for `DateTime.UtcNow`, which the analyzer
profile bans as a read of this process's clock. `now()` is left unqualified, so it resolves through
the connection's search path.

`Lower(value)` is SQL's `lower()`, the expression the trigram indexes are built over; pair it with
[LikePattern](/docs/sdk-reference/configuration/like-pattern) for a case-insensitive search.

The in-memory provider has no server and runs each method's body. Called outside a query, a
method runs its body too, which reads the process, not the database.

## Example

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Utils;
using Trax.Effect.Models.WorkQueue;

public class DueWork(IDataContext db)
{
    public Task<List<WorkQueue>> Due(CancellationToken cancellationToken) =>
        db.WorkQueues
            .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DatabaseFunctions.UtcNow())
            .ToListAsync(cancellationToken);
}
```

## See also

- [LikePattern](/docs/sdk-reference/configuration/like-pattern)
- [IDataContext](/docs/sdk-reference/configuration/i-data-context)
