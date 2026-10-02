---
layout: default
title: Inject
description: Reference for the Inject attribute, which fills a public property from the container when a train is resolved through a Trax registration helper.
parent: Attributes
grand_parent: SDK Reference
nav_order: 5
---

# Inject

Marks a public property to be filled from the container when a train is resolved through a [Trax registration helper](/docs/sdk-reference/trains-and-junctions/route-registration). `ServiceTrain` uses it for its own framework services (`EffectRunner`, `JunctionEffectRunner`, `LifecycleHookRunner`, `Logger`, `ServiceProvider`).

It is framework plumbing, not the way to give a junction or a train its dependencies. Use constructor injection for those.

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class InjectAttribute : Attribute
{
    public InjectAttribute();
}
```

## How it is filled

The `Add*TraxRoute` and `Add*TraxJunction` factories call `IServiceProvider.InjectProperties(instance)` after resolving the implementation. For every public instance property that carries `[Inject]` and has a setter:

| Property state | What happens |
|----------------|--------------|
| Already non-null | Left alone |
| Type is `IEnumerable<T>` | Set to every registered `T` |
| Any other type | Set to `GetService(type)`, or left `null` when nothing is registered |

Nothing else reads the attribute. A type resolved any other way (constructed with `new`, resolved by its concrete class, or a junction a train builds for itself in its chain) gets no property injection, and a junction's `[Inject]` properties stay `null`.

## Example

```csharp
// Do this: constructor injection
public class ChargePayment(IPaymentGateway gateway) : Junction<ValidatedOrder, PaymentReceipt>
{
    public override Task<PaymentReceipt> Run(ValidatedOrder order) =>
        gateway.Charge(order.CustomerId, order.Total, CancellationToken);
}

// Not this: the train builds the junction itself, so the property is never set
public class ChargePayment : Junction<ValidatedOrder, PaymentReceipt>
{
    [Inject] public IPaymentGateway? Gateway { get; set; }
    // ...
}
```

`IServiceProvider.InjectProperties` is public for Trax's own registration code and hidden from completion.

## Package

```
dotnet add package Trax.Effect
```
