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
| `Trax.Effect.Data.Testing` | Data layer | `DomainContextsDeriveBase`, `CompanionInterfaces`, `OneSchemaPerContext`, `NoPendingModelChanges`, `OwnerScopeCompleteness`, `OwnerScopeFilterBypasses` |
| `Trax.Api.GraphQL.Testing` | GraphQL | `EdgeManifestIsValid`, `EdgeResolversUseLoader` |
| `Trax.Mediator.Testing` | Trains | `EveryTrainHasInterface` |

A checker returns a `GuardResult` with the offenders it found, how many items it inspected, and a ready-to-use failure message.

Most guards scan source on disk. `NoPendingModelChanges` and `OwnerScopeCompleteness` are the exceptions. `NoPendingModelChanges` builds each migration-based context offline (no database) and asserts its EF model matches the latest migration snapshot, catching a model edit that shipped without `dotnet ef migrations add` before it trips `PendingModelChangesWarning` at host startup.

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

## The owner-scope census

`[TraxAuthorize]` gates a type, not its rows. Per-user data is narrowed to its owner by an EF query filter that reads the current principal, and Trax's authorization never sees that filter, so an entity that is correctly gated but has no filter passes every other check and serves every user's rows to any authenticated caller. `OwnerScopeCompleteness` reads the EF model and finds those entities.

An entity holds per-user data when the model gives it an owner key (a foreign key to your owner type, a property you name in `OwnerIdProperties`, or it is the owner type itself), or when it has a query filter that reads your principal accessor. For each one the census requires:

- a query filter whose expression references the accessor's type. A soft-delete or visibility filter reads no principal and does not count, so it neither satisfies the check nor pulls a shared entity into it. EF declares filters on a hierarchy's root, so a derived type is judged by its root's filter;
- if it is a `[TraxQueryModel]`, a bare `[TraxAuthorize]`. `[TraxAllowAnonymous]` exposes owners' rows to anonymous callers, and a role or policy gate can lock owners out of their own rows. The filter is the access control. When a gate is deliberate (a per-user entity only premium users may read, say), list the entity in `Gated` with a reason. The gate is added to the filter, never used in place of it: a gated entity still needs its principal-reading filter, still cannot be `[TraxAllowAnonymous]` or undeclared, and cannot also be exempted.

An entity reaching its owner only through a navigation, such as an answer whose poll holds the owner, has no owner key, so its filter is the only thing marking it as per-user. Declare it in `NavigationScoped` with the navigation it goes through: the census then fails if the filter disappears, and fails if a filtered entity with no owner key is not declared. `Exemptions` leaves an entity out and needs a written reason; an exemption naming an entity the census would not flag is reported too. A `Gated` entry without a reason, or one naming an entity that is not a per-user `[TraxQueryModel]` with a role or policy on its `[TraxAuthorize]`, is reported the same way.

A second entity type mapped to the same table or view as a per-user entity, such as a reporting view over the same rows, reads those rows too. The census treats it as per-user and requires its filter, whatever gate it carries.

The census takes the model rather than a context type, because an owner-scoped context usually takes its principal accessor through its constructor. Building the model needs no database:

```csharp
[TestFixture]
public sealed class MyDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs", "apps"] };

    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ClientDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new ClientDbContext(options, new SystemPrincipal());
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope => new()
    {
        OwnerType = typeof(UserProfile),
        PrincipalAccessorType = typeof(IPrincipalAccessor),
        NavigationScoped = new Dictionary<Type, string>
        {
            [typeof(PollQuestionAnswer)] = "belongs to the PollAnswer that holds the UserId",
        },
    };
}
```

### Filters switched off in query code

The model says a filter exists; it cannot say that a resolver or a train switches it off. So the fixture also scans the source under your scan roots (`DataLayerGuards.OwnerScopeFilterBypasses`) and fails on:

- `IgnoreQueryFilters()` on a per-user set. It switches every filter off, the owner scope included;
- an EF10 named-filter disable, `IgnoreQueryFilters(["Owner"])`, that names an owner-scope filter (a named filter reading your principal accessor), or whose names are not string literals the scan can read. Disabling only a soft-delete or visibility filter by name is fine;
- either call on a set the scan cannot name, such as a query passed into a helper, because it may be a per-user one.

The set is read from the call's statement: a `DbSet<T>` or `IQueryable<T>` property declared under the scan roots, or `Set<T>()`. A call on a set of shared rows (a soft-deleted article archive, say) passes. When a file switches the owner scope off on purpose, list it with the reason:

```csharp
protected override OwnerScopeCensusOptions OwnerScope => new()
{
    OwnerType = typeof(UserProfile),
    PrincipalAccessorType = typeof(IPrincipalAccessor),
    FilterBypassAllowlist = new Dictionary<string, string>
    {
        ["apps/Worker/Trains/EraseAccount/EraseAccountTrain.cs"] =
            "erasing an account deletes that owner's rows from a system context",
    },
};
```

An entry with a blank reason, or one whose file no longer switches an owner-scope filter off, is reported. Override `ScanForOwnerScopeFilterBypasses` to `false` only if your scan roots do not contain the code that queries those models.

The scan reads text, not compiled code. It does not follow a query built in one statement and filtered in a later one, and it cannot tell two contexts' `Notes` sets apart.

### What neither check can see

The census proves a principal-reading filter exists, not that it compares the right column, and not what a bypass branch inside it allows: a filter reading `principal.IsAdmin || e.OwnerId == principal.Id` under an admin gate shows admins every owner's rows. Entities mapped with `ToSqlQuery` or to a function over per-user tables, and a per-user entity reached through a navigation from a type that is exposed, are outside both checks too.

What catches those is a **cross-user behavioural test**: sign in as one user, create a row, sign in as a second user, and assert that every surface the row can be read through (each GraphQL query and model, each train that returns it) gives the second user nothing. Write one per per-user entity against the running API, not the model; it is the only check that exercises the filter as it actually runs.

## The patterns the guards enforce

The guards check first-class Trax types, so adopting them goes hand in hand with adopting the patterns:

- **`DomainDataContext<TSelf>`** (`Trax.Effect.Data`) is the base for a domain data context. It applies the default schema on PostgreSQL, a UTC datetime converter, and seals `OnModelCreating` (you override `Schema` and `ConfigureModel`). It is separate from Trax's own metadata `DataContext<T>`. Register it with `AddDomainDataContext<TInterface, TContext>` and create its schema with `EnsureSchemaCreatedAsync<TContext>`.
- **`IEntityReference`** marks a scalar-only projection of an entity owned by another schema, for cross-schema reads.
- **`CrossSchemaLoader<TContext, TEntity>`** and **`CrossSchemaEdge`** (`Trax.Api.GraphQL`) back cross-schema GraphQL edges: a batched loader collapses every cross-context lookup in a request into one `WHERE id IN (...)`, and the edge manifest is the single source of truth the guards check.
- **`[Parent(requires: ...)]`** on a resolver declares which columns of its parent it reads. Trax adds a query model's key to the projection automatically, so `Extension_resolvers_declare_what_they_read_off_their_parent` only fires on the properties it cannot infer: foreign keys, and anything else a resolver touches. Without the declaration the property arrives as `0` or `null` and the field silently returns nothing.

The Bookworm sample is the reference consumer of every package and pattern above.

## SDK Reference

> [DomainDataContext](/docs/effect/effect-providers/domain-data-contexts) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Query models](/docs/sdk-reference/graphql-api/query-models)
