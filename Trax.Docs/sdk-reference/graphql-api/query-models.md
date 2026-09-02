---
layout: default
title: Query Models
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 6
---

# Query Models

Query models expose EF Core entities directly as GraphQL queries with automatic cursor pagination, filtering, sorting, and projection. Unlike `[TraxQuery]` which wraps a train (business logic), `[TraxQueryModel]` maps a database table to a GraphQL field with zero boilerplate.

## Quick Start

1. Mark your entity with `[TraxQueryModel]`:

```csharp
[TraxQueryModel(Description = "Player profiles")]
public class PlayerRecord
{
    public long Id { get; set; }
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Rating { get; set; }
}
```

2. Add the entity to a `DbSet<T>` on a `DbContext`:

```csharp
public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<PlayerRecord> Players { get; set; } = null!;
}
```

3. Register the DbContext and enable model discovery:

```csharp
builder.Services.AddDbContextFactory<GameDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddTraxGraphQL(graphql =>
    graphql.AddDbContext<GameDbContext>());
```

This generates a `playerRecords` query field under `discover`:

```graphql
query {
  discover {
    playerRecords(first: 10, where: { rating: { gte: 1500 } }, order: { rating: DESC }) {
      nodes {
        playerId
        displayName
        rating
      }
      pageInfo {
        hasNextPage
        endCursor
      }
      totalCount
    }
  }
}
```

## TraxQueryModel Attribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxQueryModelAttribute : Attribute
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? DeprecationReason { get; init; }
    public string? Namespace { get; init; }
    public bool Paging { get; init; } = true;
    public bool Filtering { get; init; } = true;
    public bool Sorting { get; init; } = true;
    public bool Projection { get; init; } = true;
    public FieldBindingBehavior BindFields { get; init; } = FieldBindingBehavior.Implicit;
    public Type? ExposeAs { get; init; }
}
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string?` | `null` | Overrides the auto-derived GraphQL field name. When null, derived by pluralizing and camelCasing the class name (e.g. `Player` → `players`). |
| `Description` | `string?` | `null` | Human-readable description that appears in the GraphQL schema documentation. |
| `DeprecationReason` | `string?` | `null` | Marks the generated field as deprecated in the schema. |
| `Namespace` | `string?` | `null` | Groups this field under a sub-namespace. When set, the field appears under `discover { namespace { field } }` instead of directly under `discover`. |
| `Paging` | `bool` | `true` | Enables cursor-based pagination (Relay Connection spec). When true, the field returns a Connection type with `nodes`, `edges`, `pageInfo`, and `totalCount`. |
| `Filtering` | `bool` | `true` | Enables filtering via a `where` argument. HotChocolate generates filter input types for all entity properties. |
| `Sorting` | `bool` | `true` | Enables sorting via an `order` argument. HotChocolate generates sort input types for all entity properties. |
| `Projection` | `bool` | `true` | Enables field projection. Only the columns requested by the GraphQL client are selected from the database. |
| `BindFields` | `FieldBindingBehavior` | `Implicit` | Controls how fields are bound on the generated GraphQL ObjectType. When `Explicit`, only properties with `[Column]` are exposed; `[NotMapped]`, methods, and non-column members are excluded. |
| `ExposeAs` | `Type?` | `null` | Restricts the GraphQL surface to the property set declared by the supplied interface. The entity must implement the interface implicitly. Filter and sort input types are constrained to the same set unless a custom override is supplied. Mutually exclusive with `BindFields = Explicit`. |

## Feature Configuration

Each feature can be independently disabled per model. All default to `true`.

```csharp
// Full-featured (default)
[TraxQueryModel]
public class Player { ... }

// Pagination and filtering only, no sorting or projection
[TraxQueryModel(Sorting = false, Projection = false)]
public class AuditLog { ... }

