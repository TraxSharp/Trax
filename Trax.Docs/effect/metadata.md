---
layout: default
title: Metadata
parent: Effect
nav_order: 1
---

# Metadata

Every train execution produces a metadata record. It captures everything about the run: when it started, what junctions it passed through, what it was carrying, and whether it completed or failed.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `long` | Auto-generated primary key |
| `Name` | `string` | Train name (canonical interface FullName) |
| `ExternalId` | `string` | GUID for external references |
| `Executor` | `string?` | Assembly that ran the train |
| `TrainState` | `TrainState` | `Pending` / `InProgress` / `Completed` / `Failed` / `Cancelled` |
| `StartTime` | `DateTime` | When the train started |
| `EndTime` | `DateTime?` | When the train finished |
| `Input` | `string?` | Serialized input (jsonb) |
| `Output` | `string?` | Serialized output (jsonb) |
| `FailureJunction` | `string?` | Which junction failed |
| `FailureException` | `string?` | Exception type |
| `FailureReason` | `string?` | Error message |
| `StackTrace` | `string?` | Stack trace if failed |
| `FailureClass` | `FailureClass` | `Unclassified` / `Transient` / `Conflict` / `Permanent`, from the registered [failure classifier](/docs/core/trains-and-junctions#classifying-failures). `Unclassified` when the run did not fail or nothing classified it |
| `ParentId` | `long?` | The parent run's metadata id. Nothing in Trax sets it at present, so it is null for every run, including a train dispatched from a junction; see [Nested Trains](#nested-trains) |
| `ManifestId` | `long?` | Links to manifest for scheduled trains |
| `ScheduledTime` | `DateTime?` | Scheduled execution time |
| `CancellationRequested` | `bool` | Cross-server cancellation flag |
| `JunctionStartedAt` | `DateTime?` | Current junction start timestamp (requires `AddJunctionProgress`) |
| `CurrentlyRunningJunction` | `string?` | Name of the currently running junction (requires `AddJunctionProgress`) |
| `HostName` | `string?` | Machine hostname where the train ran |
| `HostEnvironment` | `string?` | Environment type (lambda, ecs, kubernetes, server) |
| `HostInstanceId` | `string?` | Instance identifier (pod name, Lambda stream, etc.) |
| `HostLabels` | `string?` | User-provided labels as JSON (region, service, team) |

The `TrainState` tracks the lifecycle: `Pending` -> `InProgress` -> `Completed`, `Failed`, or `Cancelled`. If a train fails, the metadata record captures the exception, stack trace, and which junction it happened at.

### Failure Fields

When a junction throws, Trax captures structured context without modifying the original exception. The junction name, exception type, original message, and stack trace from the throw site are attached to the exception via `Exception.Data["TrainExceptionData"]`. When the train finishes, `Metadata.AddException()` reads this structured data and populates:

| Field | Source | Example |
|-------|--------|---------|
| `FailureJunction` | Junction class name where the throw occurred | `"ValidateInputJunction"` |
| `FailureException` | Exception type short name | `"InvalidOperationException"` |
| `FailureReason` | Original exception message (unmodified) | `"Input 'email' was null"` |
| `StackTrace` | Stack trace from the original throw site | Points to the junction's `Run` method |
| `FailureClass` | The registered `IFailureClassifier`'s answer, set before the failure is recorded. Cancelled runs are not classified | `Conflict` |

The original exception is rethrown to callers with its type, message, and stack trace intact. `TrainExceptionData` rides along in `Exception.Data` for any code that wants structured context (e.g., logging, monitoring).

## Host Tracking

In distributed environments (Lambda, ECS, multiple servers), every metadata record captures where the train actually executed. Host information is auto-detected at startup and stamped on each execution. See [Host Tracking](/docs/effect/host-tracking) for details on auto-detection, custom labels, and the builder API.

## Nested Trains

A junction can dispatch another train mid-execution by injecting `ITrainBus`. The child gets a metadata record of its own, but it is not linked to the parent's: its `ParentId` is not set, and passing the parent's `Metadata` to `RunAsync` throws rather than linking them. The column, the API's `childCount` and `executionChildren`, and the cleanup that clears a deleted parent's children all exist, but no Trax code path writes a parent link today.

See [Mediator: Nested Trains](/docs/mediator#nested-trains) for how to dispatch a child train.

## Execution Flow

For a diagram of the full ServiceTrain lifecycle, from client request through metadata initialization to SaveChanges, see [Effect Architecture](/docs/effect/architecture#execution-flow).
