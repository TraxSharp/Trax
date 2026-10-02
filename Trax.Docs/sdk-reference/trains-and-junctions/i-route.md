---
layout: default
title: IRoute
description: Reference for IRoute, the one-method contract every train fulfils, where it appears, and why the registration helpers are named after it.
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 5
---

# IRoute

The contract every train fulfils: take an input, produce an output or fail. `Train<TInput, TReturn>` implements it, and `IServiceTrain<TIn, TOut>` extends it, so code that only needs to run something can depend on `IRoute` without knowing it is a train.

## Signature

```csharp
namespace Trax.Core.Route;

public interface IRoute<in TIn, TOut>
{
    Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `TIn` | The input to run with |
| `cancellationToken` | `CancellationToken` | Cancels the run |

**Returns**: `Task<TOut>`, the output. A failure is thrown, as from [Run](/docs/sdk-reference/train-methods/run).

## Where it appears

| Type | Relationship |
|------|--------------|
| [Train&lt;TInput, TReturn&gt;](/docs/sdk-reference/trains-and-junctions/train) | Implements `IRoute<TInput, TReturn>` |
| [IServiceTrain&lt;TIn, TOut&gt;](/docs/sdk-reference/trains-and-junctions/service-train) | Extends `IRoute<TIn, TOut>` and `IDisposable`, and adds `Metadata` |
| A service train's own interface | Extends `IServiceTrain<TIn, TOut>`, and so `IRoute` |

The [registration helpers](/docs/sdk-reference/trains-and-junctions/route-registration) are named after it (`AddScopedTraxRoute` and its siblings) because they register any route, train or junction.

## Example

```csharp
using Trax.Core.Route;

public class OrderEndpoint(IRoute<CreateOrderInput, OrderResult> createOrder)
{
    public Task<OrderResult> Handle(CreateOrderInput input, CancellationToken ct) =>
        createOrder.Run(input, ct);
}
```

The container has no registration for `IRoute<,>` itself: register the train under its own interface and resolve that, or forward it (`services.AddTransient<IRoute<CreateOrderInput, OrderResult>>(sp => sp.GetRequiredService<ICreateOrderTrain>())`).

## Package

```
dotnet add package Trax.Core
```
