---
title: Result codes
description: Every result code a state machine advance, rehydrate or snapshot mutation can return, the constant that declares it, and the order refusals are checked in.
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 9
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Results.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/DraftRefusalCodes.cs
---

# Result codes

Every advance and rehydrate returns a result, never an exception. On the unhappy path the result carries a code.
Only the code is contract; the detail text may differ across runtimes and is for people, not for branching on. A
client treats a code it does not recognise as a refusal, so a code added later does not break it.

## Members

| Code | Returned by | Meaning |
| --- | --- | --- |
| `no-transition` | advance, send | no edge matches the `(state, trigger)` pair. On a send, the stored draft is not in the state the effect's transition leaves (for the sample's checkout, a draft still at `Cart`), so nothing was run or written |
| `invalid-input` | advance, start | the trigger's input does not match the record its `WithInput<T>()` declares: a field missing, of another JSON type, a fraction where the record has an integer, a field the record does not declare, a number outside the range of a double, a string with a lone surrogate, or something other than an object. A trigger without `WithInput` on a declarative machine takes no input, so anything but none or `{}` is refused. Checked after the edge is found and before any guard reads the input, on every path that fires a trigger (the draft service, the generic advance, a host's `IMachineInstances.Advance`); nothing was written and no request id was recorded. An invoked train's output that does not match its output type is refused the same way |
| `guard-failed` | advance | an edge matched but its guard rejected the trigger; the detail is the `Because(...)` message |
| `invalid-context` | advance, rehydrate, start | the resulting (advance) or stored (rehydrate) context failed the target state's rule |
| `malformed` | rehydrate, advance (persisted), start | the snapshot JSON could not be parsed, or its context holds a value no store can keep: a number outside the range of a double (`1e400`) or a NUL character in a string or key. On a persisted advance, the trigger input is not valid JSON, or the result held such a value; on a start, the context the start built holds such a value; nothing was written |
| `unknown-state` | rehydrate | the snapshot names a state the definition does not have. Only the exact declared name is a state: `"1"`, `" Unlocked"` and `"Locked, Unlocked"` are unknown, and an unknown trigger token is `no-transition` |
| `version-mismatch` | rehydrate | the snapshot version is newer than the definition, or a [migration](/docs/sdk-reference/statemachine-api/migrations) is missing |
| `unknown-machine` | rehydrate, save, advance, load, send | no registered machine has that name, or the machine is [system-owned](/docs/sdk-reference/statemachine-api/machine-instances), which no user's draft operation reaches |
| `unauthenticated` | save, advance, load, send, start, discard | the request carries no user: `ISnapshotPrincipal.CurrentUserKey` is null, empty or whitespace. Checked before anything is read or written (on `start` and `discard`, after only `system-owned`) |
| `not-found` | advance, load, send, `machineChanged` | no draft with that id exists for this user and machine, or it expired and was deleted; start a new one. Another user's id, a system instance's and one the read rule refuses the caller get the same answer |
| `forbidden` | advance, send, start, discard | the caller does not meet the rule the event, `start` or `discard` declares (a machine that declares rules admits nobody to one without a rule); nothing was written or launched |
| `save-not-allowed` | save | the generic save serves only machines with no server-side behaviour, and this one has a gated event or start, a timer, an invoked train or an effect; nothing was written. Move it with its declared events |
| `too-many-drafts` | start, save | the user already holds `MaxDraftsPerUser` drafts, and this request would create another; nothing was written |
| `invalid-request-id` | advance, send, start, discard | the request id is empty, whitespace, longer than 200 characters, or holds a control character (C0, DEL or C1) or an unpaired surrogate; nothing was read, written or run |
| `no-start` | start | the machine declares no `Start<TStartInput>(...)`, so a client cannot create an instance of it |
| `system-owned` | start, discard | the machine is [system-owned](/docs/sdk-reference/statemachine-api/machine-instances): only host code creates and ends its instances |
| `too-many-subscriptions` | `machineChanged` | the caller already holds as many open subscriptions as one principal may, on this instance or in all; one that completes frees its place |
| `timer-bound` | advance | the trigger is a state's timer, which only the server fires; nothing was written |
| `client-outdated`, `server-outdated` | a persisted machine document | the document id belongs to another version of the machine than the node runs; see below |
| `schema-mismatch` | save, advance, load, send | the client's machine [schema hash](/docs/sdk-reference/statemachine-api/runtime-integrity) differs from the server's; the client is out of date and should reload |
| `client-divergence` | advance | the client's computed result differs from the server's authoritative result ([divergence detection](/docs/sdk-reference/statemachine-api/runtime-integrity)); nothing was written, reload |
| `too-large` | save, advance, start | the snapshot, the trigger input, the start input, or the advanced or started snapshot exceeds `SnapshotLimits.MaxSnapshotBytes` (64 KiB); nothing was written. The trigger input is measured before it is validated or the draft is read, on every advance path, and the start input before it is read |
| `discard-not-allowed` | discard | the server put the instance in its state (an effect's record, an invoked train's outcome, an event gated by `Requires` or `RequiresPolicy`, carried on by a timer), or it is in a state that invokes a train, or an effect is pending on it. It is the record of what the server did, so its owner may not discard it; nothing was deleted. A row from before the record existed is judged by its state: a committed state, an effect's target, an invoking state, an invoked train's outcome target or a gated event's target. A retry answers the same, unless the owner's own event has moved the instance on since |
| `request-id-reused` | advance, send, start | the request id was last used for a different trigger, so this is not a retry of it; send a new id. A send is refused before its effect runs |
| `effect-bound` | advance (server) | the trigger runs the machine's irreversible effect from this state, so only a send fires it; nothing was written. The server's advance refuses it (the draft service and `IMachineInstances.Advance`); the engine's `Advance` does not, and fires the edge |
| `state-reserved` | save | the snapshot is in a committed state or an effect's target and the stored draft is not; only a send puts a draft there, and nothing was written |
| `draft-committed` | save | the stored draft is in a committed state or an effect's target and the save would change it; only a reset to the initial state that the machine declares from that state is accepted, and a save identical to the stored draft succeeds without a write |
| `draft-unreadable` | save | the stored draft fails rehydration, so only a reset to the initial state may overwrite it |
| `draft-invoking` | save | the stored draft is in a state that [invokes a train](/docs/statemachine/invoking-trains), or holds a live run, and the save would change it; only a declared transition or the run's outcome moves it on, and a save identical to the stored draft succeeds without a write |
| `outcome-bound` | advance | the trigger is an invoked train's outcome (`Building.done`, `.failed`, `.cancelled`), which only the run applies; nothing was written. The engine's `Advance` and the TypeScript twin's `advance` refuse it the same way, and apply it only through their outcome entry points |
| `invoke-limit-reached` | advance, start | entering the state would start a run while this user already holds the machine's limit of live invoked runs, counted across every machine (10 unless the machine sets `InvokedRunLimit`); nothing was written or queued |
| `invoke-forbidden` | advance, start | the train the state invokes refuses this user (its `[TraxAuthorize]` requirements); nothing was written or queued |
| `conflict` | save, advance, send, start, discard | another write changed the draft between this request reading it and writing it, for example a second tab saving at the same moment; nothing was written, reload and retry. Over a snapshot store a host substituted for Trax's, a send's draft was written while the effect ran: the effect's receipt is kept, and sending again replays it without running the effect once the draft holds the content the effect ran on (`draft-changed` until then) |
| `internal-error` | advance, send, start | a guard, reducer, validator or an invoking state's input mapping threw. The message is fixed and carries a reference; the exception is logged on the server under that reference |
| `no-effect` | send | the machine binds no irreversible effect (`RunsOnce`), so there is nothing to send; drive it with advance |
| `draft-changed` | send | over a snapshot store a host substituted for Trax's: this draft's effect already ran, on content the draft no longer holds (it was edited after the effect loaded it). Nothing was run and nothing was written, and the receipt stays with the claim. Restore the content the effect ran on, for example by saving the snapshot the send was made from, and send again to record it |
| `effect-in-progress` | advance, send, save, start, discard | an effect is pending on this draft: a send is running it and holds its lease, so every other write waits until it is recorded or fails; nothing was written, retry once it finishes (a send with the same request id). `DraftRefusalCodes.EffectInProgress`; a discard answers `discard-not-allowed` instead |
| `delivery-failed` | send | the effect threw, or returned no receipt; the draft was not advanced, so the send can be retried. The message is fixed and carries a reference; the exception is logged on the server under that reference. A cancelled request is not a failed delivery: it propagates as a cancellation. An `OperationCanceledException` from the effect itself, such as an outbound call's timeout, is a `delivery-failed`, and its claim stays in flight until the lease passes |

