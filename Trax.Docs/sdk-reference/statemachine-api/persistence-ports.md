---
layout: default
title: Persistence ports
parent: State Machine API
grand_parent: SDK Reference
nav_order: 8
---

# Persistence ports

Two ports sit under the draft operations. A host always supplies `ISnapshotPrincipal`;
[`AddStateMachines`](/docs/sdk-reference/statemachine-api/add-trax-state-machines) provides a default
Postgres-backed `ISnapshotStore`, so you implement the store only for a custom backend.

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

| Method | Returns | Does |
| --- | --- | --- |
| `Get(userKey, id, ct)` | `StoredSnapshot?` | reads the caller's draft, or `null` if there is none |
| `Delete(userKey, id, ct)` | `Task` | deletes the caller's draft; idempotent (deleting a gone row is a no-op) |
| `Upsert(userKey, id, snapshot, ct)` | `bool` | the autosave path; `false` on a concurrent-write conflict |
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
