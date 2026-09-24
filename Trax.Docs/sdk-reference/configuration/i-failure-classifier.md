---
layout: default
title: IFailureClassifier
parent: Configuration
grand_parent: SDK Reference
nav_order: 15
---

# IFailureClassifier

Decides what kind of failure an exception represents, so Trax records a `FailureClass` on every failed run. Optional: with none registered, every failure records `Unclassified`.

## Signature

```csharp
namespace Trax.Effect.Services.FailureClassifier;

public interface IFailureClassifier
{
    FailureClass? Classify(Exception exception);
}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `exception` | `Exception` | The original exception the run failed with, not a wrapper, so you can type-check it and read its structured data |

**Returns**: `FailureClass?`. One of `Transient`, `Conflict`, `Permanent` (or `Unclassified`), or null to leave the failure unclassified. `FailureClass` lives in `Trax.Core.Exceptions`.

## Registration

There is no builder method. Register it in the container:

```csharp
services.AddSingleton<IFailureClassifier, NetSuiteFailureClassifier>();
```

Trax resolves one `IFailureClassifier` from the train's service provider, so the last registration wins. For runs executed on a remote worker, register it in the **worker** process.

## Behavior

| Situation | What is recorded |
|---|---|
| No classifier registered, or it returns null | `Unclassified` |
| The classifier throws | `Unclassified`; the classifier's exception is logged and the run's failure is unaffected |
| The run was cancelled, including an `HttpClient` timeout (`TaskCanceledException`) | `Cancelled` state; the classifier is not called |
| The failure already carries a class (a remote worker sent one, or a calling-side junction preserved it) | The carried class; it wins over the local classifier |
| The failure was rebuilt from a serialized record (a remote failure) and carries no class | `Unclassified`; a rebuilt exception is never passed to the local classifier, because its original type is gone |
| The scheduler recorded the failure (dispatch failure, stale-run reaping) | `Unclassified` |
| A failure raised outside any junction | The classifier's answer |

The recorded class is `Metadata.FailureClass`, readable in `OnFailed`, persisted in the `failure_class` column, and exposed over GraphQL as `failureClass`.

## Example

```csharp
public class NetSuiteFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is NetSuiteRestException { StatusCode: HttpStatusCode.BadRequest } ex
        && ex.ErrorDetails.Any(d => d.Detail.Contains("Record has been changed"))
            ? FailureClass.Conflict
            : null;
}
```

See [Classifying failures](/docs/core/trains-and-junctions#classifying-failures) for the concepts.

## Package

```
dotnet add package Trax.Effect
```
