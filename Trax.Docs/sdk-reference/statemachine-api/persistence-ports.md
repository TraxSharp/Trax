---
layout: default
title: Persistence ports
description: Reference for the state machine persistence ports ISnapshotPrincipal and ISnapshotStore, StoredSnapshot, request id matching and the EF Core stores.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 8
---

# Persistence ports

Two ports sit under the draft operations. A host always supplies `ISnapshotPrincipal`;
[`AddStateMachines`](/docs/sdk-reference/statemachine-api/add-trax-state-machines) provides a default
`ISnapshotStore`, `EfSnapshotStore`, over the data context your provider registers, so you implement the store
only for a custom backend.

## ISnapshotPrincipal

The authenticated user behind a request. Every draft is scoped to `CurrentUserKey`, so the draft id is not a
bearer capability: one user cannot load another's draft by guessing the id.

```csharp
public interface ISnapshotPrincipal
{
    string? CurrentUserKey { get; }   // null when the request is unauthenticated
}
```

In an HTTP host this is backed by the request principal (a claim); in tests it is a fake. Bind it in DI when
you wire the subsystem.

## ISnapshotStore

Raw, user-scoped persistence of a snapshot. It moves the four snapshot fields (context as `jsonb`) and
enforces optimistic concurrency, but does not validate: validation lives above it in the draft service. Every
write is total, a conflict or a unique-key race returns `false` rather than throwing (genuine infrastructure
failures still propagate).

A draft is keyed by its user, its machine and its id: two machines may each give one user a draft under the same
id. The writes take the machine from the snapshot, and the draft service reads and deletes through the overloads
that name it. A store that implements only the members without a machine keys a draft by user and id alone; the
draft service then reads before every save and creates through `Insert`, so one machine's save is refused as a
`conflict` rather than replacing another machine's draft under the same id.

| Method | Returns | Does |
| --- | --- | --- |
| `Get(userKey, machine, id, ct)` | `StoredSnapshot?` | reads the caller's draft of that machine, or `null` if there is none. It has a default implementation that calls `Get(userKey, id, ct)` and treats a draft naming another machine as absent; a store that keys by machine overrides it |
| `Get(userKey, id, ct)` | `StoredSnapshot?` | reads one of the caller's drafts under that id, whichever machine it belongs to, or `null` if there is none |
| `Delete(userKey, machine, id, ct)` | `Task` | deletes the caller's draft of that machine, leaving another machine's under the same id; idempotent. Its default implementation deletes through `Delete(userKey, id, ct)` only when the draft there is this machine's |
| `Delete(userKey, id, ct)` | `Task` | deletes the caller's drafts under that id; idempotent (deleting a gone row is a no-op) |
| `Insert(userKey, id, snapshot, ct)` | `bool` | creates the draft and only creates it: `false` when one already exists. The draft service creates every new draft through this, so a save that read no draft never overwrites one created since. Its default implementation checks `Get(userKey, id, ct)` and then calls `Upsert`, so it refuses while the user has any draft under the id, whichever machine it belongs to; the check and the write are two steps, so a store that can make them one statement should override it (`EfSnapshotStore` does) |
| `Upsert(userKey, id, snapshot, ct)` | `bool` | the last-writer-wins autosave of a machine with no committed state and no effect; `false` on a concurrent-write conflict |
| `Update(userKey, id, snapshot, expectedToken, requestId, ct)` | `bool` | writes only if the row still carries `expectedToken`, and records `requestId` as the last applied idempotency key with no trigger or from-state; `false` if the row changed |
| `UpdateWithRequest(userKey, id, snapshot, expectedToken, request, ct)` | `bool` | the authoritative path: `Update` that records the whole `AppliedRequest` (id, trigger, from-state), or clears it when `request` is `null`. It has a default implementation that calls `Update` with the id alone, so a custom store keeps compiling; override it, or every retry against that store is refused as `request-id-reused` rather than replayed |

### StoredSnapshot

A stored draft as read back:

| Field | Type | Meaning |
| --- | --- | --- |
| `Json` | string | the draft's canonical JSON |
| `Token` | `Guid` | the concurrency token to write against |
| `LastRequestId` | string? | the idempotency key of the last applied advance, if any |
| `LastRequestTrigger` | string? | the trigger that advance fired, or `null` when none was recorded |
| `LastRequestFromState` | string? | the state that advance fired from, or `null` when none was recorded |
| `LastRequest` | `AppliedRequest?` | the three above as one value, or `null` when there is no request id |
| `UpdatedAt` | `DateTimeOffset` | when the row was last written (the window the draft-TTL expiry checks) |

