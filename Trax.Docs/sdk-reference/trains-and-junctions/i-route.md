---
title: IRoute
description: "Reference for IRoute, the one-method contract every train implements: take an input, return an output or throw."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Route
source:
  - Trax.Core/src/Trax.Core/Route/IRoute.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 5
---

# IRoute

The contract every train implements: take an input, produce an output or fail. `Train<TInput, TReturn>` implements it and `IServiceTrain<TIn, TOut>` extends it, so code that only runs something can depend on `IRoute` without knowing it is a train.

## Signature

```csharp fragment
namespace Trax.Core.Route;

public interface IRoute<in TIn, TOut>
{
    Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `TIn` | The input to run with |
| `cancellationToken` | `CancellationToken` | Cancels the run. Defaults to `CancellationToken.None`. |

## Returns

`Task<TOut>`, the output. A failure is thrown, as from [Run](/docs/sdk-reference/train-methods/run).

## Behavior

| Type | Relationship to `IRoute` |
|------|--------------------------|
| [Train&lt;TInput, TReturn&gt;](/docs/sdk-reference/trains-and-junctions/train) | Implements `IRoute<TInput, TReturn>` |
| [IServiceTrain&lt;TIn, TOut&gt;](/docs/sdk-reference/trains-and-junctions/service-train) | Extends `IRoute<TIn, TOut>` and `IDisposable`, and adds `Metadata` |
| A service train's own interface | Extends `IServiceTrain<TIn, TOut>`, and so `IRoute` |

The [registration helpers](/docs/sdk-reference/trains-and-junctions/route-registration) (`AddScopedTraxRoute` and its siblings) are named after it because they register any route, train or junction. None of them registers `IRoute<,>` itself, so the container cannot resolve `IRoute<TIn, TOut>` unless you forward it.

## Example

```csharp compile
using Trax.Core.Route;

public class OrderEndpoint(IRoute<CreateOrderInput, OrderResult> createOrder)
{
    public Task<OrderResult> Handle(CreateOrderInput input, CancellationToken ct) =>
        createOrder.Run(input, ct);
}
```

Forwarding the train's interface so `IRoute` resolves:

```csharp compile
using Trax.Core.Route;
using Trax.Effect.Extensions;

services.AddTransientTraxRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddTransient<IRoute<CreateOrderInput, OrderResult>>(sp =>
    sp.GetRequiredService<ICreateOrderTrain>()
);
```

## See also

- [Train](/docs/sdk-reference/trains-and-junctions/train) and [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), the types that implement it
- [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration)
- [Trains and Junctions](/docs/core/trains-and-junctions), how a train runs
