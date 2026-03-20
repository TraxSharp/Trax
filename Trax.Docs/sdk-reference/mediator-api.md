---
layout: default
title: Mediator API
parent: SDK Reference
nav_order: 4
has_children: true
---

# Mediator API

The mediator pattern in Trax.Mediator routes train execution by input type. Instead of injecting specific train interfaces, you inject `ITrainBus` and dispatch by passing the input object — the bus discovers and runs the correct train automatically.

```csharp
// Instead of:
var result = await _createOrderTrain.Run(orderInput);

// You can:
var result = await _trainBus.RunAsync<OrderResult>(orderInput);
```

This decouples callers from specific train implementations and enables train composition (nested trains that participate in the same metadata chain).

| Page | Description |
|------|-------------|
| [TrainBus](/docs/sdk-reference/mediator-api/train-bus) | `ITrainBus` interface — `RunAsync`, `InitializeTrain` |
| [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | Registration and assembly scanning configuration |
| [TrainDiscovery](/docs/sdk-reference/mediator-api/train-discovery) | `ITrainDiscoveryService` — discover registered trains and their input/output types |
| [TrainExecution](/docs/sdk-reference/mediator-api/train-execution) | `ITrainExecutionService` — queue or run trains programmatically with JSON input |
| [Concurrency Limiting](/docs/sdk-reference/mediator-api/concurrency-limiting) | Per-train and global concurrency limits for RUN executions |
