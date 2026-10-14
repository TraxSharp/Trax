---
title: ConfigureFiltering
description: "Reference for ConfigureFiltering and TraxFilterBuilder, which add opt-in filter operators such as case-insensitive string matching on query models."
type: reference
area: api
package: Trax.Api.GraphQL
namespace:
  - Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder
  - Trax.Api.GraphQL.Configuration.TraxFilterBuilder
  - Trax.Api.GraphQL.Filtering
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 8
source:
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TraxFilterBuilder
  - Trax.Api/src/Trax.Api.GraphQL/Filtering/ITraxFilterModule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Filtering/CaseInsensitiveStringFilterModule.cs
  - Trax.Api/src/Trax.Api.GraphQL/Filtering/ListElements/ListElementFilterBinding.cs
---

# ConfigureFiltering

A `TraxGraphQLBuilder` method that layers opt-in operators onto HotChocolate's filter convention.
Stock filtering is the default; `ConfigureFiltering` adds operators without changing the existing
ones.

## Signature

```csharp fragment
public TraxGraphQLBuilder ConfigureFiltering(
    Func<TraxFilterBuilder, TraxFilterBuilder> configure)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Func<TraxFilterBuilder, TraxFilterBuilder>` | Yes | Builds the set of filter operations to register. Each `TraxFilterBuilder` method adds one operation module. |

### TraxFilterBuilder methods

| Method | Description |
|--------|-------------|
| `AddCaseInsensitiveStringOperations()` | Adds `icontains` and `ieq` to every string filter input in the schema. |

## Returns

The `TraxGraphQLBuilder`, for chaining.

## Behavior

`ConfigureFiltering` has an effect only when at least one `[TraxQueryModel]` entity has filtering
on (the default). With operations registered and filtering off everywhere, `Build()` throws
`InvalidOperationException`, since the operations would never be added. Calling it more than once,
or calling the same method twice, registers each module once.

Each operator set is an `ITraxFilterModule`, whose `Apply(IFilterConventionDescriptor)` registers
its operation ids, GraphQL names, input fields and expression handlers onto the convention.

### Case-insensitive string operations

`AddCaseInsensitiveStringOperations()` adds two operators beside the stock `contains` and `eq`,
which stay case-sensitive:

| Operator | Meaning | Translates to |
|----------|---------|---------------|
| `icontains` | Case-insensitive substring match | `lower(col) LIKE lower(@p)` |
| `ieq` | Case-insensitive equality | `lower(col) = lower(@p)` |

Both fold with SQL `lower()`, not Postgres `ILIKE`, on every provider (Npgsql, SQL Server, SQLite,
in-memory). A `lower(col)` expression index serves `lower(col) = lower(@p)` and
`lower(col) LIKE 'prefix%'`. The operators are registered on the convention, so they appear on
every string filter input, including [`ExposeAs`](/docs/sdk-reference/graphql-api/query-models#exposeas)-projected
inputs and [`AddFilterType`](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types)
overrides.

### Indexing

A `lower()` predicate reads the whole table without a matching expression index. Trax creates no
index for it; the index that serves each operator:

| Operator | Index |
|----------|-------|
| `icontains` | `gin (lower(col) gin_trgm_ops)` (needs the `pg_trgm` extension) |
| `ieq` | `btree (lower(col))` |

### Scalar collection elements

Filter inputs for the elements of a scalar collection (`Badge[]`, `string[]`, `List<int>`)
are restricted automatically, with no configuration. They expose `eq`, `in`, `nin` and the
comparable operators, but not `neq`.

Inside a collection, `neq` lowers to `Any(x => x != value)` over a primitive collection,
which no EF Core provider can translate, so the element position has an input type of its own:

```graphql
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

`all: { neq: X }` is the only filter this removes. `none: { eq: X }` means the same thing
and is still available. Scalar properties keep the full operation set including `neq`.

A collection of a nullable scalar (`int?[]`, `Badge?[]`) shares the input of its underlying
type, as it does in stock HotChocolate, so it has no `neq` either. `eq: null` matches an element
that is null.

The restricted types are named `{Scalar}ElementFilterInput` rather than reusing
HotChocolate's `{Scalar}OperationFilterInput`, so a restricted type never collides with a
stock input of the same scalar that another part of the schema still uses.

The restriction applies to the auto-generated filter input, to
[`ExposeAs`](/docs/sdk-reference/graphql-api/query-models#exposeas)-projected inputs, and
to custom [`AddFilterType`](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types)
overrides, because it is bound by property type rather than per field. Navigation
collections are untouched: their elements are entities, filtered by the entity's own
filter input.

See [Scalar collections](/docs/sdk-reference/graphql-api/query-models#scalar-collections-postgresql-arrays)
for the operator table and the GIN index behaviour.

## Example

```csharp compile
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

## See also

- [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
- [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
