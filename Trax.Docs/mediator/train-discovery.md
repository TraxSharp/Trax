---
layout: default
title: Train Discovery
description: "How TrainRegistry scans assemblies and TrainBus routes by input type: input type uniqueness, train name resolution and the train discovery rules."
parent: Mediator
nav_order: 1
---

# Train Discovery & Routing

This page covers the internal implementation of train discovery and routing. For the user-facing explanation of how to use the `TrainBus`, see [Mediator overview](/docs/mediator).

## TrainRegistry

The `TrainRegistry` scans the specified assemblies at startup for all types implementing `IServiceTrain<TIn, TOut>`. For each discovered train, it extracts the `TIn` type and builds a `Dictionary<Type, Type>` mapping input types to train types. Duplicate input types are silently skipped via `TryAdd`, so the first registration wins.

## TrainBus

When `RunAsync<TOut>(input, metadata?)` is called, the `TrainBus`:

1. Looks up the train type from the registry by `input.GetType()`
2. Resolves the train from the DI container in a new scope
3. Injects framework-level properties (`EffectRunner`, `Metadata`, etc.)
4. Invokes the train's `Run` method via reflection. With no `metadata` the train creates its own record; with one, which must be `Pending` (anything else throws `TrainException`), it runs as that pre-created record, the way the scheduler runs a dispatched job. The metadata is not a parent link, and nothing sets the run's `ParentId`

## Key Constraints and Design Decisions

### Input Type Uniqueness

The bus's input-keyed `RunAsync` reaches one train per input type: when two trains share one, the first scanned is the one it runs. Discovery still lists both, and a run by name runs the train named. See [AddMediator](/docs/sdk-reference/configuration/add-mediator) for the full rules and code examples.

### Train Name Resolution

When looking up a train by name (e.g., via `ITrainExecutionService`), the system tries two matches in order:

1. **Canonical name**: `ServiceType.FullName` (the interface's fully-qualified name, e.g. `MyApp.Trains.IProcessOrderTrain`)
2. **Friendly name**: `ServiceTypeName` (the display name from the registration). A name that matches more than one train's friendly name throws `AmbiguousTrainNameException`.

There is no short-name fallback.

The canonical name is the preferred identifier. It is stable across implementation class renames and matches what is stored in metadata and work queue entries.

### Train Discovery Rules

1. **Must be concrete classes** (not abstract)
2. **Must implement IServiceTrain<,>**
3. **Must have a public constructor the container can satisfy.** The startup check refuses a train with no public constructor, or one whose constructor needs a type nothing registers.
4. **Should implement its own interface**, a non-generic one deriving from `IServiceTrain<TIn, TOut>`. That interface is the train's service type and its canonical name. Other interfaces the class or its base classes implement are ignored.
5. **One class per service type.** Two different classes registered under one class service type (`AddTransient<BaseTrain, A>()` and `AddTransient<BaseTrain, B>()`) make `DiscoverTrains` throw `TrainException`, so the host fails at startup: a train is found by the name of the type it is registered under, and the container runs only the last registration, so the name would not describe the train that runs. Register each train under its own interface or class.

## SDK Reference

> [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution)
