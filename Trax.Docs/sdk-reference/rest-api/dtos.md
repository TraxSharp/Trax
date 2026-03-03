---
layout: default
title: DTOs
parent: REST API
grand_parent: SDK Reference
nav_order: 6
---

# DTOs

All request, response, and summary types used by the REST API. These are C# records defined in the `Trax.Api.DTOs` namespace and serialized as JSON by ASP.NET Core's `System.Text.Json`.

## Health DTOs

### HealthStatus

Returned by the GraphQL `health` query and used internally by `TraxHealthCheck`. Represents a point-in-time snapshot of scheduler system health.

```csharp
public record HealthStatus(
    string Status,
    string Description,
    int QueueDepth,
    int InProgress,
    int FailedLastHour,
    int DeadLetters
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Status` | `string` | `"Healthy"` or `"Degraded"` |
| `Description` | `string` | Human-readable summary (e.g. `"All systems operational"`) |
| `QueueDepth` | `int` | Work items with status `Queued` |
| `InProgress` | `int` | Executions with `TrainState.InProgress` |
| `FailedLastHour` | `int` | Failed executions in the last hour |
| `DeadLetters` | `int` | Dead letters with status `AwaitingIntervention` |

## Train DTOs

### TrainInfo

Returned by `GET /trains`. Describes a registered train and its input type schema.

```csharp
public record TrainInfo(
    string ServiceTypeName,
    string ImplementationTypeName,
    string InputTypeName,
    string OutputTypeName,
    string Lifetime,
    IReadOnlyList<InputPropertySchema> InputSchema,
    IReadOnlyList<string> RequiredPolicies,
    IReadOnlyList<string> RequiredRoles
);
```

| Property | Type | Description |
|----------|------|-------------|
| `ServiceTypeName` | `string` | Fully qualified name of the train's service interface (e.g. `"MyApp.Trains.IOrderTrain"`) |
| `ImplementationTypeName` | `string` | Fully qualified name of the concrete train class |
| `InputTypeName` | `string` | Fully qualified name of the train's input type |
| `OutputTypeName` | `string` | Fully qualified name of the train's output type (`"Unit"` for void trains) |
| `Lifetime` | `string` | DI service lifetime — `"Scoped"`, `"Transient"`, or `"Singleton"` |
| `InputSchema` | `InputPropertySchema[]` | Public properties of the input type, generated via reflection |
| `RequiredPolicies` | `string[]` | Authorization policy names from `[TraxAuthorize]` attributes. Empty array if no per-train auth. |
| `RequiredRoles` | `string[]` | Role names from `[TraxAuthorize(Roles = "...")]` attributes. Empty array if no roles required. |

### InputPropertySchema

Describes a single property on a train's input type. Part of `TrainInfo.InputSchema`.

```csharp
public record InputPropertySchema(string Name, string TypeName, bool IsNullable);
```

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Property name (e.g. `"Name"`, `"Amount"`) |
| `TypeName` | `string` | Friendly type name (e.g. `"String"`, `"Int32"`, `"Nullable<Decimal>"`) |
| `IsNullable` | `bool` | `true` for reference types and `Nullable<T>` value types |

### QueueTrainRequest

Request body for `POST /trains/queue`.

```csharp
public record QueueTrainRequest(string TrainName, JsonElement Input, int? Priority = null);
```

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `TrainName` | `string` | Yes | — | Fully qualified service interface type name |
| `Input` | `JsonElement` | Yes | — | Raw JSON matching the train's input type |
| `Priority` | `int?` | No | `null` (`0` at dispatch) | Dispatch priority. Higher values dispatched first. |

### QueueTrainResponse

Response from `POST /trains/queue`.

```csharp
public record QueueTrainResponse(long WorkQueueId, string ExternalId);
```

| Property | Type | Description |
|----------|------|-------------|
| `WorkQueueId` | `long` | Database ID of the created work queue entry |
| `ExternalId` | `string` | Unique identifier for tracking the queued work item |

### RunTrainRequest

Request body for `POST /trains/run`.

