---
layout: default
title: Writing Migrations
description: "How to write a migration for Trax's own tables: embedded SQL scripts that rerun safely, Postgres and SQLite dialects, feature packages and test databases."
parent: Reference
nav_order: 13
---

# Writing Migrations

Every table in the `trax` schema is created by a migration that applies automatically at
startup. Do not hand-create one, and do not reach for `EnsureCreated` to produce it. The rule
is about Trax's own tables: a consumer's domain tables are bootstrapped by
`EnsureSchemaCreatedAsync`, which is a different mechanism on purpose, and
`Trax.Effect/docs/adr/0002` records why.

## How it works

Migrations are plain SQL files:

```
Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/<NNN>_<name>.sql
Trax.Effect/src/Trax.Effect.Data.Sqlite/Migrations/<NNN>_<name>.sql
```

Those are the only two sets. `Trax.Effect.Data.InMemory` has no `Migrations/` folder.

They are embedded by a csproj glob, so a new file in the folder is picked up with no
per-file edit. `DatabaseMigrator` (DbUp) applies every pending script from inside the
provider registration, synchronously, when `UsePostgres(...)` or `UseSqlite(...)` runs.
`SkipMigrations()` opts out for an externally managed schema, though it is declared only in
the Postgres package.

Scripts run in ordinal filename order, which is what the `NNN_` prefix is for. Applied
scripts are journaled and never re-run. On Postgres the migrator holds a session advisory lock
for the whole run, so hosts that start together against one database migrate one after another,
and every session it opens has its time zone pinned to UTC. The Postgres migrator journals to `trax.migrations`;
the Sqlite one sets no journal and so lands on DbUp's default `SchemaVersions` table. The
runner scans exactly one assembly, the provider's own, so there is no cross-assembly
discovery.

The Postgres and Sqlite sets are numbered independently and each must be gapless from 001.
A table that must work on both needs a file in both.

## Adding a table

1. Add the model and its persistent mapping, per [Project Layout](/docs/reference/project-layout),
   and its `DbSet` on both `DataContext` and `IDataContext`. A model keyed by something other than
   a `long` id does not implement `IModel`, so `DataContext.OnModelCreating` maps it by name, as it
   does `PersistentPersistedOperation` and `PersistentRunnerNonce`. A new member on `IDataContext`
   needs a default, or package validation refuses it as a break.
2. Add `NNN_<name>.sql` to **both** provider `Migrations/` folders. The DDL column names must
   match the EF `[Column(...)]` names exactly, because the stores query by those names and
   nothing reconciles the two.
3. Write a test that builds the table from the shipped migration and round-trips through the
   real store. `EnsureCreated` cannot provide it. For the data context's own tables,
   `EveryTableIsModelledTests` and its Sqlite twin already check that every migrated table is
   mapped and every mapped column exists; a table the context should not map goes in their
   exceptions list with its reason.

## Every Postgres script can run again

The Postgres migrator runs a script without a transaction, one statement at a time. A script that
stops partway (a failed statement, a killed process) keeps the statements before the failure and is
not journaled, so at the next start it runs again from its first statement. From 046 on, every
statement has to survive that:

| Statement | Written as |
|---|---|
| a table, index, schema or sequence | `CREATE ... IF NOT EXISTS` |
| a drop | `DROP ... IF EXISTS` |
| a column | `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` |
| an enum value | `ALTER TYPE ... ADD VALUE IF NOT EXISTS` |
| a default or `NOT NULL` | `ALTER COLUMN ... SET DEFAULT` / `SET NOT NULL`, which repeat harmlessly |
| a type change, a new constraint, a rename, a new enum type | inside a `DO $$ ... $$` block that checks the catalog first |
| a row | `INSERT ... ON CONFLICT`, or an `UPDATE`/`DELETE` whose `WHERE` excludes rows already done |

