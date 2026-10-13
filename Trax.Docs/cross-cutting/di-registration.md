---
title: DI registration
description: "How trains and junctions are registered in the container: the AddTraxRoute and AddTraxJunction methods, Inject properties, CanonicalName and lifetimes."
type: reference
area: trains
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
  - Trax.Mediator/src/Trax.Mediator/Extensions/ServiceExtensions.cs
redirect_from:
  - /docs/cross-cutting/di-registration/junction-registration
  - /docs/cross-cutting/di-registration/train-registration
---

# DI registration

Trax registers a train or junction under its interface with `IServiceCollection` extensions in
`Trax.Effect.Extensions`. They wrap `AddScoped`, `AddTransient` and `AddSingleton` and add two
things on resolution: they fill the instance's `[Inject]` properties and set its `CanonicalName`.
The signatures are in [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration).

## Methods

| Method | Lifetime | Registers |
|---|---|---|
| `AddScopedTraxRoute<TService, TImplementation>()` | Scoped | A train or junction |
| `AddTransientTraxRoute<TService, TImplementation>()` | Transient | A train or junction |
| `AddSingletonTraxRoute<TService, TImplementation>()` | Singleton | A junction or other route with no per-run state. Refuses a service train. |
| `AddScopedTraxJunction<TService, TImplementation>()` | Scoped | Same as `AddScopedTraxRoute` |
| `AddTransientTraxJunction<TService, TImplementation>()` | Transient | Same as `AddTransientTraxRoute` |
| `AddSingletonTraxJunction<TService, TImplementation>()` | Singleton | Same as `AddSingletonTraxRoute` |

Each has a non-generic overload taking `(Type serviceInterface, Type serviceImplementation)`.
`TService` and `TImplementation` are constrained to `class`, and `TImplementation` to `TService`.
Every method returns the `IServiceCollection`.

## Behavior

Each method registers `TImplementation` at the lifetime it names, then `TService` with a factory
that resolves `TImplementation`, calls `InjectProperties` to fill every `[Inject]` property, sets
the instance's `CanonicalName` property (when it has one) to `TService`'s full name, and returns
the instance. The canonical name is the train's identifier in metadata, work queue entries and
subscriptions; see [Canonical Train Naming](/docs/effect/architecture#canonical-train-naming).

`ServiceTrain<TIn, TOut>` gets its effect runner, junction effect runner, lifecycle hook runner,
logger and service provider through `[Inject]` properties, so a service train resolved from a plain `AddScoped` or `AddTransient`
registration has none of them.

The `AddTraxJunction` methods call the `AddTraxRoute` method of the same lifetime.

| Situation | Result |
|---|---|
| `AddSingletonTraxRoute` or `AddSingletonTraxJunction` on a class deriving from `ServiceTrain<,>` | `InvalidOperationException` at registration, naming the train and pointing at the scoped and transient methods |
| A train in an assembly passed to [AddMediator](/docs/sdk-reference/configuration/add-mediator) | Registered by `AddMediator` through `AddTransientTraxRoute`, or `AddScopedTraxRoute` when `TrainLifetime(ServiceLifetime.Scoped)` is set. `TrainLifetime(ServiceLifetime.Singleton)` is refused. |
| A junction the train reaches with `IChain<TInterface>()` | Resolved from the container under `TInterface`, `[Inject]` properties filled |
| A junction the train reaches with `Chain<TJunction>()` | Built from its public constructor, with arguments from Memory first and the container second. The registration is not consulted and `[Inject]` properties stay `null`; the startup chain check logs a warning for each one |

A resolved train instance carries the state of one run (its metadata row, effect runner and data
context). Each `Run` call records its own metadata row, so a scoped instance run twice in one
scope records two runs.

## Example

```csharp compile
using Trax.Effect.Extensions;

services.AddTransientTraxRoute<ICreateOrderTrain, CreateOrderTrain>();
services.AddScopedTraxJunction<IValidateOrderJunction, ValidateOrderJunction>();
```

## See also

- [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration): the signatures
- [Chain](/docs/sdk-reference/train-methods/chain): `Chain` and `IChain`
- [Inject](/docs/sdk-reference/attributes/inject)
- [AddMediator](/docs/sdk-reference/configuration/add-mediator)
