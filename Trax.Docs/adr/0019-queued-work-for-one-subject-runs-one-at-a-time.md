---
authors: [Theauxm]
repos: [effect, mediator, scheduler, api, dashboard]
areas: [platform, data-model]
status: accepted
---

# Queued work for one subject runs one at a time

A train can name the subject its queued work touches with `QueueSubjectKey`. Dispatch does not
claim an entry while another entry for the same subject has a run in flight, which removes the
lost update two concurrent writes to one record would otherwise produce. The check lives in the
claim, behind a per-subject advisory lock, because "in flight" is a fact about another table.

## Status

**Accepted.**

## Considered options

**A unique partial index.** It cannot express the rule. A subject is busy while a dispatched
entry's run has not reached a terminal state, which is a join to `trax.metadata`, not a property
of the work queue row.

**Row locking alone.** Two entries for one subject are two rows, so `FOR UPDATE SKIP LOCKED` does
not make them contend, and while both are still queued neither sees a dispatched sibling to
refuse itself. The claim therefore takes `pg_advisory_xact_lock` in its two-key form under a
fixed class key, which Postgres keeps apart from the single-key space the leader lock and a
consumer's own locks use.

**Strict enqueue order within a subject.** Rejected in favour of dispatch order. Dispatch drops
entries whose `scheduled_at` is still in the future and entries of a disabled manifest group, then
orders the rest by manifest group priority, then entry priority, then age. An entry queued through
the mediator has no manifest, so its group priority is zero; within one subject that leaves
priority, then age. Priority should still mean something, and strict order across several
dispatchers would need a per-subject queue.

## Consequences

The key is an opaque string compared exactly across every train, so two trains returning the same
key serialize against each other. It is computed from the input the caller supplied, and the queue
priority is a caller-supplied argument, so a caller authorized to queue a keyed train picks which
subject their entry serializes against and where it sits in that subject's order.
`[TraxAuthorize]` gates the train, not the record, and keys are one global space rather than one per
train, so a consumer for whom that matters authorizes the record in `OnQueue` or scopes the key per
tenant. `authorization.md` and `core/trains-and-junctions.md` say so where a consumer will meet it. It is refused when empty or whitespace-only, or longer than 512 characters, both at
enqueue and by `WorkQueue.Create`, so an entry built directly cannot carry a key the index cannot
claim. A whitespace-only key is an unset identity as surely as an empty one, and would serialize
every entry carrying it against every other. The limit counts Unicode characters, not UTF-16
units: a caller counting what they wrote would otherwise see a key of 300 emoji refused as 600.
A character is at most four bytes in UTF-8, so the longest key is 2048 bytes, inside the
2704-byte Postgres btree entry limit that the subject index is bound by. Counting characters
rather than units raised that worst case from 1536 bytes, so the limit has little room left to
grow. A key holding an unpaired surrogate is refused by `WorkQueue.Create`: it is half a
character, UTF-8 cannot encode it, and the insert would otherwise fail far from the caller. The
enqueue passes the key to `WorkQueue.Create` rather than assigning it afterwards, so `Create`'s checks
apply to every entry the Mediator writes. The
guarantee holds only until something writes a terminal state for a run that has not finished,
and the subject is released even if that run is still working. Three things do: the two reapers
in the ManifestManager (pending longer than `StalePendingTimeout`, or in progress longer than
`StaleInProgressTimeout`), and the scheduler's startup recovery (`RecoverStuckJobsOnStartup`, on
by default), which on any scheduler host's start fails every `InProgress` run in the shared
database that began before it, whichever host was running it. A train excluded with
`ExcludeFromMaxActiveJobs<T>()` is skipped by both reapers, so a run of it whose worker died holds
its subject until a scheduler host restarts, or indefinitely if it never left `Pending`, which the
startup recovery does not touch. Where the ManifestManager is disabled on every host no reaper
runs at all. A synchronous run through the mediator does not
consult the key, and neither does a dormant dependent a parent train activates: its entry is built
by the scheduler with input the parent chose at runtime, and carries no subject. Nor does the
dashboard's Run dialog: it hands its input straight to the job submitter and writes no work queue
entry, so dispatch never sees the run, and it can overlap queued or in-flight work for the same
subject. That bypass is deliberate. Queue is the dashboard's serialized path and sits beside Run,
so making Run wait for a busy subject would only duplicate it, and refusing when the subject is
busy would need the dialog to compute the key and check for in-flight work outside the claim's
lock, which is the race this decision exists to close. The dialog says so instead, and points at
Queue. Nothing published tells the dashboard whether a train overrides `QueueSubjectKey`, so it
says so for every train. The API exposes
the key as `subjectKey` on work queue reads.

