---
layout: default
title: Result codes
description: Every result code a state machine advance, rehydrate or snapshot mutation can return, what each means and how a client should react.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 9
---

# Result codes

Every advance and rehydrate returns a result, never an exception. On the unhappy path the result carries a
code. Only the code is contract; the detail text is free to differ across runtimes and is for humans, not
branching. The table lists every code the engine and the snapshot mutations return. A client should still
treat a code it does not recognise as a refusal, so that a code added later does not break it.

| Code | Returned by | Meaning |
| --- | --- | --- |
| `no-transition` | advance, send | no edge matches the `(state, trigger)` pair. On a send, the stored draft is not in the state the effect's transition leaves (for the sample's checkout, a draft still at `Cart`), so nothing was run or written |
| `guard-failed` | advance | an edge matched but its guard rejected the trigger; the detail is the `Because(...)` message |
| `invalid-context` | advance, rehydrate | the resulting (advance) or stored (rehydrate) context failed the target state's rule |
| `malformed` | rehydrate, advance (persisted) | the snapshot JSON could not be parsed, or its context holds a value no store can keep: a number outside the range of a double (`1e400`) or a NUL character in a string or key. On a persisted advance, the trigger input is not valid JSON, or the result held such a value; nothing was written |
| `unknown-state` | rehydrate | the snapshot names a state the definition does not have. Only the exact declared name is a state: `"1"`, `" Unlocked"` and `"Locked, Unlocked"` are unknown, and an unknown trigger token is `no-transition` |
| `version-mismatch` | rehydrate | the snapshot version is newer than the definition, or a [migration](/docs/sdk-reference/statemachine-api/migrations) is missing |
| `unknown-machine` | rehydrate, save, advance, load, send | no registered machine has that name |
| `unauthenticated` | save, advance, load, send | the request carries no user: `ISnapshotPrincipal.CurrentUserKey` is null. Checked before anything else, so nothing was read or written |
| `not-found` | advance, load, send | no draft with that id exists for this user and machine, or it expired and was deleted; start a new one |
| `schema-mismatch` | save, advance, load, send | the client's machine [schema hash](/docs/sdk-reference/statemachine-api/runtime-integrity) differs from the server's; the client is out of date and should reload |
| `client-divergence` | advance | the client's computed result differs from the server's authoritative result ([divergence detection](/docs/sdk-reference/statemachine-api/runtime-integrity)); nothing was written, reload |
| `too-large` | save, advance | the snapshot, the trigger input, or the advanced snapshot exceeds `SnapshotLimits.MaxSnapshotBytes` (64 KiB); nothing was written |
| `request-id-reused` | advance, send | the request id was last used for a different trigger, so this is not a retry of it; send a new id. A send is refused before its effect runs |
| `effect-bound` | advance | the trigger runs the machine's irreversible effect from this state, so only a send fires it; nothing was written |
| `state-reserved` | save | the snapshot is in a committed state or an effect's target and the stored draft is not; only a send puts a draft there, and nothing was written |
| `draft-committed` | save | the stored draft is in a committed state or an effect's target and the save would change it; only a reset to the initial state that the machine declares from that state is accepted, and a save identical to the stored draft succeeds without a write |
| `draft-unreadable` | save | the stored draft fails rehydration, so only a reset to the initial state may overwrite it |
| `conflict` | save, advance, send | another write changed the draft between this request reading it and writing it, for example a second tab saving at the same moment; nothing was written, reload and retry. On a send, the draft was written while the effect ran: the effect's receipt is kept, and sending again replays it without running the effect once the draft holds the content the effect ran on (`draft-changed` until then) |
| `internal-error` | advance, send | a guard, reducer or validator threw. The message is fixed and carries a reference; the exception is logged on the server under that reference |
| `no-effect` | send | the machine binds no irreversible effect (`RunsOnce`), so there is nothing to send; drive it with advance |
| `draft-changed` | send | this draft's effect already ran, on content the draft no longer holds (it was edited after the effect loaded it). Nothing was run and nothing was written, and the receipt stays with the claim. Restore the content the effect ran on, for example by saving the snapshot the send was made from, and send again to record it |
| `effect-in-progress` | send | another send is running this draft's effect right now and holds its lease; nothing was run, retry with the same request id once it finishes |
| `delivery-failed` | send | the effect threw, or returned no receipt; the draft was not advanced, so the send can be retried. The message is fixed and carries a reference; the exception is logged on the server under that reference. A cancelled request is not a failed delivery: it propagates as a cancellation. An `OperationCanceledException` from the effect itself, such as an outbound call's timeout, is a `delivery-failed`, and its claim stays in flight until the lease passes |

Over GraphQL these surface on the mutation's `problem` field, so a client reads the code and reacts (re-enable
a control on `guard-failed`, start fresh on `version-mismatch`) without ever seeing a stack trace. When the
refusal came from an exception, a guard, reducer or migration that threw or an effect that failed, the
message is a fixed sentence ending in `Reference: {id}`, and the server logs the exception under that id at
error level: the exception's own text never reaches the client.