// Simple list query, no middleware at all
[TraxQueryModel(Paging = false, Filtering = false, Sorting = false, Projection = false)]
public class StatusCode { ... }
```

When `Paging = false`, the field returns a plain list (`[Entity!]!`) instead of a Connection type.

## Projection and hand-written resolvers

Projection narrows the `SELECT` to the columns the caller's selection set names. A query for
`{ players { nodes { displayName } } }` reads one column, not the whole row.

That has a consequence for any field you add to a query model with `[ExtendObjectType]`. Such a
resolver reads its `[Parent]` in C#, where projection cannot see what it touches, so a property
nobody selected arrives as `0` or `null` and the resolver silently returns an empty or zero
answer.

Trax closes the common case: for every field on a query model that is not backed by an entity
property, it adds the entity's key to the projection. A resolver that batches on the parent's
key needs no annotation.

```csharp
[ExtendObjectType(typeof(Player))]
public sealed class PlayerAchievements
{
    // player.Id arrives whether or not the caller selected `id`.
    public Task<IReadOnlyList<Achievement>> GetAchievements(
        [Parent] Player player,
        AchievementLoader loader,
        CancellationToken ct
    ) => loader.LoadAsync(player.Id, ct);
}
```

A resolver that reads something other than the key declares it, and HotChocolate merges that
with the key Trax already requires:

```csharp
[ExtendObjectType(typeof(Player))]
public sealed class PlayerTeam
{
    public Task<Team?> GetTeam(
        [Parent(requires: nameof(Player.TeamId))] Player player,
        TeamLoader loader,
        CancellationToken ct
    ) => loader.LoadAsync(player.TeamId, ct);
}
```

The requirement is per field and only applies when the field is selected, so a query that does
not ask for `team` still projects exactly the columns it named.

The key is read from the entity class: a `[Key]` property (all of them, for a composite key),
otherwise `Id`, otherwise `{TypeName}Id`. A key configured only through the fluent API's
`HasKey` is not visible there, so resolvers on such an entity declare what they read with
`[Parent(requires: ...)]`.

`ExtensionResolversDeclareParentRequirements` in `Trax.Api.GraphQL.Testing` fails the build when
a resolver reads an undeclared property. See
[architecture guards](/docs/reference/architecture-guards).

## Field Binding

By default, HotChocolate exposes all public properties on an entity as GraphQL fields. When your entity has `[NotMapped]` aliases, DataLoader methods, or infrastructure methods that should not appear in the schema, use explicit binding:

```csharp
[TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
[Table("players", Schema = "game")]
public class Player
{
    [Column("id")]
    public long Id { get; set; }

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [NotMapped]
    public string Alias => $"Player-{Id}";      // excluded from schema

    public void AddToDbContext(GameDb db) { }    // excluded from schema
}
```

With `BindFields = FieldBindingBehavior.Explicit`, only `Id` and `DisplayName` appear in the GraphQL schema. The `Alias` property and `AddToDbContext` method are excluded.

| Value | Behavior |
|-------|----------|
| `Implicit` (default) | All public properties exposed (standard HotChocolate behavior) |
| `Explicit` | Only properties with `[Column]` are exposed |

FK fields added via `ObjectTypeExtension` (from custom TypeModules registered with `AddTypeModule<T>()`) still work when using explicit binding, since extensions are separate from the base type's field set.

## ExposeAs

When an entity is shared across DbContexts (typical pattern: a "reference" projection of a cross-schema entity), the entity class carries every column required by every owning context, but consumers reading it through a non-owning context cannot navigate the relationships. `ExposeAs` constrains the GraphQL schema to a separately-declared interface so the schema reflects what the consumer can actually query, rather than auto-binding every public property on the entity.

```csharp
public interface IBookReference
{
    int Id { get; }
    string Title { get; }
    string Author { get; }
    int Rating { get; }
}

[TraxQueryModel(ExposeAs = typeof(IBookReference))]
public class Book : IBookReference
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Rating { get; set; }

    // Owned only by the authoring context; not on IBookReference, so
    // it is hidden from the GraphQL schema produced here.
    public ICollection<Review>? Reviews { get; set; }
}
```

The generated schema contains only the four interface fields. `reviews` does not appear on the object type, in the `FilterInput`, or in the `SortInput`. Queries referencing it fail at schema validation, not at LINQ-translation time.

| Aspect | Behavior |
|--------|----------|
| **GraphQL type name** | Still derived from the entity (`Book`), not the interface. Consumers see `type Book { ... }`. |
| **Object type fields** | The intersection of the entity's public properties and the interface's property names. |
| **Filter input type** | Restricted to the same property set. Filtering on hidden properties produces a schema-validation error. |
| **Sort input type** | Restricted to the same property set. |
| **Custom filter/sort overrides** | When `AddFilterType<T>` or `AddSortType<T>` is registered, the override wins and `ExposeAs` is not consulted for that input type. |
| **Interface inheritance** | The full inherited interface graph is walked. Properties declared on parent interfaces are exposed. |
| **Field metadata** | Description, deprecation, and other attributes are read from the **entity** property (interface declarations cannot carry attributes that influence the schema). |

### Validation

The configuration is validated at `Build()` time. Each failure mode throws `InvalidOperationException` with a message that names both the entity and the interface.

| Misconfiguration | Error |
|------------------|-------|
| `ExposeAs` combined with `BindFields = Explicit` | Both restrict the field set; pick one. |
| `ExposeAs` references a class instead of an interface | Must be an interface. |
| Entity does not implement the interface | Add the interface to the entity declaration. |
| Interface declares no properties | A GraphQL type with no fields is invalid. |
| Interface declares a property the entity implements explicitly | `ExposeAs` cannot bind explicit-interface implementations; make it implicit. |

### Mutations

`ExposeAs` only applies to `[TraxQueryModel]` (the query surface). Mutations are trains, not query models, and are not affected.

## Authorization

A `[TraxQueryModel]` entity is exposed via GraphQL, so it must declare its authorization posture explicitly: `[TraxAuthorize]` to gate it or `[TraxAllowAnonymous]` to open it. An entity with neither fails at `TraxGraphQLBuilder.Build()` (unless the endpoint is gated with `RequireAuthorization()`, which covers it). See [Authorization guide - Required Exposure Posture](/docs/authorization#required-exposure-posture).

Apply `[TraxAuthorize]` to a `[TraxQueryModel]` entity to gate access. The directive attaches at GraphQL type level *and* at the entry field, so the gate enforces uniformly:

- the top-level field under `discover` (including Connection-shaped scalars like `totalCount` and `pageInfo`),
- any other field elsewhere in the schema whose return type is this entity (e.g. a navigation property on an ungated parent).

```csharp
[TraxQueryModel(Namespace = "library")]
[TraxAuthorize(Roles = "Subscriber")]
public class Article { ... }
```

Combinator semantics, role normalization, and inheritance behavior match the per-train `[TraxAuthorize]` surface. Policy names referenced by a `[TraxQueryModel]` entity must be registered with `services.AddAuthorization(...)`; a `QueryModelAuthorizationValidator` hosted service throws at host start if any policy is missing.

The inverse opt-in, `[TraxAllowAnonymous]`, opens an entity to unauthenticated reads. It is mutually exclusive with `[TraxAuthorize]` and does not cascade through navigation properties to gated children. See [Authorization guide - Anonymous Access via TraxAllowAnonymous](/docs/authorization#anonymous-access-via-traxallowanonymous).

See the [Authorization guide - Per-Model Authorization](/docs/authorization#per-model-authorization) for the full semantics table and limitations (no field-level gating, no row-level filtering).

## Name Derivation

When `Name` is null, the field name is derived automatically:

1. Pluralize the class name (naive English rules: `Player` → `Players`, `Match` → `Matches`, `Category` → `Categories`)
2. camelCase the result (`Players` → `players`)

Override with `Name` for cases where the automatic pluralization is incorrect:

```csharp
[TraxQueryModel(Name = "people")]
public class Person { ... }
```

## Custom Filter and Sort Types

By default, HotChocolate generates `FilterInputType<TEntity>` and `SortInputType<TEntity>` based on all public properties of the entity. When you need to hide properties, rename filter fields, or customize the generated input types, register custom overrides via the builder:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>()
    .AddFilterType<Player, PlayerFilterInputType>()
    .AddSortType<Player, PlayerSortInputType>());
```

