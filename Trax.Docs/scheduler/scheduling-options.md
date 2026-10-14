---
title: How the scheduler decides when a manifest runs
description: "How the ManifestManager decides a manifest is due: schedule types, what a restart rewrites, group controls, disabling, variance, misfires and timeouts."
type: explanation
area: scheduling
nav_order: 8
source:
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Utilities/SchedulingHelpers.cs
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Junctions/DetermineJobsToQueueJunction.cs
---

# How the scheduler decides when a manifest runs

Every ManifestManager cycle (5 seconds by default) loads the enabled manifests and asks of each
one: is it due, and is anything stopping it? A due manifest that nothing stops gets a work queue
entry; whether and when that entry runs is then the JobDispatcher's business. This page is about
the first question. The options it names are listed on
[Schedule](/docs/sdk-reference/scheduler-api/schedule),
[Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) and
[AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler); bulk scheduling and groups are set
up in [Schedule manifests in bulk](/docs/scheduler/bulk-scheduling).

## When each kind of manifest runs

| Declared with | First run | After that |
|---|---|---|
| `Schedule(..., Every.X(n))` | On the first ManifestManager poll after it is seeded | `n` after its last success (or cancel). A failed run does not move that point |
| `Schedule(..., Cron.X(...))` | At its first occurrence after it is seeded, in UTC, never at startup | The next occurrence after its last success |
| `ScheduleOnce(..., delay)` | Once `delay` has passed since the host started (every start moves a one-off that has not run yet) | Never: it disables itself after its first success |
| `ThenInclude` / `Include` | After its parent's next success | After each parent success since its own latest run started |
| `Include(..., o => o.Dormant())` | Only when its parent activates it through `IDormantDependentContext` | The same |
| No schedule (`ScheduleType.None`) | Only when triggered | Only when triggered |

