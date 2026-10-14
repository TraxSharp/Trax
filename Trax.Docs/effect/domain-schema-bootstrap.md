---
title: EnsureSchemaCreatedAsync
description: "What EnsureSchemaCreatedAsync does to a domain data context's schema on each start: what it creates, what it skips, what it logs and what stops the host."
type: reference
area: data
nav_order: 4
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext/DomainDataContextServiceCollectionExtensions.cs
---

# EnsureSchemaCreatedAsync

`EnsureSchemaCreatedAsync<TContext>()` creates a domain data context's schema and the tables of its model that do not exist yet. Its signature is on [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context#ensureschemacreatedasync).

## Behavior

On a relational provider it creates the context's default schema with `IF NOT EXISTS`, probes which of the model's tables and sequences exist, and runs the create statements for the rest in one transaction, each table with its keys, indexes, foreign keys and seed rows. On the in-memory provider it calls `EnsureCreatedAsync`.

| Start | What runs |
|---|---|
| First start against an empty schema | Every table is created. EF Core logs each statement at `Information` (`Microsoft.EntityFrameworkCore.Database.Command[20101] Executed DbCommand`). |
| Every start after that | Nothing runs and nothing is logged. The existence probe reads no rows and is sent outside EF, so a missing table is not logged as a failed command. |
| A table or sequence added to the model since | It is created; the existing ones are left as they are. |
| Several hosts starting at once against a fresh database | On Postgres the probe and the creates run under a session advisory lock, so the first host creates the tables and the others find them. |

Statements tied to no table or sequence (a Postgres extension or enum the model declares) run only on the context's first creation, which is decided by its own tables, the ones in its default schema. A table it maps from another context's schema does not count, so a context that reads the catalog's `books` is still created in full the first time.

Tables are compared by name only. A column, index or constraint added to a table that already exists is not applied.

## Errors

| Condition | Result |
|---|---|
| The database refuses a statement (a permission failure, a name already taken by another object) | The provider's `DbException` propagates and the transaction rolls back, which stops the host at startup |
| The schema name is not a plain identifier (letters, digits and underscores, starting with a letter or underscore) | `InvalidOperationException` |

## See also

- [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts)
- [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations)
