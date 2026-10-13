---
title: "operations.logs queries"
description: "Reference for the operations.logs query namespace: paged, filtered reads of the trax.log table."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 9
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/LogQueries.cs
---

# operations.logs queries

`LogQueries`, the type behind `operations { logs { ... } }`.

The `operations.logs` namespace returns paginated reads of `trax.log`, the framework's per-execution log table. The dashboard's Logs page is backed by this query. Logs are written by the framework, never by API consumers, so there are no log mutations.

```graphql
query {
  operations {
    logs {
      logs(skip: 0, take: 50, minimumLevel: WARNING) {
        items { id metadataId eventId level category message exception stackTrace }
        totalCount
        isEstimatedCount
        nextCursor
      }
    }
  }
}
```

A run's log, oldest first as its page reads it, filtered by text:

```graphql
query {
  operations {
    logs {
      logs(metadataId: 1234, order: OLDEST, messageContains: "timeout", take: 100) {
        items { id level category message }
        totalCount
        isCountCapped
        nextCursor
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0`. Ignored when `afterId` is provided. |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `metadataId` | `Long` | `null` | Filter to logs for a single execution |
| `minimumLevel` | `LogLevel` | `null` | Includes the supplied level and anything more severe. `LogLevel` follows `Microsoft.Extensions.Logging`: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `NONE` |
| `category` | `String` | `null` | Exact-match filter on the logger category (e.g. `MyApp.Trains.Billing.ChargeCustomerTrain`) |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`, or `id > afterId` when `order: OLDEST` |
| `messageContains` | `String` | `null` | Only records whose message contains this text anywhere, ignoring case. `%`, `_` and `\` match themselves |
| `categoryContains` | `String` | `null` | Only records whose logger category contains this text, matched as `messageContains` is |
| `order` | `SortOrder!` | `NEWEST` | `NEWEST` (id descending) or `OLDEST` (id ascending, as a run's log reads). `afterId` pages in the chosen direction |

**Returns**: `PagedResult<LogEntry>`.

The filters, the order and the counts go through `IOperationsService` in Trax.Scheduler, the same
`LogQuery` the dashboard's Logs and run pages read with, so both apply one filter.

The count depends on the filter. Unfiltered, it is the same `pg_class.reltuples` estimate as the
other large-table queries (`isEstimatedCount: true`), because the log table grows quickly. With
`messageContains` or `categoryContains` it counts at most 10,000 matches: past that `totalCount` is
`10000` and `isCountCapped` is `true`, so a client shows "10,000+" rather than waiting on a count of
millions of rows. Under any other filter, or a text filter matching 10,000 or fewer, it is exact.

On Postgres the text filters are served by trigram indexes on `lower(message)` and
`lower(category)`. At 3,000,000 log rows a page with its count takes a few milliseconds for a term
that matches nothing, under 70 ms for one that matches often, and under 200 ms for one that matches a
single row. A term that matches often but only in old rows takes under 50 ms newest first: a first
or cursor page with a text filter reads the 10,000 ids nearest where it starts in order, then finds
the rest of its matches through the index, so it does not walk every newer row.

### LogEntry fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID (monotonic, used as the keyset cursor) |
| `metadataId` | `Long!` | The execution this log line belongs to |
| `eventId` | `Int!` | `EventId` from the `ILogger` call site |
| `level` | `LogLevel!` | Severity |
| `category` | `String!` | Logger category, typically the originating type name |
| `message` | `String!` | Truncated to 4000 chars at write time |
| `exception` | `String` | Exception message if any (truncated to 2000 chars) |
| `stackTrace` | `String` | Stack trace if any (truncated to 4000 chars) |

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [PagedResult](/docs/sdk-reference/graphql-api/queries/paged-result)
