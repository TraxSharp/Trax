---
layout: default
title: Metadata
parent: Effect
nav_order: 1
---

# Metadata

Every train execution produces a metadata record. It captures everything about the run: when it started, what junctions it passed through, what it was carrying, and whether it completed or failed.

```csharp
public class Metadata : IModel, IDisposable
{
    public long Id { get; private set; }              // Unique identifier
    public string Name { get; set; }                  // Train name
    public string ExternalId { get; set; }            // GUID for external references
    public string? Executor { get; private set; }     // Assembly that ran the train
    public TrainState TrainState { get; set; }   // Pending/InProgress/Completed/Failed/Cancelled
    public DateTime StartTime { get; set; }           // When the train started
    public DateTime? EndTime { get; set; }            // When the train finished
    public string? Input { get; set; }                // Serialized input (jsonb)
    public string? Output { get; set; }               // Serialized output (jsonb)
    public string? FailureJunction { get; }               // Which junction failed
    public string? FailureException { get; }          // Exception type
    public string? FailureReason { get; }             // Error message
    public string? StackTrace { get; set; }           // Stack trace if failed
    public long? ParentId { get; set; }               // For nested trains
    public long? ManifestId { get; set; }             // For scheduled trains
    public DateTime? ScheduledTime { get; set; }      // Scheduled execution time
    public bool CancellationRequested { get; set; }   // Cross-server cancellation flag
    public DateTime? JunctionStartedAt { get; set; }      // Current junction start timestamp
    public string? CurrentlyRunningJunction { get; set; } // Name of the currently running junction
    public string? HostName { get; set; }              // Machine hostname where the train ran
    public string? HostEnvironment { get; set; }       // Environment type (lambda, ecs, kubernetes, server)
    public string? HostInstanceId { get; set; }        // Instance identifier (pod name, Lambda stream, etc.)
    public string? HostLabels { get; set; }            // User-provided labels as JSON (region, service, team)
}
```

The `TrainState` tracks the lifecycle: `Pending` -> `InProgress` -> `Completed`, `Failed`, or `Cancelled`. If a train fails, the metadata record captures the exception, stack trace, and which junction it happened at.

## Host Tracking

In distributed environments (Lambda, ECS, multiple servers), every metadata record captures where the train actually executed. Host information is auto-detected at startup and stamped on each execution — see [Host Tracking](host-tracking.md) for details on auto-detection, custom labels, and the builder API.

## Nested Trains

A junction can dispatch another train mid-execution by injecting `ITrainBus`. Pass the current `Metadata` to the child train to link the executions — this creates a tree of metadata records you can query to trace execution across an entire network of trains.

See [Mediator: Nested Trains]({{ site.baseurl }}{% link mediator.md %}#nested-trains) for implementation details.

## Execution Flow

For a diagram of the full ServiceTrain lifecycle — from client request through metadata initialization to SaveChanges — see [Effect Architecture](architecture.md#execution-flow).
