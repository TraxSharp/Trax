---
title: Move trains off RunInternal and Activate
description: "Moving trains that overrode RunInternal or called Activate to a Junctions() declaration, the Memory rules the startup check enforces, and the async chain."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 5
redirect_from:
  - /docs/migration-guides/async-chain
---

# Move trains off RunInternal and Activate

Applies from: Trax.Core 1.7.2 and Trax.Effect 1.54.2. The async chain this builds on arrived in
Trax.Core 1.5.0; a train older than that also needs [the async chain edits](#before-traxcore-150-the-async-chain).

`Junctions()` is now the only way to declare a train's chain. `Train.RunInternal` is private,
`Train.Activate` is internal, and `ServiceTrain.Activate` is gone. A train that overrode
`RunInternal` or called `Activate` no longer compiles. The reason is in
[ADR 0016](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md):
a chain built imperatively has no single shape, so the host could not read it at startup.

The same release changed how work is enqueued and how a run's outcome is recorded; see
[Adapt to the enqueue and outcome changes](/docs/migration-guides/enqueue-and-outcome-changes).

## Overriding RunInternal

Before:

```csharp fragment
protected override Task<Either<Exception, TOut>> RunInternal(TIn input) =>
    Activate(input).Chain<A>().Resolve();
```

After:

```csharp fragment
protected override Task<Either<Exception, TOut>> Junctions() =>
    Chain<A>().Resolve();
```

The framework seeds Memory with the input before `Junctions()` runs, so the chain starts at the
first junction. A chain that names no junctions, because the return type is already in Memory as
the input type or `Unit`, becomes `Task.FromResult(Resolve())`.

## Code that read the input above the chain

`Junctions()` declares a chain; it does not process a value. `TrainInput` and `TrainOutput` throw
`ChainDeclarationException` while the chain is being read, and the startup check turns that into a
refused start. Code that used the `input` parameter of `RunInternal` moves to one of two places:

| The work | Where it goes |
|---|---|
| Needs the input as part of the run | A junction at the head of the chain, which receives the input as its argument |
| Must happen the moment a queued mutation is accepted | [`OnQueue`](/docs/mediator/enqueue-hooks), which is handed a `Metadata` carrying the input |

Values passed through `Activate(input, otherInputs)` need a declared source instead. Produce them
from an earlier junction, or seed them in the chain with `AddServices<IService>(value)` (under an
interface) or `Extract<TIn, TOut>(value)`, both of which the startup check records as seeds.

## Bodies that are not a pure declaration

A `RunInternal` body could do anything before returning. `Junctions()` cannot, and the startup
check refuses these shapes, which tend to come across from the old code:

| The body | Why it is refused | Instead |
|---|---|---|
| Awaits something before returning the chain | It does work instead of declaring a chain | Move the awaited work into a junction |
| Returns a value directly, such as `Task.FromResult(value)` | There is no chain to verify | Chain the junction that produces the value, end in `Resolve()` |
| Ends in `Resolve(value)` | It states the result instead of naming what produces it | End in `Resolve()`; a train with no junctions uses `Task.FromResult(Resolve())` |
| `Chain<T>` or `ShortCircuit<T>` of a type that is not a junction | Nothing can run it | Name a junction type |
| `IChain<T>` or `AddServices<T>` of a class | Both resolve by interface; the run refuses a class every time | Name the interface, or use `Chain<T>` for a concrete junction |
| A `ShortCircuit` whose output cannot be the train's return type | The value is returned as the result by a cast that would always fail | Short-circuit with a junction producing the return type |

## Memory rules the check enforces

The check replays Memory the way the runtime fills it, which can surface a chain that only worked
by accident. The train's input is available under its declared type and every interface it
implements (a run also stores it under the runtime subtype); each element of a tuple is available
under its declared type and interfaces (again, a run also stores the runtime type); a junction's output, an `Extract` result and an `AddServices` value
are available only under their exact declared type. A junction taking a tuple has its elements
assembled from Memory only, never from the container. A `ShortCircuit` junction's output is not counted: at runtime it is stored only when the junction returns `Right` (the chain keeps running either way), so a later junction or `Resolve()` cannot rely on it. A junction that asks for an interface the previous
junction's output merely implements is refused, because the run would not find it either. Declare
the producer's output as that interface, or ask for the concrete type.

## If the upgrade is blocked on it

The input-reading fault compiles cleanly and only surfaces at startup. While chains are being
moved over, the check can be turned off:

```csharp fragment
.AddMediator(mediator => mediator.SkipChainVerification())
```

It silences every other chain fault too, so it is a stopgap to remove once the trains are moved,
not a setting to leave on. See
[Trains and junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
for the full list of what the check refuses.

## Before Trax.Core 1.5.0: the async chain

Trax.Core 1.5.0 made `Junctions()`, `Chain`, `Resolve`, `Extract`, `ShortCircuit` and
`AddServices` async. The names and the fluent shape stayed; the return types moved into `Task`, so
the chain no longer blocks on async work. The old chain blocked synchronously on each junction's
`Task`, which deadlocked under Blazor Server, WPF and any other single-threaded
`SynchronizationContext`.

| Member | Before | After |
|---|---|---|
| `Train.Junctions()` | returns `TReturn` | returns `Task<Either<Exception, TReturn>>` |
| `Monad.Chain<T>()` | returns `Monad<,>` | returns `MonadTask<,>`, an awaitable wrapper |
| `Monad.ShortCircuit<T>()` | returns `Monad<,>` | returns `MonadTask<,>` |
| `Monad.Extract<TIn, TOut>()` | returns `Monad<,>` | unchanged on `Monad<,>`, also on `MonadTask<,>` |
| `Monad.AddServices<...>(...)` | returns `Monad<,>` | unchanged on `Monad<,>`, also on `MonadTask<,>` |
| `Monad.Resolve()` | returns `Either<Exception, TReturn>` | unchanged on `Monad<,>`; returns `Task<Either<Exception, TReturn>>` on `MonadTask<,>` |
| `implicit operator TReturn(Monad<,>)` | existed | deleted |

`MonadTask<,>` exposes the same `Chain`, `ShortCircuit`, `Extract`, `AddServices` and `Resolve`, so
`.Chain<A>().Chain<B>().Resolve()` keeps composing across the async boundary.

A train that overrode `Junctions()` makes three edits: wrap the return type in
`Task<Either<Exception, ...>>`, end the chain in `.Resolve()`, and add `using Trax.Core.Functional;`.

Before:

```csharp fragment
protected override LookupOutput Junctions() =>
    Chain<LoadInputJunction>().Chain<DispatchJunction>();
```

After:

```csharp fragment
protected override Task<Either<Exception, LookupOutput>> Junctions() =>
    Chain<LoadInputJunction>().Chain<DispatchJunction>().Resolve();
```

A train that overrode `RunInternal` at the time moves straight to `Junctions()` as
[Overriding RunInternal](#overriding-runinternal) shows. Code that used the implicit conversion,
`T result = monad;`, awaits `monad.Chain<X>().Resolve()` and reads the `Either` instead. Junction
code is unchanged: `Junction<TIn, TOut>.Run` already returned `Task<TOut>`.

Workarounds added only to avoid the deadlock, such as a sync-context suppression around a chain,
can be deleted.

## Related

- [Trains and junctions](/docs/core/trains-and-junctions): how a chain is declared now.
- [Junctions](/docs/sdk-reference/train-methods/junctions): the `Junctions()` reference.
- [Adapt to the enqueue and outcome changes](/docs/migration-guides/enqueue-and-outcome-changes): the rest of the same release.