The `Token` / `expectedToken` pair is the optimistic-concurrency contract: read a draft, then `Update` against
its `Token`; if another write landed in between, the token no longer matches and the update returns `false`
instead of clobbering it.

### How a request id is matched

The draft records the last advance that carried a request id: the id, the trigger it fired, and the state it
fired from. An advance with a request id is then one of three things:

| The stored request | The draft now | Outcome |
| --- | --- | --- |
| a different id, or none | any | fires the trigger and records this request |
| the same id and trigger | anywhere but the from-state, or on an edge that loops back to it | a retry: the current snapshot is returned and nothing fires |
| the same id and trigger | back in the from-state, on an edge that leaves it | the request's outcome was undone (a reset or a move back), so it fires again |
| the same id, another trigger or none recorded | any | refused as `request-id-reused`; nothing is written |

Advance and send share one id space. A send with no `requestId` uses `send:{id}`, so a client that keys its
advances by the draft id does not collide with it.

The recorded trigger and from-state are the `last_request_trigger` and `last_request_from_state` columns,
added by migration `048_snapshot_draft_request_scope.sql` (Postgres) and `013_snapshot_draft_request_scope.sql`
(SQLite). A row written before that migration has neither, so a retry of its last request is refused once as
`request-id-reused` instead of being replayed.

## What each path may write

Autosave stores a snapshot the client computed; advance fires one trigger on the stored draft; send runs the
machine's effect and then fires its trigger. Only send puts a draft into a committed state or into the state an
effect-bound transition lands in, because only send knows the effect ran:

| Path | Refuses | Code |
| --- | --- | --- |
| advance | a trigger bound to the machine's effect from the stored state | `effect-bound` |
| autosave | creating a draft in, or moving one into, a committed state or an effect's target | `state-reserved` |
| autosave | any change to a draft already in a committed state or an effect's target, except a reset to the initial state that the machine declares from that state | `draft-committed` |
| autosave | overwriting a stored draft that fails rehydration with anything but the initial state | `draft-unreadable` |
| send | recording the receipt on a draft that was written while the effect ran | `conflict` |
| send | replaying a receipt onto a draft whose content is not what the effect ran on | `draft-changed` |

The receipt a draft in a committed state holds is the one the effect produced for that draft's content, so its
content is as fixed as its state. A save identical to the stored draft, the snapshot a send returned for instance,
is answered as saved without a write. A reset is allowed only where the machine itself has a transition from that
state back to the initial state; a machine with none has no soft reset out of it.

A reset releases the draft's effect claims once their outcome is settled on the draft, so the next draft runs its
effect afresh. A claim whose effect is still running inside its lease is kept, and so is a completed claim whose
receipt the draft never recorded (its send reported a conflict): the next send replays that receipt rather than
running the effect a second time, provided the draft again holds the content the effect ran on, and is refused as
`draft-changed` otherwise. To seed a draft in a committed state in a test, write it through the store.

## EfSnapshotStore and EfEffectClaimStore

The default stores take the `IDataContext` their tables are reached through and the provider's `ISqlDialect`:

```csharp
public EfSnapshotStore(IDataContext db, ISqlDialect? dialect = null)
public EfEffectClaimStore(IDataContext db, ISqlDialect? dialect = null)
```

The dialect is how a lost race (a concurrent create of one draft, a claim on a key someone else holds) is told
apart from a real failure, on Postgres and SQLite alike. Without one, such a race throws instead of returning
`false` or `Lost`. Both stores stop tracking what they write, so they can share a request's data context.

`IEffectClaimStore.ReleaseForReset(effectKey, receiptRecorded, ct)` is the release a reset uses: it keeps an
in-flight claim whose lease has not passed, and deletes a completed claim only when `receiptRecorded` accepts its
receipt. `Release` deletes whatever is there, and is what deleting an expired draft uses. A custom claim store
that does not override `ReleaseForReset` gets a default that releases only a completed claim whose receipt was
recorded, and keeps every in-flight claim until the lease lets the next send reclaim it.

`TryClaim(effectKey, lease, contentFingerprint, ct)` records the fingerprint of the content the effect runs on in
`effect_claim.content_fingerprint`, on a new claim or on an expired one it takes over, and
`GetCompleted(effectKey, ct)` returns a completed claim's receipt with that fingerprint in one read as a
`CompletedEffect`. `IdempotentEffect.RunOnce(effectKey, contentFingerprint, effect, lease, ct)` passes it through,
and a lost claim's `EffectOutcome.AlreadyRan` carries the recorded `ContentFingerprint`; the effect runner compares it
with the draft's before replaying. Both members have defaults, so a custom claim store keeps compiling: one that does
not override them records no fingerprint, and its claims replay without the content check.