## Behavior

The engine's codes are constants on `RejectionReasons`: `NoTransition`, `GuardFailed`, `InvalidContext`,
`InternalError`, `InvalidInput`, `OutcomeBound`, `TimerBound` (`timer-bound`: a state's timer trigger, which only
the server fires, sent through an advance by anyone, trusted host code included), `TooLarge`, `EffectInProgress` and
`DiscardNotAllowed` (`discard-not-allowed`, which only the draft service answers, to a `discard`). An advance's
refusals are checked in this order, and the first that applies is the answer: an unknown state or trigger
(`no-transition`), an outcome trigger (`outcome-bound`), a timer trigger (`timer-bound`), no edge from the current
state (`no-transition`), `invalid-input`, `guard-failed`, a duration the target state's timer cannot read from the
successor context (`invalid-input`), `invalid-context` ([ADR 0048](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md)).
`effect-bound` is not among them: the engine fires an effect-bound edge like any other, and only the server refuses
it. `DraftRefusalCodes` declares the codes for refusals caused by the caller or the request: `Forbidden`
(`forbidden`), `RequestIdReused` (`request-id-reused`), `EffectBound` (`effect-bound`), `EffectInProgress` (`effect-in-progress`), `SaveNotAllowed` (`save-not-allowed`), `DiscardNotAllowed`
(`discard-not-allowed`), `ClientOutdated`
(`client-outdated`), `ServerOutdated` (`server-outdated`), `TooManyDrafts` (`too-many-drafts`, a start or a save
creating a draft past `MaxDraftsPerUser`), `InvalidRequestId` (`invalid-request-id`), `Unauthenticated`
(`unauthenticated`), `NoStart` (`no-start`, a machine that declares no start), `SystemOwned` (`system-owned`),
`NotFound` (`not-found`, a [`machineChanged`](/docs/sdk-reference/graphql-api/machines#machinechanged) subscription
to no instance the caller may watch) and `TooManySubscriptions` (`too-many-subscriptions`, past the subscription
caps).

`client-outdated` and `server-outdated` answer a [persisted machine document](/docs/sdk-reference/graphql-api/machines#persisted-documents)
of another version than the node runs, as a GraphQL error whose `extensions.code` is the code. The generic save returns `save-not-allowed`, the actor
advance, send, `start` and `discard` return `forbidden`, `discard` returns `discard-not-allowed`, and `start` and
`discard` return the others (see
[Actors, start and discard](/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard)).

`RehydrationErrorCodes` declares the codes a rehydrate answers: `Malformed` (`malformed`), `UnknownState`
(`unknown-state`), `VersionMismatch` (`version-mismatch`), `UnknownMachine` (`unknown-machine`) and
`InvalidContext` (`invalid-context`).

Over GraphQL these surface on the mutation's `problem` field, so a client reads the code and reacts (re-enable
a control on `guard-failed`, start fresh on `version-mismatch`) without ever seeing a stack trace. When the
refusal came from an exception, a guard, reducer or migration that threw or an effect that failed, the
message is a fixed sentence ending in `Reference: {id}`, and the server logs the exception under that id at
error level: the exception's own text never reaches the client.


## See also

- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard), the order the draft service checks its refusals in.
- [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity), `schema-mismatch` and `client-divergence`.
- [The machines root](/docs/sdk-reference/graphql-api/machines), where a typed operation answers these codes.
