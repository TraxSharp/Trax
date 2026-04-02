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

Limits are resolved in priority order:

1. **Builder override**: `ConcurrentRunLimit<TTrain>(int)` on the mediator builder
2. **Attribute**: `[TraxConcurrencyLimit(int)]` on the train class
3. **Global default**: `GlobalConcurrentRunLimit(int)` on the mediator builder

When both a per-train limit and a global limit are configured, a request must acquire both. The per-train limit prevents slamming one specific backend; the global limit caps total concurrent executions across all trains.

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
