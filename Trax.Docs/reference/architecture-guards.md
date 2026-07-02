---
layout: default
title: Architecture Guards
parent: Reference
nav_order: 5
---

# Architecture Guards

Trax ships per-concern "guard" packages that let any repo enforce the same architectural conventions the Trax samples follow: one project / one schema / one context, cross-schema reads that never leak the model graph, cross-schema GraphQL edges that batch, trains that expose a companion interface, and basic test hygiene. The rules are framework-agnostic checkers that return an offender list; you assert on them with your own test framework.

## Packages

Each package lives in the repo that owns the concern it checks, and depends only on `Trax.Core.Testing`:

| Package | Owns | Guards |
|---|---|---|
| `Trax.Core.Testing` | Infrastructure + hygiene | `RepoRoot` / `SourceFiles` / `SourceText`, `ArchitectureGuardOptions`, `GuardResult`; `HygieneGuards` (no `[Ignore]`, no legacy asserts, no fixed delays); `RepoConventionGuards` (`Directory.Build.props` version; cross-repo Trax refs centrally managed via `Directory.Packages.props` with no inline `Version`) |
| `Trax.Effect.Data.Testing` | Data layer | `DomainContextsDeriveBase`, `CompanionInterfaces`, `OneSchemaPerContext`, `NoPendingModelChanges` |
| `Trax.Api.GraphQL.Testing` | GraphQL | `EdgeManifestIsValid`, `EdgeResolversUseLoader` |
| `Trax.Mediator.Testing` | Trains | `EveryTrainHasInterface` |

A checker returns a `GuardResult` with the offenders it found, how many items it inspected, and a ready-to-use failure message.

Most guards scan source on disk. `NoPendingModelChanges` is the exception: it builds each migration-based context offline (no database) and asserts its EF model matches the latest migration snapshot, catching a model edit that shipped without `dotnet ef migrations add` before it trips `PendingModelChangesWarning` at host startup.

## Consuming the guards

Each package ships abstract NUnit base fixtures with the `[Test]` methods already written. Reference the packages in a test project, subclass the fixtures you want, supply your configuration, and run `dotnet test`. You write no test bodies, only configuration:

```csharp
[TestFixture]
public sealed class MyDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs", "apps"] };
    protected override IReadOnlyList<Type> DomainContexts => [typeof(CatalogDbContext), typeof(LendingDbContext)];
    // Only contexts that use EF migrations; omit any bootstrapped with EnsureSchemaCreatedAsync.
    protected override IReadOnlyList<Type> MigrationContexts => [typeof(CatalogDbContext)];
}

[TestFixture]
public sealed class MyCrossSchemaGuards : CrossSchemaGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs"] };
    protected override IReadOnlyList<CrossSchemaEdge> Edges => MyCrossSchemaEdges.All;
}

[TestFixture]
public sealed class MyTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies => [typeof(MyAssemblyMarker).Assembly];
}
```

That is the whole test project. `dotnet test` discovers the inherited `[Test]` methods through your subclasses. Subclass only the fixtures for concerns you have; type-list members (`DomainContexts`, `MigrationContexts`, `Edges`, `TrainAssemblies`) default to empty, so a guard you do not configure passes vacuously. The `[TestFixture]` attribute on each subclass is required for the runner to discover the inherited tests.

`ArchitectureGuardOptions` carries the per-repo configuration: scan roots, allowlists, and the expected versions. Allowlist entries are repo-relative paths; the source guards walk up from the test assembly to the nearest `*.slnx` to find the repo root.

If you prefer not to use NUnit, the same checks are available as framework-agnostic methods (`DataLayerGuards.*`, `CrossSchemaGuards.*`, `TrainGuards.*`, `HygieneGuards.*`) that return a `GuardResult` you assert on however you like.

## The patterns the guards enforce

The guards check first-class Trax types, so adopting them goes hand in hand with adopting the patterns:

- **`DomainDataContext<TSelf>`** (`Trax.Effect.Data`) is the base for a domain data context. It applies the default schema on PostgreSQL, a UTC datetime converter, and seals `OnModelCreating` (you override `Schema` and `ConfigureModel`). It is separate from Trax's own metadata `DataContext<T>`. Register it with `AddDomainDataContext<TInterface, TContext>` and create its schema with `EnsureSchemaCreatedAsync<TContext>`.
- **`IEntityReference`** marks a scalar-only projection of an entity owned by another schema, for cross-schema reads.
- **`CrossSchemaLoader<TContext, TEntity>`** and **`CrossSchemaEdge`** (`Trax.Api.GraphQL`) back cross-schema GraphQL edges: a batched loader collapses every cross-context lookup in a request into one `WHERE id IN (...)`, and the edge manifest is the single source of truth the guards check.

The Bookworm sample is the reference consumer of every package and pattern above.

## SDK Reference

> [DomainDataContext](/docs/effect/effect-providers/domain-data-contexts) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders)
