---
layout: default
title: Core
nav_order: 3
has_children: true
---

# Trax.Core

`Trax.Core` is the foundation — trains, steps, and railway error propagation. No database, no DI container, no infrastructure required.

```bash
dotnet add package Trax.Core
```

## What You Get

- **Trains** — typed pipelines that carry data through a sequence of steps
- **Steps** — single-responsibility units of work with typed input/output
- **Memory** — a type-keyed store that wires step outputs to step inputs automatically
- **Railway error propagation** — if any step fails, the rest are skipped
- **Compile-time analyzer** — catches broken chains before you run anything
- **IDE extensions** — inlay hints showing cargo types at each stop

## The Railway

Your code runs on two tracks. The train stays on the main track as long as every step succeeds. If any step fails, the train derails onto the failure track and skips every remaining step.

```
Main Track:     Input -> [Stop 1] -> [Stop 2] -> [Stop 3] -> Output
                            |
Derailed:              Exception -> [Skip]  -> [Skip]  -> Exception
```

Trax.Core uses `Either<Exception, T>` from [LanguageExt](https://github.com/louthy/language-ext) to represent this. A value is either `Left` (a derailment/failure) or `Right` (successful delivery):

```csharp
public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateUserStep>()    // If this fails, skip remaining steps
            .Chain<CreateUserStep>()      // Only runs if validation succeeded
            .Chain<SendEmailStep>()       // Only runs if creation succeeded
            .Resolve();                   // Return Either<Exception, User>
}
```

If `ValidateUserStep` throws, the train derails immediately and returns `Left(exception)`. You don't write any error-checking code — the railway handles it.

## Running a Train

```csharp
// Run and unwrap (throws on failure)
var user = await train.Run(input);

// Run and inspect both tracks
var result = await train.RunEither(input);
result.Match(
    Left: ex => Console.WriteLine($"Failed: {ex.Message}"),
    Right: user => Console.WriteLine($"Created: {user.Email}")
);
```

## When to Use Core Alone

- Validation pipelines, data transformations, CLI tools
- Anywhere you'd write nested try/catch chains
- Lightweight step composition without infrastructure overhead
- Prototyping trains before adding persistence

When you need execution logging, DI, or persistent metadata, add [Trax.Effect](effect.md).
