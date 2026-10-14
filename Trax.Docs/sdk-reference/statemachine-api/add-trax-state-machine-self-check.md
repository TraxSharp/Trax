---
title: AddTraxStateMachineSelfCheck
description: Reference for AddTraxStateMachineSelfCheck, a health check that replays each machine's committed differential corpus against the deployed engine.
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 12
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachineSelfCheckHealthCheck.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotSelfCheck.cs
---

# AddTraxStateMachineSelfCheck

Registers the state-machine runtime self-check as an ASP.NET Core health check. On every check it replays each
registered machine's committed differential corpus through the engine this build shipped, and reports `Unhealthy`
with every diff when a machine no longer reproduces it.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine.Persistence;

public static class StateMachineHealthCheckExtensions
{
    public static IHealthChecksBuilder AddTraxStateMachineSelfCheck(
        this IHealthChecksBuilder builder,
        string name = "state-machines"
    );
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `builder` | `IHealthChecksBuilder` | | The builder `AddHealthChecks()` returns |
| `name` | `string` | `"state-machines"` | The health check's registration name |

## Returns

The `IHealthChecksBuilder`, for chaining. There is no `tags` parameter; to tag the check, register it under a name
of your own and filter by name.

## Behavior

| Situation | Result |
|-----------|--------|
| Every machine reproduces its corpus | `Healthy`, "Every state machine reproduces its committed corpus." |
| Any machine drifts | `Unhealthy`, with one line per diff, prefixed by the machine |
| A machine ships no corpus (`IMachine.Corpus` is null) | Skipped, not failed |

- It resolves every `IMachine` that [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
  registered, so it needs no configuration and picks up a new machine without one.
- The replay runs synchronously on each call and ignores the cancellation token. Its cost grows with corpus size.
- It calls `SnapshotSelfCheck.Run(machines)`, which a host can also call itself, for example from an
  `IHostedService` at startup. Registering the health check does not gate startup.

## Example

```csharp compile
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddHealthChecks().AddTraxStateMachineSelfCheck();

var host = builder.Build();
host.MapHealthChecks("/health");
```

## See also

- [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity), the self-check beside the schema-hash handshake and divergence detection.
- [MachineConformance](/docs/sdk-reference/statemachine-api/machine-conformance), the same checks run in a test.
