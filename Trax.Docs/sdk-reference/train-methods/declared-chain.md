---
layout: default
title: DeclaredChain
description: Reference for DeclaredChain, ChainRecorder and ChainVerification.Verify, which read and check a train's declared chain without running any junction.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 9
---

# DeclaredChain

Reads a train's declared chain without resolving or running any junction. This is what the host's [startup chain check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) calls for every registered train; you can call it yourself, for example in a test that asserts a train's route.

## Signatures

```csharp
// Train<TInput, TReturn>
public ChainRecorder DeclaredChain()
public bool IsDeclaringChain { get; }
```

`DeclaredChain()` runs `Junctions()` against a monad that records each chain call's type arguments instead of executing it, and returns the recording. Nothing is resolved from the container and no junction runs, including on a monad created through `NewMonad()` while declaring. `NewMonad()` is the seam `ServiceTrain` uses to supply its container, not an extension point: it is `protected` because the published Trax.Effect overrides it, and hidden from completion.

`IsDeclaringChain` is true while the chain is being read. Per-execution accessors check it: `ServiceTrain.TrainInput` and `TrainOutput` throw `ChainDeclarationException` while it is true.

**Throws**: `ChainDeclarationException` when the train reads per-execution state while declaring, and whatever else `Junctions()` itself throws while it is read (a train that never overrides it throws `NotImplementedException`, for instance). A declaration that completes but is wrong, such as a body that awaits, returns a result, or starts a second chain in a separate statement, is recorded as a refusal instead.

## ChainRecorder

```csharp
namespace Trax.Core.Monad;

public sealed class ChainRecorder
{
    public IReadOnlyList<ChainStep> Steps { get; }
    public IReadOnlyList<string> Refusals { get; }
}
```

| Member | Description |
|--------|-------------|
| `Steps` | The declared steps, in order. A step naming a type that is not a junction is kept with no `In` or `Out`, so a step's index is always its written position |
| `Refusals` | Things the declaration did that no step can express, each phrased for whoever has to fix it: awaiting before returning, returning a result instead of ending in `Resolve()`, ending in `Resolve(value)`, `IChain` or `AddServices` of a class, `AddServices` of null, `Chain` or `ShortCircuit` of a type that is not a junction, `Chain<T>()` or `ShortCircuit<T>()` of a junction Trax cannot build (not exactly one public constructor, abstract, or an interface) |

## ChainStep and ChainStepKind

```csharp
public readonly record struct ChainStep(ChainStepKind Kind, Type? Junction, Type? In, Type? Out);

public enum ChainStepKind { Chain, IChain, ShortCircuit, Extract, Resolve, Seed }
```

| Field | Description |
|-------|-------------|
| `Kind` | Which chain primitive declared the step |
| `Junction` | The junction type, or null for a step that names none |
| `In` | The type the step consumes from Memory, or null |
| `Out` | The type the step contributes to Memory, or null |

| `ChainStepKind` | Declared by |
|-----------------|-------------|
| `Chain` | `Chain<TJunction>()` and its overloads |
| `IChain` | `IChain<TJunction>()` |
| `ShortCircuit` | `ShortCircuit<TJunction>()` |
| `Extract` | `Extract<TIn, TOut>()`, projecting a value already in Memory |
| `Resolve` | `Resolve()` |
| `Seed` | A value handed to the chain directly, by `AddServices(value)` or `Extract<TIn, TOut>(value)`, which puts its type into Memory without a junction producing it |

## ChainVerification.Verify

```csharp
namespace Trax.Core.Monad;

public static IReadOnlyList<ChainFault> Verify(
    ChainRecorder chain,
    Type input,
    Type output,
    Func<Type, bool>? availableElsewhere = null
)

public static IReadOnlyList<ChainFault> Verify(
    ChainRecorder chain,
    Type input,
    Type output,
    IServiceProviderIsService container
)

public readonly record struct ChainFault(int StepIndex, ChainStepKind Kind, Type? Junction, string Reason)
{
    public bool IsRefusal { get; init; }
}
```

| Parameter | Description |
|-----------|-------------|
| `chain` | The recording from `DeclaredChain()` |
| `input` | The train's input type, which seeds Memory |
| `output` | The train's return type, which the chain must end holding |
| `availableElsewhere` | Whether a type the chain never produces can still be supplied by the container. Without it, every junction taking an injected service reads as a fault |
| `container` | The container the train runs with. Answers what `availableElsewhere` would, and also turns on the constructor check below |

Replays the recording over the types Memory would hold and returns every step that does not line up, plus one fault per refusal. The rules it applies are in [Trains & Junctions](/docs/core/trains-and-junctions#what-the-check-counts-as-available).

Given the `container`, it also checks every junction Trax builds from its constructor (`Chain<T>()` and `ShortCircuit<T>()`): each constructor argument has to be in Memory by that step, registered in the container, the container itself, or an `ILogger<T>` an `ILoggerFactory` in Memory can make. A tuple argument is assembled from Memory only. A junction passed as an instance, or resolved by `IChain`, is already built and is not checked. The overload without a container leaves constructors unchecked. The host's startup check passes the container, from Trax.Mediator 1.23.3, so a junction whose constructor argument nothing supplies stops the host rather than failing its first run.

| `ChainFault` member | Description |
|---------------------|-------------|
| `StepIndex` | The step's position, counting from zero. A refusal about the whole chain rather than one step sits one past the last step |
| `Kind` | The step's kind. `Resolve` for a refusal about the whole chain |
| `Junction` | The junction the step names, or null |
| `Reason` | What is wrong, phrased for whoever has to fix it |
| `IsRefusal` | True for one of `Refusals`, false for a fault the replay found. A refused step records no output, so faults after it may be its consequences |

## Exceptions

| Type | Namespace | When |
|------|-----------|------|
| `ChainDeclarationException` | `Trax.Core.Exceptions` | A train read `TrainInput` or `TrainOutput` while its chain was being declared. The message names the train and the member |
| `ChainRecordedException` | `Trax.Core.Exceptions` | The sentinel `Resolve()` returns as `Left` while a chain is being read. The reader discards it; it should never be observed outside chain reading. It is public only because it shipped public, and is hidden from completion: do not catch, match on or throw it |

## Example

```csharp
[Test]
public void CreateUserTrain_DeclaresAChainThatLinesUp()
{
    var train = new CreateUserTrain();
    var chain = train.DeclaredChain();

    chain.Refusals.Should().BeEmpty();
    ChainVerification.Verify(chain, typeof(CreateUserRequest), typeof(User)).Should().BeEmpty();
}
```

## Package

```
dotnet add package Trax.Core
```
