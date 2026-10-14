---
title: Trax.Core.Analyzers
description: "The deprecated Trax.Core.Analyzers package, its CHAIN001 and CHAIN002 diagnostics, and why they no longer fire on any chain."
type: reference
area: tooling
nav_order: 4
source:
  - Trax.Core/src/Trax.Core.Analyzers
---

# Trax.Core.Analyzers

A Roslyn analyzer package, deprecated. It still ships on nuget.org, marked as a development
dependency, so it applies only to the project that references it and does not flow to that
project's consumers. `Trax.Core` does not depend on it. Its NuGet description says it is
deprecated and that there is no reason to install it; an existing reference can be removed.

| | |
|---|---|
| Package | `Trax.Core.Analyzers` |
| Analyzer | `TrainChainAnalyzer` |
| Diagnostics | `CHAIN001`, `CHAIN002`, both severity Error, category `Trax.Core.Train` |
| Status | Reports nothing |

## Behavior

The analyzer inspects only chains that start at `Activate()`. `Activate` is internal, so no train
can write such a chain, and a chain declared in `Junctions()` produces no diagnostic, whether it is
correct or not.

The [startup chain verification](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
replaces it. At host startup Trax reads every registered train's `Junctions()` declaration and
refuses to start if a junction's input never reaches Memory or the chain ends without the train's
return type, which are the two faults the analyzer reported.

## Diagnostics

### CHAIN001

Junction input type not available in train memory. Reported, as designed, when a junction needs a
type that no earlier step put in Memory:

```text
error CHAIN001: Junction 'LogGreetingJunction' requires input type 'HelloWorldInput'
which has not been produced by a previous junction. Available: [string, Unit].
```

```csharp fragment
// The Activate form the analyzer reads; it cannot be written any more.
Activate(input).Chain<LogGreetingJunction>().Resolve(); // CHAIN001: HelloWorldInput not in Memory
```

### CHAIN002

Train return type not available in memory. Reported, as designed, when `Resolve()` needs a type
that no step produced:

```text
error CHAIN002: Train return type 'Receipt' has not been produced by any junction.
Available: [OrderRequest, Unit].
```

```csharp fragment
Activate(input).Chain<ValidateOrderJunction>().Resolve(); // CHAIN002: Receipt not in Memory
```

## Suppressing

Either diagnostic is suppressed like any other, with `#pragma warning disable CHAIN001` around
the lines, or `<NoWarn>$(NoWarn);CHAIN001</NoWarn>` in the project file.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve)
