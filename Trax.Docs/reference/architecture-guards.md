---
title: Architecture Guards
description: "What the Trax architecture-guard packages check and why: schemas, cross-schema reads, owner scoping, train interfaces, database invariants, test hygiene."
type: explanation
area: tooling
nav_order: 3
---

# Architecture Guards

Trax ships per-concern guard packages that let a repository enforce the same architectural conventions the Trax samples follow: one project / one schema / one context, cross-schema reads that never leak the model graph, cross-schema GraphQL edges that batch, trains that each have their own train interface, and basic test hygiene. The rules are framework-agnostic checkers that return an offender list; you assert on them with your own test framework.

## Packages

Each package lives in the folder of the Trax repository that owns the concern it checks, and each
depends on `Trax.Core.Testing` and NUnit. The checkers, fixtures and options of each are on its
SDK page:

| Package | Owns | Checks | Reference |
|---|---|---|---|
| `Trax.Core.Testing` | Infrastructure and hygiene | No `[Ignore]`, no legacy asserts, no fixed delays; the repository's version and centrally pinned Trax packages; no third-party attribute where Trax has its own | [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing) |
| `Trax.Effect.Data.Testing` | Data layer | Domain contexts derive the shared base and have companion interfaces; one schema per context; no pending model changes; the owner-scope census; database invariants after a test | [Trax.Effect.Data.Testing](/docs/sdk-reference/testing/effect-data-testing) |
| `Trax.Api.GraphQL.Testing` | GraphQL | The cross-schema edge manifest is valid, edge resolvers batch through the loader, and `[Parent]` resolvers declare what they read | [Trax.Api.GraphQL.Testing](/docs/sdk-reference/testing/graphql-testing) |
| `Trax.Mediator.Testing` | Trains | Every train has its own train interface | [Trax.Mediator.Testing](/docs/sdk-reference/testing/mediator-testing) |

A checker returns a `GuardResult` with the offenders it found, how many items it inspected, and a
ready-to-use failure message. The shipped fixtures also fail when a guard inspected nothing,
because scan roots that point at the wrong directory would otherwise pass every check; a test that
calls a checker directly asserts `Inspected > 0` itself.

The source guards parse C# with Roslyn rather than matching text where the shape of a declaration
matters: `DomainContextsDeriveBase` judges every class by its own base list, so a file holding
several contexts has each one checked, and a base named elsewhere in the file excuses nothing.
`NoPendingModelChanges` and `OwnerScopeCompleteness` read the EF model instead of source, built
offline with no database. `NoPendingModelChanges` catches a model edit that shipped without
`dotnet ef migrations add` before it trips `PendingModelChangesWarning` at host startup.

`EveryTrainHasInterface` applies the rule the train registry applies when it scans. A train with
no train interface of its own would be listed only under the shared `IServiceTrain<TIn, TOut>`,
and one with two unrelated train interfaces is refused by the registry, so the guard reports both
at test time rather than at startup.

## Base fixtures

Each package ships abstract NUnit base fixtures with the `[Test]` methods already written. A test
project subclasses them, marks each subclass `[TestFixture]` (the runner discovers the inherited
tests through it), and overrides the members its SDK page lists.
[Adopt the architecture guards](/docs/reference/adopt-the-architecture-guards) has the steps.

An optional type list left empty makes its guard pass vacuously, so a repository adopts one check
at a time. A list a check cannot do without, such as `TrainAssemblies`, is abstract, and an empty
one fails rather than passing. The same checks are available as framework-agnostic methods
(`DataLayerGuards`, `CrossSchemaGuards`, `TrainGuards`, `HygieneGuards`) for a test suite that does
not use NUnit.

## The owner-scope census

`[TraxAuthorize]` gates a type, not its rows. Per-user data is narrowed to its owner by an EF query filter that reads the current principal, and Trax's authorization never sees that filter, so an entity that is correctly gated but has no filter passes every other check and serves every user's rows to any authenticated caller. `OwnerScopeCompleteness` reads the EF model and finds those entities.

An entity holds per-user data when the model gives it an owner key (a foreign key to your owner type, a property you name in `OwnerIdProperties`, or it is the owner type itself), or when it has a query filter that reads your principal accessor. For each one the census requires:

- a query filter whose expression references the accessor's type. A soft-delete or visibility filter reads no principal and does not count, so it neither satisfies the check nor pulls a shared entity into it. EF declares filters on a hierarchy's root, so a derived type is judged by its root's filter;
- if it is a `[TraxQueryModel]`, a bare `[TraxAuthorize]`. `[TraxAllowAnonymous]` exposes owners' rows to anonymous callers, and a role or policy gate can lock owners out of their own rows. The filter is the access control. When a gate is deliberate (a per-user entity only premium users may read, say), list the entity in `Gated` with a reason. The gate is added to the filter, never used in place of it: a gated entity still needs its principal-reading filter, still cannot be `[TraxAllowAnonymous]` or undeclared, and cannot also be exempted.

An entity reaching its owner only through a navigation, such as an answer whose poll holds the owner, has no owner key, so its filter is the only thing marking it as per-user. Declare it in `NavigationScoped` with the navigation it goes through: the census then fails if the filter disappears, and fails if a filtered entity with no owner key is not declared. `Exemptions` leaves an entity out and needs a written reason; an exemption naming an entity the census would not flag is reported too. A `Gated` entry without a reason, or one naming an entity that is not a per-user `[TraxQueryModel]` with a role or policy on its `[TraxAuthorize]`, is reported the same way.

