---
layout: default
title: Metadata
parent: Core Concepts
nav_order: 4
---

# Metadata

Every train execution creates a metadata record:

```csharp
public class Metadata : IModel, IDisposable
{
    public int Id { get; }                          // Unique identifier
    public string Name { get; set; }                // Train name
    public string ExternalId { get; set; }          // GUID for external references
    public string? Executor { get; }                // Assembly that ran the train
    public TrainState TrainState { get; set; } // Pending/InProgress/Completed/Failed
    public DateTime StartTime { get; set; }         // When train started
    public DateTime? EndTime { get; set; }          // When train finished
    public string? Input { get; set; }              // Serialized input (jsonb)
    public string? Output { get; set; }             // Serialized output (jsonb)
    public string? FailureStep { get; }             // Which step failed
    public string? FailureException { get; }        // Exception type
    public string? FailureReason { get; }           // Error message
    public string? StackTrace { get; set; }         // Stack trace if failed
    public int? ParentId { get; set; }              // For nested trains
    public int? ManifestId { get; set; }            // For scheduled trains
}
```

The `TrainState` tracks progress: `Pending` → `InProgress` → `Completed` (or `Failed`). If a train fails, the metadata captures the exception, stack trace, and which step failed.

## Nested Trains

Trains can run other trains by injecting `ITrainBus`. Pass the current `Metadata` to the child train to establish a parent-child relationship—this creates a tree of metadata records you can query to trace execution across trains.

See [Nested Trains](../usage-guide/mediator.md#nested-trains) for implementation details.

## Execution Flow

For a diagram of the full ServiceTrain lifecycle—from client request through metadata initialization to SaveChanges—see [Core & Effects: Execution Flow](../architecture/core-and-effects.md#execution-flow).
