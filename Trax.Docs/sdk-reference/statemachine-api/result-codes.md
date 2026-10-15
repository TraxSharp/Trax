---
title: Result codes
description: Every refusal code a machine advance, rehydrate or snapshot mutation can return, as RefusalCode members, and the order refusals are checked in.
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 9
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/RefusalCode.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Results.cs
---

# Result codes

Every advance and rehydrate returns a result, never an exception. On the unhappy path the result carries a code.
Only the code is contract; the detail text may differ across runtimes and is for people, not for branching on.

Every code is a member of one closed set, the `RefusalCode` enum. The C# engine, the draft service and the API type
their codes by it, the GraphQL schema exposes it as the `RefusalCode` enum, and the TypeScript package exports
`RefusalCode` with the same members, so a client can switch on every code exhaustively. A code travels in upper
snake case (`GUARD_FAILED`), the spelling GraphQL gives the enum: `RefusalCode.GuardFailed.ToWire()` in C#,
`RefusalCode.GuardFailed` in TypeScript. `RefusalCodes.TryParse` also reads the kebab-case spelling the codes had
before (`guard-failed`), so a request id an older node recorded still replays. `RefusalCodes.All` lists the members
in order, and `RefusalCodeJsonConverter`, which the enum carries, writes and reads the wire spelling in
`System.Text.Json`. `RefusalCodeParityTests` holds the C# and TypeScript sets equal.

## Members

