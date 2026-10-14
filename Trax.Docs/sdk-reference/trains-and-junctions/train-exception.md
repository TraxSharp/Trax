---
title: TrainException
description: "Reference for TrainException, TrainExceptionData and FailureClass: the exception Trax raises, the record of a junction failure, and how it is classified."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Exceptions
source:
  - Trax.Core/src/Trax.Core/Exceptions/TrainException.cs
  - Trax.Core/src/Trax.Core/Exceptions/TrainExceptionData.cs
  - Trax.Core/src/Trax.Core/Exceptions/FailureClass.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 7
---

# TrainException

`TrainException` is the exception Trax uses for a failure it raises itself, and the type a recorded failure is rebuilt as. `TrainExceptionData` is the structured record of a junction failure: which train, which run, which junction, what was thrown. It is attached to the exception, stored on the run's metadata, and sent between processes as JSON. `FailureClass` is how the failure was classified.

## Signatures

```csharp fragment
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

public enum FailureClass
{
    Unclassified = 0,
    Transient = 1,
    Conflict = 2,
    Permanent = 3,
}
```

The comments are the JSON property names.

## Members

| `TrainExceptionData` field | Description |
|----------------------------|-------------|
| `TrainName` | The train class's short name (`GetType().Name`) for a junction failure. For a failure raised outside any junction, Trax.Effect records the train's canonical name instead. |
| `TrainExternalId` | The failing run's `ExternalId`, matching the external id on its metadata row |
| `Type` | The thrown exception's type name, for example `InvalidOperationException` |
| `Junction` | The junction class's short name |
| `Message` | The exception's message |
| `StackTrace` | Where it was thrown; nullable so older serialized records still read |
| `FailureClass` | How the failure was classified where it happened, or `null`. Carried so a remote run's classification survives the trip home. See [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier). |

| `FailureClass` | Meaning |
|----------------|---------|
| `Unclassified` | No classifier was registered, or it did not recognise the failure. The default. |
| `Transient` | Expected to succeed if attempted again: a throttle, a lost connection, a server timeout reported as an error. |
| `Conflict` | Someone else changed the thing first; the work was built on a state that has moved. |
| `Permanent` | Will fail the same way every time: a validation error, a missing record, a rejected credential. |

## Behavior

- **On a caught exception.** When a junction throws, its exception is returned unchanged, with the record in `exception.Data["TrainExceptionData"]`. The message is not rewritten, so code outside Trax sees the original exception.
- **On a failed run's metadata.** The failure fields on `Metadata` (`FailureJunction`, `FailureException`, `FailureReason`, `StackTrace`, `FailureClass`) come from this record.
- **From a remote run.** A failure that crossed a process boundary is rebuilt as a `TrainException` whose message is the record's JSON. Only an exception whose type is exactly `TrainException` is read this way; a subclass's message is treated as its own text.
- **Its message reaches callers.** The GraphQL error filter returns an exact `TrainException`'s message as written, with the code `TRAX_TRAIN_ERROR`; a subclass of it reads as `The train failed.`. The scheduler's runner endpoints return the message of any `TrainException`, subclasses included. Other exception types are not passed through by either.

## Example

```csharp compile
using Trax.Core.Exceptions;
using Trax.Core.Junction;

public class BookShipment(IShipping shipping) : Junction<Order, Shipment>
{
    public override Task<Shipment> Run(Order order) =>
        order.Lines.Count == 0
            ? throw new TrainException($"Order {order.Id} has no lines to ship.")
            : shipping.Book(order, CancellationToken);
}
```

The caller reads `Order 42 has no lines to ship.`, so the message carries no secret and no other user's data.

## See also

- [Classifying failures](/docs/effect/failure-classification)
- [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier)
- [Metadata](/docs/effect/metadata), where a failure is stored
