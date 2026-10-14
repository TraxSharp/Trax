---
title: Trains and junctions
description: "What trains and junctions are, why Junctions() is a declaration the host can check, how EffectJunction differs from Junction, and the lifecycle hooks."
type: explanation
area: trains
nav_order: 3
source:
  - Trax.Core/src/Trax.Core/Junction/Junction.cs
  - Trax.Effect/src/Trax.Effect/Services/EffectJunction/EffectJunction.cs
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
---

# Trains and junctions

A train is a typed unit of work, `TIn` to `TOut`, whose body is a chain of junctions. A junction does
one thing: it takes one input and returns one output. The train runs its junctions in order over a
Memory of typed values, and each junction's input is found there by type and its output stored
there. The first junction that throws stops the chain, and the train returns that exception
instead of an output. [Core](/docs/core) explains that railway.

Where to put trains and their files in a project is a how-to:
[Organize trains in a project](/docs/core/organize-trains).

## Junctions

```csharp compile=tj
using System.ComponentModel.DataAnnotations;
using Trax.Core.Functional;
using Trax.Core.Junction;

public class ValidateEmailJunction(IUserRepository users) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await users.GetByEmailAsync(input.Email) is not null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}
```

A junction takes its dependencies through its constructor; the train builds it from the container
when the chain reaches it. Throwing is how a junction fails, so a junction reads like ordinary code
and the train, not the junction, decides what a failure does to the rest of the chain.

A junction also carries the run's `CancellationToken` as a property, set before `Run` is called.
[How cancellation works](/docs/cross-cutting/cancellation-tokens) covers it, and
[Make a junction cancellable](/docs/cross-cutting/make-a-junction-cancellable) shows how to use it.

## EffectJunction vs Junction

There are two base classes, and a junction's code is the same under either.

`Junction<TIn, TOut>`, in Trax.Core, handles the input, the output and the railway. It records
nothing and has no hooks, so it runs anywhere a chain runs, including a plain `Train` constructed
with `new`.

`EffectJunction<TIn, TOut>`, in Trax.Effect, derives from it and makes the junction observable when
it runs inside a `ServiceTrain`. It records a junction metadata entry (name, input and output
types, start and end times, railway state, and the id of the run it belongs to), and the junction
effect providers (`AddJunctionLogger`, `AddJunctionProgress`, junction events) hook into its
lifecycle before and after it runs. On a plain `Junction` those providers record nothing, and they
do not say so.

```csharp compile=tj
using System.ComponentModel.DataAnnotations;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

public class ValidateEmailEffectJunction(IUserRepository users)
    : EffectJunction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await users.GetByEmailAsync(input.Email) is not null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}
```

The split exists so that Trax.Core stays free of the effect system: a library of junctions can be
written against `Junction` and run in a plain `Train` with no database and no container. The cost
is a choice per junction. An `EffectJunction` needs the `ServiceTrain` it reports to, so one run
inside a plain `Train` throws `TrainException`
(`Cannot run an EffectJunction (<junction>) against a non-ServiceTrain (<train>)`).

