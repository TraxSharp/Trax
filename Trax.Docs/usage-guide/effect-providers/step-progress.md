---
layout: default
title: Step Progress
parent: Effect Providers
grand_parent: Usage Guide
nav_order: 5
---

# Step Progress

The step progress provider gives real-time visibility into which step a train is currently executing and enables between-step cancellation. It registers two step-effect providers under a single call: **CancellationCheckProvider** and **StepProgressProvider**.

## Registration

```bash
dotnet add package Trax.Effect.StepProvider.Progress
```

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddStepProgress()
);
```

*SDK Reference: [AddStepProgress]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-progress.md %})*

## What It Registers

`AddStepProgress` registers two step-effect providers that run in order on every step:

### 1. CancellationCheckProvider

Runs **before** each step executes. It queries the database for the train's `CancellationRequested` flag. If the flag is `true`, it throws an `OperationCanceledException` and the step never starts.

The provider uses `IDataContextProviderFactory` to create a fresh `DbContext` for each check. This ensures it always reads the latest database state, even if the cancellation was requested by a different server or process.

After step execution, this provider is a no-op.

### 2. StepProgressProvider

Runs **before** each step to set two columns on the train's `Metadata`:

| Field | Value |
|-------|-------|
| `CurrentlyRunningStep` | The name of the step about to execute |
| `StepStartedAt` | `DateTime.UtcNow` at the moment the step begins |

After each step completes, it clears both columns back to `null`. The provider calls `EffectRunner.Update()` and `EffectRunner.SaveChanges(ct)` on both paths so the changes are persisted immediately.

As a safety net, `FinishTrain` always clears both step progress columns regardless of outcome. This prevents stale values if a train crashes mid-step.

## Execution Order

CancellationCheck runs **first**, before StepProgress sets the columns. This means:

1. CancellationCheckProvider queries the DB for `CancellationRequested`
2. If cancellation is requested, the step never starts — no progress columns are written
3. If not cancelled, StepProgressProvider sets `CurrentlyRunningStep` and `StepStartedAt`
4. The step executes
5. StepProgressProvider clears both columns

## Requires EffectStep

Both providers only fire on steps that inherit from `EffectStep<TIn, TOut>`. If your steps use the base `Step<TIn, TOut>`, neither provider has anything to hook into.

See [Steps: EffectStep vs Step](../steps.md#effectstep-vs-step) for the difference between the two.

## Dual-Path Cancellation Architecture

Trax.Core supports two cancellation paths that work together:

**Same-server (instant):** When the cancelling code runs on the same server as the train, `ICancellationRegistry` provides direct access to the train's `CancellationTokenSource`. Cancellation is immediate and can interrupt a running step mid-execution.

**Cross-server (between-step):** When the cancellation request comes from a different server — for example, an operator clicking "Cancel" on the dashboard while the train runs on a background worker — the request is written to the database as a `CancellationRequested` flag. The `CancellationCheckProvider` picks it up before the next step starts.

The two paths are complementary. Same-server cancellation is faster but only works within a single process. The DB flag approach is slower (it only fires between steps) but works across any number of servers.

## Dashboard Integration

When a train is `InProgress`, the dashboard detail page displays the current step name and how long it has been running, drawn from the `CurrentlyRunningStep` and `StepStartedAt` columns.

## When to Use It

- **Production environments** — Where trains may need to be cancelled from the dashboard by operators.
- **Multi-server deployments** — Where the server requesting cancellation may not be the server executing the train.
- **Step-level visibility** — When operators need to see which step a long-running train is currently on.
