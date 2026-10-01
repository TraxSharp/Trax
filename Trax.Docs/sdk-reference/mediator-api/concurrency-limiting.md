---
layout: default
title: Concurrency Limiting
parent: Mediator API
grand_parent: SDK Reference
nav_order: 5
---

# Concurrency Limiting

When trains execute against remote backends with limited capacity (e.g. AWS Lambda with reserved concurrency), high request volume can cause throttling (HTTP 429), retries, and tail latency. Concurrency limiting prevents this by holding excess requests in-process until a slot opens, matching the concurrency to the actual backend capacity.

Concurrency limits apply only to **RUN** executions (via `ITrainExecutionService.RunAsync`). Queue operations are not affected because queueing is a lightweight database write, and the scheduler already has its own `MaxActiveJobs` and `MaxConcurrentDispatch` controls.

## Configuration

There are three kinds of limit, and each applies on its own:

1. **Per-train**: `ConcurrentRunLimit<TTrain>(int)` on the mediator builder, or `[TraxConcurrencyLimit(int)]` on the train class. When both are set the builder override wins.
2. **Per-principal**: `PerPrincipalMaxConcurrentRun(int)` on the mediator builder, one budget per caller.
3. **Global**: `GlobalConcurrentRunLimit(int)` on the mediator builder, one budget across all trains.

None is a fallback for another: a train with no per-train limit is still held by the global and per-principal ones, and a run must acquire a permit from every limit that applies to it. Acquisition order is deterministic (per-train → per-principal → global) so cross-lock deadlocks are impossible.

### Per-Principal Limiting

`PerPrincipalMaxConcurrentRun(int)` caps the number of concurrent `RunAsync` executions for any single authenticated principal (bucketed by the `trax:principal-id` claim). Use this to keep a single authenticated caller from saturating the global or per-train budget via request fan-out. Anonymous callers, scheduler runs, and remote-worker executions do not count against the cap.

A principal's semaphore exists only while one of its runs holds or waits for a slot, and the last run to finish removes it, so the limiter's memory tracks the principals running now rather than every principal the process has served.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connStr))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .PerPrincipalMaxConcurrentRun(10)));
```

The cap needs a principal provider that knows the caller. The mediator registers one that returns no principal, so on its own the cap limits nothing. `AddTraxApi` (which `AddTraxGraphQL` calls) replaces it with one that reads the `trax:principal-id` claim of the current request's authenticated user; call it after `AddTrax`, or the mediator's registration wins. A host without it, such as a scheduler-only worker, can configure the cap but it has no effect, unless it replaces that registration with its own `ICurrentPrincipalProvider`.

### Attribute

Place `[TraxConcurrencyLimit]` on the concrete train class:

```csharp
[TraxConcurrencyLimit(15)]
[TraxMutation]
public class ResolveCombatTrain : ServiceTrain<CombatInput, CombatResult>, IResolveCombatTrain
{
    // At most 15 concurrent RUN executions
}
```

### Builder

Override per-train limits or set a global default in `AddMediator`:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(config))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .GlobalConcurrentRunLimit(50)
        .ConcurrentRunLimit<IResolveCombatTrain>(15)
        .ConcurrentRunLimit<ITransferGoldTrain>(10)
    )
);
```

| Method | Description |
|--------|-------------|
| `GlobalConcurrentRunLimit(int)` | Maximum concurrent RUN executions across **all** trains. Default: no limit. |
| `ConcurrentRunLimit<TTrain>(int)` | Maximum concurrent RUN executions for a specific train. Overrides `[TraxConcurrencyLimit]`. |

## Behavior

- **Waiting, not rejecting**: When the limit is reached, excess requests block (async) until a slot opens. Every request eventually gets a response.
- **CancellationToken**: If a request is cancelled while waiting for a slot, `OperationCanceledException` is thrown immediately. No slot is consumed.
- **Auth and deserialization first**: Authorization and input deserialization happen before acquiring a concurrency slot. There is no point holding a slot while validating.
- **Per-train independence**: Each train has its own semaphore. A limit on one train does not affect others (unless both share the global limit).

## When to use

| Scenario | Recommendation |
|----------|---------------|
| Lambda with 15 reserved concurrency | `[TraxConcurrencyLimit(15)]` or `ConcurrentRunLimit<TTrain>(15)` |
| Shared API with overall capacity budget | `GlobalConcurrentRunLimit(50)` |
| Mix of local and remote trains | Only annotate remote trains. Local trains have no external bottleneck |
| Queue-only trains | Not needed. The scheduler handles dispatch pacing via `MaxActiveJobs` and `MaxConcurrentDispatch` |

## Interaction with HTTP retry

Concurrency limiting and [HTTP retry](/docs/scheduler/remote-execution#http-retry) are complementary:

- **Concurrency limiting** prevents oversubscription proactively, so fewer requests hit the backend simultaneously
- **HTTP retry** handles transient failures reactively, catching 429/502/503 that slip through

With both in place, the concurrency limit prevents most throttling, and the retry layer handles edge cases (e.g. cold starts, brief capacity fluctuations).