Create the custom types by extending `FilterInputType<TEntity>` or `SortInputType<TEntity>`:

```csharp
public class PlayerFilterInputType : FilterInputType<Player>
{
    protected override void Configure(IFilterInputTypeDescriptor<Player> descriptor)
    {
        // Hide internal properties from the schema
        descriptor.Field(x => x.InternalMappedId).Ignore();

        // Rename a property for the public API
        descriptor.Field(x => x.MappedId).Name("playerId");
    }
}

public class PlayerSortInputType : SortInputType<Player>
{
    protected override void Configure(ISortInputTypeDescriptor<Player> descriptor)
    {
        descriptor.Field(x => x.InternalMappedId).Ignore();
        descriptor.Field(x => x.MappedId).Name("playerId");
    }
}
```

When an override is registered, it replaces the default for that entity only. Entities without overrides continue to use the auto-generated types.

## Case-Insensitive Filtering

The auto-generated string filters (`contains`, `eq`, `startsWith`, ...) are case-sensitive, since they map to plain `LIKE` / `=` on a deterministic collation. To add case-insensitive operators, opt in with `ConfigureFiltering`:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>()
    .ConfigureFiltering(filter => filter.AddCaseInsensitiveStringOperations()));
```

This adds `icontains` (case-insensitive substring) and `ieq` (case-insensitive equality) to every string filter input, including `ExposeAs`-projected and custom filter types. The existing case-sensitive operators are unchanged; a client opts in per query by choosing the operator. See [ConfigureFiltering](/docs/sdk-reference/graphql-api/configure-filtering) for the translation, indexing, and extension details.

## Scalar Collections (PostgreSQL Arrays)

A property typed as a collection of scalars (`Badge[]`, `string[]`, `List<int>`) maps to a
PostgreSQL array column and is filtered with array containment. This fits a small bounded
set of values that carries no data of its own, such as roles or feature flags on a row.
If the membership itself needs fields (`GrantedAt`, `GrantedBy`), use a junction table
instead and expose that as its own query model.

```csharp
public enum Badge { Founder, Veteran, Champion }