The run id the metadata entry carries (`Metadata.TrainMetadataId`) is the primary key of the run's
`trax.metadata` row, which lets a junction read its own run back, for example to check that it has
not been failed before it performs a side effect.
[Identifying the run from inside a junction](/docs/effect/effect-providers/junction-logger#identifying-the-run-from-inside-a-junction)
explains why the junction's registration lifetime matters when it does.

## Declaring the chain

```csharp compile=tj
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>().Chain<CreateUserJunction>().Resolve();
}
```

`Junctions()` returns `Task<Either<Exception, TOut>>`, and the chain ends in `Resolve()`, which
yields the train's output from Memory or the exception that stopped the chain. The framework seeds
Memory with the input before the chain runs. The chain methods (`Chain`, `ShortCircuit`,
`Extract`, `AddServices` and the rest) are protected methods on the train, and each returns a
`MonadTask`, so the calls chain. [Building chains](/docs/core/building-chains) describes what each
one does on the railway.

### There is no escape hatch, on purpose

`Junctions()` is the only way to declare a chain. `RunInternal` is private and `Activate` is
internal, so a train cannot build its chain imperatively. Upgrading a train that used them is
covered in [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate).

A `ServiceTrain` cannot override `Run` either. `Run(input, ct)` is a sealed override, the
`Run(input, metadata)` overloads are not virtual, and `NewMonad()`, which builds the monad the
chain runs on, is sealed. `Run` owns what wraps the chain: the metadata row, the lifecycle hooks
and the outcome write. An override that did its own work, or skipped `base.Run`, would run
untracked while every surface still reported the train. A plain `Train<TIn, TOut>` from Trax.Core
keeps a virtual `Run` and `NewMonad()`, because it has none of that to protect.

That is what makes a chain readable before it runs. A chain assembled in code has no single shape,
so the host could not check it, and a chain that varied by input would mean the shape checked is
not necessarily the shape that runs. Keeping the guarantee means the declaration is the only
option.

Two rules follow from it.

**A chain cannot read the value being processed.** `TrainInput` and `TrainOutput` throw
`ChainDeclarationException` while a chain is being declared. Work that needs the input belongs in a
junction, which receives it as its argument.

**A value a later junction reads comes from something the chain declares.** Usually that is an
earlier junction, whose return value lands in Memory keyed by its output type. The chain can also
seed Memory with `AddServices(value)` (stored under the interface type it names) or
`Extract<TIn, TOut>(value)` (stores the `TOut` member of the value it is given). What is gone is
`Activate`'s way of handing objects in from outside the declaration.

### The host checks every chain before it serves traffic

Because a chain is a declaration of types, whether it can run is decidable without running it.
`AddMediator` registers a hosted service that reads every registered train's chain at startup and
refuses to start the host when one of them cannot run: a junction that needs a value nothing before
it produces, a junction Trax cannot build, a chain that reads the input, a train whose constructor
needs something the container does not register. Every train is checked before anything is
reported, so one start lists every fault.

The alternative was to let each of those fail at runtime, which surfaces each one only on the path
that hits it, and for a rarely run train that can be a long way from the deploy that broke it.
[Startup chain check](/docs/core/startup-chain-check) lists every fault, every warning, and how to
turn the check off.

#### What the check counts as available

The check replays the rules a run uses to store and find values in Memory, so what it accepts is
what a run would find: the input under its declared type and interfaces, a junction's output under
exactly its declared type, and a short circuit's output not at all, because it is present on only
one of its two paths. The full table is in
[Startup chain check](/docs/core/startup-chain-check#what-counts-as-available).

## Lifecycle hooks

`ServiceTrain` has four `protected virtual` hooks a train overrides to react to its own runs:
`OnStarted`, `OnCompleted`, `OnFailed` and `OnCancelled`. They default to no-ops.

```csharp compile=tj
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

public class NotifyingCreateUserTrain(ISlackClient slack)
    : ServiceTrain<CreateUserRequest, User>,
        ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>().Chain<CreateUserJunction>().Resolve();

    protected override async Task OnFailed(
        Metadata metadata,
        Exception exception,
        CancellationToken ct
    ) => await slack.PostAsync($"User creation failed: {exception.Message}", ct);
}
```

Each hook receives the run's metadata as it stands at that point: `OnStarted` sees the input,
`OnCompleted` the output too, `OnFailed` and `OnCancelled` the failure. A hook that throws is
logged and never fails the run, because a notification that failed is not a reason to report the
work as failed. [Global lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook) see
every train's runs and fire before the train's own. `OnFailed` sees the failure's `FailureClass`,
which an `IFailureClassifier` the host registers decides; see
[Failure classification](/docs/effect/failure-classification).

### OnQueue (enqueue-time hook)

`OnQueue`, `DeferQueuePromotion` and `QueueSubjectKey` are a different kind of hook: the mediator's
queue path calls them when it accepts a mutation, before the train runs, and a throw refuses the
mutation. [Enqueue hooks](/docs/mediator/enqueue-hooks) describes each one. A side effect written
in `OnQueue` and the work queue row are two writes, and a train that returns a subject key stops
two queued mutations for the same record from running at once;
[How queued work stays durable and serialized](/docs/mediator/queued-work) explains both.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | [Junction](/docs/sdk-reference/trains-and-junctions/junction) | [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
