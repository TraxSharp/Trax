---
authors: [Theauxm]
repos: [effect, scheduler, api]
areas: [data-model, migrations, providers]
status: accepted
---

# A feature table ships with its model in Trax.Effect, and the feature reaches it through IDataContext

A table a feature package needs ships in Trax.Effect as a whole model, not as DDL alone: the
migrations ([0009](./0009-feature-tables-ship-in-the-core-provider-set.md)), a base model in
`Trax.Effect/Models/<Entity>/`, its `Persistent<Entity>` mapping, and a `DbSet` on `DataContext`
and `IDataContext`, the pair `effect/0003` describes. The feature package reaches the table through
`IDataContext` and the model, with LINQ, `ExecuteUpdate` and `ExecuteDelete`. It writes no SQL of
its own and brings no `DbContext` of its own. Where the providers genuinely differ, the difference
lives behind `ISqlDialect` in Trax.Effect, never in the feature.

## Status

**Accepted.**

## Why this is written down

Because the shortcut is attractive and looks harmless. The runner nonce store in Trax.Scheduler
(scheduler/0009) first shipped `runner_nonce` as a migration with no model: the store wrote
`INSERT ... ON CONFLICT ... DO UPDATE ... WHERE` by hand through `ExecuteSqlRawAsync`, and found
the table's schema by reading the data context's mapping of `metadata`. Every piece worked, and
every piece was in the wrong place. Nothing compared the table with anything, the SQL lived in a
repo that owns no schema, and the only tests of the SQL were in that repo, so a migration change in
Trax.Effect could break it with Trax.Effect's own suites green.

## Considered options

**DDL in Effect, raw SQL in the feature.** What the runner nonce store did first, rejected. There
was no model, so no model-to-DDL drift check could cover the table. The SQL sat outside Trax.Effect,
against scheduler/0002 and this ADR, and a provider difference had to be solved by reading another
table's mapping. The table's behaviour was tested only where its SQL lived, not where its schema
did.

**The feature's own `DbContext` over the Effect-shipped table.** What state-machine persistence
did until 2026-09-29 (below). It keeps the mapping next to the feature, but the entity is then invisible
to the data context, to its drift check and to anything that reads `IDataContext`, and every such context
has to repeat the Sqlite schema stripping `SqliteContext` already does.

**Everything, operation included, in Trax.Effect.** Rejected as a default. The model and the
provider knowledge belong to Effect; what a feature does with its rows is the feature's, which is
where `DbPersistedOperationStorage` keeps persisted operations' logic and where the nonce store
keeps its own.

## Consequences

**The exemplar is persisted operations.** `trax.persisted_operation` and its history ship as
migrations with `PersistedOperation` and `PersistedOperationHistory`, their persistent mappings and
`DbSet`s, and Trax.Api's `DbPersistedOperationStorage` reaches them through `IDataContext` with LINQ
and no dedicated `DbContext`. The runner nonce now matches: `RunnerNonce`, `PersistentRunnerNonce`,
`IDataContext.RunnerNonces`, and a store that adds a row, reads a key conflict through
`ISqlDialect.IsUniqueViolation`, and takes an expired row over with one guarded `ExecuteUpdate`.

**State-machine persistence now matches.** `snapshot_draft` and `effect_claim` were first mapped on
the persistence package's own `SnapshotDbContext`, with entities that had no base and persistent pair
and a conflict check that read only the Postgres exception, and both drift guards listed them as
unmodelled. They are now `SnapshotDraft` and `EffectClaim` in Trax.Effect with their persistent
mappings and `IDataContext.SnapshotDrafts` and `IDataContext.EffectClaims`; the stores take an
`IDataContext` and read a lost race through `ISqlDialect.IsUniqueViolation`, and the package's own
context is gone. The drift guards list no exception but DbUp's journal. The Sqlite half needed one
more piece of provider knowledge: `SqliteContext` stores a `DateTimeOffset` as fixed-width UTC text, so
a lease or expiry comparison translates to SQL and orders correctly.

**Adding to `IDataContext` must not break an implementation.** A new `DbSet` on the interface is a
binary break that package validation refuses. `RunnerNonces`, `SnapshotDrafts` and `EffectClaims`
therefore have a default that reads the set from the implementation as a `DbContext`, as `Raw` already
assumes, and `ISqlDialect` members added since the interface shipped default to the safe answer.

**Provider knowledge grows in `ISqlDialect`.** `IsUniqueViolation` is its first member that
classifies an exception rather than returning SQL: Postgres `23505`, Sqlite `SQLITE_CONSTRAINT`
with the `PRIMARYKEY` or `UNIQUE` extended code, because a `CHECK`, `NOT NULL` or trigger failure
shares the primary code and must throw rather than read as a conflict.

## Exemplars

**Enforced elsewhere:** Trax.Effect's `EveryTableIsModelledTests` (Postgres) and
`SqliteEveryTableIsModelledTests` fail when a migrated table is not mapped by the data context, other
than DbUp's journal, and when a mapped column does not exist in its migrated table, and they pin
`runner_nonce`, `snapshot_draft` and `effect_claim` to their models column for column. Trax.Effect's
`SqliteStoreTests` run the state-machine stores' lost races and lease comparisons on Sqlite.
`RunnerNonceStorageTests` and `SqliteRunnerNonceStorageTests` cover the model's round trip, expiry
through `ExecuteUpdate` and `ExecuteDelete`, concurrent inserts, and `IsUniqueViolation` on both
providers. Trax.Scheduler's `SharedNonceStoreTests` and `SqliteSharedNonceStoreTests` run the store
that uses them.

- [Writing Migrations](/docs/reference/writing-migrations) and
  [Project Layout](/docs/reference/project-layout) are the rule this produces.

Not covered: nothing stops a feature package from writing raw SQL against a table that is modelled.
The guards prove every table has a model, not that every caller uses it. Scheduler's own raw SQL
(the dispatch claim, the queue loads) goes through `ISqlDialect`, which scheduler/0002 records.

## Changelog

- **2026-09-29**: State-machine persistence brought into line; the known deviation is gone.
- **2026-09-28**: Recorded, with the runner nonce's move from raw SQL to its model.
