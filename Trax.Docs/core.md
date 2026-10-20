---
title: Trains (Trax.Core)
description: "Trax.Core overview: trains, junctions, Memory and railway error propagation with no database or DI container, and when to use Core on its own."
source:
  - Trax.Core/src/Trax.Core
type: explanation
area: trains
nav_order: 1
---

# Trains (Trax.Core)

A piece of business work, such as placing an order, is rarely one call. It prices a cart, reserves
stock, takes a payment and confirms, and when the payment fails the steps after it must not run.
Written by hand, that is a method full of `if` checks between service calls. In Trax it is a
**train**: a class that lists its steps (**junctions**) in order, hands each step's output to the
next by its type, and stops at the first one that throws.

Everything else in Trax runs, records, schedules or serves a train, so this is the place to start.
[What Trax does](/docs/what-trax-does) shows how the rest attaches to it. This page covers
`Trax.Core`, the package with the train itself: no database, no DI container, no infrastructure.

```bash
dotnet add package Trax.Core
```

## What You Get

- **Trains**: typed pipelines that carry data through a sequence of junctions
- **Junctions**: single-responsibility units of work with typed input/output
- **Memory**: a type-keyed store that wires junction outputs to junction inputs automatically
- **Railway error propagation**: if any junction fails, the train switches to the left track and skips the rest
- **Declared chains**: a chain declared in `Junctions()` is readable without running it. Trax.Mediator's startup check (registered by `AddMediator`) uses that to refuse to start a host whose chains cannot run
- **Decisions**: a chain can declare several tracks and let a decider (a typed decision model, a rule table, a test double) choose one per run, with every track verified at startup. See [Decisions](/docs/core/decisions)
- **IDE extensions**: inlay hints showing cargo types at each junction

## The Railway

A train never leaves the rails. It always reaches a destination. Your code runs on two tracks: right and left. The train stays on the right track as long as every junction succeeds. If any junction fails, the train switches to the left track and passes through every remaining junction without executing them.

```
Right Track:    Input -> [Junction 1] -> [Junction 2] -> [Junction 3] -> Output
                              |
Left Track:              Exception  ->   [Skip]    ->   [Skip]    -> Exception
```

Trax.Core represents this with its own `Either<Exception, T>`, in the `Trax.Core.Functional` namespace. A value is either `Left` (an exception on the left track) or `Right` (successful delivery on the right track):

```csharp compile=core
using Trax.Core.Functional;
using Trax.Core.Train;

public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateUserJunction>()         // If this fails, skip remaining junctions
            .Chain<CreateUserJunction>()      // Only runs if validation succeeded
            .Chain<SendEmailJunction>().Resolve();      // Only runs if creation succeeded
}
```

If `ValidateUserJunction` throws, the train switches to the left track immediately and returns `Left(exception)`. You don't write any error-checking code. The railway handles it.

## Running a Train

```csharp compile=core
var train = new CreateUserTrain();
var input = new CreateUserRequest("ada@example.com");

// Run and unwrap (throws on failure)
var user = await train.Run(input);

// Run and inspect both tracks
var result = await train.RunEither(input);
result.Match(
    Left: ex => Console.WriteLine($"Failed: {ex.Message}"),
    Right: created => Console.WriteLine($"Created: {created.Email}")
);
```

## When to Use Core Alone

- Validation pipelines, data transformations, CLI tools
- Anywhere you'd write nested try/catch chains
- Lightweight junction composition without infrastructure overhead
- Prototyping trains before adding persistence

When you need execution logging, DI, or persistent metadata, add [Trax.Effect](/docs/effect).

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run)
