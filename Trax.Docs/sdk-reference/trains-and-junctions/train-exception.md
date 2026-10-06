---
layout: default
title: TrainException
description: Reference for TrainException and TrainExceptionData, the structured record of a junction failure attached to exceptions, stored on metadata and sent as JSON.
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 7
---

# TrainException and TrainExceptionData

`TrainException` is the exception Trax uses for a failure it raises itself, and the type a recorded failure is rebuilt as. `TrainExceptionData` is the structured record of a junction failure: which train, which run, which junction, what was thrown. It is attached to the exception, stored on the run's metadata, and sent between processes as JSON.

## Signatures

```csharp
namespace Trax.Core.Exceptions;

public class TrainException : Exception
{
    public TrainException(string message);
}

public class TrainExceptionData
{
    public required string TrainName { get; set; }        // "trainName"
    public required string TrainExternalId { get; set; }  // "trainExternalId"
    public required string Type { get; set; }             // "type"
    public required string Junction { get; set; }         // "junction"
    public required string Message { get; set; }          // "message"
    public string? StackTrace { get; set; }               // "stackTrace"
    public FailureClass? FailureClass { get; set; }       // "failureClass"
}

public enum FailureClass { Unclassified = 0, Transient = 1, Conflict = 2, Permanent = 3 }
```

The comments are the JSON property names.

## TrainExceptionData fields

| Field | Description |
|-------|-------------|
| `TrainName` | The train class's short name (`GetType().Name`) for a junction failure. For a failure raised outside any junction, Trax.Effect records the train's canonical name instead. |
| `TrainExternalId` | The failing run's `ExternalId`, matching the external id on its metadata row |
| `Type` | The thrown exception's type name, for example `InvalidOperationException` |
| `Junction` | The junction class's short name |
| `Message` | The exception's message |
| `StackTrace` | Where it was thrown; nullable so older serialized records still read |
| `FailureClass` | How the failure was classified where it happened, or `null`. Carried so a remote run's classification survives the trip home. See [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier). |

## Where you meet them

- **On a caught exception.** When a junction throws, its exception is returned unchanged, with the record in `exception.Data["TrainExceptionData"]`. The message is not rewritten, so code outside Trax sees the original exception.
- **On a failed run's metadata.** The failure fields on `Metadata` (`FailureJunction`, `FailureException`, `FailureReason`, `StackTrace`, `FailureClass`) come from this record.
- **From a remote run.** A failure that crossed a process boundary is rebuilt as a `TrainException` whose message is the record's JSON. Only an exception whose type is exactly `TrainException` is read this way; a subclass's message is treated as its own text.

## Throwing it

Throw `TrainException` (or your own subclass of it) from a junction for a failure whose message you mean callers to see. Trax.Api's error filter and the scheduler's runner endpoints pass a `TrainException`'s message through as written by the train's author, and do not do that for other exception types.

```csharp
public override Task<Shipment> Run(Order order) =>
    order.Lines.Count == 0
        ? throw new TrainException($"Order {order.Id} has no lines to ship.")
        : shipping.Book(order, CancellationToken);
```

Do not put a secret or another user's data in that message.

See [Classifying failures](/docs/core/trains-and-junctions#classifying-failures) and [Metadata](/docs/effect/metadata) for the concepts.

## Package

```
dotnet add package Trax.Core
```
