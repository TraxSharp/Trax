---
title: Persistence ports
description: "Reference for the state machine persistence ports: ISnapshotPrincipal, ISnapshotStore, StoredSnapshot, ISnapshotDraftService, the claim store, EF stores."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 8
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Ports.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotDraftService.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Actor.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/CallerAuthorization.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/EfSnapshotStore.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/EffectClaimStore.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/IdempotentEffect.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/RequestIdRing.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotLimits.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotMachineRegistry.cs
---

# Persistence ports

The ports under the draft operations. A host always supplies `ISnapshotPrincipal`.
[`AddStateMachines`](/docs/sdk-reference/statemachine-api/add-state-machines) registers the rest: `ISnapshotStore`
as `EfSnapshotStore` and `IEffectClaimStore` as `EfEffectClaimStore`, over the data context the provider registers,
and the `ISnapshotDraftService` each machine's registry entry builds. A host implements a store only for a custom
backend.

## ISnapshotPrincipal

```csharp fragment
public interface ISnapshotPrincipal
{
    string? CurrentUserKey { get; }
}
```

The authenticated user behind a request. Every draft is scoped to `CurrentUserKey`, so the draft id is not a
bearer capability: one user cannot load another's draft by guessing the id.

An empty or whitespace key is treated as unauthenticated, the same as `null`, and the draft service refuses one
with an `ArgumentException`.

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
`CONFLICT` rather than replacing another machine's draft under the same id.

| Method | Returns | Does |
| --- | --- | --- |
| `Get(userKey, machine, id, ct)` | `StoredSnapshot?` | reads the caller's draft of that machine, or `null` if there is none. It has a default implementation that calls `Get(userKey, id, ct)` and treats a draft naming another machine as absent; a store that keys by machine overrides it |
| `Get(userKey, id, ct)` | `StoredSnapshot?` | reads one of the caller's drafts under that id, whichever machine it belongs to, or `null` if there is none |
| `Delete(userKey, machine, id, ct)` | `Task` | deletes the caller's draft of that machine, leaving another machine's under the same id; idempotent. Its default implementation deletes through `Delete(userKey, id, ct)` only when the draft there is this machine's |
| `Delete(userKey, id, ct)` | `Task` | deletes the caller's drafts under that id; idempotent (deleting a gone row is a no-op) |
| `Insert(userKey, id, snapshot, ct)` | `bool` | creates the draft and only creates it: `false` when one already exists. The draft service creates every new draft through this, so a save that read no draft never overwrites one created since. Its default implementation checks `Get(userKey, id, ct)` and then calls `Upsert`, so it refuses while the user has any draft under the id, whichever machine it belongs to; the check and the write are two steps, so a store that can make them one statement should override it (`EfSnapshotStore` does) |
| `Upsert(userKey, id, snapshot, ct)` | `bool` | the last-writer-wins autosave of a machine with no committed state and no effect; `false` on a concurrent-write conflict |
| `Update(userKey, id, snapshot, expectedToken, requestId, ct)` | `bool` | writes only if the row still carries `expectedToken`, and records `requestId` as the last applied idempotency key with no trigger or from-state; `false` if the row changed |
| `UpdateWithRequest(userKey, id, snapshot, expectedToken, request, ct)` | `bool` | the authoritative path: `Update` that records the whole `AppliedRequest` (id, trigger, from-state), or clears it when `request` is `null`. It has a default implementation that calls `Update` with the id alone, so a custom store keeps compiling; override it, or every retry against that store is refused as `REQUEST_ID_REUSED` rather than replayed |

### Owners: users and the system

A row of `trax.snapshot_draft` belongs to a user or to the system, recorded in its `owner_kind` column (`user` or
`system`). A user's row names the user in `user_key`. A system row has no `user_key`: it is an instance created
from code by
[`IMachineInstances.Start`](/docs/sdk-reference/statemachine-api/machine-instances), and no GraphQL operation
creates, reads or advances one. The column is a closed vocabulary, so on Postgres it is the enum
`trax.snapshot_owner_kind`, and on SQLite the integer of `SnapshotOwnerKind` (0 is a user, 1 the system).

