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

This is the foundation of [Railway Oriented Programming](railway-programming.md) — the main track carries `Right` values, the derailment track carries `Left` values.

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

`Unit.Default` is the only value of the `Unit` type. When a stop returns `Unit`, it's saying "I did my work, but I'm not loading any new cargo onto the train." The next stop picks up what it needs from whatever's already on board.

## Effects

In functional programming, an *effect* is a side effect — anything that reaches outside the function boundary. Database writes, HTTP calls, logging, file I/O: these are all effects. A pure function takes input and returns output without touching the outside world. Obviously, a train that does nothing observable isn't very useful, so the question becomes: how do you manage side effects without scattering them through every stop?

In Trax, effects are **station services** — operations that happen as the train passes through its route. Stops don't write directly to a database or logger. Instead, the train tracks models (like `Metadata`) during the journey, and **effect providers** (the station service crews) handle the actual work at the end. If the train arrives at its destination, all providers run their `SaveChanges` and the services are applied atomically. If any stop derails the train, nothing is saved.

This gives you two things:

1. **Atomicity** — A derailment means no station services are applied. No half-written database records, no orphaned log entries.
2. **Modularity** — Each station service is an independent plugin. Adding Postgres persistence doesn't change your train code. Removing the JSON logger doesn't either.

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)   // Database persistence
        .AddJsonEffect()                       // Debug logging
        .SaveTrainParameters()              // Input/output serialization
        .AddStepLogger()                       // Per-step logging
);
```

Remove any line and the train still runs — it just passes through fewer stations. Add a line and you gain new observability without modifying a single stop.

The `ServiceTrain` base class manages this lifecycle. When you use a plain `Train` (the bare locomotive), you get routing and derailment propagation but no station services. When you use `ServiceTrain` (the full commercial service), you get the complete station service network on top.

See [Effect Providers](../effect/effect-providers.md) for configuring each provider, and [Core & Effects](../effect/architecture.md) for how the `EffectRunner` coordinates them internally.
