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
| `Projection` | `bool` | `true` | Enables field projection — only the columns requested by the GraphQL client are selected from the database. |

## Feature Configuration

Each feature can be independently disabled per model. All default to `true`.

```csharp
// Full-featured (default)
[TraxQueryModel]
public class Player { ... }

// Pagination and filtering only — no sorting or projection
[TraxQueryModel(Sorting = false, Projection = false)]
public class AuditLog { ... }

// Simple list query — no middleware at all
[TraxQueryModel(Paging = false, Filtering = false, Sorting = false, Projection = false)]
public class StatusCode { ... }
```

When `Paging = false`, the field returns a plain list (`[Entity!]!`) instead of a Connection type.

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

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
