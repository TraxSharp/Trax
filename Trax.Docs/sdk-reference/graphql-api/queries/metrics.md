---
title: "operations.metrics queries"
description: "Reference for operations.metrics: the dashboard's KPIs and charts, run series per train, manifest or group, the process snapshot and the CPU sampler."
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

## series

Runs over time for one train, manifest or group: for each bucket of the range, how many runs that started in it completed, failed or were cancelled, and the median and 95th percentile duration of the completed ones.

```graphql
query {
  operations {
    metrics {
      series(scope: TRAIN, name: "MyApp.Trains.IImportTrain", range: LAST7_DAYS) {
        from
        to
        bucketSeconds
        buckets { start completed failed cancelled p50DurationMs p95DurationMs }
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `scope` | `MetricSeriesScope!` | | `TRAIN`, `MANIFEST` or `GROUP` |
| `range` | `MetricSeriesRange!` | | `LAST_HOUR` (60 one-minute buckets), `LAST24_HOURS` (96 of 15 minutes) or `LAST7_DAYS` (168 of an hour). `LAST30_DAYS` (120 of 6 hours) is refused, see below |
| `id` | `Long` | `null` | The manifest's or group's id, for `MANIFEST` or `GROUP` |
| `name` | `String` | `null` | The train's interface FullName, for `TRAIN` |

A `TRAIN` scope without `name`, or a `MANIFEST` or `GROUP` scope without `id`, is refused with the error code `TRAX_METRICS_SERIES_ARGUMENT` (`MetricsQueries.SeriesArgumentCode`), before anything is read. So is a range no scope can serve inside the dashboard's metrics budget on the existing indexes, as `OperationsService.MetricSeriesRefusal` decides: today that is `LAST30_DAYS`, for every scope. A 30-day series reads every run of the 30 days, and over a million runs in one train, manifest or group it took 560 to 660 ms on the stress fixture, against a 450 ms budget; the 7-day series of the same scope (about 230,000 runs) took 170 to 340 ms. The value stays in the enum so a later index can serve it without a schema change.

**Returns**: `MetricSeries`, read through [`IOperationsService.GetMetricSeriesAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#getmetricseriesasync).

### MetricSeries fields

| Field | Type | Description |
|-------|------|-------------|
| `scope` | `MetricSeriesScope!` | What the runs are of |
| `range` | `MetricSeriesRange!` | The window |
| `from` | `DateTime!` | The first bucket's start (UTC) |
| `to` | `DateTime!` | The last bucket's end: the end of the bucket holding now |
| `bucketSeconds` | `Int!` | How long each bucket is |
| `buckets` | `[MetricSeriesBucket!]!` | Every bucket in the window, oldest first, empty ones included |

### MetricSeriesBucket fields

| Field | Type | Description |
|-------|------|-------------|
| `start` | `DateTime!` | The bucket's start (UTC). A run belongs to the bucket its start time falls in |
| `completed` | `Int!` | Runs that completed |
| `failed` | `Int!` | Runs that failed |
| `cancelled` | `Int!` | Runs that were cancelled. Runs still pending or in progress are not counted |
| `p50DurationMs` | `Float` | The median duration of the completed runs, or `null` when none completed |
| `p95DurationMs` | `Float` | The 95th percentile duration of the completed runs, or `null` |

The percentiles are read from a histogram the database builds, with bins about 13% wide, so each is within one bin of the exact value and never outside the durations recorded; a bucket whose completed runs share one bin reads them exactly when they are equal. They are computed the same way on Postgres, Sqlite and InMemory.

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
