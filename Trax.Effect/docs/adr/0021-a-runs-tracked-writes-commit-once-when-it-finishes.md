---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# A run's tracked writes commit once, when it finishes

Everything a run tracks through its effect runner (its metadata row, and any model passed to `Track`) is saved
when the run starts and when it finishes, and at no point in between. A junction effect that needs to write while
the run is going writes only its own columns, through a database context of its own, never through the run's effect
runner. Junction progress is the one such effect today: it writes `CurrentlyRunningJunction` and `JunctionStartedAt`
with a bulk update of those two columns.

## Status

**Accepted.** There is no exception. A checkpoint (central 0047) was planned as one, a declared point where a run's
tracked writes commit so a retry can resume after it; it writes its own row through a context of its own instead,
and refuses to be taken over uncommitted work.

## Why this is written down

Because junction progress did the opposite until this decision, and nothing in the code said it was wrong. It saved
through the run's effect runner before and after every junction, which committed everything else the run had
tracked so far and saved every other effect provider each time. A run that failed at its third junction left the
first two junctions' tracked writes committed, and a three-junction run saved the JSON effect eight times instead of
twice.

It matters more now that a run can execute branches in parallel: every branch's progress write going through the
one effect runner would have been concurrent saves on one database context.

## Considered options

**Keep saving through the runner and document it.** Rejected: a commit point nobody declared is a partial write
nobody asked for, and the cost grows with every junction.

**Stop writing progress mid-run.** Rejected: the dashboard and the API show which junction a run is executing, and
that needs a write while it executes.

## Exemplars

- `JunctionProgressWritesOnlyProgressTests` runs a three-junction train with progress on and off and requires the
  same number of saves of another effect provider, and reads the progress column from the store while the junction
  runs.

Not covered: nothing stops a new junction effect from saving through the runner; a reviewer has to catch that.

## Changelog

- **2026-10-08**: A checkpoint is not an exception after all: it writes its own row through a context of its own
  (central 0047).
- **2026-10-07**: Recorded.
