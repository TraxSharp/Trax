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
| `ParentId` | `long?` | Links to parent metadata for nested trains |
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

## Host Tracking

In distributed environments (Lambda, ECS, multiple servers), every metadata record captures where the train actually executed. Host information is auto-detected at startup and stamped on each execution — see [Host Tracking](host-tracking.md) for details on auto-detection, custom labels, and the builder API.

## Nested Trains

A junction can dispatch another train mid-execution by injecting `ITrainBus`. Pass the current `Metadata` to the child train to link the executions — this creates a tree of metadata records you can query to trace execution across an entire network of trains.

See [Mediator: Nested Trains](/docs/mediator#nested-trains) for implementation details.

## Execution Flow

For a diagram of the full ServiceTrain lifecycle — from client request through metadata initialization to SaveChanges — see [Effect Architecture](architecture.md#execution-flow).
