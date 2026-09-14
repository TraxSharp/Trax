---
layout: default
title: Writing Migrations
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
scripts are journaled and never re-run. The Postgres migrator journals to `trax.migrations`;
the Sqlite one sets no journal and so lands on DbUp's default `SchemaVersions` table. The
runner scans exactly one assembly, the provider's own, so there is no cross-assembly
discovery.

The Postgres and Sqlite sets are numbered independently and each must be gapless from 001.
A table that must work on both needs a file in both.

## Adding a table

1. Add the model and its persistent mapping, per [Project Layout](/docs/reference/project-layout).
2. Add `NNN_<name>.sql` to **both** provider `Migrations/` folders. The DDL column names must
   match the EF `[Column(...)]` names exactly, because the stores query by those names and
   nothing reconciles the two.
3. Write a test that builds the table from the shipped migration and round-trips through the
   real store. That is the model-versus-DDL drift guard, and `EnsureCreated` cannot provide
   it.

## Provider dialects

| | Postgres | Sqlite |
|---|---|---|
| Schema | tables live in `trax`, DDL is `trax.`-qualified | no schemas, tables unqualified |
| Types | `jsonb`, `uuid`, `timestamptz` | `TEXT`, `INTEGER` |
| Style | mixed: lowercase and uppercase keywords side by side, and 5 of the 12 `create table` statements have no `IF NOT EXISTS` | uniform: all 12 are uppercase `CREATE TABLE IF NOT EXISTS` |

The Postgres style is not a convention to match, it is drift. Write new Postgres scripts the
way the Sqlite set is already written, with `if not exists` and one case throughout.

A `DbContext` used with both strips the `trax` schema and remaps `jsonb` to `TEXT` when
running on Sqlite, branching on `Database.ProviderName`.

## Feature packages

A higher-level feature package does not carry its own migrations. Its DDL ships in the two
core provider sets above, because the runner scans only the provider assembly: a
`.sql` embedded anywhere else is never discovered and never runs. This does not invert the
dependency, since the SQL is text and the provider references nothing from the feature.

## Integration test databases

A fixture that creates or drops a throwaway database must connect to the always-present
`postgres` maintenance database, never the app database. CI's `POSTGRES_DB` differs per repo,
so a fixture assuming a specific app database fails with `3D000 database ... does not exist`.
