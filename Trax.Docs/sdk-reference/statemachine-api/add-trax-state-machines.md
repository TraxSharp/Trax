---
layout: default
title: AddStateMachines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 1
---

# AddStateMachines

Registers state-machine persistence as a step in the `AddTrax` builder chain. One call discovers every
`Machine<TState, TTrigger>` in the given assemblies and wires the whole subsystem: the snapshot store, the
effect-claim ledger, the exactly-once runner, the machine registry, and the four generic `stateMachine`
mutation trains. It also does the two things the host used to wire by hand: it **auto-registers the
`SnapshotDbContext`** against the database provider you configured in `AddEffects`, and it **contributes the
mutation trains to the mediator scan** so Trax routes them by input type. You name neither the
`SnapshotDbContext` nor the mutations' assembly.

Call it after `AddEffects(...)` (it needs a data provider) and **before** `AddMediator(...)` (the mediator
builds its route registry when it runs, so the mutations must be contributed first).

## Signature

```csharp
public static TraxBuilderWithEffects AddStateMachines(
    this TraxBuilderWithEffects builder,
    params Assembly[] assemblies
)

public static TraxBuilderWithEffects AddStateMachines(
    this TraxBuilderWithEffects builder,
    Action<StateMachineOptions>? configure,
    params Assembly[] assemblies
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `assemblies` | `Assembly[]` | Yes | The assemblies to scan for `Machine<TState, TTrigger>` subclasses. Throws `InvalidOperationException` if none are found. |
| `configure` | `Action<StateMachineOptions>?` | No | Sets host-level options (see below). The `assemblies`-only overload passes `null`. |

Throws `InvalidOperationException` if no data provider was configured in `AddEffects` (the store needs a
database), or if `AddMediator` has already run (the mutations would arrive too late to be dispatchable).

## Options

`StateMachineOptions` carries host-level settings read when the registry builds a machine's draft service.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DraftTtl` | `TimeSpan?` | `null` | How long a draft survives without activity before the next load discards it and the user starts fresh (a sliding window on the row's last update). The stale row is deleted, so an abandoned or completed draft can't linger or block a new one. `null` never expires a draft. Recommended: 7 to 30 days for a form-style flow. |

```csharp
trax.AddStateMachines(
    o => o.DraftTtl = TimeSpan.FromDays(30),
    typeof(CheckoutMachine).Assembly);
```

## Returns

`TraxBuilderWithEffects`, so you can continue the chain into `AddMediator(...)`.

## What the host still supplies

Only the two things a machine genuinely can't know:

| Registration | Why |
|--------------|-----|
| `ISnapshotPrincipal` | Maps the current caller to the user key that scopes drafts. Bind it over your auth (for example Trax's `TraxCaller`). |
| Each effect implementation | Every effect a machine references with `RunsOnce<TEffect>` is resolved from the container when its transition is sent. |

The `SnapshotDbContext` and the mutation-routing assembly are no longer host concerns: `AddStateMachines`
registers the context against your `AddEffects` provider (via an `ITraxFeatureDbConfigurator` each
`UsePostgres`/`UseSqlite`/`UseInMemory` registers), and contributes the mutations to the mediator scan.

## Example

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(cs).AddJson())
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();
```

## The tables

`snapshot_draft` and `effect_claim` (both in the `trax` schema) ship as migrations in the core data
providers and apply automatically when you register one. `UsePostgres(...)` runs
`040_state_machine_snapshots.sql`; `UseSqlite(...)` runs `006_state_machine_snapshots.sql`. A host that
calls `AddStateMachines` gets the tables for free; one that does not just carries two empty tables. You
do not create or migrate them yourself, and there is no `EnsureCreated` step. On SQLite, `SnapshotDbContext`
strips the `trax` schema and maps the `jsonb` context column to `TEXT` so the stores match the unqualified
SQLite tables.

## Obsolete: `AddTraxStateMachines(IServiceCollection)`

The earlier `services.AddTraxStateMachines(...)` form is obsolete. It sits outside the builder, so it cannot
auto-register the `SnapshotDbContext` or contribute the mutations to the mediator scan: a host using it must
also call `AddDbContext<SnapshotDbContext>(...)` and add `StateMachineMutations.Assembly` to its
`AddMediator(...)` scan by hand. It still works for back-compat, but prefer the `trax.AddStateMachines(...)`
builder step.
