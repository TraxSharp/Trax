---
authors: [Theauxm]
repos: [effect, api]
areas: [migrations, providers]
status: accepted
---

# Feature-package tables ship in the core provider migration set

A higher-level feature package does not carry its own migrations. Its table DDL goes into
the core `Trax.Effect.Data.<Provider>` migration set, alongside every other Trax table.
`035_persisted_operations.sql` and `040_state_machine_snapshots.sql` are both in the core
Postgres provider, and `Trax.Effect.StateMachine.Persistence` has no `Migrations` folder at
all.

## Status

**Accepted.**

## Why this binds two repos

The DDL lives in `Trax.Effect`, but the feature packages that need it do not all. The
persisted-operations feature is `Trax.Api.GraphQL.PersistedOperations`, so a Trax.Api
developer adding a table is exactly the person who can break this, and a Trax.Effect-local
record would never reach them.

## Why this is written down

Because it looks backwards. A feature package owning its own schema is the obvious layout,
and someone tidying this up would move the files without anything stopping them.

The reason is mechanical: DbUp is pointed at exactly one assembly
(`typeof(AssemblyMarker).Assembly`, the provider), so a `.sql` file embedded anywhere else
is never discovered. It does not fail. It silently never runs, and the feature looks broken
at query time in a way that points at the wrong place.

## Consequences

**This does not invert the dependency.** The `.sql` is text, and the provider references
nothing from the feature package.

**A host that does not enable both features carries empty tables.** Four of them:
`snapshot_draft` and `effect_claim` from `040`, `persisted_operation` and
`persisted_operation_history` from `035`. That is the accepted price, and it is small against
a table that is silently missing when the feature is on.

## Exemplars

- [Persisted Operations](/docs/persisted-operations) and
  [State Machines](/docs/statemachine) are the two features whose tables ship this way.

**Unenforced:** nothing checks it, and this is the weakest point in the migration story. A
`Migrations/` folder added to a feature package would embed its scripts, run no part of
them, and pass every guard in either repo. A census of embedded `.sql` resources outside the
two provider assemblies would close it, and does not exist.

## Changelog

- **2026-09-11**: Corrected the table count: four empty tables, not two.
- **2026-09-11**: Recorded. Moved from the Trax.Effect corpus: the persisted-operations
  feature package lives in Trax.Api, so the decision binds both repos.
