---
authors: [Theauxm]
areas: [data-model, platform]
status: accepted
---

# A disabled manifest holds its queued work, except a run someone asked for by name

Disabling a manifest pauses its schedule and the entries it already queued: the dispatch SQL
(`ISqlDialect.LoadGroupFairQueuedJobs` and `ISqlDialect.ClaimWorkQueueEntry`, both providers)
passes over an entry whose manifest is disabled. An entry marked `work_queue.is_explicit_trigger`
is the exception, because a trigger, a group trigger, a run-now or a dead-letter requeue is an
operator asking for that run while knowing the manifest is off. Before this, only the scheduler's
in-memory filter held a disabled manifest's entries, with a dead-letter requeue as its one
exception, and the group-fair load still spent per-group slots on entries it would then drop.

## Status

**Accepted.**

## Considered options

**Disabling cancels the manifest's queued entries.** Rejected: a retry queued with a backoff is the
manifest's own state, and cancelling it loses the fact that a run is owed once the manifest is
enabled again. Holding it keeps that, and re-enabling needs no repair.

**Leave queued work alone and document it.** Rejected as the security-first reading of an off
switch: an operator disabling a misbehaving job expects it to stop, and a retry due an hour later
running anyway is the surprise this removes.

**A per-origin enum instead of a flag.** A closed vocabulary would say which kind of explicit
request it was, but it is a Postgres enum under [0006](./0006-a-closed-vocabulary-is-a-postgres-enum.md),
and a value added later is unreadable to hosts that do not know it yet. Nothing reads the kind; the
dispatch rule needs one bit.

**Filter in the load only.** The load chooses candidates, and the claim is the last read before a
run is created. A manifest disabled between the two would still dispatch, so the claim applies the
same rule. It tests the manifest with `EXISTS` rather than a join, because `FOR UPDATE` locks a row
of every table in its `FROM` list and a join would make claims contend on the manifest row.

## Consequences

**A disabled manifest group still holds everything**, explicit or not. The flag lifts only the
manifest's own switch.

**The flag is set where the request is made.** `CreateWorkQueue.ExplicitTrigger` sets it, and
`WorkQueue.Create` always sets it for a dead-letter requeue. A trigger that finds the manifest's
scheduled entry already queued can set `WorkQueue.IsExplicitTrigger` on that entry to release it.
A scheduler that never sets it gets the pause for every trigger of a disabled manifest.

**Existing rows are migrated to keep their behaviour.** Migration
`052_scheduler_settings_and_manifest_scope.sql` (SQLite `016`) marks every queued dead-letter
requeue, the one kind of entry that dispatched for a disabled manifest before.

## Exemplars

- `PostgresDisabledManifestDispatchTests` and `SqliteDisabledManifestDispatchTests` run the load and
  the claim against rows EF wrote: a scheduled entry of a disabled manifest is neither loaded nor
  claimed, an explicit one is both, and a disabled group still holds an explicit one. The Postgres
  suite also checks that the claim neither locks nor waits for a manifest row another transaction
  holds.
- `WorkQueueExplicitTriggerTests` pins that a dead-letter requeue is always explicit.

Not covered: that the scheduler sets the flag for each kind of explicit request; its own tests own
that.

## Changelog

- **2026-09-30**: Recorded.
