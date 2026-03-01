---
layout: default
title: AddServiceTrainBus
parent: Configuration
grand_parent: API Reference
nav_order: 7
---

# AddServiceTrainBus

Registers the `IWorkflowBus` and `IWorkflowRegistry` services, and discovers all `IServiceTrain<,>` implementations via assembly scanning. This enables dynamic workflow dispatch by input type.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddServiceTrainBus(
    this Trax.CoreEffectConfigurationBuilder configurationBuilder,
    ServiceLifetime effectWorkflowServiceLifetime = ServiceLifetime.Transient,
    params Assembly[] assemblies
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `effectWorkflowServiceLifetime` | `ServiceLifetime` | No | `Transient` | The DI lifetime for discovered workflow registrations |
| `assemblies` | `params Assembly[]` | Yes | — | Assemblies to scan for `IServiceTrain<,>` implementations |

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(
        effectWorkflowServiceLifetime: ServiceLifetime.Scoped,
        assemblies: typeof(Program).Assembly)
);
```

## What It Registers

1. Scans the specified assemblies for all types implementing `IServiceTrain<TIn, TOut>`
2. Registers each discovered workflow with the DI container at the specified lifetime
3. Registers `IWorkflowBus` for dynamic workflow dispatch
4. Registers `IWorkflowRegistry` for workflow type lookup

## How Discovery Works

1. Scans the specified assemblies for all types implementing `IServiceTrain<TIn, TOut>`.
2. For each discovered type, extracts the `TIn` (input) type.
3. Registers the workflow in the `IWorkflowRegistry` keyed by `TIn`.
4. Registers the workflow in the DI container with the specified lifetime.

## Input Type Uniqueness

Each input type must map to **exactly one** workflow. If two workflows accept the same `TIn`, registration will throw an exception.

```csharp
// This is fine — different input types
public class CreateOrderWorkflow : ServiceTrain<CreateOrderInput, OrderResult> { }
public class CancelOrderWorkflow : ServiceTrain<CancelOrderInput, OrderResult> { }

// This will FAIL — both accept OrderInput
public class CreateOrderWorkflow : ServiceTrain<OrderInput, OrderResult> { }
public class UpdateOrderWorkflow : ServiceTrain<OrderInput, OrderResult> { }
```

## Lifetime Considerations

| Lifetime | When to Use |
|----------|-------------|
| `Transient` (default) | Most workflows — each execution gets a fresh instance |
| `Scoped` | When the workflow needs to share state with other scoped services in the same request |
| `Singleton` | Rarely appropriate — workflows typically have per-execution state |

## Remarks

- The assembly scanning uses reflection to find `IServiceTrain<,>` implementations. Ensure the assemblies containing your workflows are passed to the `assemblies` parameter.
- Workflows registered here are available both through `IWorkflowBus.RunAsync` and through the scheduler system.
- See [WorkflowBus]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %}) for the runtime dispatch API.

## Package

Part of `Trax.Mediator`.