An index on `metadata`, `log` or `work_queue` is built `CREATE INDEX CONCURRENTLY IF NOT EXISTS`, so
enqueue, dispatch and run writes carry on while it builds; a plain build blocks them for as long as
it takes. That works because the script is not in a transaction. A concurrent build that fails leaves
an `INVALID` index behind, which `IF NOT EXISTS` would skip forever, so before it runs the pending
scripts the migrator drops every invalid index in the `trax` schema whose name a shipped script
creates. It reads those names from the scripts, so an index a new script builds is covered with no
list to update. Any other invalid index, such as your own or the `_ccnew` copy of a
`REINDEX CONCURRENTLY` in progress, is left alone.

The scripts run with a five-second `lock_timeout`. An `ALTER TABLE` waiting behind a transaction on
another instance would otherwise hold every later write to that table behind itself, and
`UsePostgres` migrates at startup, so that is a rolling deploy stalling enqueue and dispatch on every
host. When a script gives up (`55P03`), the migrator drops any index that left invalid and runs the
pending scripts again, up to ten times, then fails startup. That is why a script has to survive
running again even when nothing crashed. `CREATE INDEX CONCURRENTLY`'s wait for older transactions is
a lock wait too, so an index build behind a transaction that stays open for about a minute fails
startup rather than waiting it out.

A type change that Postgres can make without rewriting the table should be written so it does.
`timestamp` to `timestamptz` is one, but only in a UTC session and with no `USING`; migration 049 sets
the zone for its own transaction with `PERFORM set_config('TimeZone', 'UTC', true)` inside each `DO`
block. A rewrite holds `ACCESS EXCLUSIVE` for as long as it copies the table.

`PostgresMigrationRerunTests` reads each script from 046 on and refuses a statement that is not in
one of these forms, and `PostgresMigrationTests` runs every one of them again over a migrated
database. `Trax.Effect/docs/adr/0014` records why the migrator works this way.

SQLite runs each script in its own transaction, so a script there lands whole or not at all.

A timestamp column is `timestamptz` defaulting to `now()`, never `timestamp without time zone` and
never `now() AT TIME ZONE 'utc'`: a plain timestamp stores the writing session's wall-clock time
(`Trax.Effect/docs/adr/0015`).

## Provider dialects

| | Postgres | Sqlite |
|---|---|---|
| Schema | tables live in `trax`, DDL is `trax.`-qualified | no schemas, tables unqualified |
| Types | `jsonb`, `uuid`, `timestamptz`, `bigint` | `TEXT`, `INTEGER`, and `REAL` for a fractional number |
| Style | mixed: lowercase and uppercase keywords side by side, and 5 of the 13 `create table` statements have no `IF NOT EXISTS` | uniform: uppercase throughout, and 13 of the 14 `CREATE TABLE` statements are `IF NOT EXISTS` (the exception is the transient table `015_snapshot_draft_machine_key.sql` rebuilds `snapshot_draft` through) |

The Postgres style is not a convention to match, it is drift. Write new Postgres scripts the
way the Sqlite set is already written, with `if not exists` and one case throughout.

A `DbContext` used with both strips the `trax` schema and remaps `jsonb` to `TEXT` when
running on Sqlite, branching on `Database.ProviderName`.

## Feature packages

A higher-level feature package does not carry its own migrations. Its DDL ships in the two
core provider sets above, because the runner scans only the provider assembly: a
`.sql` embedded anywhere else is never discovered and never runs. This does not invert the
dependency, since the SQL is text and the provider references nothing from the feature.

The table's model ships with it. A feature package reaches its table through `IDataContext` and
the model, with LINQ, `ExecuteUpdate` and `ExecuteDelete`, not with SQL of its own or a
`DbContext` of its own. Persisted operations in Trax.Api and the runner nonce store in
Trax.Scheduler both work this way. When the providers differ in a way the feature must handle,
such as how a key conflict is reported, the difference goes in `ISqlDialect` in Trax.Effect:
`IsUniqueViolation` is how the nonce store tells a repeated nonce from any other failed save, and how
the state-machine stores tell a lost race for a draft or an effect claim from a real failure.

## Integration test databases

A fixture that creates or drops a throwaway database must connect to the always-present
`postgres` maintenance database, never the app database. CI's `POSTGRES_DB` differs per folder,
so a fixture assuming a specific app database fails with `3D000 database ... does not exist`.
