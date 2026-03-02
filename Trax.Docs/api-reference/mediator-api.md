---
layout: default
title: Mediator API
parent: API Reference
nav_order: 4
has_children: true
---

# Mediator API

The mediator pattern in Trax.Core routes train execution by input type. Instead of injecting specific train interfaces, you inject `ITrainBus` and dispatch by passing the input object — the bus discovers and runs the correct train automatically.

```csharp
// Instead of:
var result = await _createOrderTrain.Run(orderInput);

// You can:
var result = await _trainBus.RunAsync<OrderResult>(orderInput);
```

This decouples callers from specific train implementations and enables train composition (nested trains that participate in the same metadata chain).

| Page | Description |
|------|-------------|
| [TrainBus]({{ site.baseurl }}{% link api-reference/mediator-api/train-bus.md %}) | `ITrainBus` interface — `RunAsync`, `InitializeTrain` |
| [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/mediator-api/add-effect-train-bus.md %}) | Registration and assembly scanning configuration |
