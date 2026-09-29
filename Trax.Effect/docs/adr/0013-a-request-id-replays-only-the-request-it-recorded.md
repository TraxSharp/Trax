---
authors: [Theauxm]
areas: [platform, data-model]
status: accepted
---

# A request id replays only the request it recorded

A state-machine draft records its last applied request as the id, the trigger it fired and the state it
fired from. A later advance with that id replays only when it names the same trigger and the draft still
shows that request's outcome; the same id with another trigger is refused as `request-id-reused`, and a
request whose outcome was undone fires again. Before this the id alone decided, so an id reused for a
different trigger was answered with an unrelated snapshot, and a send whose default key an earlier advance
had used ran its effect and then "replayed" instead of recording it.

## Status

**Accepted.**

## Considered options

**Namespacing the send's key only (`send:{id}`).** Adopted as well, because the default key is Trax's to
choose and should not collide with a key a client picked. On its own it was rejected: any client that
reuses an id across two triggers still gets the wrong answer, and nothing tells it so.

**Clearing the recorded request on every soft write.** Rejected: an autosave between a request and its
retry would turn a safe retry into a second firing. Recording the from-state gives the same protection
after a reset, where it is needed, without that cost.

**Refusing when the draft is back in the from-state, instead of firing.** Rejected for the send path: the
effect runs before the advance, so a refusal there follows a real delivery that is then never recorded.

## Consequences

The recorded trigger and from-state are two columns on `snapshot_draft` (Postgres migration 048, SQLite
013). A row written before them has no trigger, and its last request is refused once rather than replayed.
A custom `ISnapshotStore` that does not override `UpdateWithRequest` records the id alone, and every retry
against it is refused the same way. Both fail toward a reload, never toward answering one action with the
outcome of another.

A draft back in the from-state on an edge that loops to itself (from equals to) is where a completed
request leaves it, so that case still replays.

## Exemplars

- `RequestIdScopeTests` pins each row of the rule: a reused id is refused, an undone request fires, an
  unrecorded trigger is refused, and a self-loop retry replays.
- `OrderSendTests.cs` covers the send path: a reused id is refused before the effect runs, and a second
  order after a reset is placed.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched)
  is the rule this produces.

Not covered: only the last request is remembered, so a retry of an older request after a newer one fires
again. That is the single-slot design this refines, not something it changes.

## Changelog

- **2026-09-28**: Recorded.
