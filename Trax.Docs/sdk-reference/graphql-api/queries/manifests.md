---
title: "operations: manifest queries"
description: "Reference for the operations queries over scheduler manifests: the paged list, one manifest's detail, its run stats and its exclusions."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.cs
---

# operations: manifest queries

Fields of `OperationsQueries` that read scheduler manifests. Manifest groups are under [`operations.manifestGroups`](/docs/sdk-reference/graphql-api/queries/manifest-groups).

## manifests

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
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `isEnabled` | `Boolean` | `null` | Filter by enabled/disabled |
| `scheduleType` | `ScheduleType` | `null` | Filter by schedule type (`NONE`, `CRON`, `INTERVAL`, `ON_DEMAND`, `DEPENDENT`, `DORMANT_DEPENDENT`, `ONCE`) |
| `nameContains` | `String` | `null` | Case-sensitive substring match on the train name |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. When provided, `skip` is ignored. See [Pagination](/docs/sdk-reference/graphql-api/queries/paged-result#pagination) |
| `manifestGroupId` | `Long` | `null` | Only manifests belonging to this group. The dashboard uses it to list a group's manifests |
| `hideAdminTrains` | `Boolean!` | `false` | When `true`, leaves out the manifests of the framework's internal scheduler trains, by the same `AdminTrains.FullNames` list and exact match as `executions(hideAdminTrains:)`. The dashboard's manifests page hides the same rows from its "Hide admin trains" toggle |

**Returns**: `PagedResult<ManifestSummary>`

### ManifestSummary fields

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
| `replayDecisionsOnRetry` | `Boolean!` | Whether a retry of the manifest's failed run, automatic or a requeue of its dead letter, replays the decisions that run recorded. Set with [`ScheduleOptions.ReplayDecisionsOnRetry`](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions); see [Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions) |

A manifest's `properties` (the train input it runs with) are not on this type. Read them from
[`manifestDetail`](#manifestdetail), one manifest at a time.

## manifestDetail

Returns one manifest by database ID: every [`ManifestSummary`](#manifestsummary-fields) field, plus
the train input the manifest runs with and the rest of its scheduling settings. It backs the
manifest detail page.

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
| `properties` | `String` | The train input as stored JSON, with each `[TraxSensitive]` member masked. Can hold credentials; see [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate) |
| `misfirePolicy` | `MisfirePolicy!` | What the manifest manager does with a missed run |
| `misfireThresholdSeconds` | `Int` | How late a run can be before it counts as missed |
| `scheduledAt` | `DateTime` | The one-off run time, for a `Once` manifest |
| `nextScheduledRun` | `DateTime` | When the manifest manager next plans to run it |
| `varianceSeconds` | `Int` | Random jitter added to each run |

## manifestStats

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

### ManifestExecutionStats fields

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

## manifestExclusions

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

### ManifestExclusion fields

| Field | Type | Applies when `type` is | Description |
|-------|------|------------------------|-------------|
| `type` | `ExclusionType!` | always | `DAYS_OF_WEEK`, `DATES`, `DATE_RANGE`, or `TIME_WINDOW` |
| `daysOfWeek` | `[DayOfWeek!]` | `DAYS_OF_WEEK` | Weekdays to skip (`SUNDAY`..`SATURDAY`) |
| `dates` | `[Date!]` | `DATES` | Specific calendar dates to skip |
| `startDate` | `Date` | `DATE_RANGE` | First day of an inclusive skipped range |
| `endDate` | `Date` | `DATE_RANGE` | Last day of an inclusive skipped range |
| `startTime` | `LocalTime` | `TIME_WINDOW` | Daily window start (supports midnight crossover) |
| `endTime` | `LocalTime` | `TIME_WINDOW` | Daily window end |

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests)
- [PagedResult](/docs/sdk-reference/graphql-api/queries/paged-result)
