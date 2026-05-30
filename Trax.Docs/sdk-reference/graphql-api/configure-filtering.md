---
layout: default
title: ConfigureFiltering
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 7
---

# ConfigureFiltering

Layers opt-in operators onto HotChocolate's filter convention. HotChocolate's stock filtering is the default; `ConfigureFiltering` adds operators on top of it without changing the existing ones, so nothing changes for current consumers unless you call it.

```csharp
public TraxGraphQLBuilder ConfigureFiltering(
    Func<TraxFilterBuilder, TraxFilterBuilder> configure)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Func<TraxFilterBuilder, TraxFilterBuilder>` | Yes | Builds the set of filter operations to register. Each call to a `TraxFilterBuilder` method adds one operation module. |

`ConfigureFiltering` only has an effect when at least one `[TraxQueryModel]` entity has filtering enabled (the default). If you register operations but filtering is disabled everywhere, `Build()` throws `InvalidOperationException`, since the convention would never be added and the operations would be silently dropped.

Calling `ConfigureFiltering` more than once, or calling the same operation method twice, registers each module once. Repeats are ignored.

## TraxFilterBuilder Methods

| Method | Description |
|--------|-------------|
| `AddCaseInsensitiveStringOperations()` | Adds `icontains` and `ieq` to every string filter input in the schema. |

## Case-insensitive string operations

`AddCaseInsensitiveStringOperations()` adds two operators to the string filter input alongside the stock `contains` / `eq`:

| Operator | Meaning | Translates to |
|----------|---------|---------------|
| `icontains` | Case-insensitive substring match | `lower(col) LIKE lower(@p)` |
| `ieq` | Case-insensitive equality | `lower(col) = lower(@p)` |

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>()
    .ConfigureFiltering(filter => filter.AddCaseInsensitiveStringOperations()));
```

```graphql
query {
  discover {
    players(where: { displayName: { icontains: "wall" } }) {
      nodes { displayName }
    }
  }
}
```

The existing `contains` and `eq` stay case-sensitive and unchanged. A client opts in per query by choosing the operator.

The operators are registered on the filter convention, so they appear on every string filter input, including [`ExposeAs`](/docs/sdk-reference/graphql-api/query-models#exposeas)-projected inputs and custom [`AddFilterType`](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types) overrides.

### Why `lower()` and not `ILIKE`

Both operators fold with SQL `lower()` rather than Postgres `ILIKE`. `lower()` works on every provider (Npgsql, SQL Server, SQLite, and the InMemory provider used in tests), where `ILIKE` is Postgres-only. It also stays sargable: `lower(col) = lower(@p)` and `lower(col) LIKE 'prefix%'` can both use a `lower(col)` expression index, while `ILIKE` cannot use a btree at all.

### Indexing

`lower()` predicates seq-scan without a matching expression index. For hot, large tables, add the index that fits the operator:

| Operator | Index |
|----------|-------|
| `icontains` | `gin (lower(col) gin_trgm_ops)` (needs the `pg_trgm` extension) |
| `ieq` | `btree (lower(col))` |

Trax does not own migrations, so create these alongside your own schema. Without them the operators still work; they just seq-scan, which is fine for small tables.

## Extending

`ConfigureFiltering` is the extension point for future filter operators. Each operator set is an `ITraxFilterModule` that registers its operation IDs, GraphQL names, input-type fields, and queryable expression handlers onto the convention. A new capability is a new module plus a new `TraxFilterBuilder` method.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
