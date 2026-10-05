---
layout: default
title: Queries
description: "Reference for the Trax GraphQL queries: generated discover fields for TraxQuery trains and the opt-in operations queries for health, trains, manifests and more."
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
      fullName
      serviceTypeName
      implementationTypeName
      inputTypeName
      outputTypeName
      lifetime
      inputSchema {
        name
        typeName
        isNullable
        enumValues
      }
    }
  }
}
```

**Returns**: `[TrainInfo!]!`

#### TrainInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `fullName` | `String!` | The train's canonical name, its service interface's FullName (e.g. `MyApp.Trains.IProcessOrderTrain`). This is the name every field that takes a train accepts: [`queueTrain`](/docs/sdk-reference/graphql-api/mutations#queuetrain) and [`runTrain`](/docs/sdk-reference/graphql-api/mutations#runtrain), [`trainStats`](#trainstats), and the `trainName` filters on [`executions`](#executions) and `workQueues` |
| `serviceTypeName` | `String!` | Friendly name of the service interface (e.g. `IServiceTrain<OrderInput, OrderResult>`), for display. No field that takes a train accepts it; use `fullName` |
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
| `name` | `String!` | The JSON name the input reader expects: the property name under the system JSON naming policy (camelCase by default), or its `[JsonPropertyName]` when it has one. A property marked `[JsonIgnore]` is left out |
| `typeName` | `String!` | Friendly type name (e.g. `String`, `Int32`, `DateTime?`) |
| `isNullable` | `Boolean!` | Whether the property is nullable |
| `enumValues` | `[String!]` | The accepted values when the property is an enum or a nullable enum, spelled as the reader expects them. Null for any other type |

The names and enum spellings come from the same options `queueTrain` and `runTrain` deserialize
input with, so a client that builds its JSON from this schema writes what the reader accepts.

---

### adminTrainNames

Returns the canonical FullNames of the framework's internal scheduler trains (job dispatcher, manifest manager, job runner, cleanup). This is the same list `hideAdminTrains` filters against on the `trains` and `executions` queries and the `metrics.dashboard` query.

```graphql
query {
  operations {
    adminTrainNames
  }
}
```

The subscription feed streams every train on an admin host, so the dashboard reads this list once and filters the live `onTrainStateChanged` events client-side by the same rule the server applies to the grid, keeping the feed and grid consistent when "Hide admin trains" is on.

**Returns**: `[String!]!`

---

### hosts

Rolls up the processes that have executed trains, grouped by `HostInstanceId` from the metadata table. Backs the dashboard's cluster view across a split API + scheduler + worker deployment. Rows without a stamped host instance are ignored.

```graphql
query {
  operations {
    hosts {
      instanceId
      name
      environment
      lastSeen
      totalExecutions
      currentlyRunning
    }
  }
}
```

This is a full aggregation over the metadata table (like `metrics.dashboard`), so treat it as an occasional-refresh read, not a hot poll. `lastSeen` is the most recent execution start on the host, the freshness signal the dashboard renders; there is no separate heartbeat.

**Returns**: `[HostInfo!]!`

#### HostInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `instanceId` | `String!` | Stable per-process id (lives for the process's lifetime) |
| `name` | `String` | Machine / container host name, if stamped |
| `environment` | `String` | Hosting environment (e.g. `Production`), if stamped |
| `lastSeen` | `DateTime!` | Most recent execution start on this host |
| `totalExecutions` | `Long!` | Total executions attributed to this host |
| `currentlyRunning` | `Int!` | Executions on this host still `InProgress` |

---

### trainStats

Execution roll-up for a single train, keyed by its interface FullName (the value stored in `metadata.Name`). Backs the summary cards on the dashboard's per-train detail page. The state grouping and `ix_metadata_*` indexes keep it cheap against a large metadata table.

```graphql
query {
  operations {
    trainStats(trainName: "MyApp.Trains.Reports.IBuildDailyReportTrain") {
      total
      completed
      failed
      inProgress
      averageMilliseconds
      lastRun
      lastSuccessfulRun
    }
  }
}
```

**Returns**: `TrainExecutionStats!`

#### TrainExecutionStats fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | The interface FullName the stats are scoped to |
| `total` | `Long!` | Total executions of this train |
| `completed` / `failed` / `inProgress` / `pending` / `cancelled` | `Long!` | Counts by state |
| `lastRun` | `DateTime` | Start time of the most recent execution. Null when there are none |
| `lastSuccessfulRun` | `DateTime` | End time of the most recent `Completed` execution. Null when there are none |
| `averageMilliseconds` | `Float` | Mean duration of completed executions. Null when there are none |

---

### effects

Lists the observational effects registered in the API process, with their enabled and toggleable state and, for an effect whose factory exposes runtime settings, those settings. Backs the dashboard's effects list.

The effect registry is an in-memory, per-process singleton with no persistence or cross-process broadcast, so this reflects the API host only, not the scheduler/worker processes where effects actually run. [`setEffectEnabled`](/docs/sdk-reference/graphql-api/mutations#seteffectenabled) toggles an effect in this same process. Changing effect state across a distributed deployment would need a shared store plus a change broadcast, which is not built.

```graphql
query {
  operations {
    effects {
      name
      fullName
      enabled
      toggleable
      isConfigurable
      configurationTypeName
      configuration
    }
  }
}
```

**Returns**: `[EffectInfo!]!`, ordered by `fullName`.

#### EffectInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | Effect factory type name (short) |
| `fullName` | `String!` | Effect factory type FullName |
| `enabled` | `Boolean!` | Whether the effect is currently enabled in this process |
| `toggleable` | `Boolean!` | Whether the effect can be toggled (infrastructure effects are always on) |
| `isConfigurable` | `Boolean!` | Whether the effect's factory exposes runtime settings (implements `IConfigurableProviderFactory`) |
| `configurationTypeName` | `String` | FullName of the settings type. Null when not configurable |
| `configuration` | `String` | The factory's current settings as camelCase JSON, with each `[TraxSensitive]` member written as `{"_redacted": true}`. Null when not configurable |

Settings can hold credentials. Like an execution's `input`, they are reachable only under the
`operations` namespace, so the gate you put on it (`GateOperations` or `RequireAuthorization`)
decides who reads them. See [Train inputs and the operations gate](#train-inputs-and-the-operations-gate).

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
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0` |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](#page-size) |
| `isEnabled` | `Boolean` | `null` | Filter by enabled/disabled |
| `scheduleType` | `ScheduleType` | `null` | Filter by schedule type (`NONE`, `CRON`, `INTERVAL`, `ON_DEMAND`, `DEPENDENT`, `DORMANT_DEPENDENT`, `ONCE`) |
| `nameContains` | `String` | `null` | Case-sensitive substring match on the train name |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. When provided, `skip` is ignored. See [Pagination](#pagination) |
| `manifestGroupId` | `Long` | `null` | Only manifests belonging to this group. The dashboard uses it to list a group's manifests |

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
| `manifestGroupName` | `String` | Name of the parent group |
| `replayDecisionsOnRetry` | `Boolean!` | Whether a retry of the manifest's failed run, automatic or a requeue of its dead letter, replays the decisions that run recorded. Set with [`ScheduleOptions.ReplayDecisionsOnRetry`](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions); see [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) |

A manifest's `properties` (the train input it runs with) are not on this type. Read them from
[`manifestDetail`](#manifestdetail), one manifest at a time.

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

### manifestDetail

Returns everything `manifest` does plus the train input the manifest runs with and the rest of
its scheduling settings. Use it for a manifest detail page.

```graphql
query {
  operations {
    manifestDetail(id: 42) {
      id
      name
      manifestGroupName
      propertyTypeName
      properties
      misfirePolicy
      misfireThresholdSeconds
      scheduledAt
      nextScheduledRun
      varianceSeconds
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The manifest's database ID |

**Returns**: `ManifestDetail` (nullable, returns `null` if the ID does not exist). It carries every
[`ManifestSummary`](#manifestsummary-fields) field (with `manifestGroupName` non-null) and:

| Field | Type | Description |
|-------|------|-------------|
| `propertyTypeName` | `String` | Fully qualified type name of the train input |
| `properties` | `String` | The train input as stored JSON, with each `[TraxSensitive]` member masked. Can hold credentials; see [Train inputs and the operations gate](#train-inputs-and-the-operations-gate) |
| `misfirePolicy` | `MisfirePolicy!` | What the manifest manager does with a missed run |
| `misfireThresholdSeconds` | `Int` | How late a run can be before it counts as missed |
| `scheduledAt` | `DateTime` | The one-off run time, for a `Once` manifest |
| `nextScheduledRun` | `DateTime` | When the manifest manager next plans to run it |
| `varianceSeconds` | `Int` | Random jitter added to each run |

---

### manifestStats

Execution roll-up for a single manifest: run counts by state plus the most recent run and most recent successful run. Backs the summary cards on the dashboard's manifest detail page, through the same `IOperationsService.GetManifestExecutionStatsAsync` call. A manifest with no runs, or an id with no manifest, gets zeros and nulls. Served index-only by `ix_metadata_manifest_state`, so it stays fast on a manifest with a long history.

```graphql
query {
  operations {
    manifestStats(manifestId: 42) {
      manifestId
      total
      completed
      failed
      inProgress
      pending
      cancelled
      lastRun
      lastSuccessfulRun
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `manifestId` | `Long!` | Yes | The manifest's database ID |

**Returns**: `ManifestExecutionStats!` (never null; a manifest with no runs returns all-zero counts and null timestamps).

#### ManifestExecutionStats fields

| Field | Type | Description |
|-------|------|-------------|
| `manifestId` | `Long!` | The manifest these stats are for (echoes the argument) |
| `total` | `Long!` | Total executions across all states |
| `completed` | `Long!` | Executions in `Completed` |
| `failed` | `Long!` | Executions in `Failed` |
| `inProgress` | `Long!` | Executions in `InProgress` |
| `pending` | `Long!` | Executions in `Pending` |
| `cancelled` | `Long!` | Executions in `Cancelled` |
| `lastRun` | `DateTime` | Start time of the most recent execution. Null when there are none |
| `lastSuccessfulRun` | `DateTime` | End time of the most recent `Completed` execution. Null when there are none |

---

### manifestExclusions

The schedule exclusion windows configured on a manifest: the days, dates, ranges, or daily time windows during which it is intentionally skipped (not treated as a misfire). Backs the exclusions panel on the manifest detail page. The model is flat and discriminated: `type` selects which of the other fields apply.

```graphql
query {
  operations {
    manifestExclusions(manifestId: 42) {
      type
      daysOfWeek
      dates
      startDate
      endDate
      startTime
      endTime
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `manifestId` | `Long!` | Yes | The manifest's database ID |

**Returns**: `[ManifestExclusion!]!` (empty when the manifest has no exclusions or does not exist).

#### ManifestExclusion fields

| Field | Type | Applies when `type` is | Description |
|-------|------|------------------------|-------------|
| `type` | `ExclusionType!` | always | `DAYS_OF_WEEK`, `DATES`, `DATE_RANGE`, or `TIME_WINDOW` |
| `daysOfWeek` | `[DayOfWeek!]` | `DAYS_OF_WEEK` | Weekdays to skip (`SUNDAY`..`SATURDAY`) |
| `dates` | `[Date!]` | `DATES` | Specific calendar dates to skip |
| `startDate` | `Date` | `DATE_RANGE` | First day of an inclusive skipped range |
| `endDate` | `Date` | `DATE_RANGE` | Last day of an inclusive skipped range |
| `startTime` | `LocalTime` | `TIME_WINDOW` | Daily window start (supports midnight crossover) |
| `endTime` | `LocalTime` | `TIME_WINDOW` | Daily window end |

---

### manifestGroups

Manifest group queries live under the `operations.manifestGroups` namespace, not at the top level. The namespace holds the paged list (`groups`), single-group lookup (`group`), and cross-group dependency graph (`graph`). See [manifestGroups (nested under operations)](#manifestgroups-nested-under-operations).

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
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0` |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](#page-size) |
| `trainState` | `TrainState` | `null` | Filter by state (`PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED`, `CANCELLED`) |
| `trainName` | `String` | `null` | Exact-match filter on the train interface FullName |
| `startedAfter` | `DateTime` | `null` | Only executions with `startTime >= startedAfter` |
| `startedBefore` | `DateTime` | `null` | Only executions with `startTime <= startedBefore` |
| `order` | `SortOrder` | `NEWEST` | `NEWEST` (id descending) or `OLDEST` (id ascending). Both stay keyset-safe |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId` (or `id > afterId` when `order: OLDEST`). See [Pagination](#pagination) |
| `manifestId` | `Long` | `null` | Only executions of this manifest. The dashboard uses it for a manifest's execution history |
| `manifestGroupId` | `Long` | `null` | Only executions of any manifest in this group (resolved through `manifest.manifest_group_id`). The dashboard uses it for a group's recent executions |
| `hideAdminTrains` | `Boolean` | `false` | When `true`, excludes the framework's internal scheduler trains (matches `AdminTrains.FullNames` against `metadata.Name`, which stores the interface FullName). The dashboard sets this from its "Hide admin trains" toggle |
| `failureClass` | `FailureClass` | `null` | Only executions recorded with this [failure class](/docs/core/trains-and-junctions#classifying-failures): `UNCLASSIFIED`, `TRANSIENT`, `CONFLICT`, or `PERMANENT`. Every run that did not fail records `UNCLASSIFIED`, so `failureClass: UNCLASSIFIED` on its own also matches every completed, pending, in-progress and cancelled run; combine it with `trainState: FAILED` for unclassified failures only |

When any filter is supplied the count is exact (`isEstimatedCount: false`); an unfiltered list may use the database's row estimate, on every page alike (see [Estimated counts](#estimated-counts)). `startedAfter`/`startedBefore` use the `ix_metadata_start_time_desc` index so they stay fast at scale. `manifestId` and `manifestGroupId` are served by the covering index `ix_metadata_manifest_state`, so a manifest's or group's history stays index-only even against millions of rows. `failureClass` is served by `ix_metadata_failure_class` on `(failure_class, id DESC)` (Postgres). It covers every row rather than only classified ones: the class arrives as a query parameter, and a generic plan cannot prove a parameter satisfies a partial index's predicate, so a partial index would go unused. Arbitrary-column sorting is deliberately not offered: it is incompatible with keyset pagination over millions of rows (it forces OFFSET scans or a full sort). Filter to narrow the set instead.

**Returns**: `PagedResult<ExecutionSummary>`

#### ExecutionSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Metadata ID |
| `externalId` | `String!` | External identifier |
| `name` | `String!` | Train type name |
| `trainState` | `TrainState!` | Current state: `PENDING`, `IN_PROGRESS`, `COMPLETED`, `FAILED` or `CANCELLED` on the wire |
| `startTime` | `DateTime!` | When execution began |
| `endTime` | `DateTime` | When execution finished (null if still running) |
| `failureJunction` | `String` | Name of the junction that failed (null if no failure) |
| `failureReason` | `String` | Exception message on failure |
| `manifestId` | `Long` | Associated manifest ID (null if not scheduler-initiated) |
| `cancellationRequested` | `Boolean!` | Whether cancellation was requested |
| `failureClass` | `FailureClass!` | How the failure was classified: `UNCLASSIFIED`, `TRANSIENT`, `CONFLICT`, or `PERMANENT`. `UNCLASSIFIED` when the run did not fail, no [failure classifier](/docs/core/trains-and-junctions#classifying-failures) is registered, or it did not recognise the failure. Later releases may add values to `FailureClass`; a client generated from an older schema should treat a value it does not know as `UNCLASSIFIED` rather than failing to read the response |

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

### executionDetail

Returns the full detail for a single execution, including the `input` / `output` payloads and
`stackTrace` that `execution` / `executions` omit to keep list reads lean. Use this for a
detail page; use `execution` for a light single-row lookup.

```graphql
query {
  operations {
    executionDetail(id: 100) {
      id
      externalId
      name
      trainState
      startTime
      endTime
      failureJunction
      failureReason
      failureException
      stackTrace
      input
      output
      manifestId
      cancellationRequested
      currentlyRunningJunction
      junctionStartedAt
      hostName
      hostEnvironment
      hostInstanceId
      hostLabels
      failureClass
      parentId
      scheduledTime
      executor
      replayDecisionsOf
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata ID |

**Returns**: `ExecutionDetail` (nullable; `null` when the ID does not exist).

`input` and `output` are the raw JSON payloads as stored (Postgres returns them in jsonb
canonical form). There is no separate junction table: junction context is the
`currentlyRunningJunction` (while `IN_PROGRESS`) and `failureJunction` (on failure) fields.
`childCount` is the number of sub-executions (metadata rows whose `parentId` is this
execution), for rendering a parent/child tree, and `parentId` is this execution's own parent. Nothing in Trax sets `parentId` at present, a
train dispatched from a junction included (see [Nested Trains](/docs/mediator#nested-trains)), so
it is `0` unless something outside Trax writes the column. `failureClass` is the same `FailureClass` enum as
on [`ExecutionSummary`](#executionsummary-fields). `scheduledTime` is when a scheduled run was due
(null for one that was not scheduled), `executor` is the project name of the process that ran it,
and `hostLabels` is the host's user-supplied labels as a JSON object. `replayDecisionsOf` (`Long`) is
the execution whose recorded decisions this run was queued to replay, by a requeue or a manifest's
retry that replays; it is null when the run replays nothing, a run queued to ask afresh included. The dashboard shows it as
**Replays Decisions Of**.

`input` and `output` can hold credentials. They are on this single-row read and on no list; see
[Train inputs and the operations gate](#train-inputs-and-the-operations-gate).

---

### executionChildren

Paginated child executions of a parent (metadata rows whose `parentId` matches the given id),
newest first. Keyset-paginated on id like the top-level `executions` list. Backed by the
partial index `ix_metadata_parent_id`, so it stays O(page size) even on the huge metadata table.

```graphql
query {
  operations {
    executionChildren(parentId: 100, take: 25) {
      items { id name trainState startTime endTime }
      totalCount
      nextCursor
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `parentId` | `Long!` | none | The parent execution's metadata id |
| `take` | `Int!` | `25` | Page size, from 1 to 500. See [Page size](#page-size) |
| `afterId` | `Long` | `null` | Keyset cursor (`id < afterId`) |

**Returns**: `PagedResult<ExecutionSummary>` (count is always exact).

---

### junctionRuns

The steps of one execution, in the order it reached them, as
[`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events) recorded them: each
junction that ran, each question a routing step asked and the track it took. Empty for an
execution with none recorded, and for an id with no execution. Read through
`JunctionRunQueries.ForRun`, the query the dashboard's timeline reads too.

```graphql
query {
  operations {
    junctionRuns(metadataId: 100) {
      position
      kind
      name
      state
      startedAt
      durationMs
      failureClass
      questionKey
      answer
      confidence
      replayed
      answerWithheld
      nameWithheld
      trackPosition
      attempt
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `metadataId` | `Long!` | none | The execution's id. 0 or less is refused with `TRAX_INVALID_ARGUMENT` |
| `afterPosition` | `Int` | `null` | Only steps after this position, a keyset cursor for the next page |
| `take` | `Int!` | `500` | Page size, from 1 to 500 |

**Returns**: `[JunctionStep!]!`, the same type the [`onJunctionEvent`](/docs/sdk-reference/graphql-api/subscriptions#onjunctionevent)
subscription carries. A step carries no input, output or failure message, and an answer to a
question about a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type)
type is never present. Once a routing step's answer is withheld, every later step of the run is
withheld too, whatever its kind: a junction, a question or a further route is named `(withheld)`,
with `nameWithheld` true, and a question or route has `questionKey`, `answer` and `confidence` null
and `answerWithheld` true. `trackPosition` is the position of the latest routing step before a step,
of any kind, and null before the first. Trax cannot tell where a track rejoins the chain, so every
step after a route counts as on its track. The recorded decider is not kept, so `decider` is always
null here.

A withheld step still records its kind, position, state and timing, and for a failed junction its
exception type and failure class. The run's own `failureJunction`, the train's failed event, and
a train started from a junction on the track are recorded as for any run. See
[Junction Events](/docs/effect/junction-events).

A junction whose end event was dropped stays `IN_PROGRESS` in these rows after its run has ended,
so read a step's `state` together with the execution's.

It answers to the operations gate, as [`execution`](#execution) does, so a caller refused one is
refused the other. The rows trail the live subscription by moments: a client following a running
execution subscribes first, then reads this, and keeps for each position whichever is further
along.

---

## Train inputs and the operations gate

A train's input can carry credentials, so the admin surface reads it one row at a time. An
execution's `input`, a manifest's `properties` and a work queue entry's `input` are on
[`executionDetail`](#executiondetail), [`manifestDetail`](#manifestdetail) and
[`workQueue.detail`](#detail) only, never on a list type, and an effect's settings are on
[`effects`](#effects). All of them sit under the `operations` namespace, so whatever gates it
(`RequireAuthorization()` or `GateOperations(...)`, see [API security](/docs/api-security)) is what
decides who reads them. There is no separate field-level gate: a caller who can read one execution's
input can read a manifest's properties too. The reasoning is recorded in Trax.Api's ADR
`api/0005`.

A member marked [`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields) is masked in all
three, written as `{"_redacted": true}`. An execution's `input` is recorded that way. A manifest's
`properties` and a work queue entry's `input` are stored with the real value, because a run starts
from them, so the read masks them: it reads the stored JSON back as the input type of the train
registered on this host and writes it with the mask. When the host has no registered train whose
input type the copy names, or the JSON does not read as that type, nothing shows the copy holds no
sensitive member, so the whole value reads as `{"_redacted": true}`.

A member whose declared type does not say what it holds is masked on those two reads whether or not
anything under it is marked: `object`, `JsonElement`, `JsonNode`, `JsonDocument`, a collection
without an element type, and a collection or dictionary whose elements are one of those. The copy
was stored from the runtime value, so `record Pay(object Details)` holding a `Card` with a
`[TraxSensitive] Number` stores the number, and reading it back as `Pay` gives JSON with no member
left to mark. Such a member reads as `{"_redacted": true}`; the members whose types are declared read
as stored. Declare the member's real type to see it.

An effect's settings on [`effects`](#effects) are written from the settings object itself, so a
`[TraxSensitive]` member at any depth there reads as `{"_redacted": true}` too.

---

## PagedResult

All paginated queries return the same wrapper type:

| Field | Type | Description |
|-------|------|-------------|
| `items` | `[T!]!` | The page of results |
| `totalCount` | `Int!` | Total number of records matching the query's filters, the same on every page: neither `skip` nor `afterId` changes it |
| `skip` | `Int!` | The `skip` value that was applied, after a negative value was read as `0` |
| `take` | `Int!` | The `take` value that was applied, after clamping to 1 through 500 |
| `isEstimatedCount` | `Boolean!` | `true` when `totalCount` is a fast estimate rather than an exact count. See [Pagination](#estimated-counts) |
| `nextCursor` | `Long` | ID of the last item in the page. Pass as `afterId` to fetch the next page via keyset pagination. `null` when no items are returned |

---

## Pagination

Paginated queries support two strategies. Both can be used interchangeably. The dashboard uses offset pagination internally, while API consumers can opt into keyset cursors for better deep-page performance.

### Page size

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

### Offset pagination (default)

Pass `skip` and `take` as before. This uses SQL `OFFSET`/`LIMIT` under the hood. Performance degrades on deep pages (high `skip` values) because the database must scan and discard rows up to the offset, which is why `skip` stops at 10,000.

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

`totalCount` is the size of the whole list the filters select, whichever page you are on: the cursor and `skip` never change it. This is the convention HotChocolate's own connections and GitHub's API follow.

For an unfiltered list of a large table (10,000 rows or more), `totalCount` is the database's own row estimate instead of an exact `COUNT(*)`, which would scan every row on every page. The provider supplies it through `ISqlDialect.EstimateRowCount`: on PostgreSQL it is `pg_class.reltuples`, which `ANALYZE`, `VACUUM` and autovacuum keep current and which is typically within a few percent. When the estimate is used, `isEstimatedCount` is `true`, on the first page and every later one.

The count is exact, and `isEstimatedCount` is `false`, whenever a filter is supplied, when the table is smaller than that, when PostgreSQL has never analyzed the table, and on providers that keep no estimate (SQLite, and the in-memory provider).

### Performance at scale

The operations queries are stress-tested against millions of rows (`Trax.Api.Tests.Stress`, run with `dotnet test --filter TestCategory=Stress`). At 3,000,000 metadata rows on laptop-class PostgreSQL:

- **Keyset pagination stays flat.** A far-end page (an `afterId` near the end of the id sequence) returns in ~35ms no matter how deep it is, because it seeks through the primary key index rather than counting past skipped rows.
- **Offset pagination does not.** An offset scans and discards every skipped row: ~430ms at a 3,000,000-row offset, more than 10x slower than the equivalent keyset page. The deepest offset served, 10,000, takes ~60ms; a deeper one is refused with `TRAX_SKIP_TOO_DEEP`.

Build list views on keyset cursors: read the first page with `take`, then pass each response's `nextCursor` as the next request's `afterId`. Reserve `skip` for shallow jumps. Filtered reads (`status`, `trainName`, `metadataId`, `minimumLevel`, `category`) and their exact counts also stay under ~100ms at the same scale, so filter controls stay responsive.

The same suite times every operations mutation against those tables (each single-row or scoped write, including the manifest and group cancels that filter the metadata table, finishes in under ~50ms), the point reads behind the detail pages, the persisted-operations list, lookups and writes over a 100,000-operation catalog, and subscription fan-out to 1,000 subscribers. `onDataChanged` coalesces a storm of 200,000 change signals into one event per changed domain per subscriber, delivered to all of them in under half a second. `onTrainStateChanged` delivers each event to every subscriber when the rate is moderate, but at 1,000 subscribers and a sustained 80 or more state changes a second, a few subscribers miss some events: a live feed can lag behind the grid until its next refetch. `requeueAllDeadLetters` and `acknowledgeAllDeadLetters` are timed over every dead letter the seed leaves awaiting intervention, 500,000 of them: acknowledging all takes about 7.5 s, and requeueing all, which also writes one work queue entry per manifest, about 36 s in the background while the mutation itself answers at once with a job handle (see [`requeueAllDeadLetters`](/docs/sdk-reference/graphql-api/mutations#deadletters-nested-namespace)), so treat them as rare operator actions. The mutations are timed through the request executor, with the operations gate, the error filter and HotChocolate's execution timeout in the path. The batch cancels and enable/disable mutations on a full 1,000-id selection, and `runTrain`, each finish in under 20 ms.

## deadLetters (nested under operations)

Dead letters, the manifests that failed more times than their `MaxRetries` (see
[Dead Letters & Cleanup](/docs/scheduler/dead-letters-and-cleanup)). The requeue and acknowledge
mutations are under `operations.deadLetters` on the mutation type
([Mutations: deadLetters](/docs/sdk-reference/graphql-api/mutations#deadletters-nested-namespace)).

```graphql
query {
  operations {
    deadLetters {
      deadLetters(status: AWAITING_INTERVENTION, take: 10) {
        items { id manifestId manifestName status reason retryCountAtDeadLetter deadLetteredAt }
        totalCount
        nextCursor
      }
      deadLetter(id: 42) { status resolvedAt resolutionNote retryMetadataId }
    }
  }
}
```

### deadLetters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination) |
| `take` | `Int!` | `25` | Number of records to return. See [Page size](#page-size) |
| `status` | `DeadLetterStatus` | `null` | `AWAITING_INTERVENTION`, `RETRIED` or `ACKNOWLEDGED` |
| `afterId` | `Long` | `null` | Keyset cursor. See [Pagination](#pagination) |

**Returns**: `PagedResult<DeadLetterSummary>`

### deadLetter

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The dead letter's id |

**Returns**: `DeadLetterSummary`, or `null` when no dead letter has that id.

#### DeadLetterSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Dead letter id |
| `manifestId` | `Long!` | The manifest that was dead-lettered |
| `manifestName` | `String!` | The manifest's train name (the interface FullName), not its external id |
| `status` | `DeadLetterStatus!` | `AWAITING_INTERVENTION` until an operator requeues (`RETRIED`) or acknowledges (`ACKNOWLEDGED`) it |
| `deadLetteredAt` | `DateTime!` | When the ManifestManager wrote it |
| `reason` | `String!` | For example `Max retries exceeded: (3) failures > (2) max retries` |
| `retryCountAtDeadLetter` | `Int!` | The counted failures when it was written |
| `resolvedAt` | `DateTime` | When it was requeued or acknowledged |
| `resolutionNote` | `String` | The note an acknowledge took, or the one a requeue wrote: `Re-queued (WorkQueue {id})`, single or batch, or `Re-queued with dead letter {id} (WorkQueue {id})` for a dead letter a batch requeue folded into another's run |
| `retryMetadataId` | `Long` | The run a requeue started, set once that run is dispatched |

---

## config (nested under operations)

The `operations.config` namespace returns the live scheduler runtime settings (the dashboard-editable subset of `SchedulerConfiguration`, `LocalWorkerOptions`, and `MetadataCleanupConfiguration`). The dashboard's Server Settings page and this query both read from the same in-memory singleton, so they agree. The page saves through the same operations call as `updateScheduler`, sending only the fields the operator changed, and reloads from this snapshot afterwards.

Persistence: settings written via `operations.config.updateScheduler` (or the dashboard) are stored in the singleton-row `trax.scheduler_config` table, only the ones a save names. Every running scheduler applies them to its in-memory singleton at startup and re-reads the row every few seconds, so they survive restarts and reach every scheduler host. A setting never saved keeps each host's configured value. See [config](/docs/sdk-reference/graphql-api/mutations#config-nested-namespace) for which fields a save stores.

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
        failureCountWindow
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

### environmentName and logLevels

The API host's environment and its `Logging:LogLevel` configuration, which the dashboard shows as
its environment badge and on its server settings page.

```graphql
query {
  operations {
    config {
      environmentName
      logLevels { category level }
    }
  }
}
```

`environmentName` is `IHostEnvironment.EnvironmentName` (`String!`). `logLevels` is
`[LogLevelSetting!]!`, one `{ category, level }` per key under `Logging:LogLevel`, `Default` first
and the rest by category; empty when the host configures none. Only that section is read, so no
other configuration value (a connection string, a secret) is reachable from here. Both describe
the API process, not the scheduler or worker processes.

#### SchedulerConfigSnapshot fields

| Field | Type | Description |
|-------|------|-------------|
| `manifestManagerEnabled` | `Boolean!` | Whether the manifest manager polling service runs |
| `jobDispatcherEnabled` | `Boolean!` | Whether the job dispatcher polling service runs |
| `manifestManagerPollingInterval` | `TimeSpan!` | How often the manifest manager polls |
| `jobDispatcherPollingInterval` | `TimeSpan!` | How often the job dispatcher polls |
| `maxActiveJobs` | `Int` | Global concurrency cap. Null means no cap |
| `defaultMaxRetries` | `Int!` | Default retry budget for new manifests |
| `failureCountWindow` | `TimeSpan!` | How far back failed runs count toward retry backoff and `MaxRetries` |
| `defaultRetryDelay` | `TimeSpan!` | First-retry delay |
| `retryBackoffMultiplier` | `Float!` | Exponential backoff factor |
| `maxRetryDelay` | `TimeSpan!` | Upper bound on backoff |
| `defaultJobTimeout` | `TimeSpan!` | Default per-execution timeout |
| `stalePendingTimeout` | `TimeSpan!` | When pending entries are reaped |
| `recoverStuckJobsOnStartup` | `Boolean!` | Whether stuck-job recovery runs on startup |
| `deadLetterRetentionPeriod` | `TimeSpan!` | How long resolved dead letters are kept before purging |
| `autoPurgeDeadLetters` | `Boolean!` | Whether the dead letter cleanup service runs |
| `localWorkerCount` | `Int` | In-process worker thread count. Null when the scheduler runs no local worker pool (no database provider, or a replaced job submitter). Local workers are on by default with a database provider; [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) tunes them |
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

`dashboard` runs several aggregations over the last-24h and last-7-day windows on every call, and those windows hold hundreds of thousands to millions of rows at scale. The metadata table carries two covering indexes for them (`ix_metadata_metrics_state_time` and `ix_metadata_metrics_window`) so every aggregation is a heap-free index-only scan. At 3,000,000 metadata rows the whole block returns in ~400-525ms, against ~630-770ms without the covering indexes. It is the heaviest operations read, so poll it on an interval (a few seconds) rather than on every dashboard interaction.

### server

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

### serverCpuPercent

This API process's CPU utilisation since the previous poll, as a percentage of total capacity (normalised by core count, so 100 means every core fully busy). CPU% is a delta measurement, so the sampler holds the previous sample: the **first** poll after startup returns `null` while the baseline primes, and each subsequent poll reports usage since the last. Poll it on the same interval as the rest of the dashboard's server panel.

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
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0`. Ignored when `afterId` is provided. |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](#page-size) |
| `metadataId` | `Long` | `null` | Filter to logs for a single execution |
| `minimumLevel` | `LogLevel` | `null` | Includes the supplied level and anything more severe. `LogLevel` follows `Microsoft.Extensions.Logging`: `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL`, `NONE` |
| `category` | `String` | `null` | Exact-match filter on the logger category (e.g. `MyApp.Trains.Billing.ChargeCustomerTrain`) |
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

The `operations.manifestGroups` namespace exposes every read scoped to manifest groups: the paged list, single-group lookup, and the cross-group dependency graph the dashboard renders as a DAG. The list lives here (rather than as a sibling of `manifests` at the operations root) because both the namespace and a sibling `manifestGroups` field would camelCase to the same name in the schema, and HotChocolate would silently drop one.

### groups

Returns a paginated list of manifest groups, ordered by ID descending. Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    manifestGroups {
      groups(skip: 0, take: 10) {
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
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0` |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](#page-size) |
| `nameContains` | `String` | `null` | Case-sensitive substring match on the group name |
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
| `createdAt` | `DateTime!` | When the row was created |
| `updatedAt` | `DateTime!` | Last patch via `updateManifestGroup` |

### group

Single-group lookup by ID. Used by dashboards to pre-populate the group settings form before sending an `updateManifestGroup` patch.

```graphql
query {
  operations {
    manifestGroups {
      group(id: 7) {
        id
        name
        maxActiveJobs
        priority
        isEnabled
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | Manifest group database ID |

**Returns**: `ManifestGroupSummary` (nullable; `null` when the group does not exist).

### stats

Execution roll-up for a set of manifest groups in a single round-trip: manifest count, executions by state, and last run per group. Backs the per-group stat columns on the dashboard's manifest groups list, which calls it with just the ids of the visible page, through the same `IOperationsService.GetManifestGroupExecutionStatsAsync` call. Every requested id gets a row (zeros when the group has no manifests or executions), returned in the order requested so the caller can zip it to its rows. The metadata side is served by `ix_metadata_manifest_state` and the manifest side by `ix_manifest_manifest_group_id`.

```graphql
query {
  operations {
    manifestGroups {
      stats(groupIds: [1, 2, 7]) {
        groupId
        manifestCount
        totalExecutions
        completed
        failed
        inProgress
        lastRun
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupIds` | `[Long!]!` | Yes | The group ids to roll up, at most 1000 distinct ones. Duplicates are collapsed; an empty list returns an empty result. More than 1000 distinct ids fails the field with a GraphQL error coded `TRAX_TOO_MANY_IDS` |

**Returns**: `[ManifestGroupStats!]!`

#### ManifestGroupStats fields

| Field | Type | Description |
|-------|------|-------------|
| `groupId` | `Long!` | The group these stats are for |
| `manifestCount` | `Long!` | Manifests belonging to the group |
| `totalExecutions` | `Long!` | Executions across all of the group's manifests |
| `completed` | `Long!` | Executions in `Completed` |
| `failed` | `Long!` | Executions in `Failed` |
| `inProgress` | `Long!` | Executions in `InProgress` |
| `lastRun` | `DateTime` | Start time of the group's most recent execution. Null when there are none |

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

### dependencyGraph

The whole cross-group dependency graph in one shot: every manifest group as a node and every cross-group dependency (a manifest in one group depending on a manifest in another) as a directed parent → dependent edge. Nothing is highlighted. Backs the global DAG the dashboard renders on the manifest-groups page; `graph(groupId)` is the same shape narrowed to one group's neighborhood.

```graphql
query {
  operations {
    manifestGroups {
      dependencyGraph {
        nodes { id name isHighlighted }
        edges { fromId toId }
      }
    }
  }
}
```

**Returns**: `ManifestGroupDependencyGraph!` (never null; empty when there are no groups). Same-group dependencies are excluded, so a deployment whose groups never depend on each other returns nodes with no edges.

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
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0`. Ignored when `afterId` is provided. |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](#page-size) |
| `status` | `WorkQueueStatus` | `null` | Filter by lifecycle state (`QUEUED`, `DISPATCHED`, `CANCELLED`) |
| `trainName` | `String` | `null` | Exact-match filter on the interface FullName (e.g. `MyApp.Trains.Billing.IChargeCustomerTrain`) |
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
| `confirmedAt` | `DateTime` | When the entry became eligible for dispatch. Null while it is still being staged, and stays null on a staged entry that was cancelled (by an operator, or by the stale staged entry sweep); the dispatcher never claims an unconfirmed entry. Also null on an entry dispatched more than a day before the database was migrated to the version that added it, which the migration does not backfill |
| `subjectKey` | `String` | The subject the entry is serialized against, from the train's [`QueueSubjectKey`](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing). Null when the train does not set one. The value is computed by the consumer's train, so it may carry record identifiers |

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

### detail

Returns one entry with the train input it was queued with and, for a queued entry with a
subject, what it is waiting on. Backs a work queue detail page.

```graphql
query {
  operations {
    workQueue {
      detail(id: 42) { id status subjectKey input subjectHeldBy subjectQueuedBehind }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The work queue entry's database ID |

**Returns**: `WorkQueueDetail` (nullable, returns `null` if the ID does not exist). It carries every
[`WorkQueueSummary`](#workqueuesummary-fields) field and:

| Field | Type | Description |
|-------|------|-------------|
| `input` | `String` | The train input as stored JSON, with each `[TraxSensitive]` member masked. Can hold credentials; see [Train inputs and the operations gate](#train-inputs-and-the-operations-gate) |
| `subjectHeldBy` | `Long` | For a queued entry with a subject: the dispatched entry for the same subject whose run is still pending or in progress. Dispatch skips the subject until that run finishes |
| `subjectQueuedBehind` | `Long` | For a queued entry with a subject that nothing holds: the queued entry for the same subject that dispatch offers first (confirmed, due, in an enabled group, then higher priority, then older). Dispatch offers one entry per subject each cycle |

Both are null for an entry that is not queued or has no subject. The Blazor dashboard's work queue
detail page reports the same two answers from the same predicate.

---

## deadLetters (nested under operations)

The `operations.deadLetters` namespace exposes paginated dead-letter reads (`deadLetters`, `deadLetter`) and `requeueAllJob(id: UUID!)`, which reads a job [`requeueAllDeadLetters`](/docs/sdk-reference/graphql-api/mutations#deadletters-nested-namespace) started. It returns the `DeadLetterRequeueJob`, or `null` when this node does not know the id: the job was started on another node, this node has restarted since, or it finished more than 24 hours ago. Read a job on the node that started it; the backlog itself is `deadLetters(status: AWAITING_INTERVENTION) { totalCount }` on any node.

```graphql
query {
  operations {
    deadLetters {
      requeueAllJob(id: "6f1c2a7e-0d4b-4f53-9a3e-2b8f1c0d9e11") { status count message finishedAt }
    }
  }
}
```

See [scheduler/dead-letters-and-cleanup](/docs/scheduler/dead-letters-and-cleanup) for the full surface and examples.