```csharp
public record RunTrainRequest(string TrainName, JsonElement Input);
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `TrainName` | `string` | Yes | Fully qualified service interface type name |
| `Input` | `JsonElement` | Yes | Raw JSON matching the train's input type |

### RunTrainResponse

Response from `POST /trains/run`.

```csharp
public record RunTrainResponse(long MetadataId);
```

| Property | Type | Description |
|----------|------|-------------|
| `MetadataId` | `long` | Database ID of the metadata record created for this execution |

## Scheduler DTOs

### TriggerDelayedRequest

Request body for `POST /scheduler/trigger/{externalId}/delayed`.

```csharp
public record TriggerDelayedRequest(TimeSpan Delay);
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Delay` | `TimeSpan` | Yes | How long to wait before dispatching. Serialized as `"HH:MM:SS"` in JSON. |

### ScheduleOnceRequest

Request body for `POST /scheduler/schedule-once`. (Endpoint currently returns 501.)

```csharp
public record ScheduleOnceRequest(string TrainName, JsonElement Input, TimeSpan Delay);
```

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `TrainName` | `string` | Yes | Fully qualified service interface type name |
| `Input` | `JsonElement` | Yes | Raw JSON matching the train's input type |
| `Delay` | `TimeSpan` | Yes | Time span before the one-off execution |

### ScheduleOnceResponse

Intended response from `POST /scheduler/schedule-once`. (Not currently returned — endpoint is 501.)

```csharp
public record ScheduleOnceResponse(long ManifestId, string ExternalId);
```

| Property | Type | Description |
|----------|------|-------------|
| `ManifestId` | `long` | Database ID of the created manifest |
| `ExternalId` | `string` | External identifier for the manifest |

### OperationResponse

Returned by all scheduler mutation endpoints (trigger, disable, enable, cancel).

```csharp
public record OperationResponse(bool Success, int? Count = null, string? Message = null);
```

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Whether the operation succeeded |
| `Count` | `int?` | Number of affected records (present on cancel and group operations) |
| `Message` | `string?` | Human-readable description of what happened |

## Query DTOs

### ManifestSummary

Returned by `GET /manifests` and `GET /manifests/{id}`.

```csharp
public record ManifestSummary(
    long Id,
    string ExternalId,
    string Name,
    bool IsEnabled,
    ScheduleType ScheduleType,
    string? CronExpression,
    int? IntervalSeconds,
    int MaxRetries,
    int? TimeoutSeconds,
    DateTime? LastSuccessfulRun,
    long ManifestGroupId,
    long? DependsOnManifestId,
    int Priority
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `long` | Database ID |
| `ExternalId` | `string` | Unique external identifier used for upsert semantics |
| `Name` | `string` | Fully qualified train interface type name |
| `IsEnabled` | `bool` | Whether the manifest is active. Disabled manifests are skipped during polling. |
| `ScheduleType` | `ScheduleType` | Schedule kind — `Cron`, `Interval`, `Once`, or `Dependent` |
| `CronExpression` | `string?` | Cron expression (5 or 6 fields). `null` for non-cron schedules. |
| `IntervalSeconds` | `int?` | Interval in seconds for `Interval` schedule type. `null` otherwise. |
| `MaxRetries` | `int` | Maximum retry attempts before dead-lettering |
| `TimeoutSeconds` | `int?` | Execution timeout in seconds. `null` uses the global default. |
| `LastSuccessfulRun` | `DateTime?` | Timestamp of the last successful completion. `null` if never run successfully. |
| `ManifestGroupId` | `long` | Database ID of the manifest's group |
| `DependsOnManifestId` | `long?` | ID of the parent manifest for dependent scheduling. `null` for independent manifests. |
| `Priority` | `int` | Dispatch priority (0-31). Higher values dispatched first. |

### ManifestGroupSummary

Returned by `GET /manifest-groups` and `GET /manifest-groups/{id}`.

```csharp
public record ManifestGroupSummary(
    long Id,
    string Name,
    int? MaxActiveJobs,
    int Priority,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `long` | Database ID |
| `Name` | `string` | Group name (defaults to the manifest's `externalId` when not set explicitly) |
| `MaxActiveJobs` | `int?` | Maximum concurrent active jobs for this group. `null` means no per-group limit. |
| `Priority` | `int` | Group dispatch priority |
| `IsEnabled` | `bool` | Kill switch for the entire group |
| `CreatedAt` | `DateTime` | When the group was created |
| `UpdatedAt` | `DateTime` | When the group was last modified |

### ExecutionSummary

Returned by `GET /executions` and `GET /executions/{id}`.

```csharp
public record ExecutionSummary(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    string? FailureStep,
    string? FailureReason,
    long? ManifestId,
    bool CancellationRequested
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `long` | Database ID |
| `ExternalId` | `string` | Unique identifier for this execution |
| `Name` | `string` | Train name |
| `TrainState` | `TrainState` | Execution status — `Pending`, `InProgress`, `Completed`, or `Failed` |
| `StartTime` | `DateTime` | When execution began |
| `EndTime` | `DateTime?` | When execution finished. `null` if still in progress. |
| `FailureStep` | `string?` | Name of the step that caused a failure. `null` on success. |
| `FailureReason` | `string?` | Error message from the failure. `null` on success. |
| `ManifestId` | `long?` | Database ID of the associated manifest. `null` for ad-hoc (non-scheduled) executions. |
| `CancellationRequested` | `bool` | Whether cancellation was requested for this execution |

## Pagination Wrapper

### PagedResult\<T\>

Wraps all list endpoint responses.

```csharp
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take);
```

| Property | Type | Description |
|----------|------|-------------|
| `Items` | `IReadOnlyList<T>` | The page of results |
| `TotalCount` | `int` | Total number of records before pagination |
| `Skip` | `int` | Number of records skipped |
| `Take` | `int` | Page size |

## Package

All DTOs are defined in `Trax.Api` (the core package). Both `Trax.Api.Rest` and `Trax.Api.GraphQL` depend on it.

```
dotnet add package Trax.Api
```
