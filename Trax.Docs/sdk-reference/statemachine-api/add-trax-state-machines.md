---
layout: default
title: AddStateMachines
description: Reference for AddStateMachines, which discovers every Machine in the given assemblies and registers the snapshot store, effect claims and mutation trains.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 1
---

# AddStateMachines

Registers state-machine persistence as a step in the `AddTrax` builder chain. One call discovers every
`Machine<TState, TTrigger>` in the given assemblies and wires the whole subsystem: the snapshot store, the
effect-claim ledger, the exactly-once runner, the machine registry, and the four generic `stateMachine`
mutation trains. The stores reach their tables through the `IDataContext` the database provider you
configured in `AddEffects` registers (`IDataContext.SnapshotDrafts` and `IDataContext.EffectClaims`), so there
is no context of the subsystem's own to register, and it **contributes the mutation trains to the mediator
scan** so Trax routes them by input type. You name neither a context nor the mutations' assembly.

Call it after `AddEffects(...)` (it needs a data provider) and **before** `AddMediator(...)` (the mediator
builds its route registry when it runs, so the mutations must be contributed first). Called after `AddMediator(...)`, it
does not compile: the error is `Call AddStateMachines(...) before AddMediator(...).`

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
| `InvokeOutcomeSweepInterval` | `TimeSpan` | 5 seconds | How often this host's outcome reconciler sweeps for [invoked runs](/docs/statemachine/invoking-trains#how-an-outcome-comes-back) that have ended and applies each one's outcome. The run's own host applies an outcome as soon as it is recorded, and on Postgres a notification wakes every host's reconciler sooner, so this bounds only how long an outcome waits when both miss it: a crash after the run, a run the scheduler reaped or cancelled, a cancel before dispatch. It is also the sweep's pace whenever the notifications are not being heard. Must be positive. |
| `InvokeOutcomeSweepIntervalWhileListening` | `TimeSpan` | 1 minute | How often the reconciler sweeps while it hears Postgres's notification of invoked runs ending. Each notification delivers its run at once, and the reconciler sweeps in full each time it subscribes again after losing them, so this sweep only catches what both miss. Never shorter than `InvokeOutcomeSweepInterval`. Must be positive. |

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

The database wiring and the mutation-routing assembly are not host concerns: the stores use the
`IDataContext` and `ISqlDialect` your `UsePostgres`/`UseSqlite`/`UseInMemory` registers, and `AddStateMachines`
contributes the mutations to the mediator scan. `UseInMemory` registers no dialect, so there a concurrent create
of one draft throws instead of losing the race, and the in-memory provider cannot run the stores' atomic
updates at all: use it for wiring tests, not for the draft operations.

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
`040_state_machine_snapshots.sql`, `048_snapshot_draft_request_scope.sql`,
`051_snapshot_draft_machine_key.sql` and `053_effect_claim_content_fingerprint.sql`; `UseSqlite(...)` runs `006`,
`013`, `015` and `018` of the same names. A host
that calls `AddStateMachines` gets the tables for free; one that does not just carries two empty tables. You do
not create or migrate them yourself, and there is no `EnsureCreated` step. Their models are
`Trax.Effect.Models.SnapshotDraft.SnapshotDraft` and `Trax.Effect.Models.EffectClaim.EffectClaim`, mapped on the
core data context like every other Trax table, so on SQLite the data context strips the `trax` schema, maps the
`jsonb` context column to `TEXT`, and stores each timestamp as fixed-width UTC text that sorts in time order.

A draft is keyed by its user, its machine and its id, so two machines can each give one user a draft under
the same well-known id without touching each other's.