Every dispatcher must be upgraded before any train overrides `QueueSubjectKey`. A dispatcher from
before this decision claims without the subject check or lock, so during a rolling deploy it can
run a second entry for a subject that already has one in flight, and the guarantee does not hold
until the last old dispatcher is gone. Dispatch drops busy subjects and
duplicate siblings from its candidates, so entries the claim would refuse do not use up
`MaxActiveJobs`.

**The lock is a dialect member, and only Postgres implements it.** `ISqlDialect.LockSubject()`
defaults to a no-op, which is right for SQLite's single writer and keeps third-party dialects
compiling. A third-party dialect for a database with several concurrent writers that does not
override it loses the serialization silently: two dispatchers claiming entries for one subject at
the same moment each find no dispatched sibling, for the reason row locking alone was rejected
above, and both proceed.

## Exemplars

**Enforced elsewhere:** `SubjectKeySerializationTests` in Trax.Scheduler (one run per subject,
release on completion and on reaping, the two-transaction claim race, and capacity on both load
paths), `SubjectKeyTests` in Trax.Mediator (where the key comes from, and the empty and length
limits), `SubjectKeyGoesThroughCreateTests` in Trax.Mediator (the key reaches `Create`),
`WorkQueueCreateTests` and `SubjectKeyStorageTests` in Trax.Effect (the same limits on an entry
built directly, and a key of 512 four-byte characters fitting the subject index), and
`RunTrainDialogSubjectWarningTests` in Trax.Dashboard (the Run dialog warns that it bypasses the
serialization and points at Queue).

Not covered: ordering with more than one dispatcher, and a reaper or the startup recovery
releasing a subject whose run is still working, which is the documented limit rather than
something a test can rule out. Nothing checks that a dialect for a multi-writer database
overrides `LockSubject()`.

## Changelog

- **2026-09-27**: The enqueue passes the key through `WorkQueue.Create` and refuses an unpaired
  surrogate; `SubjectKeyGoesThroughCreateTests` in Trax.Mediator keeps the key out of later assignment.
- **2026-09-27**: The enqueue refuses a key that is only whitespace, as it refuses an empty one.
- **2026-09-27**: Recorded that a whitespace-only key is refused like an empty one, and that the
  512 limit counts Unicode characters rather than UTF-16 units. `WorkQueue.Create` in Trax.Effect
  applies both first, and also refuses a key holding an unpaired surrogate; the enqueue counting
  Unicode characters follows in Trax.Mediator once that ships.
- **2026-09-27**: Recorded the dashboard's Run dialog as a deliberate bypass, and why it warns
  rather than waiting or refusing.
- **2026-09-24**: Recorded that the key and the priority both come from the caller, and that
  authorizing the record in `OnQueue` or scoping keys per tenant is what a consumer does about it.
- **2026-09-24**: Recorded that `WorkQueue.Create` refuses the same keys the enqueue does.
- **2026-09-23**: Corrected dispatch order (group priority, then priority, then age, after
  dropping future-scheduled entries and disabled groups), recorded that the scheduler's startup
  recovery also releases subjects and that trains excluded from `MaxActiveJobs` escape both
  reapers, and recorded that `ISqlDialect.LockSubject()` defaults to a no-op.
- **2026-09-23**: Recorded.
