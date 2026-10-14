---
title: DatabaseMigrator
description: "Reference for DatabaseMigrator, which applies Trax's embedded Postgres schema migrations: Migrate, its advisory lock and lock timeout, and the DbUp engine."
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 12
package: Trax.Effect.Data.Postgres
namespace: Trax.Effect.Data.Postgres.Utils
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Utils/DatabaseMigrator.cs
---

# DatabaseMigrator

Applies the Postgres migration scripts embedded in `Trax.Effect.Data.Postgres`, journaled by DbUp in
`trax.migrations`. [UsePostgres](/docs/sdk-reference/configuration/use-postgres) calls `Migrate` during service
registration unless [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) was called. The SQLite
provider has its own, described on [UseSqlite](/docs/sdk-reference/configuration/use-sqlite#databasemigrator).

## Signature

```csharp fragment
namespace Trax.Effect.Data.Postgres.Utils;

public static class DatabaseMigrator
{
    public static Task Migrate(string connectionString);
    public static UpgradeEngine CreateEngineWithEmbeddedScripts(string connectionString);
}
```

## Members

| Member | Description |
|--------|-------------|
| `Migrate(connectionString)` | Brings the database to the latest schema version. Throws the failing script's exception when a migration fails. |
| `CreateEngineWithEmbeddedScripts(connectionString)` | A DbUp `UpgradeEngine` over the embedded scripts, journaling to `trax.migrations` and logging to trace output. It takes no lock and does not pin the time zone. |

## Behavior

- `Migrate` takes a session advisory lock (a two-key lock, apart from the single-key space the scheduler's leader
  lock uses) and holds it until every script has run, so hosts starting together migrate one after another. The
  wait has no timeout.
- Inside the lock it creates the `trax` schema, drops any `INVALID` index a script builds that an interrupted
  `CREATE INDEX CONCURRENTLY` left behind, and applies the pending scripts. An invalid index no script builds is
  left alone.
- Scripts run without a transaction, one statement at a time. A script that stops partway keeps the statements
  before the failure and is not journaled, so it runs again from the top at the next start. Every script from 046 on
  can be run again.
- Scripts run with a `lock_timeout` of five seconds. When a script gives up on a lock (`55P03`), or a unique index a
  script builds meets a duplicate (`23505` naming that index), the pending scripts run again, up to ten times, and
  then the error is thrown.
- Every connection it opens is unpooled and has its time zone pinned to UTC.

## Example

```csharp compile
using Trax.Effect.Data.Postgres.Utils;

await DatabaseMigrator.Migrate(connectionString);
```

## See also

- [Run migrations outside startup](/docs/effect/run-migrations-outside-startup)
- [Database migrations](/docs/reference/schema-migrations), what each migration changes
- [Writing Migrations](/docs/contributing/writing-migrations)
