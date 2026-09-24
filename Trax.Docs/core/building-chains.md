---
layout: default
title: Building Chains
parent: Core
nav_order: 2
---

# Building Chains

A train's route is a chain of junctions. Override `Junctions()` to define it. `.Chain<T>()` adds junctions, and the framework handles activation and resolution automatically.

## Chain

`.Chain<TJunction>()` is the primary way to add a junction to a train's route. It resolves the junction, pulls its input from [Memory](/docs/core/memory), runs it, and stores the output back in Memory.

```csharp
protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
        .Chain<CreateUserJunction>()
        .Chain<SendEmailJunction>().Resolve();
```

For all overloads, type parameter constraints, and junction-wiring behavior, see [SDK Reference: Chain](/docs/sdk-reference/train-methods/chain). The host's [startup chain verification](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) catches missing types before it serves traffic, so a broken chain fails the deploy rather than the first request that takes it.

### Railway Behavior

If a previous junction switched the train to the left track, `.Chain<TJunction>()` is skipped entirely. The exception propagates through the chain and is returned to the caller.

```csharp
Chain<ValidateEmailJunction>()    // Throws ValidationException
    .Chain<CreateUserJunction>()  // Skipped
    .Chain<SendEmailJunction>();  // Skipped. Caller receives the ValidationException
```

## Resolve

`.Resolve()` ends the chain and returns `Either<Exception, TReturn>`:

```csharp
protected override Task<Either<Exception, User>> Junctions() =>
    Chain<ValidateEmailJunction>()
        .Chain<CreateUserJunction>()
        .Chain<SendEmailJunction>()
        .Resolve();
```

`Resolve` checks for a captured exception, then a [ShortCircuit](#shortcircuit) value, then looks up `TReturn` in [Memory](/docs/core/memory), in that order. See [SDK Reference: Resolve](/docs/sdk-reference/train-methods/resolve) for the full resolution priority and error behavior.

On a train whose return type is already in Memory, because it is the input type or `Unit`, the chain names no junctions and the whole declaration is `Task.FromResult(Resolve())`: the train's own `Resolve()` is synchronous, so it is wrapped to match the `Task` that `Junctions()` returns.

There is no overload taking a value. To merge a nested train's result into the output, the junction that calls the nested train returns the merged value, which lands in Memory like any other junction output.

The host catches a missing return type at startup: the [chain verification](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) refuses to start when a chain ends without `TReturn` in Memory. (The Roslyn [Analyzer](/docs/core/analyzer) that used to report this as **CHAIN002** is deprecated and no longer fires.)

## ShortCircuit

`.ShortCircuit<TJunction>()` lets a junction take the express route, capturing a result for early return. If the junction returns a value of the train's return type, that value is stored as the short-circuit result and `Resolve()` will return it instead of doing a Memory lookup. If the junction throws, the train continues normally.

> **Note:** Subsequent `Chain` calls after a successful `ShortCircuit` still execute. The short-circuit value only affects `Resolve()`. Nothing in a chain skips the remaining junctions and still returns the short-circuit value. `Junctions()` cannot branch on its input, because a chain is declared once and checked at startup (`Trax.Docs/adr/0016`), and a later junction that fails puts the train on the left track, where `Resolve()` returns that failure rather than the captured value. A junction after a `ShortCircuit` that should not repeat work has to decide that itself, from what it is given.

```csharp
public class ProcessOrderTrain : ServiceTrain<OrderRequest, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrderJunction>()
            .ShortCircuit<CheckCacheJunction>()  // If cached, capture result for Resolve
            .Chain<CalculatePricingJunction>()   // Still executes (short-circuit only affects Resolve)
            .Chain<ProcessPaymentJunction>()     // Still executes (short-circuit only affects Resolve)
            .Chain<SaveOrderJunction>()
            .Resolve();
}
```

> **This behavior is intentionally inverted from Chain.** A `Chain` junction that throws switches the train to the left track with an error. A `ShortCircuit` junction that throws means "no short-circuit available, keep going." The exception is swallowed, not propagated.

See [SDK Reference: ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) for all overloads, the junction signature, and a full example.

**When to use it:**
- **Caching**: return a cached result if available, otherwise compute it
- **Feature flags**: return a default result if a feature is disabled
- **Early exits**: skip expensive processing when a precondition is already satisfied

## Extract

`.Extract<TSource, TTarget>()` pulls a nested value out of an object in [Memory](/docs/core/memory). It finds the `TSource` object, looks for a property or field of type `TTarget`, and stores that value in Memory under the `TTarget` type.

```csharp
Chain<LoadUserJunction>()                   // Returns User, stored in Memory
    .Extract<User, EmailAddress>()          // Finds EmailAddress property on User, stores it
    .Chain<ValidateEmailJunction>();         // Takes EmailAddress from Memory
```

`Extract` uses reflection to find a property or field on `TSource` whose type matches `TTarget` and stores it in Memory. See [SDK Reference: Extract](/docs/sdk-reference/train-methods/extract) for the full search order and failure behavior.

`Extract` is a convenience for avoiding a junction that exists solely to pull a property off an object. Without it, you'd write:

```csharp
public class GetUserEmailJunction : Junction<User, EmailAddress>
{
    public override Task<EmailAddress> Run(User input)
        => Task.FromResult(input.Email);
}
```

`.Extract<User, EmailAddress>()` does the same thing without the boilerplate. Use it when the property access is trivial. If you need any logic (null checking, transformation, validation), write a junction instead.

## AddServices

`.AddServices()` puts service instances directly into [Memory](/docs/core/memory), making them available to subsequent junctions. This bypasses the DI container. The instances you pass are stored as-is.

```csharp
protected override Task<Either<Exception, User>> Junctions()
{
    var validator = new CustomValidator();
    var notifier = new SlackNotifier();

    return AddServices<IValidator, INotifier>(validator, notifier)
        .Chain<ValidateJunction>()     // Can take IValidator from Memory
        .Chain<CreateUserJunction>()
        .Chain<NotifyJunction>()       // Can take INotifier from Memory
        .Resolve();
}
```

Each type argument is stored in Memory with the corresponding instance. See [SDK Reference: AddServices](/docs/sdk-reference/train-methods/add-services) for all overloads and interface-type storage behavior.

Use `AddServices` when you need to inject runtime-created instances into the chain, like objects that aren't available through the DI container or that need to be created per-execution. For standard dependencies, prefer constructor injection in your junctions instead.

> **Note:** setup that needs async work, a try/catch, or a nested train's result belongs in a junction at the head of the chain, whose return value lands in Memory for the junctions after it.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | [Extract](/docs/sdk-reference/train-methods/extract) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [Resolve](/docs/sdk-reference/train-methods/resolve)
