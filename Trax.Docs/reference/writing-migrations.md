---
layout: default
title: Writing Migrations
parent: Reference
nav_order: 13
---

# Writing Migrations

Every Trax table is created by a migration that applies automatically at startup. Do not
hand-create tables, and do not use `EnsureCreated` for a table a host relies on.

## How it works

Migrations are plain SQL files:

```
Trax.Effect.Data.<Provider>/Migrations/<NNN>_<name>.sql
```

They are embedded by a csproj glob, so a new file in the folder is picked up with no
per-file edit. `DatabaseMigrator` (DbUp) applies every pending script from inside the
provider registration, synchronously, when `UsePostgres(...)` or `UseSqlite(...)` runs.
`SkipMigrations()` opts out for an externally managed schema, though it is declared only in
the Postgres package.

Scripts run in ordinal filename order, which is what the `NNN_` prefix is for. Applied
scripts are journaled (Postgres: `trax.migrations`) and never re-run. The runner scans
exactly one assembly, the provider's own, so there is no cross-assembly discovery.

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
| Style | lowercase keywords, `create table if not exists` | same |

A `DbContext` used with both strips the `trax` schema and remaps `jsonb` to `TEXT` when
running on Sqlite, branching on `Database.ProviderName`.

## Feature packages

A higher-level feature package does not carry its own migrations. Its DDL ships in the core
`Trax.Effect.Data.<Provider>` set, because the runner scans only the provider assembly: a
`.sql` embedded anywhere else is never discovered and never runs. This does not invert the
dependency, since the SQL is text and the provider references nothing from the feature.

## Integration test databases

A fixture that creates or drops a throwaway database must connect to the always-present
`postgres` maintenance database, never the app database. CI's `POSTGRES_DB` differs per repo,
so a fixture assuming a specific app database fails with `3D000 database ... does not exist`.