| Code | Returned by | Meaning |
| --- | --- | --- |
| `NO_TRANSITION` | advance, send | no edge matches the `(state, trigger)` pair. On a send, the stored draft is not in the state the effect's transition leaves (for the sample's checkout, a draft still at `Cart`), so nothing was run or written |
| `INVALID_INPUT` | advance, start | the trigger's input does not match the record its `WithInput<T>()` declares: a field missing, of another JSON type, a fraction where the record has an integer, a field the record does not declare, a number outside the range of a double, a string with a lone surrogate, or something other than an object. A trigger without `WithInput` on a declarative machine takes no input, so anything but none or `{}` is refused. Checked after the edge is found and before any guard reads the input, on every path that fires a trigger (the draft service, the generic advance, a host's `IMachineInstances.Advance`); nothing was written and no request id was recorded. An invoked train's output that does not match its output type is refused the same way |
| `GUARD_FAILED` | advance | an edge matched but its guard rejected the trigger; the detail is the `Because(...)` message |
| `INVALID_CONTEXT` | advance, rehydrate, start | the resulting (advance) or stored (rehydrate) context failed the target state's rule |
| `MALFORMED` | rehydrate, advance (persisted), start | the snapshot JSON could not be parsed, or its context holds a value no store can keep: a number outside the range of a double (`1e400`) or a NUL character in a string or key. On a persisted advance, the trigger input is not valid JSON, or the result held such a value; on a start, the context the start built holds such a value; nothing was written |
| `UNKNOWN_STATE` | rehydrate | the snapshot names a state the definition does not have. Only the exact declared name is a state: `"1"`, `" Unlocked"` and `"Locked, Unlocked"` are unknown, and an unknown trigger token is `NO_TRANSITION` |
| `VERSION_MISMATCH` | rehydrate | the snapshot version is newer than the definition, or a [migration](/docs/sdk-reference/statemachine-api/migrations) is missing |
| `UNKNOWN_MACHINE` | rehydrate, save, advance, load, send | no registered machine has that name, or the machine is [system-owned](/docs/sdk-reference/statemachine-api/machine-instances), which no user's draft operation reaches |
| `UNAUTHENTICATED` | save, advance, load, send, start, discard | the request carries no user: `ISnapshotPrincipal.CurrentUserKey` is null, empty or whitespace. Checked before anything is read or written (on `start` and `discard`, after only `SYSTEM_OWNED`) |
| `NOT_FOUND` | advance, load, send, the push | no draft with that id exists for this user and machine, or it expired and was deleted; start a new one. Another user's id, a system instance's and one the read rule refuses the caller get the same answer |
| `FORBIDDEN` | advance, send, start, discard | the caller does not meet the rule the event, `start` or `discard` declares (a machine that declares rules admits nobody to one without a rule); nothing was written or launched |
| `SAVE_NOT_ALLOWED` | save | the generic save serves only machines with no server-side behaviour, and this one has a gated event or start, a timer, an invoked train or an effect; nothing was written. Move it with its declared events |
| `TOO_MANY_DRAFTS` | start, save | the user already holds `MaxDraftsPerUser` drafts, and this request would create another; nothing was written |
| `INVALID_REQUEST_ID` | advance, send, start, discard | the request id is empty, whitespace, longer than 200 characters, or holds a control character (C0, DEL or C1) or an unpaired surrogate; nothing was read, written or run |
| `NO_START` | start | the machine declares no `Start<TStartInput>(...)`, so a client cannot create an instance of it |
| `SYSTEM_OWNED` | start, discard | the machine is [system-owned](/docs/sdk-reference/statemachine-api/machine-instances): only host code creates and ends its instances |
| `TOO_MANY_SUBSCRIPTIONS` | the push | the caller already holds as many open subscriptions as one principal may, on this instance or in all; one that completes frees its place |
| `TIMER_BOUND` | advance | the trigger is a state's timer, which only the server fires; nothing was written |
| `CLIENT_OUTDATED`, `SERVER_OUTDATED` | a persisted machine document | the document id belongs to another version of the machine than the node runs; see below |
| `SCHEMA_MISMATCH` | save, advance, load, send | the client's machine [schema hash](/docs/sdk-reference/statemachine-api/runtime-integrity) differs from the server's; the client is out of date and should reload |
| `CLIENT_DIVERGENCE` | advance | the client's computed result differs from the server's authoritative result ([divergence detection](/docs/sdk-reference/statemachine-api/runtime-integrity)); nothing was written, reload |
| `TOO_LARGE` | save, advance, start | the snapshot, the trigger input, the start input, or the advanced or started snapshot exceeds `SnapshotLimits.MaxSnapshotBytes` (64 KiB); nothing was written. The trigger input is measured before it is validated or the draft is read, on every advance path, and the start input before it is read |
| `DISCARD_NOT_ALLOWED` | discard | the server put the instance in its state (an effect's record, an invoked train's outcome, an event gated by `Requires` or `RequiresPolicy`, carried on by a timer), or it is in a state that invokes a train, or an effect is pending on it. It is the record of what the server did, so its owner may not discard it; nothing was deleted. A row from before the record existed is judged by its state: a committed state, an effect's target, an invoking state, an invoked train's outcome target or a gated event's target. A retry answers the same, unless the owner's own event has moved the instance on since |
| `REQUEST_ID_REUSED` | advance, send, start | the request id was last used for a different trigger, so this is not a retry of it; send a new id. A send is refused before its effect runs |
| `EFFECT_BOUND` | advance (server) | the trigger runs the machine's irreversible effect from this state, so only a send fires it; nothing was written. The server's advance refuses it (the draft service and `IMachineInstances.Advance`); the engine's `Advance` does not, and fires the edge |
| `STATE_RESERVED` | save | the snapshot is in a committed state or an effect's target and the stored draft is not; only a send puts a draft there, and nothing was written |
| `DRAFT_COMMITTED` | save | the stored draft is in a committed state or an effect's target and the save would change it; only a reset to the initial state that the machine declares from that state is accepted, and a save identical to the stored draft succeeds without a write |
| `DRAFT_UNREADABLE` | save | the stored draft fails rehydration, so only a reset to the initial state may overwrite it |
| `DRAFT_INVOKING` | save | the stored draft is in a state that [invokes a train](/docs/statemachine/invoking-trains), or holds a live run, and the save would change it; only a declared transition or the run's outcome moves it on, and a save identical to the stored draft succeeds without a write |
| `OUTCOME_BOUND` | advance | the trigger is an invoked train's outcome (`Building.done`, `.failed`, `.cancelled`), which only the run applies; nothing was written. The engine's `Advance` and the TypeScript twin's `advance` refuse it the same way, and apply it only through their outcome entry points |
| `INVOKE_LIMIT_REACHED` | advance, start | entering the state would start a run while this user already holds the machine's limit of live invoked runs, counted across every machine (10 unless the machine sets `InvokedRunLimit`); nothing was written or queued |
| `INVOKE_FORBIDDEN` | advance, start | the train the state invokes refuses this user (its `[TraxAuthorize]` requirements); nothing was written or queued |
| `INVOKE_CHAIN_USER_OWNED` | advance | an outcome or a timer would enter a state that invokes a train on a user-owned instance; only a system-owned instance chains runs that way, and nothing was written or queued |
| `CONFLICT` | save, advance, send, start, discard | another write changed the draft between this request reading it and writing it, for example a second tab saving at the same moment; nothing was written, reload and retry. Over a snapshot store a host substituted for Trax's, a send's draft was written while the effect ran: the effect's receipt is kept, and sending again replays it without running the effect once the draft holds the content the effect ran on (`DRAFT_CHANGED` until then) |
| `INTERNAL_ERROR` | advance, send, start | a guard, reducer, validator or an invoking state's input mapping threw. The message is fixed and carries a reference; the exception is logged on the server under that reference |
| `NO_EFFECT` | send | the machine binds no irreversible effect (`RunsOnce`), so there is nothing to send; drive it with advance |
| `DRAFT_CHANGED` | send | over a snapshot store a host substituted for Trax's: this draft's effect already ran, on content the draft no longer holds (it was edited after the effect loaded it). Nothing was run and nothing was written, and the receipt stays with the claim. Restore the content the effect ran on, for example by saving the snapshot the send was made from, and send again to record it |
| `EFFECT_IN_PROGRESS` | advance, send, save, start, discard | an effect is pending on this draft: a send is running it and holds its lease, so every other write waits until it is recorded or fails; nothing was written, retry once it finishes (a send with the same request id). A discard answers `DISCARD_NOT_ALLOWED` instead |
| `DELIVERY_FAILED` | send | the effect threw, or returned no receipt; the draft was not advanced, so the send can be retried. The message is fixed and carries a reference; the exception is logged on the server under that reference. A cancelled request is not a failed delivery: it propagates as a cancellation. An `OperationCanceledException` from the effect itself, such as an outbound call's timeout, is a `DELIVERY_FAILED`, and its claim stays in flight until the lease passes |