Before the schedule is even read, a manifest is skipped if its group is disabled, it was
dead-lettered this cycle or has a dead letter awaiting intervention, it already has a queued
entry, or it has a run `Pending` or `InProgress`. So a manifest never overlaps itself, and a retry
is simply the next due run, delayed (see [How retries work](/docs/scheduler/how-retries-work#when-a-retry-runs)).
[Dependent Trains](/docs/scheduler/dependent-trains) and [Run a job once, later](/docs/scheduler/delayed-jobs)
cover the last rows in detail.

## What a Restart Rewrites

Every host start schedules its manifests again (an upsert by `ExternalId`). The schedule, input and train always come from code. The settings an operator can change at runtime are written only when the code states them:

| Setting | Stated in code | Not stated |
|---|---|---|
| Manifest `Enabled(...)` | Written at every start | New manifest: enabled. Existing: keeps its value, including a runtime disable |
| Manifest `MaxRetries(...)` | Written at every start | New manifest: `DefaultMaxRetries`. Existing: keeps its value, including a runtime change; a later change to `DefaultMaxRetries` does not reach it |
| Manifest `Timeout(...)` | Written at every start | New manifest: no timeout of its own (`DefaultJobTimeout` applies). Existing: keeps its value |
| Manifest `Priority(...)` | Written at every start | New manifest: 0. Existing: keeps its value |
| Manifest `FailureWindow(...)` | Written at every start | New manifest: none (`FailureCountWindow` applies). Existing: keeps its value |
| Manifest `ReplayDecisionsOnRetry(...)` | Written at every start, so `true` re-enables replay over a runtime opt-out | New manifest: replays. Existing: keeps its value, including a runtime change |
| Group `MaxActiveJobs(...)` | Written at every start | New group: no limit. Existing: keeps its value |
| Group `Priority(...)` | Written at every start | New group: the manifest's priority. Existing: keeps its value |
| Group `Enabled(...)` | Written at every start | New group: enabled. Existing: keeps its value |

So a manifest or group disabled from the dashboard as a kill switch stays disabled across deploys, crashes and scale-outs, unless the code says `.Enabled(true)`. To hand a setting back to code, state it; removing a stated value from code leaves the last value in place.

## Per-Group Dispatch Controls

Every manifest belongs to a manifest group, named after its `externalId` unless it names one.
A group has three settings, from code through `.Group(...)` or from the dashboard's Manifest Group
page: `MaxActiveJobs` (no limit by default), `Priority` (0 to 31, default 0) and `IsEnabled`.
Several manifests can share a group. A setting only one member states applies to the whole group,
and two members that state different values for it fail `AddScheduler` at startup, with an error
naming both, because each start would otherwise overwrite one with the other.

**MaxActiveJobs** limits concurrent active jobs within a single group. When a group hits its cap, the [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher) skips it and moves on to the next group, so other groups can still dispatch normally. This prevents a single high-throughput group from monopolizing all capacity. The global `MaxActiveJobs` (configured in code) still applies as an overall ceiling across all groups. Both limits are counted by each dispatching host on its own, so with N hosts dispatching the total can reach N times either one (see [Capacity Limit Approximation](/docs/scheduler/concurrency#capacity-limit-approximation)).

**Priority** determines the order in which groups are considered during dispatch. The JobDispatcher processes groups from highest priority (31) to lowest (0). If a high-priority group continually re-queues work, it is dispatched first, but because `MaxActiveJobs` caps how many jobs it can have active at once, lower-priority groups still get their fair share of capacity. This solves the starvation problem: priority controls *ordering*, while `MaxActiveJobs` controls *capacity*.

The manifest's own `.Priority(...)` orders work within a group: each scheduled run is queued at its manifest's priority, as a manual trigger or a dead-letter requeue is. When a manifest has a group of its own (no group name), or a name-based batch uses its own group, the manifest's stated priority is also the group's. In a shared group, a new group starts at the priority of the first manifest seeded into it, and after that changes only when a member states `.Group(..., g => g.Priority(...))`.

**IsEnabled** acts as a kill switch for an entire group. Disabling a group prevents its manifests from being queued or dispatched until re-enabled. This is useful during maintenance windows or when a downstream system is unavailable.

## Disabling a job

Disabling a manifest (`DisableAsync`, the dashboard, or `IsEnabled = false`) stops it until it is re-enabled:

- The ManifestManager does not queue it on its schedule, and a dependent is not queued after its parent succeeds.
- A dormant dependent is not activated by its parent. `IDormantDependentContext.ActivateAsync` logs a warning and skips it, as it does for a disabled manifest group.
- Work the manifest already has queued (a retry waiting out its backoff, say) is held by the dispatcher. It stays `Queued` and is dispatched once the manifest is re-enabled.

A run someone asked for by name is the exception, because the operator asked for exactly that run: an entry queued by `TriggerAsync`, `TriggerGroupAsync` or a dead-letter requeue runs whether or not the manifest is enabled, after its delay if it has one. `TriggerGroupAsync` triggers only the group's enabled members, but the entries it queues run even if a member is disabled afterwards. A trigger that finds the manifest's entry already waiting marks that entry instead of queueing another, which releases it the same way. The manifest stays disabled, so its schedule does not resume. A disabled manifest group still holds everything, explicit or not.

## Exclusion windows

An exclusion skips a manifest during a period (weekends, holidays, a nightly maintenance window).
An excluded period is skipped on purpose, not missed, so it is not a misfire; when it ends, the
misfire policy decides whether the manifest catches up. See [Exclusion Windows](/docs/scheduler/exclusions).

## Schedule Variance

Variance adds a random delay to a recurring schedule, so many manifests on the same interval or
cron expression do not all fire at once. After each successful run, the next run is delayed by a
random value between zero and the variance; a manifest never runs earlier than its base schedule.
It is set with `Schedule.WithVariance()` or `ScheduleOptions.Variance()`, and the schedule's value
wins when both are set.

### How It Works


1. A manifest completes successfully. `LastSuccessfulRun` is set to now
2. `ComputeNextScheduledRun` calculates: base next time + random `[0, variance]` seconds
   - **Interval**: base = `LastSuccessfulRun + IntervalSeconds`
   - **Cron**: base = next cron occurrence after `LastSuccessfulRun`
3. The result is persisted to `NextScheduledRun` in the database
4. On each polling cycle, `ShouldRunNow` checks `NextScheduledRun`, if it's in the past, the job fires; if in the future, it waits

`NextScheduledRun` also holds a new cron's first run: scheduling a cron that has never succeeded stores its first occurrence after the scheduling time, with no jitter, so variance applies from the second run. When `NextScheduledRun` is null (variance not configured, or an interval that has never run), the scheduler falls back to standard interval/cron evaluation, and an interval that has never run fires on the next poll.

A cancelled run later than the last success resets the variance for one run. The schedule is then evaluated from the cancelled run and the stored `NextScheduledRun` is ignored, because it was computed from the earlier success, so the next run is the next base time after the cancelled run with no jitter. The next success computes a jittered `NextScheduledRun` again.

### Constraints

- Variance applies only to `Interval` and `Cron` schedules. `ScheduleOptions.Variance()` on a dependent (`ThenInclude`, `Include`) or one-off (`ScheduleOnce`) manifest is ignored: nothing is stored and nothing throws.
- Variance must be non-negative. A negative value throws `InvalidOperationException` when the manifest is scheduled, which for the builder is at startup seeding.
- Variance can exceed the interval (e.g., 5-minute variance on a 1-minute interval), this is allowed but means runs may be delayed well past the next base interval.

### Interaction with Other Features

- **Exclusion windows**: Exclusions are checked after `NextScheduledRun` is due, if the current time falls in an exclusion window, the job is skipped even if `NextScheduledRun` is in the past.
- **Misfire policies**: Work the same as without variance. If a job with `DoNothing` policy misses its `NextScheduledRun` by more than the misfire threshold, it's skipped. `FireOnceNow` fires immediately regardless.

## Misfire Policies

A **misfire** occurs when a scheduled job was supposed to fire but couldn't, the scheduler was down, or the job was blocked by an active execution or dead letter. When the scheduler recovers, the misfire policy determines what happens.

### Policies

| Policy | Behavior |
|--------|----------|
| `FireOnceNow` | Fire once immediately if overdue. This is the default and preserves backward compatibility. |
| `DoNothing` | If overdue beyond the misfire threshold, skip and wait for the next natural occurrence. |

### Misfire Threshold

The misfire threshold is a grace period. If a job is overdue by less than the threshold, it fires normally regardless of policy. Only when the overdue period exceeds the threshold does the policy take effect.

- **Global default**: `DefaultMisfireThreshold` (default: 60 seconds), set via `AddScheduler`
- **Per-manifest override**: `MisfireThreshold(TimeSpan)` on `ScheduleOptions`, overrides the global default for a single manifest

### How DoNothing Works

For interval-based schedules, the scheduler finds the most recent interval boundary relative to `LastSuccessfulRun` and checks if the current time is within the misfire threshold of that boundary. If yes, fire. If no, wait for the next boundary.

**Example**: A job runs every 5 minutes with a 60-second threshold. The scheduler goes down at 10:00 and comes back at 13:02:

- Most recent 5-minute boundary from `LastSuccessfulRun` (10:00): **13:00**
- Time since boundary: 2 minutes (120 seconds) > 60-second threshold → **skip**
- Next boundary: **13:05**: the job fires then (if within threshold)

If the scheduler comes back at 13:00:30 instead:

- Most recent boundary: **13:00**
- Time since boundary: 30 seconds ≤ 60-second threshold → **fire**

For cron-based schedules, the scheduler uses the Cronos library to find the latest cron occurrence at or before now, in UTC, and checks if the current time is within the misfire threshold of that occurrence. The search takes the same few dozen evaluations however long the scheduler was down, so a secondly cron that missed two days, or a minutely cron that missed a year, fires at its next occurrence like any other. Both 5-field and 6-field (seconds) cron expressions are supported.

A cron that has never succeeded counts from its first occurrence after it was scheduled, not from when it was scheduled. A cron manifest written by an earlier Trax version that has never succeeded and has no stored `NextScheduledRun` is instead due on the next poll; scheduling it again, which the builder does for its own manifests at every start, records its first occurrence.

> **Seconds-granularity cron:** When using 6-field cron expressions with second-level precision, verify the `ManifestManagerPollingInterval` is set appropriately. The default 5-second polling interval means the scheduler checks for due manifests every 5 seconds. For "every 10 seconds" cron (`*/10 * * * * *`), the default polling is adequate. For "every second" cron (`* * * * * *`), reduce the polling interval accordingly.

Misfire policies apply only to cron and interval manifests. A dependent fires on its parent's
success, not on time, so it has no misfire.

## Timeout Enforcement

The ManifestManager also cancels runs that pass their timeout, a manifest's own `Timeout` or the
scheduler's `DefaultJobTimeout`. How a run's timeout is resolved, which runs are exempt, and how
the stale in-progress reaper relates to it are in
[Cancellation and timeouts](/docs/scheduler/cancellation-and-timeouts).

What matters here is the effect on the schedule. A cancelled run, whether it timed out or an
operator cancelled it, **consumes the occurrence it ran for**: the schedule is evaluated from the
later of the last successful run and the last cancelled one, so an hourly job whose run is
cancelled at 10:20 next runs at 11:20, not on the next cycle. A job that always exceeds its timeout
runs once per occurrence rather than back to back. A cancelled run is not retried and does not
count toward a dead letter. A `Once` manifest whose run was cancelled is not run again (trigger it
to), and a dependent whose run was cancelled waits for its parent's next success.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [TriggerAsync / DisableAsync / EnableAsync / CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management)
