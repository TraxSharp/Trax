---
layout: default
title: Functional Programming
parent: Core Concepts
nav_order: 1
---

# Functional Programming

Trax.Core borrows a few ideas from functional programming. You don't need an FP background to use it, but knowing where these types come from makes the API click faster.

## LanguageExt

Trax.Core depends on [LanguageExt](https://github.com/louthy/language-ext), a functional programming library for C#. You'll interact with two of its types: `Either` and `Unit`.

## Either\<L, R\>

`Either<L, R>` represents a value that is one of two things: `Left` or `Right`. By convention, `Left` is the failure case and `Right` is the success case.

Trax.Core uses `Either<Exception, T>` as the return type for workflows. A workflow either fails with an exception or succeeds with a result:

```csharp
protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
    => Activate(input)
        .Chain<ValidateEmailStep>()
        .Chain<CreateUserStep>()
        .Resolve();
```

You'll see `Either<Exception, T>` in every `RunInternal` signature. The chain handles the wrapping—if a step throws, the chain catches it and returns `Left(exception)`. If everything succeeds, you get `Right(result)`.

To inspect the result:

```csharp
var result = await workflow.Run(input);

result.Match(
    Left: exception => Console.WriteLine($"Failed: {exception.Message}"),
    Right: user => Console.WriteLine($"Created: {user.Email}"));

// Or check directly
if (result.IsRight)
{
    var user = (User)result;
}
```

This is the foundation of [Railway Oriented Programming](railway-programming.md)—the success track carries `Right` values, the failure track carries `Left` values.

## Unit

`Unit` means "no meaningful return value." It's the functional equivalent of `void`, except you can use it as a generic type argument.

In C#, you can't write `Task<void>` or `Step<string, void>`. `Unit` fills that gap:

```csharp
public class ValidateEmailStep : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (!IsValidEmail(input.Email))
            throw new ValidationException("Invalid email");

        return Unit.Default;
    }
}
```

`Unit.Default` is the only value of the `Unit` type. When a step returns `Unit`, it's saying "I did my work, but I'm not producing a new value for [Memory](memory.md)." The next step in the chain pulls its input from whatever's already available.

## Effects

In functional programming, an *effect* is a side effect—anything that reaches outside the function boundary. Database writes, HTTP calls, logging, file I/O: these are all effects. A pure function takes input and returns output without touching the outside world. Obviously, a workflow that does nothing observable isn't very useful, so the question becomes: how do you manage side effects without scattering them through every step?

Trax.Core separates the *description* of a side effect from its *execution*. Steps don't write directly to a database or logger. Instead, the workflow tracks models (like `Metadata`) during execution, and **effect providers** handle the actual side effects at the end. If every step succeeds, all providers run their `SaveChanges` and the effects are applied atomically. If any step fails, nothing is saved.

This gives you two things:

1. **Atomicity** — A failure means no effects are applied. No half-written database records, no orphaned log entries.
2. **Modularity** — Each effect provider is an independent plugin. Adding Postgres persistence doesn't change your workflow code. Removing the JSON logger doesn't either.

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)   // Database persistence
        .AddJsonEffect()                       // Debug logging
        .SaveWorkflowParameters()              // Input/output serialization
        .AddStepLogger()                       // Per-step logging
);
```

Remove any line and the workflow still runs—it just has fewer side effects. Add a line and you gain new observability without modifying a single step.

The `ServiceTrain` base class manages this lifecycle. When you use a plain `Train`, you get chaining and error propagation but no effects. When you use `ServiceTrain`, you get the full effect system on top.

See [Effect Providers](../usage-guide/effect-providers.md) for configuring each provider, and [Core & Effects](../architecture/core-and-effects.md) for how the `EffectRunner` coordinates them internally.
