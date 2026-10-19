---
authors: [Theauxm]
areas: [data-model, platform]
status: accepted
---

# A machine instance's history is written by the store, with its snapshot, and goes with the instance

Every write of a state-machine instance's snapshot through `EfSnapshotStore` adds one row to
`trax.snapshot_transition`, in the same transaction as the write: the revision the write gave the instance, what wrote
it (a start, a command, a save, a timer, an outcome), the command's name, who made it (the owner, host code, or the
machine itself), the states it left and entered, when, and the token of the run or child it started. A write that
leaves the snapshot alone (a pending effect's mark, a run's token, keeping a draft active) adds none. The row holds no
context and no owner key. It belongs to the instance's row (`row_id`, one incarnation) and is deleted with it by an
`AFTER DELETE` trigger on `snapshot_draft`, so a discarded, expired or deleted instance takes its history with it.

## Status

**Accepted.**

## Why this is written down

Because two things about it look wrong until the reasons are known. The history is written by the application, not
by a database trigger, although the revision it carries is bumped by one (`076`) precisely so that every writer is
covered. And an instance's history disappears with the instance, which is the moment an operator might most want it.

## Considered options

**A database trigger on `snapshot_draft`.** Rejected. A trigger sees the old and new state and the revision, but not
the command, the kind or the actor, which are the point of the history. Passing them would mean columns on
`snapshot_draft` every writer sets and the trigger reads and clears, and SQLite cannot clear a column in the row it is
triggered by without a second `UPDATE`, which fires the revision trigger again. The InMemory provider has no triggers,
so the store would write the rows there anyway, and the two would drift. A writer that goes around the store (raw SQL,
a custom `ISnapshotStore`) writes no history; revisions can skip, and a reader is told so.

**One statement on Postgres, a `WITH ... UPDATE ... RETURNING` feeding an `INSERT`.** Rejected: it is SQL of the
feature's own, which central `0036` rules out, it has no SQLite form, and the transaction already gives the same
guarantee. The store reads the row's state and revision first and guards the update by that revision as well, so the
history row names exactly the write that landed; every snapshot write was guarded already (by a concurrency token, a
revision or a pending effect's claimant), and each of those moves with the revision, so no write that would have
landed is refused.

**A row only when the state changes.** Rejected: a command that keeps the state (a self-loop, a save) is still
something an operator asks about, and one row per revision is what lets a reader check nothing is missing.

**Keep the history after the instance is deleted, pruned by age.** Rejected for now: it needs its own retention, and
a deleted user's draft would leave rows nobody can reach through the instance. The history is an operator's view of
a live instance. A discard is therefore not a kind; it would be the one row that is deleted as it is written.

**A foreign key with `ON DELETE CASCADE` for the delete.** Rejected: it would depend on `pk_snapshot_draft`, and 051,
which every script from 046 on must survive running again over the finished schema (effect/0014), drops and rebuilds
that key. The trigger is statement-level on Postgres, so a bulk delete removes every history it ends in one more
statement. Nothing in the database stops a history row naming a row that does not exist; only the store writes
them, after the row's own write in the same transaction. On InMemory, which runs no trigger, the store removes the
history itself.

## Consequences

**A snapshot write costs a read and an insert more,** and runs in a transaction when the caller holds none. The
history insert is a save through the data context, so a change host code tracked on the same context and has not
saved is saved with it, as the store's inserts and upserts already did.

**Saves are history too.** A client that autosaves often grows its draft's history with every save; the history is
bounded by the draft's lifetime, which the expiry sweep enforces.

## Exemplars

- `TransitionHistoryTests` makes one write at each write site (an autosave's insert and update, a start, a system
  start, an advance, entering and leaving an invoking state, a send, a system advance, an outcome by the hook and by
  the reconciler, a stranded outcome, a timer, a spawn, the public store's writes) and requires exactly one new row
  at the instance's new revision, with its kind, command, actor and states; a token write, a refusal, a stale write and
  a lost race add none; a discard, the expiry sweep and a raw delete remove the history, on Postgres and SQLite.
  `TransitionHistoryInMemoryTests` covers the InMemory provider.

Not covered: nothing stops a new write path that bypasses `EfSnapshotStore`'s write methods from writing a snapshot
without its history row; a reviewer has to catch that.

## Changelog

- **2026-10-19**: Recorded.
