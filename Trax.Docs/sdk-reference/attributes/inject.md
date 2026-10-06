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

## Checked at startup

The mediator's [startup chain check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) reads the `[Inject]` properties a train declares itself (`ServiceTrain`'s own are left out). It reads them on the train the container builds, because `InjectProperties` skips a property that already holds a value: one the constructor or an initializer sets (`[Inject] public IClock Clock { get; set; } = SystemClock.Instance;`) is never filled, needs no registration, and is not checked. For a property that is null on the built train:

| Property | Startup |
|----------|---------|
| Type not registered, property declared non-nullable (or in a project without nullable annotations) | Refused: the property would be null on every run |
| Type not registered, property declared nullable (`T?`) | Warning: the train is taken to cope without it |
| Type registered, but its class can never be built | Refused, whatever the declaration: filling the property throws, so every resolution of the train fails |
| `IEnumerable<T>` | Not checked: it is always filled, if only with nothing |

When resolving the train fails, its class is built without its properties filled and read the same way, so a property whose filling throws is named. When even that fails (a constructor that needs something only a request provides), whether the constructor sets a property cannot be told, so anything found is logged as a warning and the host starts. In a trimmed app, which turns `NullabilityInfoContext` off, every property reads as of unknown nullability; that is taken as nullable, so such a property is warned about rather than refused. `SkipChainVerification()` turns this check off together with the rest of the chain check.

A junction built with `Chain<T>()` or `ShortCircuit<T>()` that declares `[Inject]` properties is logged as a warning naming the train, the junction and the properties.

## Package

```
dotnet add package Trax.Effect
```
