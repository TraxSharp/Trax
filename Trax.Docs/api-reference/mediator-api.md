---
layout: default
title: Mediator API
parent: API Reference
nav_order: 4
has_children: true
---

# Mediator API

The mediator pattern in Trax.Core routes workflow execution by input type. Instead of injecting specific workflow interfaces, you inject `IWorkflowBus` and dispatch by passing the input object — the bus discovers and runs the correct workflow automatically.

```csharp
// Instead of:
var result = await _createOrderWorkflow.Run(orderInput);

// You can:
var result = await _workflowBus.RunAsync<OrderResult>(orderInput);
```

This decouples callers from specific workflow implementations and enables workflow composition (nested workflows that participate in the same metadata chain).

| Page | Description |
|------|-------------|
| [WorkflowBus]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %}) | `IWorkflowBus` interface — `RunAsync`, `InitializeWorkflow` |
| [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/mediator-api/add-effect-workflow-bus.md %}) | Registration and assembly scanning configuration |
