---
layout: default
title: Mediator API
description: "Index of the Trax.Mediator reference: ITrainBus dispatch by input type, registration, train discovery and execution, concurrency limits and trusted scopes."
parent: SDK Reference
nav_order: 4
has_children: true
---

# Mediator API

The mediator pattern in Trax.Mediator routes train execution by input type. Instead of injecting specific train interfaces, you inject `ITrainBus` and dispatch by passing the input object. The bus discovers and runs the correct train automatically.

```csharp
// Instead of:
var result = await _createOrderTrain.Run(orderInput);

// You can:
var result = await _trainBus.RunAsync<OrderResult>(orderInput);
```

This decouples callers from specific train implementations and enables train composition (a junction can dispatch another train; the child is recorded as a run of its own, not linked to the parent, see [Nested Trains](/docs/mediator#nested-trains)).

| Page | Description |
|------|-------------|
| [TrainBus](/docs/sdk-reference/mediator-api/train-bus) | `ITrainBus` interface: `RunAsync`, `InitializeTrain` |
| [AddMediator](/docs/sdk-reference/configuration/add-mediator) | Registration and assembly scanning configuration |
| [TrainDiscovery](/docs/sdk-reference/mediator-api/train-discovery) | `ITrainDiscoveryService`: discover registered trains and their input/output types |
| [TrainExecution](/docs/sdk-reference/mediator-api/train-execution) | `ITrainExecutionService`: queue or run trains programmatically with JSON input |
| [Concurrency Limiting](/docs/sdk-reference/mediator-api/concurrency-limiting) | Per-train and global concurrency limits for RUN executions |
| [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) | The data context an enqueue commits on, for an `OnQueue` hook's side-effect. Defined in Trax.Effect.Data; `AddMediator` registers it |
| [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) | Marks an async flow as trusted infrastructure, which skips per-train authorization |
| [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) | Supplies the caller id the per-principal concurrency limit buckets by |
| [IConcurrencyLimiter](/docs/sdk-reference/mediator-api/i-concurrency-limiter) | The permit gate behind the concurrency limits |
| [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration) | The resolved mediator settings, readable from the container |
| [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus) | The lower-level registration `AddMediator` performs, and `RegisterServiceTrains` |