[TraxQueryModel(Namespace = "players")]
[Table("player_records", Schema = "game")]
public class PlayerRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("badges")]
    public Badge[] Badges { get; set; } = [];
}
```

The DbContext maps the enum and declares the index. Both matter, for different reasons
(see [The GIN index declaration changes the SQL](#the-gin-index-declaration-changes-the-sql)):

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder options) =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MapEnum<Badge>("badge", "game"));

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasPostgresEnum<Badge>(schema: "game");
    modelBuilder.Entity<PlayerRecord>().HasIndex(x => x.Badges).HasMethod("gin");
}
```

The column and its index are created by a migration, like any other table.

### Generated filter surface

The schema exposes the enum by name and the collection as a list filter:

```graphql
enum Badge { FOUNDER, VETERAN, CHAMPION }

input ListBadgeElementFilterInput {
  all: BadgeElementFilterInput
  none: BadgeElementFilterInput
  some: BadgeElementFilterInput
  any: Boolean
}

input BadgeElementFilterInput {
  eq: Badge
  in: [Badge!]
  nin: [Badge!]
}
```

A collection of a nullable scalar (`int?[]`) is the exception: HotChocolate's comparable
filter input is constrained to non-nullable value types, so those keep the stock input,
`neq` included.

Set membership is expressed with the standard list operators, and each one reaches
PostgreSQL as an array operator a GIN index can serve:

