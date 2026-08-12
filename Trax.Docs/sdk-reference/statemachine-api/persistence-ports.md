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
| `Update(userKey, id, snapshot, expectedToken, requestId, ct)` | `bool` | the authoritative path: writes only if the row still carries `expectedToken`, and records `requestId` as the last applied idempotency key; `false` if the row changed |

### StoredSnapshot

A stored draft as read back:

| Field | Type | Meaning |
| --- | --- | --- |
| `Json` | string | the draft's canonical JSON |
| `Token` | `Guid` | the concurrency token to write against |
| `LastRequestId` | string? | the idempotency key of the last applied advance, if any |
| `UpdatedAt` | `DateTimeOffset` | when the row was last written (the window the draft-TTL expiry checks) |

The `Token` / `expectedToken` pair is the optimistic-concurrency contract: read a draft, then `Update` against
its `Token`; if another write landed in between, the token no longer matches and the update returns `false`
instead of clobbering it.
