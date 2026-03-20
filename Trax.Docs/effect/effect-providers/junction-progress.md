---
layout: default
title: Junction Progress
parent: Effect Providers
grand_parent: Effect
nav_order: 5
---

# Junction Progress

The junction progress provider gives real-time visibility into which junction a train is currently executing and enables between-junction cancellation. It registers two junction-effect providers under a single call: **CancellationCheckProvider** and **JunctionProgressProvider**.

## Registration

```bash
dotnet add package Trax.Effect.JunctionProvider.Progress
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionProgress()
    )
);
```

## What It Registers

`AddJunctionProgress` registers two junction-effect providers that run in order on every junction:

### 1. CancellationCheckProvider

Runs **before** each junction executes. It queries the database for the train's `CancellationRequested` flag. If the flag is `true`, it throws an `OperationCanceledException` and the junction never starts.

The provider uses `IDataContextProviderFactory` to create a fresh `DbContext` for each check. This ensures it always reads the latest database state, even if the cancellation was requested by a different server or process.

After junction execution, this provider is a no-op.

### 2. JunctionProgressProvider

Runs **before** each junction to set two columns on the train's `Metadata`:

| Field | Value |
|-------|-------|
| `CurrentlyRunningJunction` | The name of the junction about to execute |
| `JunctionStartedAt` | `DateTime.UtcNow` at the moment the junction begins |

After each junction completes, it clears both columns back to `null`. The provider calls `EffectRunner.Update()` and `EffectRunner.SaveChanges(ct)` on both paths so the changes are persisted immediately.

As a safety net, `FinishTrain` always clears both junction progress columns regardless of outcome. This prevents stale values if a train crashes mid-junction.

## Execution Order

CancellationCheck runs **first**, before JunctionProgress sets the columns. This means:

1. CancellationCheckProvider queries the DB for `CancellationRequested`
2. If cancellation is requested, the junction never starts — no progress columns are written
3. If not cancelled, JunctionProgressProvider sets `CurrentlyRunningJunction` and `JunctionStartedAt`
4. The junction executes
5. JunctionProgressProvider clears both columns

## Requires EffectJunction

Both providers only fire on junctions that inherit from `EffectJunction<TIn, TOut>`. If your junctions use the base `Junction<TIn, TOut>`, neither provider has anything to hook into.

See [Junctions: EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction) for the difference between the two.

## Dual-Path Cancellation Architecture

Trax supports two cancellation paths that work together:

**Same-server (instant):** When the cancelling code runs on the same server as the train, `ICancellationRegistry` provides direct access to the train's `CancellationTokenSource`. Cancellation is immediate and can interrupt a running junction mid-execution.

**Cross-server (between-junction):** When the cancellation request comes from a different server — for example, an operator clicking "Cancel" on the dashboard while the train runs on a background worker — the request is written to the database as a `CancellationRequested` flag. The `CancellationCheckProvider` picks it up before the next junction starts.

The two paths are complementary. Same-server cancellation is faster but only works within a single process. The DB flag approach is slower (it only fires between junctions) but works across any number of servers.

## Dashboard Integration

When a train is `InProgress`, the dashboard detail page displays the current junction name and how long it has been running, drawn from the `CurrentlyRunningJunction` and `JunctionStartedAt` columns.

## Performance Considerations

Junction progress adds **2 database round-trips per junction** — one before (set `CurrentlyRunningJunction`) and one after (clear it). For a train with N junctions, that's 2N additional writes on top of the normal metadata saves.

These writes reuse the existing EF `DbContext` and Npgsql connection pool — they don't open new connections. The overhead is the round-trip latency, not connection creation.

For most trains (3-5 junctions), this is negligible. For high-frequency trains with many junctions (e.g., a 15-junction ETL running every 30 seconds), the extra writes add up. If you don't need real-time junction visibility or cross-server cancellation for a particular train, you can omit `AddJunctionProgress()` from that deployment's configuration.

## When to Use It

- **Production environments** — Where trains may need to be cancelled from the dashboard by operators.
- **Multi-server deployments** — Where the server requesting cancellation may not be the server executing the train.
- **Junction-level visibility** — When operators need to see which junction a long-running train is currently on.

## SDK Reference

> [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress)
