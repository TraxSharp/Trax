---
title: AddServices
description: "Reference for AddServices, which stores up to seven services in Memory under the interfaces they are passed as, for the junctions after it."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.AddServices.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 5
---

# AddServices

Stores services in Memory so the junctions after it can take them through their constructors. Each service is stored under the interface it is passed as (the type argument in its position), not under its concrete type. It has overloads for one to seven services.

## Signatures

```csharp fragment
// Train<TInput, TReturn>
protected Monad<TInput, TReturn> AddServices<T1>(T1 service);
protected Monad<TInput, TReturn> AddServices<T1, T2>(T1 s1, T2 s2);
protected Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3);
protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4);
protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5);
protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6);
protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6, T7 s7);

// MonadTask<TInput, TReturn>, later in a chain: the same overloads, returning MonadTask<TInput, TReturn>
public MonadTask<TInput, TReturn> AddServices<T1>(T1 service);
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `T1` ... `T7` | The interface each service is stored under |
| `service`, `s1` ... `s7` | A class instance that implements the interface in its position. Not `null`, and available when `Junctions()` runs. |

## Returns

The chain: `Monad<TInput, TReturn>` from the train's method, `MonadTask<TInput, TReturn>` from a later link.

## Behavior

1. Every service is checked before any is stored, so a refused call stores nothing.
2. Each service is stored under the type argument in its own position: `AddServices<IReader, IWriter>(store, store)` fills both the `IReader` and the `IWriter` slot with `store`, whatever else `store` implements.
3. A [Moq](https://github.com/moq/moq4) mock (`new Mock<IMyService>().Object`) is stored under the type it mocks.
4. The startup chain check records the same slots, as seeds, so what it verifies is what the run fills.

A refused call sets a `TrainException` on the train, which fails like any other step, rather than throwing out of `Junctions()`:

| Condition | Message | While the chain is read |
|-----------|---------|-------------------------|
| A service is `null` | `AddServices<IWriter> received null for the service at position 2 of 2. A service cannot be null.` (no position when one service is passed) | Refused: a field assigned later, in `OnStarted` say, is not supported |
| A service is a struct | `Params (<type>) to AddServices must be Classes.` | Refused |
| A type argument is a class, or the service does not implement it | `Class (<type>) passed to AddServices as (<slot>) must be passed as an interface it implements.` | A class type argument is refused |

More than seven services take more than one call.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class NotifyingOrderTrain(IPaymentGateway payments, INotificationService notifications)
    : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        AddServices<IPaymentGateway, INotificationService>(payments, notifications)
            .Chain<ValidateOrder>()
            .Chain<ProcessPayment>()      // takes IPaymentGateway from Memory
            .Chain<SendConfirmation>()    // takes INotificationService from Memory
            .Resolve();
}
```

## See also

- [Extract](/docs/sdk-reference/train-methods/extract)
- [Dependency Injection in Junctions](/docs/sdk-reference/trains-and-junctions/route-registration)
