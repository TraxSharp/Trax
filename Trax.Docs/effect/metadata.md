---
layout: default
title: Metadata
parent: Effect
nav_order: 1
---

# Metadata

Every train journey produces a log — the metadata record. It captures everything about the trip: when it departed, where it went, what it was carrying, and whether it arrived or derailed.

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
    public string? FailureStep { get; }               // Which step failed
    public string? FailureException { get; }          // Exception type
    public string? FailureReason { get; }             // Error message
    public string? StackTrace { get; set; }           // Stack trace if failed
    public long? ParentId { get; set; }               // For nested trains
    public long? ManifestId { get; set; }             // For scheduled trains
    public DateTime? ScheduledTime { get; set; }      // Scheduled execution time
    public bool CancellationRequested { get; set; }   // Cross-server cancellation flag
    public DateTime? StepStartedAt { get; set; }      // Current step start timestamp
    public string? CurrentlyRunningStep { get; set; } // Name of the currently running step
}
```

The `TrainState` tracks the journey: `Pending` (boarding) -> `InProgress` (in transit) -> `Completed` (arrived), `Failed` (derailed), or `Cancelled`. If a train derails, the journey log captures the exception, stack trace, and which stop it happened at.

## Nested Trains

A stop can dispatch another train mid-journey by injecting `ITrainBus`. Pass the current `Metadata` to the child train to link the journeys — this creates a tree of journey logs you can query to trace execution across an entire network of trains.

See [Mediator: Nested Trains]({{ site.baseurl }}{% link mediator.md %}#nested-trains) for implementation details.

## Execution Flow

For a diagram of the full ServiceTrain lifecycle — from client request through metadata initialization to SaveChanges — see [Effect Architecture](architecture.md#execution-flow).
