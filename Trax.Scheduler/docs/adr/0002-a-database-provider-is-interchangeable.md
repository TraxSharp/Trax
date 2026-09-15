---
authors: [Theauxm]
areas: [scheduling, providers]
status: accepted
---

# Swapping the database provider changes no scheduler registration

`AddScheduler()` registers the same services for either **relational** provider. A host that
moves from Postgres to Sqlite swaps the provider package (`Trax.Effect.Data.Postgres` for
`Trax.Effect.Data.Sqlite`), its `using`, and the one call in `AddEffects(...)`. No scheduler
registration moves with it: the manifest manager, the SQL dialect and the provider flag all
resolve the same way.

The InMemory provider is deliberately not in that set. With no database there is nothing for
the work queue to poll, so `AddScheduler()` branches on `HasDatabaseProvider` and registers a
reduced surface: a different manifest manager and job submitter, no local worker service, and
none of the three polling services that need a relational store (`JobDispatcherPollingService`
and `MetadataCleanupPollingService` write raw SQL; `DeadLetterCleanupPollingService` is EF
LINQ, but `ExecuteDeleteAsync` is a relational operation all the same).
`SchedulerConfigBootstrapHostedService`, `SchedulerStartupService` and
`ManifestManagerPollingService` are registered either way, the last because the InMemory
manifest manager dispatches jobs inline and needs no dispatcher behind it.

## Status

**Accepted.**

## Why this is written down

Because the scheduler is the layer most tempted to break it. It writes SQL for the work
queue, the background job queue and the leader lock, and the two providers differ (`jsonb`
and `timestamptz` against `TEXT` and `INTEGER`, a `trax` schema against no schemas at all).
The easy path is a provider branch in the scheduler. Instead the difference is confined
behind `ISqlDialect`, and the scheduler asks for the dialect rather than asking which
provider it got.

The payoff is that a consumer can develop against Sqlite and deploy against Postgres without
a second code path, and that the Sqlite integration suite is testing the same wiring
production uses.

## Consequences

**A new provider must supply a dialect, not a special case.** Anything the scheduler cannot
express through `ISqlDialect` is a gap in the dialect, and widening it is the fix.

**A provider-specific optimisation has nowhere to live.** That is deliberate and it is a
real cost: a Postgres-only index hint or an `ON CONFLICT` form has to be expressed through
the dialect or not at all.

**One provider name survives in the scheduler, and the dialect could not hold it today.**
`SchedulerStartupService.IsTransient` decides whether a seeding failure is worth retrying by
matching the exception type's `FullName` against `"Npgsql."`. It inspects no connection and
no connection string. Provider names appear elsewhere in `src/` on types the branch selects
between, such as `PostgresJobSubmitter` and `InMemoryManifestManagerTrain`; this is the only
place a provider is named in a *string*. Moving it
behind `ISqlDialect` would mean adding exception classification to an interface that today
returns nothing but SQL strings, so the string stays until a second provider needs the same
treatment.

## Exemplars

- `ProviderConsistencyTests` builds the full `AddTrax().AddEffects(UseSqlite).AddScheduler()`
  stack against a throwaway database file and resolves the registrations that would break
  first if a provider branch crept in: `HasDatabaseProvider`, `ManifestManagerTrain` and
  `ISqlDialect`.
- `SqliteSchedulerBuilderTests`, in the same project, repeats two of those and adds the one
  they miss: that `JobDispatcherPollingService` is among the hosted services under Sqlite, so
  a service gated on Postgres alone fails there.

Not covered:

- Neither guard diffs the Sqlite service set against the Postgres one. Between them they
  name four things (`HasDatabaseProvider`, `ManifestManagerTrain`, `ISqlDialect`,
  `JobDispatcherPollingService`). `DefaultJobSubmitterTests.cs` covers the adjacent case,
  asserting both polarities of the InMemory branch across four hosted services, so the
  genuinely uncovered space is a relational-only registration that none of those names.

## Changelog

- **2026-09-12**: Corrected the unconditional-registration list (three hosted services, not
  two) and narrowed the "only mention of a provider" claim to string literals.
- **2026-09-11**: Corrected the InMemory branch, the
  hand-written SQL (the dead-letter cleanup is EF LINQ, not SQL), the `"Npgsql."` check (an
  exception classifier, not a connection sniff) and the provider swap (the package moves
  too). Credited `SqliteSchedulerBuilderTests`.
- **2026-09-11**: Recorded.