`ISnapshotStore` is a user's port. Every member of `EfSnapshotStore` filters on `owner_kind = user` as well as the
user's key, so `loadSnapshot`, `saveSnapshot`, `advanceSnapshot` and `sendSnapshot` with a system instance's id
answer as though nothing were there (`NotFound`, or a save that creates the user's own draft), and a user's delete
or draft expiry never removes a system row. A user may hold a draft under the same id as a system instance: the
two are separate rows, and every lookup names the owner kind. A custom store must keep the same rule: never return
or touch a row a user does not own.

A user's row is unique by `(user_key, machine, id)`, and a system row by `(machine, id)` among system rows, each
through a partial unique index. Because `user_key` can be null, the table's primary key is a surrogate `row_id`.
Migration `070_snapshot_draft_owner_kind.sql` (Postgres) and `032_snapshot_draft_owner_kind.sql` (SQLite) add the
columns, the indexes, a check that a user row has a key and a system row has none, and an index on
`(machine, state)` for listing instances by state.

System rows and the `invoke_token` column are written only by Trax's own server code, through the data context, and
never through `ISnapshotStore`. `invoke_token` is unique where set.

### Draft expiry and live runs

The draft TTL applies to user drafts only: a system instance never expires. Expiry is the draft sweep's, never a
read's: a host that registers machines sweeps every minute for users' drafts idle past `DraftTtl` or created longer
ago than `MaxDraftLifetime`, and deletes each one only while it is still the row, revision and invoke token it read,
so a draft written after the read is kept. The idle TTL leaves a draft that holds a live `invoke_token` (its state is
waiting on a run, and the run's outcome moves it on) or a pending timer; the lifetime deletes such a draft too,
cancelling its run first. Neither deletes a row the server put in its state (an effect's record, an invoked train's
outcome, an event gated by a role or a policy, or a timer that moved such a row on): that is the record of what the
server did. The row records it (`server_record`, `SnapshotDraft.ServerRecord`), so a state that a client and the
server can both enter keeps only the rows the server moved there; a row that does not record it (written before it was
recorded, or by host code through the store) is kept while its state is a committed state, an effect's target, an
invoked train's outcome target or a gated event's target. The sweep passes over a row with an effect pending on it,
and a row a claim from the earlier effect-claim ledger still holds; the delete removes the draft's ledger claims in its
transaction.

`Discard` deletes a draft that holds a live `invoke_token` by cancelling its run first, and a cancel that throws
keeps the draft. The cancel, which `AddStateMachines` registers, works the way the operations surface's does: a work
queue entry still queued is marked cancelled, and a dispatched run has its cancel flag set, which it reads at its
next junction on whichever host runs it. A draft with an effect pending on it, or one a claim from the earlier ledger
still holds, is refused as `DISCARD_NOT_ALLOWED`, as is a row the server put in its state. The delete is conditional
on the row id, revision and invoke token that were checked, and on no effect being pending, so a draft written in
between (moved into a committed state, say, or marked by a send) is kept, and is read and checked again. The draft's
ledger claims go in the delete's transaction.

## StoredSnapshot

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
| `InvokeToken` | string? | the token of the train run the draft's current state invoked, or `null` when no run is live. Server-only: it is never in `Json`. A custom store that records none leaves it `null` |

`StoredSnapshot` also carries `Revision` and `RowId`, init-only, so a store that builds it through the
positional constructor keeps compiling and leaves them `null`. `Revision` is 1 when the row was created and one
more for every write since: the database bumps it in a trigger, so a write from a custom store, raw SQL or an
older host is numbered too, and on the EF InMemory provider, which has no triggers, the store bumps it. `RowId`
is the row's surrogate key and names one incarnation: a draft deleted and created again under the same id has a
new one and starts again at revision 1, so revisions order the writes of one `RowId` only. Both travel beside the
snapshot, never inside `Json`, so `Serialize`, `clientResult` and the replay fingerprint never see them.

`LoadResult.Loaded`, `AutosaveResult.Saved`, `AdvanceOutcome.Advanced` and the generic mutations' outputs carry
the same `Revision` and `RowId`. A write guarded by the token it read (an advance, a send, a guarded save)
reports the revision it read plus one, without reading the row back; a replay, or a save that wrote nothing,
reports the revision the row is at.

`StoredSnapshot` also carries `RecentRequests`, the row's last client requests, oldest first, as
`RecordedRequest(RequestId, Fingerprint, Revision, ResultSnapshot)` records with the `Trigger` and `FromState`
each fired, at most `RequestIdRing.Capacity` (16). The EF store fills it from the `recent_requests` column; a
custom store that records none leaves it `null`, and the last-request fields apply instead. `DueAt` is when the
current state's timer fires, from the `due_at` column, or `null` when the state has no timer; it travels beside the
snapshot, never inside it. Every write that enters a timed state sets it from the database's clock in the same
statement as the state, a write that leaves the state clears it, and one that stays (a host's update, a strand) keeps
the time set at entry. The EF store does this on every path, `Upsert` and `Insert` included.

The EF store also keeps, beside the snapshot and never inside it, the effect pending on the row
(`SnapshotDraft.EffectToken`, `EffectKey`, `EffectTrigger`, `EffectRequestId` and `EffectLeaseUntil`; see
[ISnapshotEffect](/docs/sdk-reference/statemachine-api/i-snapshot-effect)) and whether the server put the row in its state
(`SnapshotDraft.ServerRecord`). Every write the EF store makes matches only a row with no pending effect, apart from
the writes of that effect itself, so nothing moves or changes a row while its effect runs.

`LoadResult.Loaded`, `AutosaveResult.Saved`, `AdvanceOutcome.Advanced`, `StartOutcome.Started` and the generic
mutations' outputs carry the row's `DueAt` beside the snapshot as well: what the read found, or what the write left,
reported by the EF store from the token-guarded write itself rather than a second read another write could overtake.
A replay of a request the row has moved past since answers its recorded revision with `DueAtKnown` false: the ring
records the result and not its due time, so `DueAt` is `null` there and says nothing. Every other answer has
`DueAtKnown` true, and a `null` `DueAt` in it means the state has no timer or the server cleared it. A custom store
that does not report it answers `null` after a write.

The `Token` / `expectedToken` pair is the optimistic-concurrency contract: read a draft, then `Update` against
its `Token`; if another write landed in between, the token no longer matches and the update returns `false`
instead of clobbering it.

### How a request id is matched

The draft keeps a ring of the last 16 client requests that carried a request id (`RequestIdRing.Capacity`):
each one's id, a fingerprint of its trigger and input, the state it fired from, the revision it produced and the
snapshot it was answered with. The ring is also bounded in size: past `RequestIdRing.MaxBytes` (128 KiB of the
column's JSON text, written with relaxed escaping, under which a character outside the Basic Multilingual Plane still
costs 12 bytes: about six 20 KiB snapshots) the oldest entries are
evicted first, so a machine with large snapshots remembers fewer than 16, and the newest request is always kept. Writes the server makes (an invoked run's outcome, a timer) never take a place in
it. An advance with a request id is then one of these:

| The ring | The draft now | Outcome |
| --- | --- | --- |
| no entry with the id | any | fires the trigger and records this request as the newest, evicting the oldest past 16 |
| the same id, another trigger or input | any | refused as `REQUEST_ID_REUSED`; nothing is written |
| the same id, trigger and input | any | a retry: the result recorded for the request is returned, never the current snapshot, and nothing fires |

A retry replays even when the draft is back in the state the request fired from. A timer, an invoked run's outcome
or any other write can bring it there, and the client still sent one request, so it is applied once. A send is
matched the same way before its effect runs: a retried send answers with its recorded result wherever the draft is
now, before the state checks, and never runs the effect again. A replay, of an advance or a send, carries the row's
`DueAt` only while the row is still at the recorded revision; past it, it says `DueAtKnown` false. A client that
means a new action sends a new id.

An id that has left the ring cannot be told from a new one, so it fires again under every rule, and from a state the
machine has since left it is a `NO_TRANSITION`. A `clientResult` sent with a retry is compared with the result
recorded for the request. A request id that is empty, whitespace or longer than `RequestIdRing.MaxRequestIdLength`
(200), or that holds a control character (C0, DEL or C1) or an unpaired surrogate, is refused as `INVALID_REQUEST_ID`
on every path (advance, send, start and discard), before anything is read or an effect claimed. The id is stored on
the row, and Postgres cannot store a NUL or an unpaired surrogate in text; the other control characters are refused
with them so that both providers accept the same ids. An advance with no request id at all is not recorded in the
ring.

Advance and send share one id space. A send with no `requestId` uses `send:{id}`, so a client that keys its
advances by the draft id does not collide with it. A send's fingerprint is its trigger alone: the receipt the effect
returns is the server's input, not the client's.

The ring is the `recent_requests` column (`SnapshotDraft.RecentRequests`), added by migration `077_snapshot_draft_start_and_request_ring.sql`
(Postgres) and `039_snapshot_draft_start_and_request_ring.sql` (SQLite). A row no client request has advanced since
has none, and its last request, from the columns below, is matched as before: the same id and trigger replay the
current snapshot.

The recorded trigger and from-state are the `last_request_trigger` and `last_request_from_state` columns,
added by migration `048_snapshot_draft_request_scope.sql` (Postgres) and `013_snapshot_draft_request_scope.sql`
(SQLite). A row written before that migration has neither, so a retry of its last request is refused once as
`REQUEST_ID_REUSED` instead of being replayed.

## ISnapshotDraftService

The draft service each machine's registry entry builds (`ISnapshotMachineRegistry.Service(machine)`). Besides the
actor members below, it declares the `userKey` overloads of `Advance` and `Load` (each acting as a client that holds
no role), `Autosave(userKey, id, snapshotJson, ct)`, the generic save, which answers an `AutosaveResult`
(`Saved`, `Conflict` or `Rejected(Code, Message)`), and `Serialize(snapshot)`, the canonical wire of a snapshot.
`Load` answers a `LoadResult` (`Loaded`, `NotFound` or `Invalid(Code, Message)`), and `Advance` and `Send` an
`AdvanceOutcome` (`Advanced`, `NotFound`, `Rejected(Reason, Detail)`, `LoadError(Code, Message)` or `Conflict`).

### Actors, start and discard

`ISnapshotDraftService` declares the operations of the typed machine surface, implemented by the draft service a
host gets from `ISnapshotMachineRegistry`.

| Member | Description |
| --- | --- |
| `Advance(Actor actor, Guid id, string trigger, ...)` | An advance on behalf of an actor, with the same parameters as the `userKey` overloads (with and without `clientResult`). A client's event is checked against the event's declared rule; a client the rule does not admit is refused as `FORBIDDEN`. |
| `Send(Actor actor, Guid id, string requestId, ...)` | The effect runner's send on behalf of an actor, after checking a client against the effect-bound event's rule, though a send names no trigger. A service taken from `ISnapshotMachineRegistry` sends; one built on its own throws. |
| `Start(Actor actor, JsonObject? input, string requestId, ...)` | Starts an instance from the machine's declared start input (see below), minting its id, at revision 1. Returns a `StartOutcome`: `Started(Id, Snapshot, Revision, Created)`, where `Created` is false for a retry of the same `requestId` and input, or `Rejected(Code, Message)`. |
| `Load(Actor actor, Guid id, ...)` | Reads the actor's draft, as `Load(userKey, ...)` does, when the actor is a client the machine's `Get` rule admits beyond its owner-only floor. A rule that refuses the caller, a client with no user key and any other actor are answered `NotFound`, exactly as an unknown id is. A service that does not override it answers `NotFound` to every actor. The machines root's `get` reads through it. |
| `Discard(Actor actor, Guid id, string requestId, ...)` | Deletes the actor's draft, when the machine's `Discard` rule admits the client (`FORBIDDEN` otherwise; on a machine that declares rules but no `Discard` rule, nobody is admitted) and the draft is not a row the server put in its state (an effect's record, an invoked train's outcome, an event gated by `Requires` or `RequiresPolicy`, carried on by a timer), an instance in a state that invokes a train, and one with an effect pending on it (`DISCARD_NOT_ALLOWED` otherwise). A live invoked run is cancelled first. Idempotent: a draft already gone, or someone else's id, answers `Discarded` exactly as a missing one does. Returns a `DiscardOutcome`: `Discarded` or `Rejected(Code, Message)`. |

`Start` refuses, writing nothing, with one of these codes, in this order (the first that applies is the answer):

| Code | When |
| --- | --- |
| `SYSTEM_OWNED` | the machine is system-owned: only host code creates its instances |
| `UNAUTHENTICATED` | the actor is not a client, or its user key is empty or whitespace |
| `NO_START` | the machine declares no `Start<TStartInput>(...)` |
| `FORBIDDEN` | the caller does not meet the start's rule (a start that declares none admits nobody) |
| `INVALID_REQUEST_ID` | the request id is empty, whitespace, longer than 200 characters, or holds a control character or an unpaired surrogate |
| `TOO_LARGE` | the input is over 64 KiB as JSON, checked before it is read |
| `INVALID_INPUT` | the input does not match the start's record: an unknown field, a wrongly typed or missing one |
| `INVALID_CONTEXT` | the reduction could not build the initial context |
| `MALFORMED` | the context the reduction produced holds a value no store can keep (a number outside the range of a double, a NUL character) |
| `TOO_LARGE` | the started snapshot is over 64 KiB |
| `INVALID_CONTEXT` | the started snapshot fails the initial state's rule when read back |
| `INVALID_INPUT` | the initial state is timed, and its duration field is missing or outside the timer's bounds |
| `REQUEST_ID_REUSED` | the user's start with this request id on this machine was started with a different input |
| `TOO_MANY_DRAFTS` | the user already holds `MaxDraftsPerUser` drafts |
| `INVOKE_LIMIT_REACHED`, `INVOKE_FORBIDDEN`, `INTERNAL_ERROR` | the initial state invokes a train, and the user is at the live-run limit, the train refuses them, or the state's input mapping threw |
| `CONFLICT` | the start raced another write it could not be ordered with; retry it |

The input is validated against the start's record, never the context type, and the declared reduction is applied to
the machine's initial context. A start into an invoking initial state queues the run in the transaction that inserts
the row. The request id is kept on the row as `start_request_id` (`SnapshotDraft.StartRequestId`), unique per owner
kind, user and machine, with a fingerprint of the input (`start_fingerprint`, `SnapshotDraft.StartFingerprint`), so
a retry returns the instance it created (and marks it active), however many retries race. `Discard` refuses
`SYSTEM_OWNED`, `UNAUTHENTICATED` and `INVALID_REQUEST_ID` the same way, then `FORBIDDEN` and `DISCARD_NOT_ALLOWED`.

An `Actor` has one kind. `Actor.Client(UserKey, IsInRole, SatisfiesPolicy)` is a caller acting through a client
surface: the user it acts for, a role check and an asynchronous policy check. `new Actor.Client(userKey, roles)`
builds one that holds exactly those roles and satisfies no policy. Code outside Trax cannot declare another kind: a
record deriving from `Actor` does not compile. There is no trusted actor for a user's instance: host code acting on
one is a client, holding the roles it passes (none, through the `userKey` overloads), and is held to every rule.
Host code drives a system-owned instance through
[`IMachineInstances`](/docs/sdk-reference/statemachine-api/machine-instances), which takes no actor and checks no
event rule.

**Rules are checked below the resolver.** The draft service checks a client's event against its rule, with the
roles and policies the client holds when the event is sent, never remembered on the draft. The refusal is
`FORBIDDEN`, and it comes after only a timer trigger (`TIMER_BOUND`), a request id the row cannot record
(`INVALID_REQUEST_ID`) or an oversized input (`TOO_LARGE`), an id the caller holds no instance under (`NOT_FOUND`, so
another user's id answers like a missing one) and an effect- or outcome-bound trigger. An unknown trigger passes the
rule check and is refused by the engine as `NO_TRANSITION`, after the request-id ring. A forbidden caller learns
nothing about its input, a retry of an earlier request is checked against the rule as it
is now (so a caller who lost the role is refused, not replayed), and nothing is written or launched. A client with
no user key is refused as `FORBIDDEN` too. On a machine that declares no rule at all, an authenticated
client may fire every event, as before rules existed; once a machine declares any rule, an event without one is
refused. The `userKey` overloads act as a client holding no role, and so does `ISnapshotEffectRunner.Run`.

The generic `advanceSnapshot` and `sendSnapshot` mutations build their client from the request's
`ISnapshotPrincipal` and the registered `ICallerAuthorization`, which reports the caller's roles
(`IsInRole(role)`, exact and case-sensitive), policies (`SatisfiesPolicy(policy, cancellationToken)`) and which
policies exist (`IsPolicyRegistered(policy)`). Trax.Api registers one over the request's `ClaimsPrincipal`, as
`[TraxAuthorize]` is evaluated. Without one, a caller holds no role and satisfies no policy.

**The generic save serves only machines with no server-side behaviour.** `Autosave` refuses a machine with a gated
event or a gated `start` (a rule that names a role or a policy), a timer, an invoked train or an effect as
`SAVE_NOT_ALLOWED`, and writes nothing; such a machine moves through its declared events.

### What each path may write

Autosave stores a snapshot the client computed; advance fires one trigger on the stored draft; send runs the
machine's effect and then fires its trigger. Only send puts a draft into a committed state or into the state an
effect-bound transition lands in, because only send knows the effect ran:

| Path | Refuses | Code |
| --- | --- | --- |
| advance | a trigger bound to the machine's effect from the stored state | `EFFECT_BOUND` |
| advance | an invoked train's outcome trigger | `OUTCOME_BOUND` |
| advance | entering an invoking state past the user's live-run limit, or for a user the train refuses | `INVOKE_LIMIT_REACHED`, `INVOKE_FORBIDDEN` |
| autosave | any change to a draft in an invoking state or holding a live run | `DRAFT_INVOKING` |
| autosave | creating a draft in, or moving one into, a committed state, an effect's target, an invoking state or an invoked train's outcome target | `STATE_RESERVED` |
| autosave | any change to a draft already in a committed state or an effect's target, except a reset to the initial state that the machine declares from that state | `DRAFT_COMMITTED` |
| autosave | overwriting a stored draft that fails rehydration with anything but the initial state | `DRAFT_UNREADABLE` |
| advance, autosave, send | any write to a draft with an effect pending on it | `EFFECT_IN_PROGRESS` |
| send | (over a store a host substituted) recording the receipt on a draft that was written while the effect ran | `CONFLICT` |
| send | (over a store a host substituted) replaying a receipt onto a draft whose content is not what the effect ran on | `DRAFT_CHANGED` |

The receipt a draft in a committed state holds is the one the effect produced for that draft's content, so its
content is as fixed as its state. A save identical to the stored draft, the snapshot a send returned for instance,
is answered as saved without a write. A reset is allowed only where the machine itself has a transition from that
state back to the initial state; a machine with none has no soft reset out of it.

Over the EF store a send marks the draft's row before it runs the effect, and records the receipt, the move to the
effect's target and the end of the mark in one write: every other write to the draft, a reset included, is refused as
`EFFECT_IN_PROGRESS` until then, so the receipt is always recorded on the content the effect ran on. The next send of
the event, after a reset, runs the effect afresh under a new key. To seed a draft in a committed state in a test, write
it through the store.

A host that substitutes its own `ISnapshotStore` cannot hold the mark, and keeps the effect-claim ledger beside the
draft instead: a reset releases a claim once its outcome is settled on the draft (a claim running inside its lease is
kept, and so is a completed claim whose receipt, under `receipt`, the draft never recorded), and the next send
replays that receipt only onto the content the effect ran on, `DRAFT_CHANGED` otherwise.

### Invoked-run ports

Three ports connect a state that [invokes a train](/docs/statemachine/invoking-trains) to the rest of Trax. They are
internal: a host neither implements nor calls them, and `AddStateMachines` and `AddMediator` register them.

- **The launcher** queues the run. Trax.Mediator implements it. It authorizes the run and writes its work queue entry
  into the outbox's data context, inside the transaction that moves the snapshot, so the two commit together; it
  commits nothing itself. It refuses a train whose output reaches a `[TraxSensitive]` member, and, for a user-owned
  instance, a `[TraxBroadcast]` train, as the startup check does. A user-owned instance's run is authorized against
  the current caller. A system-owned instance's run is authorized in Trax's trusted execution scope, including one that the previous run's outcome started (no user is
  present). A user-owned instance never launches from an outcome: the startup check refuses such a machine, and the
  launcher refuses such a launch with `UnauthorizedAccessException`. The launcher also tells the startup check what
  about a train stops a machine from invoking it. It lives in the persistence package rather than the engine because
  it writes through `IDataContext`, and the engine depends on no data provider.
- **The run cancellation** cancels a run by its invoke token, as described under
  [Draft expiry and live runs](#draft-expiry-and-live-runs).
- **The outcome delivery** applies the outcome of an ended run to the instance waiting on it, now, through the
  delivery the lifecycle hook and the reconciler make: one conditional update on the instance's invoke token, so the
  outcome is applied once however many callers deliver it. The operations service calls it after an operator's
  [`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance) cancels a still-queued
  run, so the instance moves in that request on a host that registers the machine.

## ISnapshotMachineRegistry

| Member | Returns | Description |
| --- | --- | --- |
| `Service(machine)` | `ISnapshotDraftService?` | the draft service for the machine with that id, or `null` for an unknown or system-owned one |
| `EffectRunner(machine)` | `ISnapshotEffectRunner?` | the machine's effect runner, or `null` when it binds no effect |
| `SchemaHash(machine)` | `string?` | the machine's IR hash, or `null` (see [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity)) |

`ISnapshotEffectRunner.Run(userKey, id, requestId, ct)` is the send: it runs the machine's effect once and fires its
trigger, acting as a client that holds no role.

## IEffectClaimStore

The effect-claim ledger. A machine's effects use it only over a snapshot store a host substituted, and for claims
recorded before the pending effect moved onto the draft's row; `IdempotentEffect` uses it outside machines.

| Member | Returns | Description |
| --- | --- | --- |
| `TryClaim(effectKey, lease, ct)` | `ClaimResult` | claims the key under a lease: `Won(OwnerToken)` by inserting a row or reclaiming an in-flight one whose lease expired, `Lost` when the key is held or completed |
| `TryClaim(effectKey, lease, contentFingerprint, ct)` | `ClaimResult` | the same, recording the fingerprint of the content the effect runs on |
| `Complete(effectKey, ownerToken, receipt, ct)` | `bool` | records the receipt, only while `ownerToken` still holds the claim |
| `GetReceipt(effectKey, ct)` | `string?` | the receipt, or `null` when the key is unclaimed or in flight |
| `GetCompleted(effectKey, ct)` | `CompletedEffect?` | the receipt and its `ContentFingerprint` in one read |
| `Release(effectKey, ct)` | `Task` | deletes the claim, completed or in flight; idempotent |
| `ReleaseForReset(effectKey, receiptRecorded, ct)` | `Task` | the release a reset uses (below) |
| `ReleaseOwned(effectKey, ownerToken, ct)` | `bool` | deletes the claim only while `ownerToken` owns it and it is in flight |
| `ReclaimStale(cutoff, ct)` | `int` | deletes in-flight claims whose lease expired before `cutoff`, and returns the count |

`IdempotentEffect.RunOnce(effectKey, effect, lease, ct)` (and the overload with a `contentFingerprint`) claims the
key, runs `effect` and records its receipt, answering an `EffectOutcome`: `Ran(Receipt)`, `AlreadyRan(Receipt)` with
the recorded `ContentFingerprint`, or `InProgress`. A null `lease` is `SnapshotLimits.DefaultEffectLease`.
`EffectClaimSweeper.Sweep(cutoff, ct)` calls `ReclaimStale`.

## EfSnapshotStore and EfEffectClaimStore

```csharp fragment
public EfSnapshotStore(IDataContext db, ISqlDialect? dialect = null)
public EfEffectClaimStore(IDataContext db, ISqlDialect? dialect = null)
```

The default stores take the `IDataContext` their tables are reached through and the provider's `ISqlDialect`:

The dialect is how a lost race (a concurrent create of one draft, a claim on a key someone else holds) is told
apart from a real failure, on Postgres and SQLite alike. Without one, such a race throws instead of returning
`false` or `Lost`. Both stores stop tracking what they write, so they can share a request's data context.

Over the EF snapshot store, a machine's effects do not use the claim ledger: the effect is pending on the draft's
row. The ledger serves `IdempotentEffect` outside machines, a host whose snapshot store is its own, and claims recorded
before the upgrade, which a send records or drops (see migration
[`081`](/docs/reference/schema-migrations#state-machine-pending-effect-and-server-record-081-sqlite-041)).

`IEffectClaimStore.ReleaseForReset(effectKey, receiptRecorded, ct)` is the release a reset uses: it keeps an
in-flight claim whose lease has not passed, and deletes a completed claim only when `receiptRecorded` accepts its
receipt. `Release` deletes whatever is there. A discard and the draft sweep claim the draft's free keys before the
delete and, inside the delete's transaction, free those and release the settled claims through `ReleaseForReset`, so
a claim still in flight or still on its way to the draft is never released with it. A custom claim store
that does not override `ReleaseForReset` gets a default that releases only a completed claim whose receipt was
recorded, and keeps every in-flight claim until the lease lets the next send reclaim it.

`TryClaim(effectKey, lease, contentFingerprint, ct)` records the fingerprint of the content the effect runs on in
`effect_claim.content_fingerprint`, on a new claim or on an expired one it takes over, and
`GetCompleted(effectKey, ct)` returns a completed claim's receipt with that fingerprint in one read as a
`CompletedEffect`. `IdempotentEffect.RunOnce(effectKey, contentFingerprint, effect, lease, ct)` passes it through,
and a lost claim's `EffectOutcome.AlreadyRan` carries the recorded `ContentFingerprint`; the effect runner compares it
with the draft's before replaying. Both members have defaults, so a custom claim store keeps compiling: one that does
not override them records no fingerprint, and its claims replay without the content check.

## Limits

| Constant | Value | Description |
| --- | --- | --- |
| `SnapshotLimits.MaxSnapshotBytes` | 65536 | the largest snapshot, trigger input or start input, as JSON (64 KiB); past it a request is `TOO_LARGE` |
| `SnapshotLimits.DefaultEffectLease` | 5 minutes | how long an effect stays pending before the next send or the reconciler may take it over |
| `RequestIdRing.Capacity` | 16 | the client requests a draft remembers |
| `RequestIdRing.MaxBytes` | 131072 | the ring's size bound, in bytes of the column's JSON text |
| `RequestIdRing.MaxRequestIdLength` | 200 | the longest request id |

## See also

- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines), what registers these.
- [ISnapshotEffect](/docs/sdk-reference/statemachine-api/i-snapshot-effect).
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
- [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances), system-owned instances.
