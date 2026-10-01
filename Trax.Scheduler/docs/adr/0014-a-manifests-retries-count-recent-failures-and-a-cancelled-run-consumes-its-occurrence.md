---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A manifest's retries count recent failures, and a cancelled run consumes its occurrence

Three rules decide when a scheduled manifest runs again after a run that did not succeed.
`MaxRetries(n)` is the number of retries after the first run, so a manifest is dead-lettered
when its counted failures exceed `n`. A failure counts toward that and toward the retry backoff
only while it started within `FailureCountWindow` (24 hours by default) and after the manifest's
latest resolved dead letter. A cancelled run, whether it timed out or an operator cancelled it,
consumes the occurrence it ran for: the schedule is evaluated from the later of the last success
and the last cancelled run.

## Status

**Accepted.** Amended 2026-09-30 to record when a dependent runs again: at least once after its
parent's latest success, compared on the database's clock.

## Why this is written down

Each rule replaced one that looked reasonable and failed in production-shaped cases.
`FailedCount >= MaxRetries` dead-lettered a `MaxRetries(0)` manifest before it ever ran and gave
`MaxRetries(n)` only n - 1 retries. Counting every failure since the last resolution meant a
failure a month old delayed every later run by the backoff forever, and three failures spread
over three months dead-lettered a healthy manifest. And because nothing looked at a cancelled
run, a job that always outran its timeout was queued again on the next five-second cycle, for
ever, with no backoff and no dead letter.

## Considered options

**Count consecutive failures (reset on every success).** Recommended by the audit and rejected by
the user: a manifest that alternates failure and success would never dead-letter, and flapping
is exactly what an operator needs to see. A window counts the flapping and forgets the old.

**Keep `>=` and require `MaxRetries >= 1`.** Rejected: `MaxRetries(0)` is the natural way to
say "run once, do not retry", and "retries" in the name reads as retries after the first run.
The cost is a behaviour change: the default of 3 now allows four attempts, where it allowed
three.

**A dependent runs at least once after its parent's latest success.** A dependent is due when
its parent succeeded after the dependent's latest run started (successful or cancelled). Several
parent successes that land while one dependent run is going collapse into a single re-run: the
next run reads the parent's latest output, which is what the earlier successes produced too.
Decided by the user on 2026-09-30.

**Record the parent run each dependent run was queued for** (so every parent success earns its own
dependent run). The audit recommended it and the user did not choose it: it needs a new column in
Trax.Effect on the dependent's entry or run, a schema change, to deliver a run per parent success
that no user has asked for. A dependent re-reads its parent's latest state, so the extra runs
would read the same data again.

**Compare timestamps from each process's own clock.** Rejected, and fixed with the decision above:
the parent's success was stamped by the worker that ran it and the dependent's start by the
dispatcher, so a skew between those machines larger than the parent-to-dependent latency
re-queued the dependent after every run. Both are now read from the database's clock: the
parent's `LastSuccessfulRun` and the dependent entry's `DispatchedAt`. Comparing processes'
clocks would have needed every host in sync; the database is the one clock they all share.

**Count a timeout as a failure** (so backoff and dead-lettering apply) **and let an operator's
cancel run again.** Rejected: a cancel is deliberate, and restarting a runaway run seconds after
an operator cancelled it is the opposite of what they asked. Treating both kinds of cancel alike
also needs no new column: the cancelled run's own end time is the record.

## Consequences

**A dependent's baseline is its latest dispatch.** The dependent is compared by when its latest
run's work queue entry was dispatched, stamped by the database. A run with no dispatched entry
(run directly, or dispatched by the in-memory provider) falls back to its own start time, which
is the process's clock again; with an entry the dependent never needs it. Stamping from the
database costs one small query per dispatch and per scheduled success.

**Only a retry waits out the backoff.** The failures in the window decide how long a retry
waits, but a run is a retry only when the manifest's latest finished run failed. After a
success or a cancel the next occurrence runs on time, however many failures the window still
holds; they still count toward the dead letter, so flapping is still caught. Before this, three
failures in the morning delayed an hourly job by twenty minutes for the rest of the day.

**A manifest can carry its own window.** `FailureWindow` on `ScheduleOptions` and
`ManifestOptions` stores it in `manifest.failure_window_seconds`, and the failure count uses it
in place of `FailureCountWindow` for that manifest. Following scheduler/0011 it is written on a
seed only when the code states it, so removing it from the code leaves the stored window in
place.

**A window too short for the retry count is warned about, not refused.** Each retry waits out
its backoff first, so when the backoff for `DefaultMaxRetries` retries adds up to the window, the
oldest failure leaves it before the last one happens and a manifest that always fails is retried
for ever. The scheduler logs a warning at startup instead of refusing to build: the window and
the retry settings each change at runtime, and the combination is a trade-off rather than a value
the scheduler cannot run with. The reaper also dead-letters only a manifest with at least one
counted failure, so a negative `MaxRetries` reaching a row cannot dead-letter one that never
failed.

**A triggered run that is cancelled moves the schedule** the same way a triggered success
already did, because the anchor is the run's end time, not the occurrence it was queued for.

**A cancelled dependent run consumes only the parent success it started for.** A dependent is
anchored on when its latest run started, cancelled or successful, not when it ended: a parent
success that landed while the run was going was not seen by it, so it still earns a run.

## Exemplars

- `FailureCountWindowTests` covers a failure 30 days ago (no backoff), three failures over 90
  days (no dead letter), failures inside the window (backoff and dead letter), a configured
  window, a manifest's own window overriding it either way, and no backoff after a success.
- `DependentRunsAfterEachParentSuccessTests` (and its SQLite twin) covers a parent success during
  a dependent's successful or cancelled run, and a dispatcher whose clock runs behind or ahead
  of the database's; `DatabaseClockTests` pins that the stamp is read on the server.
- `MaxRetriesBoundaryTests` pins the boundary: dead-lettered on failure n + 1, never on n.
- `CancelledOccurrenceIsNotRerunTests` covers interval, cron, `Once` and dependent manifests
  whose run was cancelled, and that the following occurrence still runs.
- [Scheduling Options](/docs/scheduler/scheduling-options) and
  [ManifestManager](/docs/scheduler/admin-trains/manifest-manager) are the rules this produces.

Not covered: nothing checks that a new place counting failures (a dashboard statistic, a health
check) applies the same window, or that a new place deciding "due" consults the cancelled run.

## Changelog

- **2026-09-30**: Recorded.
- **2026-09-30**: A manifest's own `FailureWindow` overrides `FailureCountWindow`, now that
  Trax.Effect 1.57.4 has the column.
- **2026-09-30**: A window the retry backoff outlasts is warned about at startup; the reaper
  requires a counted failure before it dead-letters.
- **2026-09-30**: A cancelled dependent run is anchored on its start, not its end, so a parent
  success during the cancelled run is not lost.
- **2026-09-30**: Recorded the dependent semantics, decided by the user: at least once after the
  parent's latest success, not once per parent success, because recording the parent run needs a
  Trax.Effect schema change for behaviour nobody asked for. Both timestamps compared are now the
  database's clock.
- **2026-09-30**: The backoff applies only when the latest finished run failed, not whenever a
  failure is in the window.
