---
layout: default
title: JobDispatcher
parent: Administrative Trains
grand_parent: Scheduling
nav_order: 2
---

# JobDispatcherTrain

The JobDispatcher is the single gateway between the work queue and the background task server. It reads `Queued` entries, enforces both global and per-group `MaxActiveJobs` limits, creates Metadata records, and enqueues to Hangfire (or whatever `IBackgroundTaskServer` implementation is configured).

## Chain

```
LoadQueuedJobs → LoadDispatchCapacity → ApplyCapacityLimits → DispatchJobs
```

## Steps

### LoadQueuedJobsStep

Loads all `WorkQueue` entries with `Status = Queued`, filtering out entries whose `ManifestGroup` has `IsEnabled = false`. The results are ordered by three keys:

1. **ManifestGroup.Priority** (descending) — higher priority groups are dispatched first.
2. **WorkQueue.Priority** (descending) — within a group, higher priority entries come first.
3. **CreatedAt** (ascending) — FIFO tiebreaker within the same priority.

This replaces the previous dependent-first ordering with a fully configurable priority system.

### DispatchJobsStep

The core of the dispatcher. For each entry that passes the capacity checks in earlier steps, the dispatcher atomically claims and dispatches it within its own DI scope and database transaction.

**Atomic claim via `FOR UPDATE SKIP LOCKED`**: before dispatching an entry, the step re-selects it from the database with a row-level lock:

```sql
SELECT * FROM trax.work_queue
WHERE id = :entry_id AND status = 'queued'
FOR UPDATE SKIP LOCKED
```

If the entry has already been claimed by another server (locked in another transaction or already `Dispatched`), the query returns no rows and the entry is skipped. This prevents duplicate dispatch in multi-server deployments. See [Multi-Server Concurrency](../concurrency.md#jobdispatcher-row-level-locking) for details.

For each successfully claimed entry, the dispatcher:

1. **Deserializes the input**: uses `InputTypeName` to resolve the CLR type, then deserializes `Input` from JSON. Type resolution searches all loaded assemblies.

2. **Creates a Metadata record**: a new `Metadata` row with `TrainState = Pending`, linked to the manifest (if present). Saved immediately so it gets a database-generated ID.

3. **Updates the work queue entry**: sets `Status = Dispatched`, records the `MetadataId` and `DispatchedAt` timestamp.

4. **Commits the transaction**: the Metadata creation and WorkQueue status update are committed as a single atomic unit. This makes the Metadata visible to the task server before enqueue.

5. **Enqueues to the background task server**: calls `IBackgroundTaskServer.EnqueueAsync` with the metadata ID and deserialized input. This happens after commit because the `InMemoryTaskServer` executes the train synchronously and needs to read the committed Metadata.

Each entry is processed in its own DI scope with a fresh `IDataContext`. If any individual entry fails (type resolution, serialization, database error), its transaction is rolled back, the error is logged, and the loop continues to the next entry. One bad entry doesn't affect the rest of the queue.

## MaxActiveJobs Enforcement

Capacity is enforced at two independent levels: global and per-group.

### Global MaxActiveJobs

The global limit works the same as before. The count is based on `Metadata` rows in `Pending` or `InProgress` state, excluding administrative trains (and any types registered via `ExcludeFromMaxActiveJobs<T>()`).

The count happens once at the start of each dispatch cycle, not per-entry. If you have `MaxActiveJobs = 100` and 95 are active, the dispatcher will take up to 5 entries from the queue. The remaining entries stay `Queued` and get picked up on the next polling cycle.

Setting `MaxActiveJobs` to `null` disables the global check entirely—all queued entries are dispatched (subject to per-group limits).

```csharp
.AddScheduler(scheduler => scheduler
    .MaxActiveJobs(100)                              // limit to 100 concurrent jobs
    .ExcludeFromMaxActiveJobs<IMyInternalTrain>() // don't count these
)
```

*SDK Reference: [AddScheduler — MaxActiveJobs]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-scheduler.md %})*

### Per-Group MaxActiveJobs

Each `ManifestGroup` can have its own `MaxActiveJobs` limit, configured from the dashboard on the ManifestGroup detail page. A group's active count only includes jobs belonging to that group—limits are completely independent across groups.

Both limits are enforced simultaneously. In practice, a group can dispatch at most `min(group limit, remaining global capacity)` jobs in a given cycle. When a group hits its per-group cap, the dispatcher uses `continue` to skip that group's entries and keeps processing entries from other groups. This prevents a single busy group from starving lower-traffic groups that still have capacity.

### How Global and Per-Group Limits Interact

The global `MaxActiveJobs` is a hard ceiling on total concurrent jobs across all groups. Per-group limits are independent caps within that ceiling. When the sum of per-group limits exceeds the global limit, the global limit wins — not every group can run at full capacity simultaneously.

The dispatcher processes entries in priority order and applies two checks with different behaviors:

- **Global limit hit** → `break` — stops all further dispatching for this cycle.
- **Per-group limit hit** → `continue` — skips that group's entry but keeps processing other groups.

This means higher-priority groups consume global capacity first, but a group hitting its own cap doesn't waste the remaining global slots — they flow to lower-priority groups.

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
| 7 | B-3 | 0 + 6 > 5 ✗ | — | **Stopped** (global cap) |

**Result:** 5 jobs dispatched — Group A gets 3 (its per-group max), Group B gets 2 (limited by the remaining global capacity, not its own cap). Group B's remaining entry stays `Queued` and is picked up on the next polling cycle once a slot frees up.

**Key takeaway:** Per-group limits exceeding the global limit is a valid and useful configuration. It means each group *could* use up to its limit if other groups are idle, but when all groups are busy, the global limit determines the overall throughput and priority determines who gets slots first.

## Why a Separate Train

Before the JobDispatcher existed, the ManifestManager handled dispatch directly. `MaxActiveJobs` was checked in its `EnqueueJobsStep`. That worked fine when the ManifestManager was the only source of job execution.

But other sources exist: `TriggerAsync` for manual triggers, the dashboard for re-runs. Each of those had to independently create Metadata and enqueue to Hangfire, bypassing the ManifestManager's capacity check entirely.

The work queue + JobDispatcher pattern fixes this. All sources write to the same queue. The JobDispatcher is the only thing that reads from it. Capacity enforcement happens exactly once, in one place.
