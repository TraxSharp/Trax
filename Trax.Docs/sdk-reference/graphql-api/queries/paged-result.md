---
title: "PagedResult"
description: "Reference for PagedResult, the page type every paged operations query returns, and its offset and keyset pagination, page size limits and estimated counts."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 12
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsPageBounds.cs
  - Trax.Api/src/Trax.Api.GraphQL/Queries/CountEstimator.cs
---

# PagedResult

The wrapper every paged query under `operations` returns, and the arguments those queries page with.

All paginated queries return the same wrapper type:

| Field | Type | Description |
|-------|------|-------------|
| `items` | `[T!]!` | The page of results |
| `totalCount` | `Int!` | Total number of records matching the query's filters, the same on every page: neither `skip` nor `afterId` changes it |
| `skip` | `Int!` | The `skip` value that was applied, after a negative value was read as `0` |
| `take` | `Int!` | The `take` value that was applied, after clamping to 1 through 500 |
| `isEstimatedCount` | `Boolean!` | `true` when `totalCount` is a fast estimate rather than an exact count. See [Pagination](#estimated-counts) |
| `isCountCapped` | `Boolean!` | `true` when `totalCount` stopped at its cap, so the list holds at least that many and probably more: show it as "10,000+". A lower bound, not an estimate. Only [`logs`](/docs/sdk-reference/graphql-api/queries/logs) filtered by text and [`executions`](/docs/sdk-reference/graphql-api/queries/executions#executions) filtered by `failureReasonContains` cap their count; every other list returns `false` |
| `nextCursor` | `Long` | ID of the last item in the page. Pass as `afterId` to fetch the next page via keyset pagination. `null` when no items are returned |

## Pagination

Paginated queries support two strategies. Both can be used interchangeably. The dashboard uses offset pagination internally, while API consumers can opt into keyset cursors for better deep-page performance.

## Page size

Every paged read in the `operations` namespace clamps `take` to 1 through 500 and reads a negative `skip` as `0`, rather than refusing the request. A `take` above 500 returns 500 rows, and `take: 0` or a negative `take` returns one row. The `skip` and `take` on the returned page are the values that were applied, so a client can see that its request was clamped. To read more than 500 rows, page with `afterId`.

`skip` is at most 10,000. A deeper one is refused rather than clamped, because there is no nearby page to serve instead: the field fails with a GraphQL error whose `extensions.code` is `TRAX_SKIP_TOO_DEEP` and whose `extensions.maxSkip` is `10000`, and the message points at `afterId`.

```json
{
  "errors": [{
    "message": "skip may be at most 10000. To read further, page with afterId: pass each page's nextCursor as the next request's afterId.",
    "extensions": { "code": "TRAX_SKIP_TOO_DEEP", "maxSkip": 10000 }
  }]
}
```

## Offset pagination (default)

Pass `skip` and `take` as before. This uses SQL `OFFSET`/`LIMIT` under the hood. Performance degrades on deep pages (high `skip` values) because the database must scan and discard rows up to the offset, which is why `skip` stops at 10,000.

```graphql
query {
  operations {
    executions(skip: 100, take: 25) { items { id } totalCount }
  }
}
```

## Keyset cursor pagination

Pass `afterId` (the `nextCursor` from the previous page) instead of `skip`. This uses `WHERE id < @afterId`, which is constant-time regardless of how deep you paginate because it seeks directly to the cursor position via the primary key index.

```graphql
# First page
query {
  operations {
    executions(take: 25) { items { id } totalCount nextCursor }
  }
}

# Next page: pass nextCursor as afterId
query {
  operations {
    executions(afterId: 4201, take: 25) { items { id } totalCount nextCursor }
  }
}
```

When `afterId` is provided, `skip` is ignored.

## Estimated counts

`totalCount` is the size of the whole list the filters select, whichever page you are on: the cursor and `skip` never change it. This is the convention HotChocolate's own connections and GitHub's API follow.

For an unfiltered list of a large table (10,000 rows or more), `totalCount` is the database's own row estimate instead of an exact `COUNT(*)`, which would scan every row on every page. The provider supplies it through `ISqlDialect.EstimateRowCount`: on PostgreSQL it is `pg_class.reltuples`, which `ANALYZE`, `VACUUM` and autovacuum keep current and which is typically within a few percent. When the estimate is used, `isEstimatedCount` is `true`, on the first page and every later one.

The count is exact, and `isEstimatedCount` is `false`, whenever a filter is supplied, when the table is smaller than that, when PostgreSQL has never analyzed the table, and on providers that keep no estimate (SQLite, and the in-memory provider).

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
