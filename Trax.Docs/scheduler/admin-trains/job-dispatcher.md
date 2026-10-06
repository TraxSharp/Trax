---
layout: default
title: JobDispatcher
description: The JobDispatcher train that moves queued work queue entries to the job submitter, enforcing global and per-group MaxActiveJobs and handling dispatch failures.
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 2
---

# JobDispatcherTrain

The JobDispatcher is the single gateway between the work queue and the job submitter. It reads `Queued` entries, enforces both global and per-group `MaxActiveJobs` limits, creates Metadata records, and enqueues to the configured `IJobSubmitter` implementation.

## Chain

```
LoadQueuedJobsJunction → LoadDispatchCapacityJunction → ApplyCapacityLimitsJunction → DispatchJobsJunction
```

## Junctions

### LoadQueuedJobsJunction

Loads `WorkQueue` entries with `Status = Queued`, filtering out:

- entries whose `ManifestGroup` has `IsEnabled = false`
- entries whose manifest has `IsEnabled = false`, unless the entry is an explicit trigger (`WorkQueue.IsExplicitTrigger`: queued by `TriggerAsync`, `TriggerGroupAsync` or a dead-letter requeue). The entry stays `Queued` and is dispatched once the manifest is re-enabled (see [Disabling a job](/docs/scheduler/scheduling-options#disabling-a-job))
- entries whose `ScheduledAt` is in the future
- unconfirmed entries (`ConfirmedAt` is null), staged by a train with [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable) and not yet promoted
- manual entries whose [subject key](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) already has a run in flight (a dispatched entry whose metadata is `Pending` or `InProgress`)

Both load paths apply these filters, the group-fair SQL and the load-all path used when `MaxQueuedJobsPerCycle` is `null`. The group-fair SQL filters on the group only, so the disabled-manifest filter is applied to what it returns. After loading, the batch keeps only the **first queued entry per subject key**, in dispatch order; the rest are loaded again on a later cycle. The claim would refuse all of these anyway, but a candidate the claim refuses still takes a capacity slot in the cycle that loaded it, so without the filtering a backlog for one subject, or a few stranded staged entries, could fill `MaxActiveJobs` while ready work waited.

#### Group-Fair Batching

When `MaxQueuedJobsPerCycle` is set (default: 100), the junction uses a window function (`ROW_NUMBER() OVER (PARTITION BY manifest_group_id)`) to load up to that many entries **per manifest group**. This guarantees every group with queued work is represented in the loaded batch, preventing a single high-priority group from monopolizing the batch and starving lower-priority groups.

Without group-fair batching, if a high-priority group floods the queue (e.g., 841 entries from a `ScheduleMany` call), a flat `ORDER BY priority LIMIT 100` would load only that group's entries. `ApplyCapacityLimitsJunction` would cap the group and skip the rest, but entries from other groups would never be loaded, even when they have available capacity.

Manual entries (no manifest) are always included regardless of the per-group limit.

Set `MaxQueuedJobsPerCycle` to `null` to disable the limit and load all queued entries without group-fair batching.

```csharp
.AddScheduler(scheduler => scheduler
    .MaxQueuedJobsPerCycle(200)  // load up to 200 entries per group per cycle
)
```

#### Ordering

After loading, entries are sorted by three keys:

1. **ManifestGroup.Priority** (descending), higher priority groups are dispatched first.
2. **WorkQueue.Priority** (descending), within a group, higher priority entries come first.
3. **CreatedAt** (ascending), FIFO tiebreaker within the same priority.

This ordering is applied in-memory after the database query returns, since EF Core wraps `FromSqlRaw` in a subquery when navigation properties are included and Postgres does not guarantee `ORDER BY` preservation through subqueries.

### DispatchJobsJunction

The core of the dispatcher. For each entry that passes the capacity checks in earlier junctions, the dispatcher atomically claims and dispatches it within its own DI scope and database transaction.

**Atomic claim via `FOR UPDATE SKIP LOCKED`**: before dispatching an entry, the junction re-selects it from the database with a row-level lock:

```sql
SELECT * FROM trax.work_queue w
WHERE w.id = :entry_id
  AND w.status = 'queued'
  AND w.confirmed_at IS NOT NULL
  AND (w.manifest_id IS NULL OR w.is_explicit_trigger OR EXISTS (
      -- the entry's manifest, enabled
  ))
  AND (w.subject_key IS NULL OR NOT EXISTS (
      -- a dispatched entry for the same subject whose run is Pending or InProgress
  ))
FOR UPDATE SKIP LOCKED
```

If the entry has already been claimed by another server (locked in another transaction or already `Dispatched`), is unconfirmed, belongs to a manifest disabled since it was loaded and is not an explicit trigger, or its subject has a run in flight, the query returns no rows and the entry is skipped. The manifest is tested with `EXISTS` rather than joined, so the claim never locks the manifest row. This prevents duplicate dispatch in multi-server deployments. See [Multi-Server Concurrency](/docs/scheduler/concurrency#jobdispatcher-row-level-locking) for details.

**Subject lock**: for an entry with a subject key, the claim transaction first takes a per-subject advisory lock on Postgres, `pg_advisory_xact_lock(hashtext('trax_subject'), hashtext(key))`. Row locking alone cannot serialize two entries for one subject, because they are different rows. The lock is released when the claim commits, before the job is submitted. SQLite relies on its single writer and takes no lock. See [Multi-Server Concurrency](/docs/scheduler/concurrency#subject-serialization-advisory-lock-per-subject).

For each successfully claimed entry, the dispatcher:

1. **Deserializes the input**: looks `InputTypeName` up among the input types of the registered trains, then deserializes `Input` from JSON into that type. The name is only compared, never loaded, so it must be the full name of a registered train's input (an assembly-qualified name also matches when its assembly is the one the input lives in). A type name that matches no train registered **on this host** may still be known to another one (a rolling deploy that adds a train, or hosts that scan different assemblies), so the entry is left for a host that does: inside the claim transaction the dispatcher counts a dispatch attempt, pushes `ScheduledAt` back by the dispatch backoff (5 s, doubling, up to 5 minutes, from the database's clock), logs a warning and records no run, so nothing counts toward the manifest's retries. After ten such claims, about 20 minutes (the default stale-pending timeout), no host is taken to know the type and the entry is settled as below.

JSON that no longer fits its type will not become readable on any later cycle or any other host, so that entry is settled at once rather than retried: in the same transaction the dispatcher records a `Failed` run carrying the reason and marks the entry `Dispatched` to that run. The failure counts toward the manifest's retries and dead letter like any other, and the next entry for the same subject can be dispatched. When the entry is a dead-letter retry, the failed run is linked to the dead letter as its retry run in the same transaction, as a dispatched retry is. Nothing is constructed from an unresolved name. `LocalWorkerService` resolves a background job's input type the same way, and fails a job whose type matches none. The `TrainName` stored in the work queue entry is the canonical interface name (e.g. `MyApp.Trains.IProcessOrderTrain`), set during scheduling or queue submission.

2. **Creates a Metadata record**: a new `Metadata` row with `TrainState = Pending`, linked to the manifest (if present), and carrying the entry's `ReplayDecisionsOf` when it has one. Before copying the link, the dispatcher checks the manifest's `ReplayDecisionsOnRetry` again and drops the link when the manifest no longer replays, so turning replay off reaches a retry that was already queued (see [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)). Saved immediately so it gets a database-generated ID.

3. **Updates the work queue entry**: sets `Status = Dispatched`, records the `MetadataId` and `DispatchedAt` timestamp.

4. **Commits the transaction**: the Metadata creation and WorkQueue status update are committed as a single atomic unit. This makes the Metadata visible to the job submitter before enqueue.

5. **Enqueues to the job submitter**: calls `IJobSubmitter.EnqueueAsync` with the metadata ID, deserialized input, and the work queue entry's **priority**. The priority flows from the WorkQueue entry to the `background_job` table, where `LocalWorkerService` dequeues by `priority DESC, created_at ASC`. This means high-priority jobs are executed before low-priority ones. This happens after commit because the `InMemoryJobSubmitter` executes the train synchronously and needs to read the committed Metadata.

Each entry is processed in its own DI scope with a fresh `IDataContext`. If any individual entry fails (a database error, for example), its transaction is rolled back, the error is logged, and the loop continues to the next entry. One bad entry doesn't affect the rest of the queue.

### Dispatch failures

When `EnqueueAsync` throws, the claim is already committed: the run's row is `Pending` and the entry `Dispatched`. The dispatcher then:

1. **Fails the run only if no runner started it.** A single conditional write moves the row to `Failed` only while it is still `Pending`. If it has left `Pending`, a runner has the job (a remote runner that ran it and answered with an error, or that is still running it when the HTTP timeout expires, or an in-memory submitter that ran it inline): the entry stays `Dispatched`, no dispatch attempt is counted, and the train is not run again. See [Delivery and execution](/docs/scheduler/remote-execution#delivery-and-execution).
2. **Requeues the entry if attempts remain.** It increments `DispatchAttempts`; below [`MaxDispatchAttempts`](/docs/sdk-reference/scheduler-api/add-scheduler) (default 5) it resets the entry to `Queued` and sets `ScheduledAt` to hold it back: 5 seconds after the first failure, doubling per attempt, up to 5 minutes, counted from the database's clock, the one `LoadQueuedJobsJunction` compares it with. The run of a requeued attempt records `FailureException = "DispatchRequeued"`, with the submitter's exception in `FailureReason`, and is **not** counted in the manifest's failed runs, so a short outage of a remote worker does not dead-letter the manifest.
3. **Leaves an exhausted entry `Dispatched`.** The attempt that reaches `MaxDispatchAttempts` records the submitter's exception as the run's failure, which counts once toward the manifest's retries.
4. **Publishes a terminal failure.** Once the `Failed` row is committed, a run failed for good (attempts exhausted, or `MaxDispatchAttempts` is 0) goes through the lifecycle hooks a train's own failure goes through: `OnFailed`, then `OnStateChanged`. The broadcaster and the GraphQL subscriptions (`onTrainFailed`, `onTrainStateChanged`) see it, so a client following the queued run by its `externalId` gets its outcome. A requeued attempt publishes nothing, because its entry runs again under the same `externalId`, and neither does a row a runner has already started, which the runner reports. An entry whose input cannot be read is settled the same way and publishes its failed run too. The event carries `failureException` `DispatchFailed` and a fixed `failureReason` ("The run could not be dispatched, so it never started. See the run's record for the cause."), not what the row records: the cause is the dispatcher's and the runner's (a remote worker's response body, a Lambda error, an input type's assembly-qualified name), and a lifecycle event reaches subscribers outside the operations view. The row keeps the full cause for `operations.executions`. The dispatcher waits for the hooks at most 30 seconds, whether or not a hook honours its token, so a hook that hangs does not hold up the entries behind it.

This handling runs on its own token, bounded at 30 seconds, not on the dispatcher's. A host that stops while a job is being submitted cancels the dispatcher's token, and the failed submit must still be recorded; otherwise the run would stay `Pending` and hold its subject until the stale-pending reaper found it.

## Parallel Dispatch

By default, `DispatchJobsJunction` processes entries sequentially, one `TryClaimAndDispatchAsync` at a time. This is optimal for local workers where `EnqueueAsync` is a fast database INSERT. But when using `UseRemoteWorkers()`, each dispatch is an HTTP POST that blocks until the remote endpoint finishes executing the train. Sequential dispatch in this scenario means cycle duration scales linearly with the number of entries.

`MaxConcurrentDispatch` controls how many entries are dispatched in parallel within a single polling cycle:

```csharp
.AddScheduler(scheduler => scheduler
    .MaxConcurrentDispatch(10)
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://my-workers.example.com/trax/execute",
        routing => routing.ForTrain<IHeavyComputeTrain>())
)
```

When `MaxConcurrentDispatch > 1`, the junction uses a `SemaphoreSlim` to bound concurrency and `Task.WhenAll` to dispatch entries in parallel. Each entry still gets its own DI scope and database transaction, the `FOR UPDATE SKIP LOCKED` pattern prevents two concurrent dispatches from claiming the same entry, even within the same cycle.

| `MaxConcurrentDispatch` | Behavior |
|-------------------------|----------|
| `1` (default) | Sequential `foreach` loop. Zero overhead, exact backward compatibility |
| `> 1` | Parallel dispatch bounded by `SemaphoreSlim`. entries are started in priority order but may complete in any order |

**Considerations:**
- Each concurrent dispatch opens its own database connection. Keep the value well below your connection pool size (default Npgsql pool: 100).
- Priority ordering within a cycle is best-effort when dispatching in parallel, entries are *started* in priority order, but complete in arbitrary order. This matches the existing behavior in multi-server deployments.
- Error handling is per-entry: if one dispatch fails (HTTP timeout, network error), the others continue. A failed dispatch is handled as in [Dispatch failures](#dispatch-failures), same as the sequential path.

## MaxActiveJobs Enforcement

Capacity is enforced at two independent levels: global and per-group.

### Global MaxActiveJobs

The global limit works the same as before. The count is based on `Metadata` rows in `Pending` or `InProgress` state, excluding administrative trains (and any types registered via `ExcludeFromMaxActiveJobs<T>()`). The capacity count query uses a `HashSet<string>` for excluded train names, which Npgsql translates to a single parameterized `<> ALL(@excluded)` array comparison instead of N separate `NOT LIKE` clauses. This allows PostgreSQL to use the covering partial index on active metadata efficiently.

The count happens once at the start of each dispatch cycle, not per-entry. If you have `MaxActiveJobs = 100` and 95 are active, the dispatcher will take up to 5 entries from the queue. The remaining entries stay `Queued` and get picked up on the next polling cycle.

Setting `MaxActiveJobs` to `null` disables the global check entirely, all queued entries are dispatched (subject to per-group limits).

```csharp
.AddScheduler(scheduler => scheduler
    .MaxActiveJobs(100)                              // limit to 100 concurrent jobs
    .ExcludeFromMaxActiveJobs<IMyInternalTrain>() // don't count these
)
```

### Per-Group MaxActiveJobs

Each `ManifestGroup` can have its own `MaxActiveJobs` limit, configured from the dashboard on the ManifestGroup detail page. A group's active count only includes jobs belonging to that group, limits are completely independent across groups.

Both limits are enforced simultaneously. In practice, a group can dispatch at most `min(group limit, remaining global capacity)` jobs in a given cycle. When a group hits its per-group cap, the dispatcher uses `continue` to skip that group's entries and keeps processing entries from other groups. This prevents a single busy group from starving lower-traffic groups that still have capacity.

### How Global and Per-Group Limits Interact

On one dispatching host, the global `MaxActiveJobs` caps total concurrent jobs across all groups, and per-group limits are independent caps within it. Neither is a hard ceiling across hosts: each dispatcher counts active jobs on its own, so with N hosts dispatching the total can reach N times either limit (see [Capacity Limit Approximation](/docs/scheduler/concurrency#capacity-limit-approximation)). When the sum of per-group limits exceeds the global limit, the global limit wins. Not every group can run at full capacity simultaneously.

The dispatcher processes entries in priority order and applies two checks with different behaviors:

- **Global limit hit** → `break`, stops all further dispatching for this cycle.
- **Per-group limit hit** → `continue`, skips that group's entry but keeps processing other groups.

This means higher-priority groups consume global capacity first, but a group hitting its own cap doesn't waste the remaining global slots, they flow to lower-priority groups.

#### Example

**Setup:**
- Global `MaxActiveJobs = 5`
- Group A: `MaxActiveJobs = 3`, `Priority = 20`
- Group B: `MaxActiveJobs = 3`, `Priority = 10`
- Currently 0 active jobs, 4 queued in each group

Because Group A has higher priority, its entries appear first in the sorted queue.

| # | Entry | Global Check | Group Check | Result |
|---|-------|-------------|-------------|--------|
| 1 | A-1 | 0 + 1 ≤ 5 ✓ | 0 + 1 ≤ 3 ✓ | **Dispatched** |
| 2 | A-2 | 0 + 2 ≤ 5 ✓ | 0 + 2 ≤ 3 ✓ | **Dispatched** |
| 3 | A-3 | 0 + 3 ≤ 5 ✓ | 0 + 3 ≤ 3 ✓ | **Dispatched** |
| 4 | A-4 | 0 + 4 ≤ 5 ✓ | 0 + 3 ≥ 3 ✗ | **Skipped** (group cap) |
| 5 | B-1 | 0 + 4 ≤ 5 ✓ | 0 + 1 ≤ 3 ✓ | **Dispatched** |
| 6 | B-2 | 0 + 5 ≤ 5 ✓ | 0 + 2 ≤ 3 ✓ | **Dispatched** |
| 7 | B-3 | 0 + 6 > 5 ✗ |. | **Stopped** (global cap) |

**Result:** 5 jobs dispatched. Group A gets 3 (its per-group max), Group B gets 2 (limited by the remaining global capacity, not its own cap). Group B's remaining entry stays `Queued` and is picked up on the next polling cycle once a slot frees up.

**Key takeaway:** Per-group limits exceeding the global limit is a valid and useful configuration. It means each group *could* use up to its limit if other groups are idle, but when all groups are busy, the global limit determines the overall throughput and priority determines who gets slots first.

## Why a Separate Train

Before the JobDispatcher existed, the ManifestManager handled dispatch directly. `MaxActiveJobs` was checked in its `EnqueueJobsJunction`. That worked fine when the ManifestManager was the only source of job execution.

But other sources exist: `TriggerAsync` for manual triggers, the dashboard for re-runs. Each of those had to independently create Metadata and enqueue to Hangfire, bypassing the ManifestManager's capacity check entirely.

The work queue + JobDispatcher pattern fixes this. All sources write to the same queue. The JobDispatcher is the only thing that reads from it. Capacity enforcement happens exactly once, in one place.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
