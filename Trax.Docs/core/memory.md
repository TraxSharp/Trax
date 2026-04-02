---
layout: default
title: Memory
parent: Core
nav_order: 3
---

# Memory

Memory is how junctions communicate in a train. Think of it as the cargo the train carries between junctions: a type-keyed dictionary that accumulates as the train executes. Each junction pulls its input from Memory and pushes its output back in.

## How It Works

When a train starts, Memory is seeded with two entries: the input type and `Unit`. As each junction runs, its output is stored in Memory under that output's type:

```csharp
// Memory starts with: { CreateUserRequest, Unit }
Chain<ValidateEmailJunction>()         // Takes CreateUserRequest, returns Unit -> Memory unchanged
    .Chain<CreateUserJunction>()       // Takes CreateUserRequest, returns User -> Memory: { CreateUserRequest, Unit, User }
    .Chain<SendEmailJunction>();       // Takes User from Memory
// User is resolved from Memory automatically
```

Each junction declares what it needs (its `TIn`) and what it produces (its `TOut`). The chain looks up `TIn` in Memory, passes it to the junction, and stores `TOut` back. If `TIn` isn't in Memory, the train fails at runtime, though the [Analyzer](analyzer.md) catches this at compile time.

## Storage by Type

Memory stores one value per type. If two junctions both return `string`, the second one overwrites the first. This is by design. Use distinct types to avoid collisions:

```csharp
// These would collide in Memory (both produce string)
.Chain<GetFirstNameJunction>()   // Returns string
.Chain<GetLastNameJunction>()    // Returns string, overwrites the first!

// Use distinct types instead
.Chain<GetFirstNameJunction>()   // Returns FirstName
.Chain<GetLastNameJunction>()    // Returns LastName
```

This is why Trax encourages specific types (records, value objects) over primitives. A junction signature like `Junction<User, EmailAddress>` tells you exactly what goes in and what comes out, while `Junction<User, string>` doesn't.

## References, Not Copies

Memory stores references, not copies. When you modify an object in a junction, every subsequent junction sees the modification:

```csharp
public class EnrichUserJunction : Junction<User, Unit>
{
    public override async Task<Unit> Run(User user)
    {
        user.EnrichedData = "some data";
        // No need to return the User. The reference in Memory is already updated
        return Unit.Default;
    }
}
```

This means you don't need to "pass through" a type just to keep it in Memory. If a junction receives a `User` and modifies it in place, return `Unit`. The next junction that needs `User` will get the same (now modified) reference:

```csharp
Chain<CreateUserJunction>()            // Returns User -> stored in Memory
    .Chain<ValidateUserJunction>()     // Takes User, returns Unit (validation only)
    .Chain<EnrichUserJunction>()       // Takes User, returns Unit (modifies in place)
    .Chain<SendNotificationJunction>() // Takes User, sees all modifications
```

Only return a type from a junction when you're producing something **new** for Memory. If you're just reading or mutating an existing object, return `Unit`.

## Tuples

When a junction returns a tuple, Memory deconstructs it and stores each element individually:

```csharp
public class LoadEntitiesJunction : Junction<LoadRequest, (User, Order, Payment)>
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

```csharp
public class ProcessCheckoutJunction : Junction<(User, Order, Payment), Receipt>
{
    public override async Task<Receipt> Run((User User, Order Order, Payment Payment) input)
    {
        return await checkout.ProcessAsync(input.User, input.Order, input.Payment);
    }
}
// Memory finds User, Order, and Payment individually, constructs the tuple, and passes it in
```

This lets you load multiple entities in one junction and consume them individually, or as a group, in later junctions:

```csharp
public class CheckoutTrain : ServiceTrain<CheckoutRequest, Receipt>
{
    protected override Receipt Junctions() =>
        Chain<LoadEntitiesJunction>()          // Returns (User, Order, Payment), deconstructed into Memory
            .Chain<ValidateUserJunction>()     // Takes User from Memory
            .Chain<ValidateOrderJunction>()    // Takes Order from Memory
            .Chain<ProcessCheckoutJunction>(); // Takes (User, Order, Payment), reconstructed from Memory
}
```

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain)
