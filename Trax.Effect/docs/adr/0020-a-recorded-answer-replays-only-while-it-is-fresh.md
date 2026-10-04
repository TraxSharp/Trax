---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# A recorded answer replays only into the same state, and only while it is fresh

`AddDecisionRecording` stores, with each answer in `trax.decision`, the hash of the state the
question was asked about (`state_hash`, from `DecisionMade.StateHash`), and hands it back with the
answer so Trax.Core replays it only into a state that hashes the same (`core/0004`). It also
replays an answer only while it is younger than `ReplayAnswersFor`, 24 hours by default, measured
from when a decider gave it. An answer outside either is asked afresh. `TimeSpan.MaxValue`
(or any span longer than the calendar goes back) means no bound.

The hash is keyed when the host supplies a key, `AddDecisionRecording(o => o.HashStatesWith(key))`
or base64 in configuration under `Trax:Decisions:StateHashKey`: Trax.Core then writes an
HMAC-SHA256 under it, prefixed `k1:`, instead of a SHA-256 prefixed `s1:`. Without a key, the
journal records no hash for a question whose state can hold a member marked `[TraxSensitive]`
(the state's type, a type it holds, or the question's own type, by the same marks that mask a
run's input), so that answer is never replayed, and it logs a warning once per state type saying to
configure a key.

## Status

**Accepted.** Bounds the replay of `Trax.Docs/adr/0041`: a recorded answer replays only into the
same state, and only while it is fresh.

## Why this is written down

Because a replay is unattended, and an answer is a judgement about the world as it was when a
decider gave it. Each requeue in a chain replays the answer and records a new row for it, so the
bound counts from the row a decider wrote, following the
replayed rows back to it, so replaying an answer never makes it younger. A row whose answering run
is no longer in the chain cannot be dated and is asked afresh.

The hash covers every value of the state, including the ones Trax masks everywhere else. An
unkeyed hash can be computed by anyone who can guess the state, so for a state holding a value the
host marked as sensitive it is a second copy of that value in a weaker form. Keyed, only a holder of
the key can compute it. Without a key Trax gives up the replay for those states rather than store
the hash: replay is a convenience, and keeping a marked value out of every record is the rule
(`0010`).

## Considered options

**Always store the unkeyed hash.** Rejected for a state that holds a marked value, above.

**Refuse to start without a key.** Rejected: a host with no marked state loses nothing without a
key, and one with marked state still runs, asking afresh where it would have replayed.

**No age bound, state hash only.** Rejected: a state that hashes the same can still be judged
differently later (a policy, a model, a fraud signal outside the state), and a decision is about
the world, not only about its input.

**A bound per train or per question.** Not taken now: one host-wide bound covers the case, and a
finer one can be added without changing what is stored.

**Report an aged-out answer in `ReplayRefused`.** Not possible from the journal: Trax.Core writes
that field only for answers it was handed. The journal leaves the answer out and logs the reason at
Information.

## Consequences

A manifest's retry was queued by the scheduler, not by someone who asked for the original's
decisions, so a replay it cannot honour (the source run is gone or belongs to another train, the
host does not record decisions, a recorded answer cannot be read) is logged as a warning and asked
afresh, where a manual requeue still fails, classified permanent, rather than ask what its caller
asked it to repeat.

Rows written before `state_hash` existed have none and are never replayed, so the first requeue
after upgrading asks afresh. A requeue more than a day after the original asks afresh unless the
host raises the bound. Both apply to every path that names a run to replay: a manual requeue, a
dead letter's requeue and a manifest's retry.

A key must be the same in every process that may repeat a run, and changing or adding it means the
next repeat asks afresh, once, since hashes are compared exactly. A key in configuration that is not
base64 of at least 32 bytes fails resolving the key, so states are not hashed and nothing replays
until it is fixed.

## Exemplars

- `StateHashKeyTests` pins that a key in code or in configuration keys the hash, that the hash is
  unkeyed without one, and that without one a state holding a marked member is recorded with no
  hash, warned about once, and asked afresh on a requeue.
- `DecisionRecordingTests` pins that the hash is stored, that a changed state and an aged answer
  are asked afresh, that a replay does not refresh an answer's age, and that a row with no hash is
  asked afresh.

Not covered: Postgres and Sqlite pin only that the hash is stored; the age and state rules are the
same code for every provider and are exercised on the in-memory one.

## Changelog

- **2026-10-02**: The state hash is keyed when the host supplies a key; without one, a state that
  can hold a marked member is recorded with no hash.
- **2026-10-02**: A manifest's retry asks afresh when its replay cannot be honoured.
- **2026-10-02**: A bound longer than the calendar means no bound.
- **2026-10-02**: Recorded.
