---
layout: default
title: Core
nav_order: 3
has_children: true
section: Packages
---

# Trax.Core

`Trax.Core` is the foundation — trains, junctions, and railway error propagation. No database, no DI container, no infrastructure required.

```bash
dotnet add package Trax.Core
```

## What You Get

- **Trains** — typed pipelines that carry data through a sequence of junctions
- **Junctions** — single-responsibility units of work with typed input/output
- **Memory** — a type-keyed store that wires junction outputs to junction inputs automatically
- **Railway error propagation** — if any junction fails, the train switches to the left track and skips the rest
- **Compile-time analyzer** — catches broken chains before you run anything
- **IDE extensions** — inlay hints showing cargo types at each junction

## The Railway

A train never leaves the rails — it always reaches a destination. Your code runs on two tracks: right and left. The train stays on the right track as long as every junction succeeds. If any junction fails, the train switches to the left track and passes through every remaining junction without executing them.

```
Right Track:    Input -> [Junction 1] -> [Junction 2] -> [Junction 3] -> Output
                              |
Left Track:              Exception  ->   [Skip]    ->   [Skip]    -> Exception
```

Trax.Core uses `Either<Exception, T>` from [LanguageExt](https://github.com/louthy/language-ext) to represent this. A value is either `Left` (an exception on the left track) or `Right` (successful delivery on the right track):

```csharp
public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override User Junctions() =>
        Chain<ValidateUserJunction>()         // If this fails, skip remaining junctions
            .Chain<CreateUserJunction>()      // Only runs if validation succeeded
            .Chain<SendEmailJunction>();      // Only runs if creation succeeded
}
```

If `ValidateUserJunction` throws, the train switches to the left track immediately and returns `Left(exception)`. You don't write any error-checking code — the railway handles it.

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
- Lightweight junction composition without infrastructure overhead
- Prototyping trains before adding persistence

When you need execution logging, DI, or persistent metadata, add [Trax.Effect](effect.md).

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run)
