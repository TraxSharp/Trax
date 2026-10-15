---
authors: [Theauxm]
repos: [effect, mediator, scheduler, api, cli]
areas: [graphql, data-model]
status: accepted
---

# Only the caller who started a run may follow its progress

A frontend that starts a long train had two ways to show where it stood: poll for the output, or subscribe to
`onJunctionEvent`, which serves only the operations view and `[TraxBroadcast]` trains, and so shows every subscriber
who may see the train every run of it, not their own. A train marked `[TraxRunProgress]` now records who started each
run, as a hash of the caller's principal id on the work queue entry and the run's metadata, and serves
`runProgress(runId)` as a query and a subscription that answer only that caller. Everyone else, and every run nobody
started directly, gets the same `NOT_FOUND`.

## Status

**Accepted.**

## Decisions

- **The starter is recorded, not inferred.** `starter_hash` (migrations 082 and 083, SQLite 042) holds the hex SHA-256
  of the qualified principal id, so the table never stores an id a reader could use elsewhere. A train mutation
  records its caller in both modes, a user-owned machine instance records its owner for the runs it invokes, a
  requeue or resume keeps the original run's, and a scheduled, dashboard, operations or remote run records nobody.
  The invoked-run cap counts by the same column, so a run whose instance was deleted still counts until it ends.
- **One answer for every refusal.** A missing id, another caller's run, a run with no starter and a run of a train
  without the attribute go through the same query and answer the same `NOT_FOUND`, so the answer says nothing about
  whether a run exists.
- **Opt in per train.** The attribute, not a host flag, turns it on, because whether a run's steps may be shown to the
  person who started it is a property of the train. Its steps are published to the host's subscriptions because of
  it; a `[TraxBroadcast]` subscriber still sees only broadcast trains.
- **Broadcast detail, not operations detail.** A step shows what a broadcast subscriber would see: no data, no host
  detail, a failure message only from a `TrainException`. `Answers = true` shows a decision's answer and track.
- **One stream, numbered.** The subscription merges the train-state and step topics into one numbered sequence that
  starts with the state found at subscribe time and ends after the run's end, holding the end until the steps
  published before it have arrived, for at most a second. A skipped number tells the client to read again.
- **Bounded per caller.** A caller holds at most 10 streams on one run and 500 in all on one host.

## Considered options

- **Filter `onJunctionEvent` by caller.** Rejected: its visibility is per train, by design (0037 in Trax.Api), and
  mixing a per-run rule into it would make one field answer two different questions.
- **Store the principal id.** Rejected: a hash answers "is this the same caller" without the table holding the id.
- **Let operators follow through runProgress too.** Rejected: they have `onJunctionEvent` with operations detail.

## Exemplars

- `RunProgressE2ETests` in Trax.Api.Tests: the starter reads and follows a run; another caller, a missing id and an
  anonymous caller are refused alike; no data reaches anyone.
- `RunProgressLimitsTests` in Trax.Api.Tests: the per-run and per-caller caps.
- [Run progress](/docs/sdk-reference/graphql-api/run-progress) is the rule as published.

Not covered: a remote runner records no starter, so a run it executes from a `RUN` mutation cannot be followed.

## Changelog

- **2026-10-15**: Recorded.
