---
layout: default
title: Train Discovery
parent: Mediator
nav_order: 1
---

# Train Discovery & Routing

This page covers the internal implementation of train discovery and routing. For the user-facing explanation of how to use the `TrainBus`, see [Mediator overview](/docs/mediator).

## TrainRegistry

The `TrainRegistry` scans the specified assemblies at startup for all types implementing `IServiceTrain<TIn, TOut>`. For each discovered train, it extracts the `TIn` type and builds a `Dictionary<Type, Type>` mapping input types to train types. Duplicate input types are silently skipped via `TryAdd` — the first registration wins.

## TrainBus

When `RunAsync<TOut>(input, parentMetadata?)` is called, the `TrainBus`:

1. Looks up the train type from the registry by `input.GetType()`
2. Resolves the train from the DI container in a new scope
3. Injects framework-level properties (`EffectRunner`, `Metadata`, etc.)
4. Invokes the train's `Run` method via reflection, passing the input and optional parent metadata for parent-child linking

## Key Constraints and Design Decisions

### Input Type Uniqueness

Each input type maps to exactly one train. When duplicate input types are found, the first registration wins — subsequent duplicates are silently skipped via `TryAdd`. See [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) for the full uniqueness rules and code examples.

### Train Name Resolution

When looking up a train by name (e.g., via `ITrainExecutionService`), the system tries three matches in order:

1. **Canonical name** — `ServiceType.FullName` (the interface's fully-qualified name, e.g. `MyApp.Trains.IProcessOrderTrain`)
2. **Friendly name** — `ServiceTypeName` (the display name from the registration)
3. **Short name** — `ServiceType.Name` (the unqualified interface name, e.g. `IProcessOrderTrain`)

The canonical name is the preferred identifier. It is stable across implementation class renames and matches what is stored in metadata and work queue entries.

### Train Discovery Rules

1. **Must be concrete classes** (not abstract)
2. **Must implement IServiceTrain<,>**
3. **Must have parameterless constructor or be registered in DI**
4. **Should implement a non-generic interface** for better DI integration

## SDK Reference

> [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution)
