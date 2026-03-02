---
layout: default
title: Railway Programming
parent: Core Concepts
nav_order: 2
---

# Railway Oriented Programming

Railway Oriented Programming comes from functional programming — and it's the reason for the train metaphor. The idea: your code runs on two tracks. The train stays on the main track as long as every stop succeeds. If any stop fails, the train derails onto the failure track and skips every remaining stop.

```
Main Track:     Input → [Stop 1] → [Stop 2] → [Stop 3] → Output
                            ↓
Derailed:              Exception → [Skip]  → [Skip]  → Exception
```

Trax.Core uses `Either<Exception, T>` from LanguageExt to represent this. A value is either `Left` (a derailment) or `Right` (the successful delivery):

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateUserStep>()    // If this fails, skip remaining steps
            .Chain<CreateUserStep>()      // Only runs if validation succeeded
            .Chain<SendEmailStep>()       // Only runs if creation succeeded
            .Resolve();                   // Return Either<Exception, User>
}
```

If `ValidateUserStep` throws, the train derails immediately and returns `Left(exception)`. `CreateUserStep` and `SendEmailStep` are never reached. You don't write any error-checking code — the railway handles it.

## The Effect Pattern

The Effect Pattern separates *describing* what should happen from *doing* it. Think of it as loading cargo onto the train versus actually delivering it. If you've used Entity Framework, you already know this pattern:

```csharp
// Track changes (doesn't hit database yet)
context.Users.Add(user);
context.Orders.Update(order);

// Execute all changes atomically
await context.SaveChanges();
```

Trax.Core's `ServiceTrain` does the same thing. Stops can track models, log entries, and other effects along the journey. Nothing actually persists until the train arrives at its destination and calls `SaveChanges`. If any stop derails the train, nothing is saved.

This gives you atomic journeys — either the train arrives and all station services are applied, or it derails and nothing is applied.

## Train vs ServiceTrain

Trax.Core has two base classes for trains:

**`Train<TIn, TOut>`** — The bare locomotive. Handles routing through stops, [cargo](memory.md) management, and derailment propagation. No journey logging, no station services, no automatic dependency injection from an `IServiceProvider`. Use this when:
- You want a lightweight train without persistence
- You're composing stops inside a larger system that handles its own concerns
- Testing or prototyping

```csharp
public class SimpleUserCreation : Train<CreateUserRequest, User>
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateUserStep>()
            .Chain<CreateUserStep>()
            .Resolve();
}
```

**`ServiceTrain<TIn, TOut>`** — The full commercial service. Extends `Train` with:
- Automatic journey logging (departure time, arrival time, success/derailment, cargo in/out)
- Station services (database persistence, JSON logging, parameter serialization)
- Integration with `ITrainBus` for dispatch station discovery
- `IServiceProvider` access for stop instantiation

Use this when you want observability, persistence, or the dispatch station pattern:

```csharp
public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateUserStep>()
            .Chain<CreateUserStep>()
            .Resolve();
}
```

Most applications should use `ServiceTrain`. The bare `Train` locomotive exists for cases where you need the routing pattern without the infrastructure.
