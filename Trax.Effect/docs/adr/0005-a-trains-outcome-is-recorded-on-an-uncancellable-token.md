---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# A train's outcome is recorded on a token the caller cannot cancel

`ServiceTrain` threads the caller's `CancellationToken` through everything it does, which is the
point of the token. The one write that does not take it is the terminal one: the `SaveChanges` that
persists `Completed`, `Failed` or `Cancelled` runs on `CancellationToken.None`. The outcome is the
audit record of the work rather than part of the work, and a record that can only be written when
the caller is still interested is not a record.

## Status

**Accepted.**

## Why this is written down

Because the obvious edit undoes it. Every other `SaveChanges` in `ServiceTrain` takes
`CancellationToken`, so the one that does not looks like an oversight, and "fixing" it restores a
bug that is invisible in normal operation: it only appears when a caller cancels, which is the case
nobody runs in development.

That bug shipped. `FinishServiceTrain` set the terminal state in memory and the write that would
persist it was handed the token that had just been cancelled, so it never landed. The row stayed
`InProgress` with no `EndTime`. A caller who timed out got an exception, the train may or may not
have stopped, and the database could say neither.

## Considered options

**Leaving it to the reaper.** A scheduler's `ReapStaleInProgressMetadataJunction` already fails
orphaned `InProgress` rows, so the row does eventually reach a terminal state. Rejected on three
counts. It takes `StaleInProgressTimeout`, an hour by default, during which the execution is
unaccounted for. It writes `Failed` rather than `Cancelled`, losing the distinction the state machine
exists to carry, and for a train whose downstream work completed despite the cancellation it records
something that did not happen. And it only runs where a scheduler runs: a host that exposes trains
over GraphQL and schedules nothing has no reaper, so the orphan is permanent.

**A bounded token for the terminal write.** A fresh `CancellationTokenSource` with its own timeout
would cap how long a shutting-down process waits on the final write. Rejected as a tunable nobody
asked for, with a new failure mode of its own: a write that times out leaves exactly the orphan this
decision exists to prevent, and now on a schedule that is configuration rather than behaviour. The
data provider's own command timeout already bounds the write.

## Consequences

**The terminal write is not interruptible.** A process being killed can still lose it, and that is
what the reaper remains for. The decision narrows the window to a provider command rather than
closing it.

**Cancelling a caller no longer implies a cancelled result.** A train whose downstream call takes no
token finishes its work after the caller gives up, and that run is now recorded and returned as
`Completed` rather than surfacing `OperationCanceledException`. This is a behaviour change for
anything that treated a cancelled request as proof the work did not happen. It never was.

**A failed save is never rewritten as a different outcome.** If saving a completed run's outcome
throws, the save error propagates as it is; the run is not recorded as `Failed`, because the work
happened, and the row stays `InProgress` for the stale-run reaper. If recording a failed or
cancelled run's outcome throws, the recording error is logged and the train's original failure
still propagates, with its failure hooks, so the caller learns why the train failed rather than
why the bookkeeping did.

**The rule is one method, not a convention.** `SaveOutcome` exists so the terminal write is a named
thing with the reason attached, rather than three call sites that each have to remember.

## Exemplars

- `CancelledOutcomePersistenceTests` pins both halves: a train stopped by the caller's token persists
  `Cancelled` with an `EndTime`, and a train whose work completed anyway persists `Completed`.
- `OutcomeSaveFailureTests` pins what happens when the terminal save throws: a completed run
  propagates the save error without being recorded as `Failed`, and a failed run propagates its
  own exception and fires `OnFailed` once.
- [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens) is the rule this produces.

Not covered: nothing stops a new terminal-path write from taking `CancellationToken` directly
instead of going through `SaveOutcome`. The guard pins the behaviour at the two outcomes it can
observe, not the shape of the code that produces them.

## Changelog

- **2026-09-23**: Recorded what happens when the terminal save itself fails: a completed run
  propagates the save error instead of being rewritten as `Failed` (the row stays `InProgress`
  for the reaper), and a failed run's recording error is logged while the original failure
  propagates with its hooks.
- **2026-09-17**: Recorded.
