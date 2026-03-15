---
layout: default
title: Functional Programming
parent: Cross-Cutting
nav_order: 5
---

# Functional Programming

Trax.Core borrows a few ideas from functional programming. You don't need an FP background to use it, but knowing where these types come from makes the API click faster.

## LanguageExt

Trax.Core depends on [LanguageExt](https://github.com/louthy/language-ext), a functional programming library for C#. You'll interact with two of its types: `Either` and `Unit`.

## Either\<L, R\>

`Either<L, R>` represents a value that is one of two things: `Left` or `Right`. By convention, `Left` is the failure case and `Right` is the success case.

Trax.Core uses `Either<Exception, T>` as the return type for trains. A train either fails with an exception or succeeds with a result:

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
var result = await train.Run(input);

result.Match(
    Left: exception => Console.WriteLine($"Failed: {exception.Message}"),
    Right: user => Console.WriteLine($"Created: {user.Email}"));

// Or check directly
if (result.IsRight)
{
    var user = (User)result;
}
```

This is the foundation of [Railway Oriented Programming](railway-programming.md) — the right track carries `Right` values, the left track carries `Left` values.

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

`Unit.Default` is the only value of the `Unit` type. When a junction returns `Unit`, it's saying "I did my work, but I'm not loading any new cargo onto the train." The next junction picks up what it needs from whatever's already on board.

## Effects

In functional programming, an *effect* is a side effect — anything that reaches outside the function boundary. Database writes, HTTP calls, logging, file I/O: these are all effects. A pure function takes input and returns output without touching the outside world. Obviously, a train that does nothing observable isn't very useful, so the question becomes: how do you manage side effects without scattering them through every junction?

In Trax, effects are operations that happen as the train passes through its route. Junctions don't write directly to a database or logger. Instead, the train tracks models (like `Metadata`) during the journey, and **effect providers** handle the actual work at the end. If the train reaches the right track (success), all providers run their `SaveChanges` and effects are applied atomically. If any junction switches the train to the left track (failure), nothing is saved.

This gives you two things:

1. **Atomicity** — A left-track result means no effects are applied. No half-written database records, no orphaned log entries.
2. **Modularity** — Each effect provider is an independent plugin. Adding Postgres persistence doesn't change your train code. Removing the JSON logger doesn't either.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)             // Database persistence
        .AddJson()                                 // Debug logging
        .SaveTrainParameters()                     // Input/output serialization
        .AddStepLogger()                           // Per-step logging
    )
);
```

Remove any line and the train still runs — it just passes through fewer junctions. Add a line and you gain new observability without modifying a single junction.

The `ServiceTrain` base class manages this lifecycle. When you use a plain `Train`, you get routing and error propagation but no effect providers. When you use `ServiceTrain`, you get the full effect provider network on top.

See [Effect Providers](../effect/effect-providers.md) for configuring each provider, and [Core & Effects](../effect/architecture.md) for how the `EffectRunner` coordinates them internally.

## Null Assertion Helpers

Trax.Core provides two extension methods for asserting that values have been loaded (i.e., are not null). These replace scattered `!` operators with a single, expressive call that throws a clear `InvalidOperationException` when the value is missing.

```csharp
// Assert a single value is loaded
EffectRunner.AssertLoaded();           // throws if EffectRunner is null
registration.ServiceType.FullName.AssertLoaded();

// Assert a property across a collection
steps.AssertEachLoaded(s => s.Metadata);  // throws if any step's Metadata is null
```

Both methods use `[CallerArgumentExpression]` to include the source expression in the error message, making it easy to identify what failed without a debugger.
