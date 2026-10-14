---
title: Query models and query trains
description: "How a TraxQueryModel entity and a TraxQuery train differ as GraphQL read surfaces: what each resolves through, takes, returns and suits."
type: explanation
area: api
nav_order: 24
---

# Query models and query trains

Trax has two ways to put a read under `discover`. A [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
train runs business logic and returns a computed result. A
[`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) entity is a table read, with the
paging, filtering and sorting arguments generated from the entity.

| | `[TraxQuery]` | `[TraxQueryModel]` |
|-|--------------|-------------------|
| **Target** | Train class (workflow) | Entity class (data model) |
| **Resolves via** | `ITrainBus.RunAsync` | `DbContext.Set<T>()` → IQueryable |
| **Input** | Typed input DTO | Filter/sort/page arguments (auto-generated) |
| **Output** | Typed output DTO | Entity properties (with projection) |
| **Use case** | Business logic, computed results | Direct CRUD reads, admin dashboards |
| **Schema location** | `discover { trainName(input: ...) }` | `discover { modelNames(first: ..., where: ...) }` |

Both appear under the `discover` namespace in the GraphQL schema.

A query train is the place for a read that computes, checks or combines; it has a typed input, runs
as a train with its effects, and the caller sees only the output type. A query model is the place
for a read the caller shapes: which rows, in what order, which columns. Projection means only the
selected columns are read, so a wide table costs what the query asks for.
