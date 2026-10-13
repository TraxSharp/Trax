---
title: Mediator exceptions
description: "Reference for the exceptions in Trax.Mediator.Exceptions: what throws each one, the properties it carries, and what its message contains."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 5
package: Trax.Mediator
namespace: Trax.Mediator.Exceptions
source:
  - Trax.Mediator/src/Trax.Mediator/Exceptions
---

# Mediator exceptions

The exceptions Trax.Mediator throws, all in `Trax.Mediator.Exceptions`. None of them is a
`TrainException`: Trax.Api's error filter and the scheduler's runner endpoints pass a
`TrainException`'s message through as a train author's words, and these are host or caller errors
instead.

## Signatures

```csharp fragment
namespace Trax.Mediator.Exceptions;

public class TrainNotFoundException : InvalidOperationException
{
    public TrainNotFoundException(string requestedName);
    public string RequestedName { get; }
}

public class AmbiguousTrainNameException : InvalidOperationException
{
    public AmbiguousTrainNameException(string requestedName, IReadOnlyList<string> candidateFullNames);
    public string RequestedName { get; }
    public IReadOnlyList<string> CandidateFullNames { get; }
}

public class NoTrainForInputException : InvalidOperationException
{
    public NoTrainForInputException(Type inputType, IReadOnlyList<string> scannedAssemblies);
    public Type InputType { get; }
    public IReadOnlyList<string> ScannedAssemblies { get; }
}

public class TrainInputValidationException : InvalidOperationException
{
    public TrainInputValidationException(string trainName, int observedBytes, int maxBytes);
    public string TrainName { get; }
    public int ObservedBytes { get; }
    public int MaxBytes { get; }
}

public class TrainAuthorizationNotConfiguredException : InvalidOperationException
{
    public TrainAuthorizationNotConfiguredException(string trainName, string message);
    public string TrainName { get; }
}

public class QueueHookTimeoutException : InvalidOperationException
{
    public QueueHookTimeoutException(string trainName, TimeSpan limit);
    public string TrainName { get; }
    public TimeSpan Limit { get; }
}

public class QueuedWorkCancelledException : InvalidOperationException
{
    public QueuedWorkCancelledException(long workQueueId, string trainName);
    public long WorkQueueId { get; }
    public string TrainName { get; }
}

public class DecisionReplayNotSupportedException : NotSupportedException
{
    public DecisionReplayNotSupportedException(Type implementationType);
    public Type ImplementationType { get; }
}
```

## Members

| Exception | Thrown by | Properties | Message |
|-----------|-----------|------------|---------|
| `TrainNotFoundException` | `ITrainExecutionService`, when no registered train has the name | `RequestedName`: the name as the caller passed it | Always "The requested train was not found."; the name is only in `RequestedName`, which is caller input |
| `AmbiguousTrainNameException` | `ITrainExecutionService`, when the name matches more than one train's friendly name | `RequestedName`; `CandidateFullNames`: each match's interface `FullName`, any of which resolves | Lists every candidate |
| `NoTrainForInputException` | `ITrainBus.RunAsync` and `InitializeTrain`, when no registered train takes the input's type | `InputType`; `ScannedAssemblies`: the names of the assemblies the registry scanned, empty when it does not scan | Names the input type, the scanned assemblies and `ScanAssemblies(...)`. Meant for the host's log |
| `TrainInputValidationException` | `ITrainExecutionService` and `TrainInputReader`, when input JSON is over the size cap, or a queued input's stored form is over its cap | `TrainName`; `ObservedBytes`; `MaxBytes` | Always "The train input failed validation." |
| `TrainAuthorizationNotConfiguredException` | `ITrainExecutionService`, for a `[TraxAuthorize]` train on a host with no `ITrainAuthorizationService`, outside a trusted scope and without `AllowMissingAuthorizationService()` | `TrainName` | Names the train and how to fix the host |
| `QueueHookTimeoutException` | `ITrainExecutionService.QueueAsync`, when an `OnQueue` hook runs past `MaxQueueHookDuration`; the enqueue is rolled back | `TrainName`; `Limit` | Names the train and the limit in seconds |
| `QueuedWorkCancelledException` | `ITrainExecutionService.QueueAsync`, for a train that defers promotion whose staged entry was cancelled while its hook ran | `WorkQueueId`; `TrainName` | Names the entry and the train, and says the hook's side-effect may already be applied |
| `DecisionReplayNotSupportedException` | The default `QueueAsync(..., QueueTrainOptions, ...)` body, when `ReplayDecisionsOf` is set on an implementation that does not override it | `ImplementationType`: the registered `ITrainExecutionService` | Names the implementation |

## Behavior

Before Trax.Mediator 1.23.3, `ITrainBus` threw a `TrainException` with the same message where it
now throws `NoTrainForInputException`. The conditions under which each exception is thrown are on
the page of the member that throws it.

## Example

```csharp compile
public class OrderEndpoint(ITrainExecutionService trains, ILogger<OrderEndpoint> logger)
{
    public async Task<IResult> Run(string trainName, string inputJson, CancellationToken ct)
    {
        try
        {
            var result = await trains.RunAsync(trainName, inputJson, ct);
            return Results.Ok(result.MetadataId);
        }
        catch (TrainNotFoundException ex)
        {
            logger.LogWarning("Unknown train {Name}", ex.RequestedName);
            return Results.NotFound();
        }
        catch (AmbiguousTrainNameException ex)
        {
            logger.LogWarning("Ambiguous train {Name}", ex.RequestedName);
            return Results.BadRequest();
        }
    }
}
```

## See also

- [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
- [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus)
- [TrainInputReader](/docs/sdk-reference/mediator-api/train-input-reader)
