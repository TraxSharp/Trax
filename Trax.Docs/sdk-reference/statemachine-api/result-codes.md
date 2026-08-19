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
| `malformed` | rehydrate | the snapshot JSON could not be parsed |
| `unknown-state` | rehydrate | the snapshot names a state the definition does not have |
| `version-mismatch` | rehydrate | the snapshot version is newer than the definition, or a [migration](/docs/sdk-reference/statemachine-api/migrations) is missing |
| `unknown-machine` | rehydrate | no registered machine has that name |
| `schema-mismatch` | save, advance, load, send | the client's machine [schema hash](/docs/sdk-reference/statemachine-api/runtime-integrity) differs from the server's; the client is out of date and should reload |
| `client-divergence` | advance | the client's computed result differs from the server's authoritative result ([divergence detection](/docs/sdk-reference/statemachine-api/runtime-integrity)); reload |

Over GraphQL these surface on the mutation's `problem` field, so a client reads the code and reacts (re-enable
a control on `guard-failed`, start fresh on `version-mismatch`) without ever seeing a stack trace.
