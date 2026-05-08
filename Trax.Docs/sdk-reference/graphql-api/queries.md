---
layout: default
title: Queries
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 2
---

# Queries

Queries are organized into two groups under the root `Query` type:

```graphql
type Query {
  discover: DiscoverQueries!
  operations: OperationsQueries!  # only when ExposeOperationQueries() is set
}
```

- **`discover`**: auto-generated typed query fields for trains annotated with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
- **`operations`**: predefined operational queries: health status, registered trains, manifests, manifest groups, execution history, and the nested `deadLetters` namespace. **Off by default**, opt in with [`ExposeOperationQueries()`](/docs/sdk-reference/graphql-api/add-trax-graphql) on the builder.

## Discover Queries (Auto-Generated)

Trax auto-generates strongly-typed query fields for trains that opt in with `[TraxQuery]`. Only trains with this attribute appear under `discover`.

Each whitelisted query train gets a single field named after the train (no prefix). The field accepts a strongly-typed `input` argument and returns the train's output type directly. Trains with `Namespace` set are grouped under a sub-namespace (e.g. `discover { players { lookupPlayer } }`).

### Naming Convention

The query field names are derived from the train's service interface name (or overridden via `[TraxQuery(Name = "...")]`):

1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Use the result as the field name (lowercase first letter)

For example, `ILookupPlayerTrain` produces `lookupPlayer`.

### Example

Given a train annotated with `[TraxQuery]`:

```csharp
public record LookupPlayerInput
{
    public required string PlayerId { get; init; }
}

public record LookupPlayerOutput
{
    public required string PlayerId { get; init; }
    public required int Rank { get; init; }
}
```

The schema exposes:

```graphql
query {
  discover {
    lookupPlayer(input: { playerId: "player-42" }) {
      playerId
      rank
    }
  }
}
```

### Query trains with typed output

When a query train has a non-`Unit` output type, the output type is returned directly (not wrapped in a response type):

```graphql
type DiscoverQueries {
  lookupPlayer(input: LookupPlayerInput!): LookupPlayerOutput!
}
```

### Query trains with `Unit` output

When a query train has `Unit` output, it returns a response with the execution metadata:

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |

---

## Operations Queries

### health

Returns the current health status of the Trax scheduler system. This is the same data reported by the ASP.NET `IHealthCheck` at `/trax/health`, exposed as a structured GraphQL type.

```graphql
query {
  operations {
    health {
      status
      description
      queueDepth
      inProgress
      failedLastHour
      deadLetters
    }
  }
}
```

**Returns**: `HealthStatus!`

#### HealthStatus fields

| Field | Type | Description |
|-------|------|-------------|
| `status` | `String!` | `"Healthy"` or `"Degraded"` |
| `description` | `String!` | Human-readable summary |
| `queueDepth` | `Int!` | Work items with status `Queued` |
| `inProgress` | `Int!` | Executions with `TrainState.InProgress` |
| `failedLastHour` | `Int!` | Failed executions in the last hour |
| `deadLetters` | `Int!` | Dead letters with status `AwaitingIntervention` |

Status is `Degraded` when `deadLetters > 0` or `failedLastHour > 10`.

---

### trains

Returns every train registered in the DI container, including a runtime-generated input schema describing each property on the input type. Pass `hideAdminTrains: true` to exclude the framework's internal scheduler trains (manifest manager, job dispatcher, dead letter cleanup, etc.) from the result; the dashboard uses this flag when its "Hide admin trains" toggle is on.

```graphql
query {
  operations {
    trains {
      serviceTypeName
      implementationTypeName
      inputTypeName
      outputTypeName
      lifetime
      inputSchema {
        name
        typeName
        isNullable
      }
    }
  }
}
```

**Returns**: `[TrainInfo!]!`

#### TrainInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `serviceTypeName` | `String!` | Friendly name of the service interface (e.g. `IServiceTrain<OrderInput, OrderResult>`) |
| `implementationTypeName` | `String!` | Friendly name of the concrete class |
| `inputTypeName` | `String!` | Friendly name of the input type |
| `outputTypeName` | `String!` | Friendly name of the output type |
| `lifetime` | `String!` | DI lifetime (`Singleton`, `Scoped`, `Transient`) |
| `inputSchema` | `[InputPropertySchema!]!` | Public readable properties on the input type |

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `hideAdminTrains` | `Boolean` | `false` | When `true`, filters out framework-internal scheduler trains (matches `AdminTrains.FullNames` in `Trax.Scheduler.Configuration`) |