## Behavior

An advance's refusals are checked in this order, and the first that applies is the answer: an unknown state or
trigger (`NO_TRANSITION`), an outcome trigger (`OUTCOME_BOUND`), a timer trigger (`TIMER_BOUND`, refused to anyone,
trusted host code included), no edge from the current state (`NO_TRANSITION`), `INVALID_INPUT`, `GUARD_FAILED`, a
duration the target state's timer cannot read from the successor context (`INVALID_INPUT`), `INVALID_CONTEXT`
([ADR 0048](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md)).
`EFFECT_BOUND` is not among them: the engine fires an effect-bound edge like any other, and only the server refuses
it. A rehydrate answers `MALFORMED`, `UNKNOWN_STATE`, `VERSION_MISMATCH`, `UNKNOWN_MACHINE` or `INVALID_CONTEXT`.

`CLIENT_OUTDATED` and `SERVER_OUTDATED` answer a [persisted machine document](/docs/sdk-reference/graphql-api/machines#persisted-documents)
of another version than the node runs, as a GraphQL error whose `extensions.code` is the code. The generic save returns `SAVE_NOT_ALLOWED`, the actor
advance, send, `start` and `discard` return `FORBIDDEN`, `discard` returns `DISCARD_NOT_ALLOWED`, and `start` and
`discard` return the others (see
[Actors, start and discard](/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard)).

Over GraphQL these surface on the mutation's `problem` field, so a client reads the code and reacts (re-enable
a control on `GUARD_FAILED`, start fresh on `VERSION_MISMATCH`) without ever seeing a stack trace. When the
refusal came from an exception, a guard, reducer or migration that threw or an effect that failed, the
message is a fixed sentence ending in `Reference: {id}`, and the server logs the exception under that id at
error level: the exception's own text never reaches the client.


## See also

- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard), the order the draft service checks its refusals in.
- [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity), `SCHEMA_MISMATCH` and `CLIENT_DIVERGENCE`.
- [The machines root](/docs/sdk-reference/graphql-api/machines), where a typed operation answers these codes.
