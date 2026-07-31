---
layout: default
title: AddTraxStateMachines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxStateMachines

Discovers every `Machine<TState, TTrigger>` in the given assemblies and wires the whole subsystem in one
call: the snapshot store, the effect-claim ledger, the exactly-once runner, the machine registry, and the
four generic `stateMachine` mutation trains. No per-machine registration, and no effect wiring in the
composition root. It sits alongside the other optional subsystems (`AddTraxGraphQL`, `AddTraxDashboard`),
so it is a separate call rather than a step in the `AddTrax` builder chain.

## Signature

```csharp
public static IServiceCollection AddTraxStateMachines(
    this IServiceCollection services,
    params Assembly[] assemblies
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `assemblies` | `Assembly[]` | Yes | The assemblies to scan for `Machine<TState, TTrigger>` subclasses. Throws `InvalidOperationException` if none are found. |

## Returns

`IServiceCollection`, for continued chaining.

## What the host still supplies

Two things a machine can't know, plus the mediator scan that lets Trax route the mutations:

| Registration | Why |
|--------------|-----|
| `AddMediator(..., StateMachineMutations.Assembly)` | The four mutation trains ship in the persistence package, not your assembly. Trax routes a train by its input type through an assembly-scanned registry, so that assembly must be in the mediator scan. |
| `ISnapshotPrincipal` | Maps the current caller to the user key that scopes drafts. Bind it over your auth (for example Trax's `TraxCaller`). |
| Each effect implementation | Every effect a machine references with `RunsOnce<TEffect>` is resolved from the container when its transition is sent. |
| `SnapshotDbContext` | The EF context the stores query. Register it against the same database; the tables themselves are created by the migration set (see below), so this call does not create them. |

## Example

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(cs).AddJson())
        .AddMediator(typeof(CheckoutMachine).Assembly, StateMachineMutations.Assembly));

builder.Services.AddTraxStateMachines(typeof(CheckoutMachine).Assembly);

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();
builder.Services.AddDbContext<SnapshotDbContext>(o => o.UseNpgsql(cs));
```

## The tables

`snapshot_draft` and `effect_claim` (both in the `trax` schema) ship as migrations in the core data
providers and apply automatically when you register one. `UsePostgres(...)` runs
`040_state_machine_snapshots.sql`; `UseSqlite(...)` runs `006_state_machine_snapshots.sql`. A host that
calls `AddTraxStateMachines` gets the tables for free; one that does not just carries two empty tables. You
do not create or migrate them yourself, and there is no `EnsureCreated` step. On SQLite, `SnapshotDbContext`
strips the `trax` schema and maps the `jsonb` context column to `TEXT` so the stores match the unqualified
SQLite tables.
