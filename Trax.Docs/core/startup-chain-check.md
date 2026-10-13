---
title: Startup chain check
description: "The check AddMediator runs at host start: when it runs, every fault that refuses a start, what it only warns about, and which values count as available."
type: reference
area: trains
nav_order: 20
source:
  - Trax.Mediator/src/Trax.Mediator/Services/ChainVerification/TrainChainStartupValidator.cs
  - Trax.Core/src/Trax.Core/Monad/ChainVerification.cs
---

# Startup chain check

A hosted service, registered by `AddMediator`, that reads the chain every registered train declares
in `Junctions()` and refuses to start the host when one of them cannot run. Why the chain can be
checked at all is in [Trains and junctions](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic).

## When it runs

| Host | Check |
|---|---|
| Generic host (`WebApplication`, `Host.CreateApplicationBuilder`) | In `IHostedLifecycleService.StartingAsync`. The host finishes `StartingAsync` for every hosted service before it calls any `StartAsync`, so a refusal stops the host before any worker starts, also under `HostOptions.ServicesStartConcurrently`. |
| Something that starts hosted services itself and calls only `StartAsync` (a custom `IHost`, a test harness) | In `StartAsync`. |
| The Lambda runner, a bare `ServiceProvider` | Never. Nothing starts the hosted service. |

The `[TraxAuthorize]` registration check runs the same way.

## Refusals

A refusal throws `TrainException` from the host's start, with the message
`<n> of <m> registered trains cannot run:` followed by one line per fault. Every train is checked
before anything is reported. Each line is prefixed with the train; a step's fault names its step,
numbered from one by written position, and its junction:

```text
IOrderTrain: step 2 (ChargeCard) needs 'Payment' in Memory and nothing before it puts one there.
```

The check refuses a start when:

| Fault | Example message or note |
|---|---|
| The chain could not be read because `Junctions()` reads `TrainInput` or `TrainOutput` | Both throw `ChainDeclarationException` while a chain is declared. Read the input in a junction, or in [`OnQueue`](/docs/mediator/enqueue-hooks#onqueue) for work done when a mutation is accepted. |
| `Junctions()` throws anything else | For example a `NullReferenceException` from reading `Metadata`, which is null at startup. |
| `Junctions()` awaits something before returning | It does work instead of declaring a chain. |
| `Junctions()` returns a result directly instead of ending in `Resolve()` | For example `Task.FromResult(value)`. |
| The chain ends in `Resolve(value)` | It states the result instead of naming the junction that produces it. A train with no junctions uses `Task.FromResult(Resolve())`. |
| A junction needs something in Memory that nothing before it produces | `IOrderTrain: step 2 (ChargeCard) needs 'Payment' in Memory and nothing before it puts one there.` |
| An `IChain` step names a class, or a junction interface that neither Memory nor the container holds | |
| `AddServices<T>` names a class rather than an interface | |
| `AddServices` receives null | The service has to exist when `Junctions()` runs. A field assigned later, in a lifecycle hook such as `OnStarted`, is null when the check reads the chain. |
| `AddServices` receives a struct passed as an interface | |
| `Chain<T>` or `ShortCircuit<T>` names a type that is not a junction | `step 2: Chain names NotAJunction, which does not implement IJunction<TIn, TOut>.` |
| `Chain<T>` or `ShortCircuit<T>` names a junction Trax cannot build | It does not have exactly one public constructor, or it is abstract or an interface. |
| A junction Trax builds has a constructor argument that neither Memory (as filled by that step) nor the container supplies | `IScheduleTrain: step 2 (ClockJunction) needs 'IClock' as a constructor argument; nothing before it puts one in Memory and the container does not register it. Register it or chain a junction that produces it first.` |
| A second chain call starts from the train as a separate statement after a junction step the body did not await | `Chain<A>(); return Chain<B>().Resolve();` runs two chains at once over one Memory. Link them, or `await` the first. Synchronous calls such as `AddServices` or `Extract` before the first junction step are allowed. |
| A `ShortCircuit` junction's output cannot be the train's return type | Its value is returned as the result by a cast, which would fail on every run. |
| The chain ends without the train's return type in Memory | |
| The train's constructor needs a type the container does not register | `ITdTrain cannot be built: its constructor needs 'IClock', which is not registered. Register it before building the host.` |
| A registered dependency of the train needs such a type | `INeedsNestedTrain cannot be built: ProbeRepository needs 'IClock', which is not registered, and the train's constructor reaches it through 'IProbeRepository'. Register it before building the host.` The check follows registrations that name an implementation class, open generics included. Factory and instance registrations are not followed. |
| The train's class has no public constructor | `IFooTrain cannot be built: FooTrain has no public constructor. Give it one.` |
| A train's `[Inject]` property names a type the container does not register | `IReportTrain: its [Inject] property 'Clock' needs 'IClock', which is not registered, so the property is null on every run. Register it before building the host, or declare the property nullable if the train runs without it.` A nullable property (`IClock?`) is a warning instead. `ServiceTrain`'s own framework properties are not checked. |
| A train's `[Inject]` property names a registered type that can never be built | Refused however the property is declared. |

A refused step that produced nothing leaves the chain without what it would have produced, so a
"chain ends without" fault that follows from such a step is left out. One that does not follow
from a refusal is listed with it.

## Warnings

These are logged at `Warning`, and the host starts:

| Case | Why it is not refused |
|---|---|
| An `[Inject]` property on a junction the chain builds itself (`Chain<T>()`, `ShortCircuit<T>()`) | Such a junction is built from its constructor and its properties are never filled. Whether it reads the property cannot be told from its type. |
| A train that cannot be built at startup, when nothing shows it can never be built | Usually a service only a request supplies, such as a current user read from `HttpContext` through a factory registration. Its chain goes unverified: `The chain of <train> was not verified at startup: <reason>`. |
| A registered train that does not derive from `Train<,>` | Skipped the same way. |

A train that can be built but whose `Junctions()` throws is refused, not skipped.

## What counts as available

The check replays the rules a run uses to store and find values in Memory.

| Value | Available under |
|---|---|
| The train's input | Its declared type and every interface that type implements. A run stores the input under its declared type and its runtime type (with interfaces). |
| An element of a tuple | Its declared type and every interface that type implements. A run stores each element under its declared type and its runtime type and interfaces; a null element is skipped. |
| A junction's output, an `Extract` result, an `AddServices` value | Exactly its declared type. |
| A `ShortCircuit` junction's output | Not counted. A short circuit that returns `Right` stores its output and the chain keeps running; one that returns `Left` stores nothing and the chain also keeps running. A value that is there on one path only cannot be relied on. |

Lookup is by exact type, then the container, except:

- A junction whose input is a tuple has each element assembled from Memory only, never from the
  container.
- `Extract` reads Memory only.
- A junction asking for an interface that the previous junction's declared output only implements
  is a fault: the run would not find it either.
- An `ILogger<T>` the container does not supply can be made by an `ILoggerFactory` passed to
  `AddServices`.

Container availability is checked without constructing anything, so a service whose factory works
only inside a request does not fail startup. Reading a chain runs no junction: a monad the train
creates while declaring, including through `NewMonad()`, records its steps instead of executing
them.

The replay knows the train's declared input type, not the subtype a caller passes at runtime. A
junction asking for an interface that only the runtime subtype implements is reported as a fault
although the run would find it. Declaring the input as that subtype fixes both.

## Turning it off

```csharp compile
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax =>
    trax.AddEffects()
        .AddMediator(mediator => mediator.ScanAssemblies(assembly).SkipChainVerification())
);
```

`SkipChainVerification()` turns off every check on this page, the `[Inject]` checks included.

## See also

- [Trains and junctions](/docs/core/trains-and-junctions): why a chain is a declaration.
- [Troubleshooting](/docs/cross-cutting/troubleshooting): the messages by symptom.
- [AddMediator](/docs/sdk-reference/configuration/add-mediator), [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain).
- [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate): moving a train onto `Junctions()`.

## SDK Reference

> [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [Junctions](/docs/sdk-reference/train-methods/junctions) | [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception)
