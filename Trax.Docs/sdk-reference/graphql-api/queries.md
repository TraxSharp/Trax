---
layout: default
title: Queries
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 2
---

# Queries

All queries are defined on `TrainQueries`, the root query type. They provide read access to registered trains, scheduled manifests, manifest groups, and train executions.

## trains

Returns every train registered in the DI container, including a runtime-generated input schema describing each property on the input type.

```graphql
query {
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
```

**Returns**: `[TrainInfo!]!`

### TrainInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `serviceTypeName` | `String!` | Friendly name of the service interface (e.g. `IServiceTrain<OrderInput, OrderResult>`) |
| `implementationTypeName` | `String!` | Friendly name of the concrete class |
| `inputTypeName` | `String!` | Friendly name of the input type |
| `outputTypeName` | `String!` | Friendly name of the output type |
| `lifetime` | `String!` | DI lifetime (`Singleton`, `Scoped`, `Transient`) |
| `inputSchema` | `[InputPropertySchema!]!` | Public readable properties on the input type |

### InputPropertySchema fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | Property name |
| `typeName` | `String!` | Friendly type name (e.g. `String`, `Int32`, `DateTime?`) |
| `isNullable` | `Boolean!` | Whether the property is nullable |

---

## manifests

Returns a paginated list of scheduler manifests, ordered by ID descending (newest first).

```graphql
query {
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
    skip
    take
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip |
| `take` | `Int` | `25` | Number of records to return |

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

---

## manifest

Returns a single manifest by database ID.

```graphql
query {
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
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The manifest's database ID |

**Returns**: `ManifestSummary` (nullable — returns `null` if the ID does not exist)

---

## manifestGroups

Returns a paginated list of manifest groups, ordered by ID descending.

```graphql
query {
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
    skip
    take
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip |
| `take` | `Int` | `25` | Number of records to return |

**Returns**: `PagedResult<ManifestGroupSummary>`

### ManifestGroupSummary fields

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

## executions

Returns a paginated list of train executions (metadata records), ordered by start time descending.

```graphql
query {
  executions(skip: 0, take: 10) {
    items {
      id
      externalId
      name
      trainState
      startTime
      endTime
      failureStep
      failureReason
      manifestId
      cancellationRequested
    }
    totalCount
    skip
    take
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int` | `0` | Number of records to skip |
| `take` | `Int` | `25` | Number of records to return |

**Returns**: `PagedResult<ExecutionSummary>`

### ExecutionSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Metadata ID |
| `externalId` | `String!` | External identifier |
| `name` | `String!` | Train type name |
| `trainState` | `TrainState!` | Current state (`Activated`, `Completed`, `Faulted`, `Cancelled`) |
| `startTime` | `DateTime!` | When execution began |
| `endTime` | `DateTime` | When execution finished (null if still running) |
| `failureStep` | `String` | Name of the step that failed (null if no failure) |
| `failureReason` | `String` | Exception message on failure |
| `manifestId` | `Long` | Associated manifest ID (null if not scheduler-initiated) |
| `cancellationRequested` | `Boolean!` | Whether cancellation was requested |

---

## execution

Returns a single execution by metadata ID.

```graphql
query {
  execution(id: 100) {
    id
    externalId
    name
    trainState
    startTime
    endTime
    failureStep
    failureReason
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata ID |

**Returns**: `ExecutionSummary` (nullable — returns `null` if the ID does not exist)

---

## PagedResult

All paginated queries return the same wrapper type:

| Field | Type | Description |
|-------|------|-------------|
| `items` | `[T!]!` | The page of results |
| `totalCount` | `Int!` | Total number of records matching the query |
| `skip` | `Int!` | The `skip` value that was applied |
| `take` | `Int!` | The `take` value that was applied |
