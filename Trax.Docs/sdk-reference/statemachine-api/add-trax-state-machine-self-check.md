---
layout: default
title: AddTraxStateMachineSelfCheck
description: Reference for AddTraxStateMachineSelfCheck, a health check that replays each machine's committed differential corpus against the deployed engine.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 12
---

# AddTraxStateMachineSelfCheck

Registers the state-machine runtime self-check as an ASP.NET Core health check. On every check it replays each registered machine's committed differential corpus through the engine this build shipped, and reports `Unhealthy` with every diff when a machine no longer reproduces it. That is drift a build-time test cannot see, because it runs against the deployed binary.

## Signature

```csharp
namespace Trax.Effect.StateMachine.Persistence;

public static class StateMachineHealthCheckExtensions
{
    public static IHealthChecksBuilder AddTraxStateMachineSelfCheck(
        this IHealthChecksBuilder builder,
        string name = "state-machines"
    );
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `builder` | `IHealthChecksBuilder` | | The builder `AddHealthChecks()` returns |
| `name` | `string` | `"state-machines"` | The health check's registration name |

**Returns**: the `IHealthChecksBuilder`, for chaining. There is no `tags` parameter; to tag the check, register it under your own name and filter by name.

## Behavior

| Situation | Result |
|-----------|--------|
| Every machine reproduces its corpus | `Healthy`, "Every state machine reproduces its committed corpus." |
| Any machine drifts | `Unhealthy`, with one line per diff, prefixed by the machine |
| A machine ships no corpus (`IMachine.Corpus` is null) | Skipped, not failed |

- It resolves every `IMachine` that [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) registered, so it needs no configuration and picks up a new machine automatically.
- The replay runs synchronously on each call and ignores the cancellation token. Its cost grows with corpus size, so poll it at a modest interval.
- It calls `SnapshotSelfCheck.Run(machines)`, the same check a host can run itself, for example from an `IHostedService` at startup.

## Example

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddStateMachines(typeof(Program).Assembly)
    .AddMediator(typeof(Program).Assembly));

builder.Services.AddHealthChecks().AddTraxStateMachineSelfCheck();

var app = builder.Build();
app.MapHealthChecks("/health");
```

## Package

```
dotnet add package Trax.Effect.StateMachine.Persistence
```
