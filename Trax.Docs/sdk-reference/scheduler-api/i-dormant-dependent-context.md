---
title: IDormantDependentContext
description: "Reference for IDormantDependentContext, which a running parent train uses to activate its dormant dependent manifests with input chosen at runtime."
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 24
package: Trax.Scheduler
namespace: Trax.Scheduler.Services.DormantDependentContext
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/DormantDependentContext/IDormantDependentContext.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/DormantDependentContext/DormantDependentContext.cs
---

# IDormantDependentContext

A scoped service a running train injects to activate its own dormant dependents: manifests declared
with `.Dormant()` (see [Dependent scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling#dormant-option))
that never fire on their own. The `JobRunner` initializes it with the parent manifest before the
train runs, and only dormant dependents of that manifest can be activated.

## Signature

```csharp fragment
namespace Trax.Scheduler.Services.DormantDependentContext;

public interface IDormantDependentContext
{
    Task ActivateAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    Task ActivateManyAsync<TTrain, TInput, TOutput>(
        IEnumerable<(string ExternalId, TInput Input)> activations,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;
}
```

## Members

| Member | Description |
|--------|-------------|
| [`ActivateAsync`](#activateasync) | Queues one dormant dependent with the given input |
| [`ActivateManyAsync`](#activatemanyasync) | Queues several, in one transaction |

## ActivateAsync

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `externalId` | `string` | required | The external id of the dormant dependent manifest |
| `input` | `TInput` | required | The input the dependent runs with |
| `ct` | `CancellationToken` | `default` | Cancels the activation |

**Throws**: `InvalidOperationException` when the manifest does not exist, is not
`DormantDependent`, or does not depend on the current parent.

A disabled manifest, or one in a disabled group, is skipped with a warning log. So is one that
already has a queued work queue entry or an active run (`Pending` or `InProgress` metadata).

## ActivateManyAsync

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `activations` | `IEnumerable<(string ExternalId, TInput Input)>` | required | The manifests to activate and their inputs |
| `ct` | `CancellationToken` | `default` | Cancels the activation |

Every activation is made in one database transaction. A failed check (a wrong parent, a manifest
that is not dormant) rolls back the whole batch; a skipped entry (disabled, already queued or
active) does not.

## Behavior

Outside a scheduled run (a train invoked through `ITrainBus`, a GraphQL mutation or a test) the
context has no parent manifest. `ActivateAsync` and `ActivateManyAsync` then log a warning and
return without activating anything or throwing, so the same train can run both ways.

`AddScheduler` and `AddTraxJobRunner` register the default `DormantDependentContext` as scoped.

## Example

```csharp compile
public class SelectiveDispatchJunction(IDormantDependentContext dormants)
    : Junction<DispatchInput, Unit>
{
    public override async Task<Unit> Run(DispatchInput input)
    {
        await dormants.ActivateAsync<IChildTrain, ChildInput, Unit>(
            "child-1",
            new ChildInput { Data = input.RuntimeData });

        var activations = input.Items
            .Select(item => ($"child-{item.Id}", new ChildInput { Data = item.Data }));
        await dormants.ActivateManyAsync<IChildTrain, ChildInput, Unit>(activations);

        return Unit.Default;
    }
}
```

## See also

- [Dependent scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling), declaring a dormant dependent
- [Dependent Trains](/docs/scheduler/dependent-trains)
