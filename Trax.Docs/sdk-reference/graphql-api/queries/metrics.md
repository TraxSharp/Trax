---
title: "operations.metrics queries"
description: "Reference for the operations.metrics query namespace: the dashboard's KPI and chart data, the process snapshot and the CPU sampler."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 8
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/MetricsQueries.cs
  - Trax.Api/src/Trax.Api/Services/Metrics/ProcessCpuSampler.cs
---

# operations.metrics queries

`MetricsQueries`, the type behind `operations { metrics { ... } }`.

The `operations.metrics` namespace returns the data behind the dashboard's KPI cards, charts, and server health panel. Every field comes from the shared `IOperationsService`, so the GraphQL response and the dashboard render exactly the same numbers.

These are the dashboard's reads, behind the operations gate. A monitor that only needs to know whether the host is healthy reads `/trax/health` ([AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check)), not GraphQL.

## dashboard

```graphql
query {
  operations {
    metrics {
      dashboard(range: LAST24_HOURS, hideAdminTrains: true) {
        kpis { executionsToday successRate currentlyRunning unresolvedDeadLetters }
        executionsOverTime { timestamp completed failed cancelled }
        topFailures { trainName count }
        topAverageDurations { trainName averageMilliseconds }
        throughputSeries {
          trainName
          buckets { timestamp count }
        }
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `range` | `MetricsRange` | `LAST24_HOURS` | Granularity of the executions-over-time chart. `LAST60_MINUTES` returns 60 buckets (1 minute each); `LAST24_HOURS` returns 24 buckets (1 hour each). The other series are always over the last 7 days |
| `hideAdminTrains` | `Boolean` | `false` | When `true`, framework admin trains (matching `AdminTrains.FullNames`) are excluded from every series |

**Returns**: `DashboardMetrics`.

### DashboardMetrics fields

| Field | Type | Description |
|-------|------|-------------|
| `kpis` | `DashboardKpis!` | Today's headline counts |
| `executionsOverTime` | `[ExecutionsBucket!]!` | Per-bucket counts at the requested granularity |
| `topFailures` | `[TrainFailureCount!]!` | Top 10 trains by failure count over the last 7 days |
| `topAverageDurations` | `[TrainAverageDuration!]!` | Top 10 trains by average duration over the last 7 days (root-level, completed executions only). Averaged in the database on every provider, Sqlite included |
| `throughputSeries` | `[ThroughputSeries!]!` | Top-3 trains plus an `"Other"` series, 28 6-hour buckets covering 7 days. Empty series are dropped |

### DashboardKpis fields

| Field | Type | Description |
|-------|------|-------------|
| `executionsToday` | `Int!` | Total executions started today (UTC) |
| `successRate` | `Float!` | `Completed / (Completed + Failed)` as a percentage. Zero when no terminal executions exist today |
| `currentlyRunning` | `Int!` | Executions currently in `InProgress` |
| `unresolvedDeadLetters` | `Int!` | Dead letters in `AwaitingIntervention` |

### ExecutionsBucket fields

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | `DateTime!` | UTC start of the bucket |
| `completed` | `Int!` | Completed executions in the bucket |
| `failed` | `Int!` | Failed executions |
| `cancelled` | `Int!` | Cancelled executions |

### TrainFailureCount fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName |
| `count` | `Int!` | Failures over the last 7 days |

### TrainAverageDuration fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName |
| `averageMilliseconds` | `Float!` | Mean execution time over completed root-level runs in the last 7 days |

### ThroughputSeries fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName, or the literal string `"Other"` for the aggregated remainder series |
| `buckets` | `[ThroughputBucket!]!` | 28 6-hour buckets, oldest first |

### ThroughputBucket fields

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | `DateTime!` | UTC start of the bucket |
| `count` | `Int!` | Completed executions in the bucket |

`dashboard` runs several aggregations over the last-24h and last-7-day windows on every call, and those windows hold hundreds of thousands to millions of rows at scale. The metadata table carries two covering indexes for them (`ix_metadata_metrics_state_time` and `ix_metadata_metrics_window`) so every aggregation is a heap-free index-only scan. The aggregations run at once, each on a connection of its own, so the block takes as long as the slowest of them, the 7-day throughput aggregation, rather than their sum; a call holds up to seven pooled connections while it runs. At 3,000,000 metadata rows the whole block returns in ~290-345ms, with or without `hideAdminTrains`, against ~460-700ms when they ran one after another. It is the heaviest operations read, so poll it on an interval (a few seconds) rather than on every dashboard interaction.

## server

Process-level snapshot. CPU% is not on `ServerMetrics` because it can only be measured as a delta between two samples; use the sibling `serverCpuPercent` field for that.

```graphql
query {
  operations {
    metrics {
      server { processStartTimeUtc uptimeSeconds workingSetBytes gcHeapBytes }
    }
  }
}
```

**Returns**: `ServerMetrics`.

| Field | Type | Description |
|-------|------|-------------|
| `processStartTimeUtc` | `DateTime!` | When the host process started |
| `uptimeSeconds` | `Float!` | Seconds since process start |
| `workingSetBytes` | `Long!` | `Process.WorkingSet64` |
| `gcHeapBytes` | `Long!` | `GC.GetTotalMemory(false)` |

## serverCpuPercent

This API process's CPU utilisation since the previous poll, as a percentage of total capacity (normalised by core count, so 100 means every core fully busy). CPU% is a delta measurement, so the sampler holds the previous sample: the **first** poll after startup returns `null` while the baseline primes, and each subsequent poll reports usage since the last. Poll it on the same interval as the rest of the dashboard's server panel. It reads `ProcessCpuSampler.SamplePercent()` (`Trax.Api.Services.Metrics`), a singleton that holds the previous sample.

It is a sibling of `server` rather than a field on `ServerMetrics` because the sampling state is per-process and stateful, which is exactly why it is not part of the shared `IOperationsService` snapshot the scheduler and dashboard also read.

```graphql
query {
  operations {
    metrics {
      serverCpuPercent
    }
  }
}
```

**Returns**: `Float` (nullable; `null` on the first poll or if no measurable time has elapsed since the last).

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [Dashboard](/docs/dashboard)