A second entity type mapped to the same table or view as a per-user entity, such as a reporting view over the same rows, reads those rows too. The census treats it as per-user and requires its filter, whatever gate it carries.

The census takes the model rather than a context type, because an owner-scoped context usually takes its principal accessor through its constructor. A context listed in `DomainContexts` as well is also built from its options alone, so it keeps an options-only constructor beside the one that takes the accessor (see [Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts)). Building the model needs no database, only a provider.

### Filters switched off in query code

The model says a filter exists; it cannot say that a resolver or a train switches it off. So the fixture also scans the source under your scan roots (`DataLayerGuards.OwnerScopeFilterBypasses`) and fails on:

- `IgnoreQueryFilters()` on a per-user set. It switches every filter off, the owner scope included;
- an EF10 named-filter disable, `IgnoreQueryFilters(["Owner"])`, that names an owner-scope filter (a named filter reading your principal accessor), or whose names are not string literals the scan can read. Disabling only a soft-delete or visibility filter by name is fine;
- either call on a set the scan cannot name, such as a query passed into a helper, or a generic repository's `Set<T>()` over a type parameter, because it may be a per-user one.

The set is read from the receiver the call chain starts from: a `DbSet<T>` or `IQueryable<T>` property declared under the scan roots, or `Set<T>()` over a mapped entity. A call on a set of shared rows (a soft-deleted article archive, say) passes. A shared set mentioned elsewhere in the statement, inside a lambda for instance, does not make the receiver shared, and a per-user set mentioned anywhere in the statement is reported, because the call switches the filter off for its subqueries too. A file that switches the owner scope off on purpose is listed in `FilterBypassAllowlist`, keyed by its repo-relative path, with the reason.

An entry with a blank reason, or one whose file no longer switches an owner-scope filter off, is reported. Override `ScanForOwnerScopeFilterBypasses` to `false` only if your scan roots do not contain the code that queries those models.

The scan reads text, not compiled code. It does not follow a query built in one statement and filtered in a later one, and it cannot tell two contexts' `Notes` sets apart.

### What neither check can see

The census proves a principal-reading filter exists, not that it compares the right column, and not what a bypass branch inside it allows: a filter reading `principal.IsAdmin || e.OwnerId == principal.Id` under an admin gate shows admins every owner's rows. Entities mapped with `ToSqlQuery` or to a function over per-user tables, and a per-user entity reached through a navigation from a type that is exposed, are outside both checks too.

What catches those is a cross-user test against the running API, one per per-user entity; [Adopt the architecture guards](/docs/reference/adopt-the-architecture-guards#configure-the-owner-scope-census) describes it.

## Database invariants after a test

`TraxInvariants` (`Trax.Effect.Data.Testing`) checks a Trax database once every host a test
started has stopped. Some of its checks fail on states that are normal while a host runs, such as
a run still in progress or an effect claimed without a receipt; after the host stops, each is work
that was started and lost. The rest hold whenever no write is half done, because one statement or
transaction keeps each of them: a dispatched entry has its run, a completed run has no
checkpoints, an instance waiting on an invoked run holds the token that brings its outcome back.

The database does not record which states invoke a train or carry a timer, so the checks that
depend on them run only for the machines a test passes in. A claim names its effect's intent, not
a run, so the claim check cannot ask whether the claim's run finished; it reports every claim
left without a receipt.

A test that leaves one of these states on purpose, such as a crash simulation, carries
`[LeavesStuckRuns("why")]`, and `TraxInvariants.IsExempt` returns true for it. The checks, their
names and every overload are on
[Trax.Effect.Data.Testing](/docs/sdk-reference/testing/effect-data-testing#traxinvariants).

## The patterns the guards enforce

The guards check first-class Trax types, so adopting them goes hand in hand with adopting the patterns:

- **`DomainDataContext<TSelf>`** (`Trax.Effect.Data`) is the base for a domain data context. It applies the default schema on PostgreSQL, a UTC datetime converter, and seals `OnModelCreating` (you override `Schema` and `ConfigureModel`). It is separate from Trax's own metadata `DataContext<T>`. Register it with `AddDomainDataContext<TInterface, TContext>` and create its schema with `EnsureSchemaCreatedAsync<TContext>`.
- **`IEntityReference`** marks a scalar-only projection of an entity owned by another schema, for cross-schema reads.
- **`CrossSchemaLoader<TContext, TEntity>`** and **`CrossSchemaEdge`** (`Trax.Api.GraphQL`) back cross-schema GraphQL edges: a batched loader collapses every cross-context lookup in a request into one `WHERE id IN (...)`, and the edge manifest is the single source of truth the guards check.
- **`[Parent(requires: ...)]`** on a resolver declares which columns of its parent it reads. Trax adds a query model's key to the projection automatically, so `Extension_resolvers_declare_what_they_read_off_their_parent` only fires on the properties it cannot infer: foreign keys, and anything else a resolver touches. Without the declaration the property arrives as `0` or `null` and the field silently returns nothing.

The Bookworm sample is the reference consumer of every package and pattern above.

## SDK Reference

> [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing) | [Trax.Effect.Data.Testing](/docs/sdk-reference/testing/effect-data-testing) | [Trax.Api.GraphQL.Testing](/docs/sdk-reference/testing/graphql-testing) | [Trax.Mediator.Testing](/docs/sdk-reference/testing/mediator-testing) | [DomainDataContext](/docs/effect/effect-providers/domain-data-contexts) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Query models](/docs/sdk-reference/graphql-api/query-models)
