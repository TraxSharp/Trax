---
layout: default
title: Railway Programming
parent: Core Concepts
nav_order: 2
---

# Railway Oriented Programming

Railway Oriented Programming comes from functional programming. The idea: your code has two tracks, success and failure. Each operation either continues down the success track or switches to the failure track.

```
Success Track:  Input → [Step 1] → [Step 2] → [Step 3] → Output
                            ↓
Failure Track:          Exception → [Skip] → [Skip] → Exception
```

Trax.Core uses `Either<Exception, T>` from LanguageExt to represent this. A value is either `Left` (an exception) or `Right` (the success value):

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

If `ValidateUserStep` throws, the train immediately returns `Left(exception)`. `CreateUserStep` and `SendEmailStep` never execute. You don't write any error-checking code—the chain handles it.

## The Effect Pattern

The Effect Pattern separates *describing* what should happen from *doing* it. If you've used Entity Framework, you already know this pattern:

```csharp
// Track changes (doesn't hit database yet)
context.Users.Add(user);
context.Orders.Update(order);

// Execute all changes atomically
await context.SaveChanges();
```

Trax.Core's `ServiceTrain` does the same thing. Steps can track models, log entries, and other effects. Nothing actually persists until the train completes successfully and calls `SaveChanges`. If any step fails, nothing is saved.

This gives you atomic trains—either everything succeeds and all effects are applied, or something fails and nothing is applied.

## Train vs ServiceTrain

Trax.Core has two base classes for trains:

**`Train<TIn, TOut>`** — The core class. Handles chaining, [Memory](memory.md), and error propagation. No metadata, no effects, no automatic dependency injection from an `IServiceProvider`. Use this when:
- You want a lightweight train without persistence
- You're composing steps inside a larger system that handles its own concerns
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

**`ServiceTrain<TIn, TOut>`** — Extends `Train` with:
- Automatic metadata tracking (start time, end time, success/failure, inputs/outputs)
- Effect providers (database persistence, JSON logging, parameter serialization)
- Integration with `ITrainBus` for train discovery
- `IServiceProvider` access for step instantiation

Use this when you want observability, persistence, or the mediator pattern:

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

Most applications should use `ServiceTrain`. The base `Train` exists for cases where you need the chaining pattern without the infrastructure.
