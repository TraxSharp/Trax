---
authors: [Theauxm]
areas: [migrations, data-model]
status: accepted
---

# Trax's tables are migrated; a consumer's domain tables are bootstrapped

Two different mechanisms, deliberately:

- **Trax's own tables** (the `trax` schema) are created in production only by the shipped
  migrations ([0001](./0001-schema-changes-are-hand-written-sql.md)). Tests may build them
  another way against a throwaway database, and several do.
- **A consumer's domain tables** are created by `EnsureSchemaCreatedAsync`, which runs the
  EF model's create script on a relational provider and falls back to `EnsureCreatedAsync`
  in memory. It is a convenience bootstrap, and it says so: production apps are expected to
  bring their own migrations.

## Status

**Accepted.**

## Why this is written down

Because "never use `EnsureCreated`" is the rule people remember, and it is wrong as stated:
the call is right there in `DomainDataContextServiceCollectionExtensions`. The rule applies
to tables **Trax** owns, where `EnsureCreated` would bypass the DbUp journal, drift from the
migrated schema that production actually runs, and make a feature look like it needs manual
table setup.

For a consumer's own tables Trax has no journal and no authority, so the bootstrap is the
friendliest thing that can be offered, and the docstring is explicit that it is not a
migration system.

## Consequences

**The bootstrap swallows a `DbException` on its second run.** The create script carries no
`IF NOT EXISTS`, so the steady state is an "already exists" failure on the first statement,
caught and ignored. The catch is unfiltered, so a connectivity or permission failure is
swallowed identically. That is why this is a bootstrap rather than something to build on.

**`EnsureCreated` in a test is fine**, and several integration tests use it against
throwaway databases. What is not fine is a Trax table whose only creation path is
`EnsureCreated`, because nothing would then catch the model drifting from the shipped DDL.

## Exemplars

- `MigrationSchemaTests` builds the state machine tables from the **shipped** migrations
  (Postgres `040`, Sqlite `006`) and round-trips through the real stores, so a column added
  to the model without a migration fails when the store queries a column that does not
  exist.

Not covered:

- The round-trip is written down only for the state machine tables. `metadata` gets an
  equivalent check incidentally: `tests/Trax.Effect.Tests.Integration` registers `UsePostgres`
  against `trax_data_tests`, so its schema is the shipped migrations and nothing else, and
  `HostTrackingIntegrationTests.cs` reads a whole row back through `Metadatas`, which projects
  every mapped column of `Metadata`, the entity `PersistentMetadata.OnModelCreating`
  configures. A column the model expects and the DDL omits fails that test, not at runtime.
  The coverage is a side effect of a host-attribution test, not a guard: it asserts host
  fields, and it is named here as prose rather than credited as an exemplar.
  Coverage of the rest is thinner than it looks. Seven of the eight `IDataContext` tables are
  emptied by the suite's cleanup, and the per-provider migration tests assert the names of
  tables and indexes, never columns, so model-versus-DDL drift on those is still caught at
  runtime. `persisted_operation` and `persisted_operation_history` have neither: both
  providers create them, and `SqliteMigrationTests.ExpectedTables` lists neither.
- **These suites require a live Postgres and fail without one rather than skipping.** In
  `tests/Trax.Effect.Tests.Integration` it is `UsePostgres` running the migrator at
  registration time inside `[OneTimeSetUp]`; in
  `tests/Trax.Effect.StateMachine.Persistence.Integration` it is `PostgresSetup`, an
  assembly-wide `[SetUpFixture]` that opens a connection unconditionally, so even the Sqlite
  case fails when Postgres is absent. That contradicts
  `Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md`, which binds this repo. The
  pattern to copy is the reachability probe in
  `RabbitMqBroadcasterIntegrationTests.DataChangeMessage_RoundTrips_DomainAndEventTypeAcrossTheBroker`,
  which is the only test in that file that has one; its six siblings fail without a broker.
  The stress fixtures gate on an opt-in environment variable, which is a different thing.

## Changelog

- **2026-09-11**: Dropped the claim that `metadata` has no model-versus-DDL coverage. The
  Postgres integration suite runs against a migration-built database and materialises the
  whole row, so drift there fails a test. Narrowed the line to the tables genuinely left
  uncovered, and corrected what the per-provider migration tests assert.
- **2026-09-11**: Recorded.
