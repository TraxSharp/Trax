---
title: Train discovery
description: "How the train registry scans assemblies and the train bus routes by input type or name: service type selection, shared input types and discovery rules."
type: reference
area: mediator
nav_order: 1
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainRegistry
  - Trax.Mediator/src/Trax.Mediator/Services/TrainBus
  - Trax.Mediator/src/Trax.Mediator/Services/TrainDiscovery/TrainDiscoveryService.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainExecutionService.cs
---

# Train discovery

How `AddMediator` finds trains and how the train bus picks one. Why the bus routes by input type
is in [Mediator](/docs/mediator).

## The registry scan

The train registry scans each assembly passed to `AddMediator`, in the order given (an assembly
passed twice is scanned once), for non-abstract classes that implement `IServiceTrain<TIn, TOut>`.
For each class it selects a service type:

| The class implements | Service type |
|---|---|
| One non-generic interface deriving from `IServiceTrain<TIn, TOut>` (`IMyTrain`) | That interface |
| Several such interfaces, one extending the others | The most derived one |
| Several such interfaces, none extending the others | None: `TrainException`, "implements more than one train interface" |
| No such interface | The closed `IServiceTrain<TIn, TOut>` |

The service type is the train's registration and its canonical name (its `FullName`). The
registry then maps each service type's `TIn` to the service type. When two trains take the same
input type, the first one found keeps the mapping: assemblies in the order given, then each
assembly's own type order. Both trains are still registered and discovered.

## Running by input type

`ITrainBus.RunAsync<TOut>(input, ...)`:

1. Looks up `input.GetType()` in the registry. The lookup uses the exact runtime type, so an
   input of a derived type finds only a train registered for that derived type. No entry throws
   `NoTrainForInputException`, whose message names the scanned assemblies (see
   [Troubleshooting](/docs/cross-cutting/troubleshooting#could-not-find-train-with-input-type-x)).
2. Creates a DI scope and resolves the service type in it.
3. Fills the train's `[Inject]` properties.
4. Calls the train's `Run`. With no `metadata` the train creates its own record. With one, which
   must be `Pending` (any other state throws `TrainException`), it runs as that pre-created record,
   the way the scheduler runs a dispatched job. The metadata is not a parent link, and nothing sets
   the run's `ParentId`.
5. Disposes the scope when the run returns.

## Running by name

`ITrainBus.RunByNameAsync(trainName, input, ...)` takes the canonical name only. A name no train
is registered under throws `TrainException` "No train is registered under the name (X)", and an
input that is not an instance of the train's input type throws `TrainException`. It then resolves
and runs the train in a scope of its own, as `RunAsync` does.

`ITrainExecutionService` finds a train by name in this order:

1. **Canonical name**: the service type's `FullName`, such as `MyApp.Trains.IProcessOrderTrain`.
2. **Friendly name**: the registration's `ServiceTypeName`. A name that matches more than one
   train's friendly name throws `AmbiguousTrainNameException`.

There is no short-name fallback. No match throws `TrainNotFoundException`, whose message is always
"The requested train was not found.". The canonical name is what metadata and work queue entries
store, and it does not change when the implementing class is renamed.

## Discovery rules

| Rule | When broken |
|---|---|
| The class is concrete | An abstract class is not scanned |
| The class implements `IServiceTrain<,>` | The class is not scanned |
| The class has a public constructor the container can satisfy | The startup chain check refuses the host, naming the missing type |
| One class per class service type | Two classes registered under one class service type (`AddTransient<BaseTrain, A>()` and `AddTransient<BaseTrain, B>()`) make `DiscoverTrains` throw `TrainException`, so the host fails at startup |

## SDK Reference

> [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