#### InputPropertySchema fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | Property name |
| `typeName` | `String!` | Friendly type name (e.g. `String`, `Int32`, `DateTime?`) |
| `isNullable` | `Boolean!` | Whether the property is nullable |

---

### manifests

Returns a paginated list of scheduler manifests, ordered by ID descending (newest first). Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    manifests(skip: 0, take: 10) {
      items {
        id
        externalId
        name
        isEnabled
        scheduleType
        cronExpression
        intervalSeconds
        maxRetries
        timeoutSeconds
        lastSuccessfulRun
        manifestGroupId
        dependsOnManifestId
        priority
      }
      totalCount
      isEstimatedCount
      skip
      take
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip (offset pagination) |
| `take` | `Int` | `25` | Number of records to return |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. When provided, `skip` is ignored. See [Pagination](#pagination) |

**Returns**: `PagedResult<ManifestSummary>`

#### ManifestSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID |
| `externalId` | `String!` | Unique external identifier (used for upsert/trigger) |
| `name` | `String!` | Train type name |
| `isEnabled` | `Boolean!` | Whether the manifest is active |
| `scheduleType` | `ScheduleType!` | `Cron` or `Interval` |
| `cronExpression` | `String` | Cron expression (when `scheduleType` is `Cron`) |
| `intervalSeconds` | `Int` | Interval in seconds (when `scheduleType` is `Interval`) |
| `maxRetries` | `Int!` | Maximum retry count on failure |
| `timeoutSeconds` | `Int` | Execution timeout |
| `lastSuccessfulRun` | `DateTime` | Timestamp of last successful execution |
| `manifestGroupId` | `Long!` | Parent group ID |
| `dependsOnManifestId` | `Long` | ID of the manifest this one depends on |
| `priority` | `Int!` | Dispatch priority (0-31, higher runs first) |

---

### manifest

Returns a single manifest by database ID.

```graphql
query {
  operations {
    manifest(id: 42) {
      id
      externalId
      name
      isEnabled
      scheduleType
      cronExpression
      priority
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The manifest's database ID |

**Returns**: `ManifestSummary` (nullable, returns `null` if the ID does not exist)

---

### manifestGroups

Returns a paginated list of manifest groups, ordered by ID descending. Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    manifestGroups(skip: 0, take: 10) {
      items {
        id
        name
        maxActiveJobs
        priority
        isEnabled
        createdAt
        updatedAt
      }
      totalCount
      isEstimatedCount
      skip
      take
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip (offset pagination) |
| `take` | `Int` | `25` | Number of records to return |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. See [Pagination](#pagination) |

**Returns**: `PagedResult<ManifestGroupSummary>`

#### ManifestGroupSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID |
| `name` | `String!` | Group name |
| `maxActiveJobs` | `Int` | Concurrency limit for the group (null = unlimited) |
| `priority` | `Int!` | Default priority for manifests in this group |
| `isEnabled` | `Boolean!` | Whether the group is active |
| `createdAt` | `DateTime!` | When the group was created |
| `updatedAt` | `DateTime!` | When the group was last modified |

---

### executions

Returns a paginated list of train executions (metadata records), ordered by ID descending (newest first). Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    executions(skip: 0, take: 10) {
      items {
        id
        externalId
        name
        trainState
        startTime
        endTime
        failureJunction
        failureReason
        manifestId
        cancellationRequested
      }
      totalCount
      isEstimatedCount
      skip
      take
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip (offset pagination) |
| `take` | `Int` | `25` | Number of records to return |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. See [Pagination](#pagination) |

**Returns**: `PagedResult<ExecutionSummary>`

#### ExecutionSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Metadata ID |
| `externalId` | `String!` | External identifier |
| `name` | `String!` | Train type name |
| `trainState` | `TrainState!` | Current state (`Pending`, `InProgress`, `Completed`, `Failed`, `Cancelled`) |
| `startTime` | `DateTime!` | When execution began |
| `endTime` | `DateTime` | When execution finished (null if still running) |
| `failureJunction` | `String` | Name of the junction that failed (null if no failure) |
| `failureReason` | `String` | Exception message on failure |
| `manifestId` | `Long` | Associated manifest ID (null if not scheduler-initiated) |
| `cancellationRequested` | `Boolean!` | Whether cancellation was requested |

---

### execution

Returns a single execution by metadata ID.

```graphql
query {
  operations {
    execution(id: 100) {
      id
      externalId
      name
      trainState
      startTime
      endTime
      failureJunction
      failureReason
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata ID |

**Returns**: `ExecutionSummary` (nullable, returns `null` if the ID does not exist)

---

## PagedResult

All paginated queries return the same wrapper type:

| Field | Type | Description |
|-------|------|-------------|
| `items` | `[T!]!` | The page of results |
| `totalCount` | `Int!` | Total number of records matching the query |
| `skip` | `Int!` | The `skip` value that was applied |
| `take` | `Int!` | The `take` value that was applied |
| `isEstimatedCount` | `Boolean!` | `true` when `totalCount` is a fast estimate rather than an exact count. See [Pagination](#estimated-counts) |
| `nextCursor` | `Long` | ID of the last item in the page. Pass as `afterId` to fetch the next page via keyset pagination. `null` when no items are returned |

---

## Pagination

Paginated queries support two strategies. Both can be used interchangeably. The dashboard uses offset pagination internally, while API consumers can opt into keyset cursors for better deep-page performance.

### Offset pagination (default)

Pass `skip` and `take` as before. This uses SQL `OFFSET`/`LIMIT` under the hood. Performance degrades on deep pages (high `skip` values) because the database must scan and discard rows up to the offset.

```graphql
query {
  operations {
    executions(skip: 100, take: 25) { items { id } totalCount }
  }
}
```

### Keyset cursor pagination

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

### Estimated counts

For unfiltered queries on large tables (>10,000 rows), `totalCount` uses PostgreSQL's `pg_class.reltuples` statistic instead of an exact `COUNT(*)`. This is O(1) rather than O(n), and the difference matters when the metadata table has millions of rows.

When the estimate is used, `isEstimatedCount` is `true`. The estimate is updated by PostgreSQL's autovacuum/autoanalyze and is typically accurate within a few percent. For filtered queries or small tables, an exact count is always used and `isEstimatedCount` is `false`.

## config (nested under operations)

The `operations.config` namespace returns the live scheduler runtime settings (the dashboard-editable subset of `SchedulerConfiguration`, `LocalWorkerOptions`, and `MetadataCleanupConfiguration`). The dashboard's ServerSettingsPage and this query both read from the same in-memory singleton, so they agree.

Persistence: settings written via `operations.config.updateScheduler` (or the dashboard) are stored in the singleton-row `trax.scheduler_config` table and re-applied to the in-memory singleton at startup by the `SchedulerConfigBootstrapHostedService`. Settings survive restarts.

### scheduler

```graphql
query {
  operations {
    config {
      scheduler {
        manifestManagerEnabled
        jobDispatcherEnabled
        manifestManagerPollingInterval
        jobDispatcherPollingInterval
        maxActiveJobs
        defaultMaxRetries
        defaultRetryDelay
        retryBackoffMultiplier
        maxRetryDelay
        defaultJobTimeout
        stalePendingTimeout
        recoverStuckJobsOnStartup
        deadLetterRetentionPeriod
        autoPurgeDeadLetters
        localWorkerCount
        metadataCleanupInterval
        metadataCleanupRetention
      }
    }
  }
}
```

**Returns**: `SchedulerConfigSnapshot`.

#### SchedulerConfigSnapshot fields

| Field | Type | Description |
|-------|------|-------------|
| `manifestManagerEnabled` | `Boolean!` | Whether the manifest manager polling service runs |
| `jobDispatcherEnabled` | `Boolean!` | Whether the job dispatcher polling service runs |
| `manifestManagerPollingInterval` | `TimeSpan!` | How often the manifest manager polls |
| `jobDispatcherPollingInterval` | `TimeSpan!` | How often the job dispatcher polls |
| `maxActiveJobs` | `Int` | Global concurrency cap. Null means no cap |
| `defaultMaxRetries` | `Int!` | Default retry budget for new manifests |
| `defaultRetryDelay` | `TimeSpan!` | First-retry delay |
| `retryBackoffMultiplier` | `Float!` | Exponential backoff factor |
| `maxRetryDelay` | `TimeSpan!` | Upper bound on backoff |
| `defaultJobTimeout` | `TimeSpan!` | Default per-execution timeout |
| `stalePendingTimeout` | `TimeSpan!` | When pending entries are reaped |
| `recoverStuckJobsOnStartup` | `Boolean!` | Whether stuck-job recovery runs on startup |
| `deadLetterRetentionPeriod` | `TimeSpan!` | How long resolved dead letters are kept before purging |
| `autoPurgeDeadLetters` | `Boolean!` | Whether the dead letter cleanup service runs |
| `localWorkerCount` | `Int` | In-process worker thread count. Null when `UseLocalWorkers()` is not configured |
| `metadataCleanupInterval` | `TimeSpan` | Metadata cleanup poll interval. Null when cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | How long completed metadata is kept. Null when cleanup is not configured |

---

## metrics (nested under operations)

The `operations.metrics` namespace returns the data behind the dashboard's KPI cards, charts, and server health panel. Every field comes from the shared `IOperationsService`, so the GraphQL response and the dashboard render exactly the same numbers.

### dashboard

```graphql
query {
  operations {
    metrics {
      dashboard(range: LAST_24_HOURS, hideAdminTrains: true) {
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
| `range` | `MetricsRange` | `LAST_24_HOURS` | Granularity of the executions-over-time chart. `LAST_60_MINUTES` returns 60 buckets (1 minute each); `LAST_24_HOURS` returns 24 buckets (1 hour each). The other series are always over the last 7 days |
| `hideAdminTrains` | `Boolean` | `false` | When `true`, framework admin trains (matching `AdminTrains.FullNames`) are excluded from every series |

**Returns**: `DashboardMetrics`.

#### DashboardMetrics fields

| Field | Type | Description |
|-------|------|-------------|
| `kpis` | `DashboardKpis!` | Today's headline counts |
| `executionsOverTime` | `[ExecutionsBucket!]!` | Per-bucket counts at the requested granularity |
| `topFailures` | `[TrainFailureCount!]!` | Top 10 trains by failure count over the last 7 days |
| `topAverageDurations` | `[TrainAverageDuration!]!` | Top 10 trains by average duration over the last 7 days (root-level executions only) |
| `throughputSeries` | `[ThroughputSeries!]!` | Top-3 trains plus an `"Other"` series, 28 6-hour buckets covering 7 days. Empty series are dropped |

#### DashboardKpis fields

| Field | Type | Description |
|-------|------|-------------|
| `executionsToday` | `Int!` | Total executions started today (UTC) |
| `successRate` | `Float!` | `Completed / (Completed + Failed)` as a percentage. Zero when no terminal executions exist today |
| `currentlyRunning` | `Int!` | Executions currently in `InProgress` |
| `unresolvedDeadLetters` | `Int!` | Dead letters in `AwaitingIntervention` |

#### ExecutionsBucket fields

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | `DateTime!` | UTC start of the bucket |
| `completed` | `Int!` | Completed executions in the bucket |
| `failed` | `Int!` | Failed executions |
| `cancelled` | `Int!` | Cancelled executions |

#### TrainFailureCount fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName |
| `count` | `Int!` | Failures over the last 7 days |

#### TrainAverageDuration fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName |
| `averageMilliseconds` | `Float!` | Mean execution time over completed root-level runs in the last 7 days |

#### ThroughputSeries fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | Train interface FullName, or the literal string `"Other"` for the aggregated remainder series |
| `buckets` | `[ThroughputBucket!]!` | 28 6-hour buckets, oldest first |

#### ThroughputBucket fields

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | `DateTime!` | UTC start of the bucket |
| `count` | `Int!` | Completed executions in the bucket |

### server

Process-level snapshot. CPU% is intentionally not returned since it requires per-instance sampling state; consumers that need it can take two snapshots and compute it themselves.

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

---

## logs (nested under operations)

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

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip (offset pagination). Ignored when `afterId` is provided. |
| `take` | `Int` | `25` | Number of records to return |
| `metadataId` | `Long` | `null` | Filter to logs for a single execution |
| `minimumLevel` | `LogLevel` | `null` | Includes the supplied level and anything more severe. `LogLevel` follows `Microsoft.Extensions.Logging`: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `NONE` |
| `category` | `String` | `null` | Exact-match filter on the logger category (e.g. `Trax.Samples.GameServer.Trains.Combat.ResolveCombatTrain`) |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId` |

**Returns**: `PagedResult<LogEntry>`.

When any filter or `afterId` is supplied, the count is exact (`isEstimatedCount: false`). Unfiltered first-page reads use the same `pg_class.reltuples` estimator as the other large-table queries because the log table grows quickly.

#### LogEntry fields

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

---

## manifestGroups (nested under operations)

The `operations.manifestGroups` namespace exposes queries scoped to manifest groups beyond the top-level `manifestGroups` paged list. Currently this is the cross-group dependency graph that the dashboard renders as a DAG.

### graph

Returns the 1-hop cross-group dependency neighborhood for a manifest group: every group containing a manifest the focal group's manifests depend on (upstream), every group containing a manifest depending on the focal group's manifests (downstream), and the focal group itself. Edges are directed parent → dependent.

```graphql
query {
  operations {
    manifestGroups {
      graph(groupId: 7) {
        nodes { id name isHighlighted }
        edges { fromId toId }
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | Database ID of the focal manifest group |

**Returns**: `ManifestGroupDependencyGraph` (nullable). Returns `null` only when the group does not exist. Empty groups still return a single-node graph (focal group, no edges) so the UI can render the focal node.

#### ManifestGroupDependencyGraph fields

| Field | Type | Description |
|-------|------|-------------|
| `nodes` | `[DependencyGraphNode!]!` | All groups in the neighborhood plus the focal group |
| `edges` | `[DependencyGraphEdge!]!` | Cross-group edges only. Same-group dependencies are excluded |

#### DependencyGraphNode fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Manifest group ID |
| `name` | `String!` | Group name |
| `isHighlighted` | `Boolean!` | `true` for the focal group; the UI uses this to render it differently |

#### DependencyGraphEdge fields

| Field | Type | Description |
|-------|------|-------------|
| `fromId` | `Long!` | Parent group ID (the group whose manifests are depended on) |
| `toId` | `Long!` | Dependent group ID |

---

## workQueue (nested under operations)

The `operations.workQueue` namespace exposes paginated reads of the work queue. The work queue is the intermediary between scheduling and dispatch: every queued execution (manifest triggers, dashboard re-runs, dead-letter requeues, GraphQL `queueTrain` calls) lands here as a `Queued` row that the JobDispatcher picks up.

```graphql
query {
  operations {
    workQueue {
      workQueues(skip: 0, take: 25, status: QUEUED) {
        items {
          id
          externalId
          trainName
          status
          createdAt
          dispatchedAt
          scheduledAt
          priority
          dispatchAttempts
          manifestId
          metadataId
          deadLetterId
          inputTypeName
        }
        totalCount
        isEstimatedCount
        nextCursor
      }
    }
  }
}
```

### workQueues

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip (offset pagination). Ignored when `afterId` is provided. |
| `take` | `Int` | `25` | Number of records to return |
| `status` | `WorkQueueStatus` | `null` | Filter by lifecycle state (`QUEUED`, `DISPATCHED`, `CANCELLED`) |
| `trainName` | `String` | `null` | Exact-match filter on the interface FullName (e.g. `Trax.Samples.GameServer.Trains.Combat.IResolveCombatTrain`) |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. See [Pagination](#pagination) |

**Returns**: `PagedResult<WorkQueueSummary>`

When any filter or `afterId` is supplied, the count is exact and `isEstimatedCount` is `false`. Unfiltered first-page reads use the same fast estimator as the other large-table queries.

#### WorkQueueSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID |
| `externalId` | `String!` | GUID assigned at creation |
| `trainName` | `String!` | Train interface FullName |
| `status` | `WorkQueueStatus!` | `QUEUED`, `DISPATCHED`, or `CANCELLED` |
| `createdAt` | `DateTime!` | When the entry was queued |
| `dispatchedAt` | `DateTime` | When the dispatcher picked it up (null while queued or if cancelled before dispatch) |
| `scheduledAt` | `DateTime` | Earliest dispatch time. Null means dispatch immediately |
| `priority` | `Int!` | Dispatch priority 0-31 |
| `dispatchAttempts` | `Int!` | Number of times dispatch was attempted and failed |
| `manifestId` | `Long` | Source manifest ID, if scheduled |
| `metadataId` | `Long` | Metadata ID created at dispatch, if dispatched |
| `deadLetterId` | `Long` | Dead letter that triggered this requeue, if applicable |
| `inputTypeName` | `String` | Fully qualified type name of the input, for deserialization |

### workQueue (single)

Returns a single entry by database ID.

```graphql
query {
  operations {
    workQueue {
      workQueue(id: 42) { id status priority dispatchAttempts }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The work queue entry's database ID |

**Returns**: `WorkQueueSummary` (nullable, returns `null` if the ID does not exist).

---

## deadLetters (nested under operations)

The `operations.deadLetters` namespace exposes paginated dead-letter reads (`deadLetters`, `deadLetter`). See [scheduler/dead-letters-and-cleanup](/docs/scheduler/dead-letters-and-cleanup) for the full surface and examples.
