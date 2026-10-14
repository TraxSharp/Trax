---
title: Memory
description: "How Memory, the type-keyed store a train carries, passes junction outputs to later junction inputs: storage by type, references, tuples and their limits."
source:
  - Trax.Core/src/Trax.Core/Monad/Monad.cs
  - Trax.Core/src/Trax.Core/Extensions/MonadExtensions.cs
type: explanation
area: trains
nav_order: 2
---

# Memory

Memory is how junctions communicate in a train. Think of it as the cargo the train carries between junctions: a type-keyed dictionary that accumulates as the train executes. Each junction pulls its input from Memory and pushes its output back in.

## How It Works

When a train starts, Memory holds `Unit` and the train's input, stored under the input's declared
type, its runtime type and every interface that type implements. As each junction runs, its
output is stored in Memory under the junction's declared output type:

```csharp fragment
// Memory starts with: { CreateUserRequest, Unit }
Chain<ValidateEmailJunction>()         // Takes CreateUserRequest, returns Unit -> Memory unchanged
    .Chain<CreateUserJunction>()       // Takes CreateUserRequest, returns User -> Memory: { CreateUserRequest, Unit, User }
    .Chain<SendEmailJunction>();       // Takes User from Memory
// User is resolved from Memory automatically
```

Each junction declares what it needs (its `TIn`) and what it produces (its `TOut`). The chain looks up `TIn` in Memory, passes it to the junction, and stores `TOut` back. A `TIn` that is not in Memory is taken from the train's container, when it has one (a `ServiceTrain` does). If neither has it, the run fails, though the [startup chain verification](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) catches this before the host serves traffic.

## Storage by Type

Memory stores one value per type. If two junctions both return `string`, the second one overwrites the first. This is by design. Use distinct types to avoid collisions:

```csharp fragment
// These would collide in Memory (both produce string)
.Chain<GetFirstNameJunction>()   // Returns string
.Chain<GetLastNameJunction>()    // Returns string, overwrites the first!

// Use distinct types instead
.Chain<GetFirstNameJunction>()   // Returns FirstName
.Chain<GetLastNameJunction>()    // Returns LastName
```

A junction's output is stored under its declared output type only, not its runtime type or interfaces, so a later junction that takes an interface finds the value only when the producing junction declares that interface as its output.

This is why Trax encourages specific types (records, value objects) over primitives. A junction signature like `Junction<User, EmailAddress>` tells you exactly what goes in and what comes out, while `Junction<User, string>` doesn't.

## References, Not Copies

Memory stores references, not copies. When you modify an object in a junction, every subsequent junction sees the modification:

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Junction;

public class EnrichUserJunction : Junction<User, Unit>
{
    public override Task<Unit> Run(User user)
    {
        user.EnrichedData = "some data";
        // No need to return the User. The reference in Memory is already updated
        return Task.FromResult(Unit.Default);
    }
}
```

This means you don't need to "pass through" a type just to keep it in Memory. If a junction receives a `User` and modifies it in place, return `Unit`. The next junction that needs `User` will get the same (now modified) reference:

```csharp fragment
Chain<CreateUserJunction>()            // Returns User -> stored in Memory
    .Chain<ValidateUserJunction>()     // Takes User, returns Unit (validation only)
    .Chain<EnrichUserJunction>()       // Takes User, returns Unit (modifies in place)
    .Chain<SendNotificationJunction>() // Takes User, sees all modifications
```

Only return a type from a junction when you're producing something **new** for Memory. If you're just reading or mutating an existing object, return `Unit`.

## Tuples

When a junction returns a tuple, Memory deconstructs it and stores each element individually, under its declared type, its runtime type and its interfaces:

```csharp compile=checkout
using Trax.Core.Junction;

public class LoadEntitiesJunction(IRepository repo) : Junction<LoadRequest, (User, Order, Payment)>
{
    public override async Task<(User, Order, Payment)> Run(LoadRequest input)
    {
        var user = await repo.GetUserAsync(input.UserId);
        var order = await repo.GetOrderAsync(input.OrderId);
        var payment = await repo.GetPaymentAsync(input.PaymentId);

        return (user, order, payment);
    }
}
// After this junction, Memory contains: { LoadRequest, Unit, User, Order, Payment }
```

When a junction takes a tuple as input, Memory reconstructs it from the individual elements:

```csharp compile=checkout
using Trax.Core.Junction;

public class ProcessCheckoutJunction(ICheckout checkout)
    : Junction<(User User, Order Order, Payment Payment), Receipt>
{
    public override Task<Receipt> Run((User User, Order Order, Payment Payment) input) =>
        checkout.ProcessAsync(input.User, input.Order, input.Payment);
}
// Memory finds User, Order, and Payment individually, constructs the tuple, and passes it in
```

Element names belong on the junction's type argument, as here: C# does not let `Run` rename the
elements of the tuple it overrides.

A tuple input is assembled from Memory alone: an element missing from Memory is missing, even
when the container could supply it.

This lets you load multiple entities in one junction and consume them individually, or as a group, in later junctions:

```csharp compile=checkout
using Trax.Core.Functional;
using Trax.Core.Train;

public class CheckoutTrain : Train<LoadRequest, Receipt>
{
    protected override Task<Either<Exception, Receipt>> Junctions() =>
        Chain<LoadEntitiesJunction>()          // Returns (User, Order, Payment), deconstructed into Memory
            .Chain<ValidateUserJunction>()     // Takes User from Memory
            .Chain<ValidateOrderJunction>()    // Takes Order from Memory
            .Chain<ProcessCheckoutJunction>().Resolve(); // Takes (User, Order, Payment), reconstructed from Memory
}
```

### Tuple limits

Two limits, both refused by the startup chain check rather than left to fail when the train runs:

- **A tuple holds at most seven elements.** Memory cannot store a longer one, so group the extra values into a type of their own.
- **Deconstruction is one level deep.** A tuple nested inside a tuple goes into Memory as a single value under its own type, and its inner elements are not separately findable. Returning `(int, (Guid, bool))` puts `int` and `(Guid, bool)` in Memory, not `Guid`. Return a flat tuple, or a type that names its parts.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain)
