---
layout: default
title: Memory
parent: Core Concepts
nav_order: 3
---

# Memory

Memory is how steps communicate in a train. Think of it as the cargo the train carries between stops — a type-keyed dictionary that accumulates as the train executes. Each step pulls its input from Memory and pushes its output back in.

## How It Works

When you call `Activate(input)`, Memory is seeded with two entries: your input type and `Unit`. As each step runs, its output is stored in Memory under that output's type:

```csharp
Activate(request)                  // Memory: { CreateUserRequest, Unit }
    .Chain<ValidateEmailStep>()    // Takes CreateUserRequest, returns Unit → Memory unchanged
    .Chain<CreateUserStep>()       // Takes CreateUserRequest, returns User → Memory: { CreateUserRequest, Unit, User }
    .Chain<SendEmailStep>()        // Takes User from Memory
    .Resolve();                    // Resolves User from Memory
```

Each step declares what it needs (its `TIn`) and what it produces (its `TOut`). The chain looks up `TIn` in Memory, passes it to the step, and stores `TOut` back. If `TIn` isn't in Memory, the train fails at runtime — though the [Analyzer](../analyzer.md) catches this at compile time.

## Storage by Type

Memory stores one value per type. If two steps both return `string`, the second one overwrites the first. This is by design — use distinct types to avoid collisions:

```csharp
// These would collide in Memory (both produce string)
.Chain<GetFirstNameStep>()   // Returns string
.Chain<GetLastNameStep>()    // Returns string — overwrites the first!

// Use distinct types instead
.Chain<GetFirstNameStep>()   // Returns FirstName
.Chain<GetLastNameStep>()    // Returns LastName
```

This is why Trax.Core encourages specific types (records, value objects) over primitives. A step signature like `Step<User, EmailAddress>` tells you exactly what goes in and what comes out — `Step<User, string>` doesn't.

## References, Not Copies

Memory stores references, not copies. When you modify an object in a step, every subsequent step sees the modification:

```csharp
public class EnrichUserStep : Step<User, Unit>
{
    public override async Task<Unit> Run(User user)
    {
        user.EnrichedData = "some data";
        // No need to return the User — the reference in Memory is already updated
        return Unit.Default;
    }
}
```

This means you don't need to "pass through" a type just to keep it in Memory. If a step receives a `User` and modifies it in place, return `Unit`. The next step that needs `User` will get the same (now modified) reference:

```csharp
Activate(input)
    .Chain<CreateUserStep>()       // Returns User → stored in Memory
    .Chain<ValidateUserStep>()     // Takes User, returns Unit (validation only)
    .Chain<EnrichUserStep>()       // Takes User, returns Unit (modifies in place)
    .Chain<SendNotificationStep>() // Takes User — sees all modifications
    .Resolve();
```

Only return a type from a step when you're producing something **new** for Memory. If you're just reading or mutating an existing object, return `Unit`.

## Tuples

When a step returns a tuple, Memory deconstructs it and stores each element individually:

```csharp
public class LoadEntitiesStep : Step<LoadRequest, (User, Order, Payment)>
{
    public override async Task<(User, Order, Payment)> Run(LoadRequest input)
    {
        var user = await repo.GetUserAsync(input.UserId);
        var order = await repo.GetOrderAsync(input.OrderId);
        var payment = await repo.GetPaymentAsync(input.PaymentId);

        return (user, order, payment);
    }
}
// After this step, Memory contains: { LoadRequest, Unit, User, Order, Payment }
```

When a step takes a tuple as input, Memory reconstructs it from the individual elements:

```csharp
public class ProcessCheckoutStep : Step<(User, Order, Payment), Receipt>
{
    public override async Task<Receipt> Run((User User, Order Order, Payment Payment) input)
    {
        return await checkout.ProcessAsync(input.User, input.Order, input.Payment);
    }
}
// Memory finds User, Order, and Payment individually, constructs the tuple, and passes it in
```

This lets you load multiple entities in one step and consume them individually — or as a group — in later steps:

```csharp
public class CheckoutTrain : ServiceTrain<CheckoutRequest, Receipt>
{
    protected override async Task<Either<Exception, Receipt>> RunInternal(CheckoutRequest input)
        => Activate(input)
            .Chain<LoadEntitiesStep>()     // Returns (User, Order, Payment) — deconstructed into Memory
            .Chain<ValidateUserStep>()     // Takes User from Memory
            .Chain<ValidateOrderStep>()    // Takes Order from Memory
            .Chain<ProcessCheckoutStep>()  // Takes (User, Order, Payment) — reconstructed from Memory
            .Resolve();
}
```
