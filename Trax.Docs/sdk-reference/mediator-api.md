---
title: Mediator API
description: "Index of the Trax.Mediator reference: ITrainBus dispatch by input type, registration, train discovery and execution, concurrency limits and trusted scopes."
type: landing
area: mediator
parent: SDK Reference
has_children: true
nav_order: 6
---

# Mediator API

Trax.Mediator runs a train from its input or its name rather than from an injected train
interface: `ITrainBus` dispatches by the input's type in process, `ITrainExecutionService` by name
with JSON input. How the pieces fit is on [Mediator](/docs/mediator).

## Trax.Mediator

| Page | What it documents |
|------|-------------------|
| [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | `RunAsync`, `RunByNameAsync` and `InitializeTrain`: in-process dispatch by input type or by name |
| [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service) | The registered trains, as `TrainRegistration` records |
| [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | `QueueAsync`, `RunAsync` and `PrepareAsync`: a train by name, with JSON input |
| [TrainInputReader](/docs/sdk-reference/mediator-api/train-input-reader) | The rules every surface reads train input JSON by |
| [Mediator exceptions](/docs/sdk-reference/mediator-api/exceptions) | The exceptions in `Trax.Mediator.Exceptions` |
| [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting) | `ConcurrentRunLimit`, `PerPrincipalMaxConcurrentRun`, `GlobalConcurrentRunLimit` and `[TraxConcurrencyLimit]` |
| [IConcurrencyLimiter](/docs/sdk-reference/mediator-api/i-concurrency-limiter) | The permit gate behind the concurrency limits |
| [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) | The caller id the per-principal limit buckets by |
| [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) | Marks an async flow as trusted infrastructure, which skips per-train authorization |
| [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration) | The resolved mediator settings, readable from the container |
| [AddMediator](/docs/sdk-reference/mediator-api/add-mediator) | Where `AddMediator` is documented |
| [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus) | The registration `AddMediator` performs, and `RegisterServiceTrains` |

## Trax.Effect.Data

| Page | What it documents |
|------|-------------------|
| [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) | The data context an enqueue commits on, for an `OnQueue` hook. `AddMediator` registers it |