| Query | SQL | Uses GIN |
|-------|-----|----------|
| `badges: { some: { eq: CHAMPION } }` | `badges @> ARRAY[$v]` | Yes |
| `badges: { some: { in: [A, B] } }` | `badges && ARRAY[$v]` | Yes |
| `and: [{ badges: { some: { eq: A } } }, { badges: { some: { eq: B } } }]` | `badges @> ARRAY[$a] AND badges @> ARRAY[$b]` | Yes |
| `badges: { all: { in: [A, B] } }` | `badges <@ ARRAY[$v]` | Yes |
| `badges: { none: { eq: A } }` | `NOT (badges @> ARRAY[$v])` | No, negated |
| `badges: { any: false }` | `cardinality(badges) = 0` | No |

Those first three cover "contains", "contains any" and "contains all" respectively. There
is no separate `contains` operator, because `some: { eq: }` already compiles to exactly
the containment operator a hand-written one would emit.

```graphql
query PlayersHoldingBothBadges {
  discover {
    players {
      playerRecords(
        where: {
          and: [
            { badges: { some: { eq: CHAMPION } } }
            { badges: { some: { eq: VETERAN } } }
          ]
        }
      ) {
        totalCount
        nodes { id badges }
      }
    }
  }
}
```

### The GIN index declaration changes the SQL

`HasIndex(...).HasMethod("gin")` does more than describe the database. Npgsql reads it
when it compiles a single-value membership filter and picks a different operator:

| EF model | `some: { eq: X }` compiles to | Plan |
|----------|-------------------------------|------|
| `HasIndex(x => x.Badges).HasMethod("gin")` | `badges @> ARRAY[$v]` | Index scan |
| No index declared | `$v = ANY(badges)` | Sequential scan, no index can serve it |

Both return identical rows, and the GraphQL schema, the query and the response are the
same either way. Only the plan differs, so the omission stays invisible until the table
grows. Trax logs a warning at startup for any filterable scalar collection with no GIN
index declared in the EF model, naming the property and the fix. The multi-value
operators (`some: { in: }`, `all: { in: }`) compile to `&&` and `<@` either way and are
unaffected, so a collection filtered only those ways does not need the declaration.

The collection type is not what decides this. `Badge[]` and `List<Badge>` both map to the
same array column and behave identically; only the index declaration matters.

### `neq` is not available inside a collection

The element filter offers `eq`, `in` and `nin` but not `neq`. Inside a collection, `neq`
lowers to `Any(x => x != value)` over a primitive collection, which no EF Core provider
can translate, so it would pass GraphQL validation and then fail at execution. Trax
removes it from the element input so the query is rejected up front instead.

Scalar properties are unaffected and keep `neq`:

```graphql
# Rejected: `neq` does not exist on the element input.
where: { badges: { some: { neq: CHAMPION } } }

# Fine: `tier` is a scalar enum property, not a collection.
where: { tier: { neq: CHAMPION } }
```

The one filter this costs is `all: { neq: X }`, which did translate. `none: { eq: X }` is
exactly equivalent and still available.

## AddDbContext

Register one or more DbContext types whose `DbSet<T>` properties contain attributed entities:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>()
    .AddDbContext<InventoryDbContext>());
```

Only `DbSet<T>` properties where `T` has `[TraxQueryModel]` are exposed. Other `DbSet` properties on the same DbContext are ignored.

The DbContext must be registered in DI separately (via `AddDbContext`, `AddDbContextFactory`, or `AddPooledDbContextFactory`).

## vs TraxQuery

| | `[TraxQuery]` | `[TraxQueryModel]` |
|-|--------------|-------------------|
| **Target** | Train class (workflow) | Entity class (data model) |
| **Resolves via** | `ITrainBus.RunAsync` | `DbContext.Set<T>()` → IQueryable |
| **Input** | Typed input DTO | Filter/sort/page arguments (auto-generated) |
| **Output** | Typed output DTO | Entity properties (with projection) |
| **Use case** | Business logic, computed results | Direct CRUD reads, admin dashboards |
| **Schema location** | `discover { trainName(input: ...) }` | `discover { modelNames(first: ..., where: ...) }` |

Both appear under the `discover` namespace in the GraphQL schema.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ConfigureFiltering](/docs/sdk-reference/graphql-api/configure-filtering) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards)
