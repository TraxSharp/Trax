---
layout: default
title: Result codes
parent: State Machine API
grand_parent: SDK Reference
nav_order: 9
---

# Result codes

Every advance and rehydrate returns a result, never an exception. On the unhappy path the result carries a
code. Only the code is contract; the detail text is free to differ across runtimes and is for humans, not
branching.

| Code | Returned by | Meaning |
| --- | --- | --- |
| `no-transition` | advance | no edge matches the `(state, trigger)` pair |
| `guard-failed` | advance | an edge matched but its guard rejected the trigger; the detail is the `Because(...)` message |
| `invalid-context` | advance, rehydrate | the resulting (advance) or stored (rehydrate) context failed the target state's rule |
| `malformed` | rehydrate, advance (persisted) | the snapshot JSON could not be parsed, or its context holds a value no store can keep: a number outside the range of a double (`1e400`) or a NUL character in a string or key. On a persisted advance, the result held such a value and nothing was written |
| `unknown-state` | rehydrate | the snapshot names a state the definition does not have. Only the exact declared name is a state: `"1"`, `" Unlocked"` and `"Locked, Unlocked"` are unknown, and an unknown trigger token is `no-transition` |
| `version-mismatch` | rehydrate | the snapshot version is newer than the definition, or a [migration](/docs/sdk-reference/statemachine-api/migrations) is missing |
| `unknown-machine` | rehydrate | no registered machine has that name |
| `not-found` | advance, load, send | no draft with that id exists for this user and machine, or it expired and was deleted; start a new one |
| `schema-mismatch` | save, advance, load, send | the client's machine [schema hash](/docs/sdk-reference/statemachine-api/runtime-integrity) differs from the server's; the client is out of date and should reload |
| `client-divergence` | advance | the client's computed result differs from the server's authoritative result ([divergence detection](/docs/sdk-reference/statemachine-api/runtime-integrity)); nothing was written, reload |
| `too-large` | save, advance | the snapshot, the trigger input, or the advanced snapshot exceeds `SnapshotLimits.MaxSnapshotBytes` (64 KiB); nothing was written |
| `request-id-reused` | advance, send | the request id was last used for a different trigger, so this is not a retry of it; send a new id. A send is refused before its effect runs |
| `effect-bound` | advance | the trigger runs the machine's irreversible effect from this state, so only a send fires it; nothing was written |
| `state-reserved` | save | the snapshot is in a committed state or an effect's target and the stored draft is not already there; only a send puts a draft there, and nothing was written |
| `draft-committed` | save | the stored draft is in a committed state and the save would move it anywhere but that state or the initial state |
| `draft-unreadable` | save | the stored draft fails rehydration, so only a reset to the initial state may overwrite it |
| `conflict` | save, advance, send | another write changed the draft between this request reading it and writing it, for example a second tab saving at the same moment; nothing was written, reload and retry |
| `internal-error` | advance, send | a guard, reducer or validator threw. The message is fixed and carries a reference; the exception is logged on the server under that reference |
| `no-effect` | send | the machine binds no irreversible effect (`RunsOnce`), so there is nothing to send; drive it with advance |
| `effect-in-progress` | send | another send is running this draft's effect right now and holds its lease; nothing was run, retry with the same request id once it finishes |
| `delivery-failed` | send | the effect threw, or returned no receipt; the draft was not advanced, so the send can be retried. The message is fixed and carries a reference; the exception is logged on the server under that reference. A cancelled request is not a failed delivery: it propagates as a cancellation |

Over GraphQL these surface on the mutation's `problem` field, so a client reads the code and reacts (re-enable
a control on `guard-failed`, start fresh on `version-mismatch`) without ever seeing a stack trace. When the
refusal came from an exception, a guard, reducer or migration that threw or an effect that failed, the
message is a fixed sentence ending in `Reference: {id}`, and the server logs the exception under that id at
error level: the exception's own text never reaches the client.
