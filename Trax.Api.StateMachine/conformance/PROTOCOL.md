# The conformance runner and its testee protocol

`tools/conformance/run.mjs` turns every case in `machines/` and `conformance/` into requests, sends them to one
engine, the **testee**, and compares what it answers with what the case expects. An engine in any language joins by
implementing the testee side of this protocol over its public API, not over its internals, so a bug in how a binding
marshals a value is caught too. Only codes and wire bytes are compared; messages are never sent.

## Running it

```bash
node tools/conformance/run.mjs --testee node tools/conformance/testee-ts.mjs   # spawn a testee command
node tools/conformance/run.mjs --stdio                                          # be spawned by one
```

With `--testee`, the runner starts the command and talks to it over the command's stdin and stdout. With `--stdio`,
the roles on the pipes are reversed: the runner writes requests on its own stdout and reads answers on its stdin, so a
test in another language (the C# `ConformanceRunnerTests`) can start the runner and answer from inside its own
process. Either way the runner writes its report on stderr and exits 0 when every case passed, 1 when any failed.

`--only <prefix>` runs the cases whose name starts with the prefix.

## The protocol

One JSON object per line, UTF-8, in both directions. The runner sends a request and waits for its answer before
sending the next, so a testee may answer synchronously. Every request has an `id` and an `op`; the answer carries the
same `id`. A testee answers an `op` it does not implement with `{ "id", "unsupported": true }`, which fails that case.

**`load`** gives the testee a machine to serve under `machine`, as its IR. A testee that builds machines from an IR
builds it; one that only has machines compiled in checks that the IR is the one its machine exports, and answers
`error` when it is not.

```json
{ "id": 1, "op": "load", "machine": "turnstile", "ir": { ... } }
{ "id": 1, "loaded": true }
{ "id": 1, "error": "the turnstile this engine has exports a different IR" }
```

**`advance`**, **`outcome`** and **`timer`** apply a trigger to a snapshot through the entry point that owns it: a
client's trigger through advance, an invoked train's outcome (`Fetching.done`) through the outcome path, a state's
timer (`Review.after`) through the timer path. `snapshot` is an object (`machine`, `version`, `state`, `context`);
`input` is absent or `null` when the trigger takes none. The answer is the canonical wire after the step, or the
refusal code.

```json
{ "id": 2, "op": "advance", "machine": "turnstile", "snapshot": { ... }, "trigger": "Coin", "input": { "coin": "quarter" } }
{ "id": 2, "outcome": "transitioned", "wire": "{\"machine\":\"turnstile\",...}" }
{ "id": 2, "outcome": "rejected", "reason": "GUARD_FAILED" }
```

**`rehydrate`** reads stored text as a snapshot of `machine`. The answer is the snapshot's canonical wire, or the
refusal code.

```json
{ "id": 3, "op": "rehydrate", "machine": "turnstile", "text": "{\"machine\":\"turnstile\",...}" }
{ "id": 3, "result": "ok", "wire": "..." }
{ "id": 3, "result": "error", "code": "MALFORMED" }
```

**`serialize`** writes a snapshot object of `machine` as canonical wire, without validating it.

```json
{ "id": 4, "op": "serialize", "machine": "checkout", "snapshot": { ... } }
{ "id": 4, "wire": "..." }
```

**`canonical`** writes the snapshot `{ "machine": "m", "version": 1, "state": "S", "context": <context> }` as
canonical wire, for the vectors in `canonical-wire.json`. The context is given as JSON text in `context`, or, for a
string no JSON parser carries (a lone surrogate), as UTF-16 code units: `stringUnits` is the context
`{ "s": <string> }`, `keyUnits` the context `{ <string>: 1 }`.

```json
{ "id": 5, "op": "canonical", "context": "{\"n\":1e21}" }
{ "id": 5, "wire": "{\"machine\":\"m\",\"version\":1,\"state\":\"S\",\"context\":{\"n\":1e+21}}" }
```

**`end`** is the last request. The testee answers `{ "id" }` and exits.

## What the runner sends

| Source | Requests |
| --- | --- |
| `machines/<m>/<m>.ir.json` | `load` |
| `machines/<m>/differential.json` | one `advance`, `outcome` or `timer` per case, by the trigger's kind in the IR |
| `machines/<m>/fixtures/advance/*.json` | `advance` |
| `machines/<m>/fixtures/rehydrate/*.json` | `rehydrate` |
| `machines/<m>/wire-handoff.json` | `serialize` |
| `conformance/strict-input/` | `load`, then one `advance` per case |
| `conformance/canonical-wire.json` | `canonical` |

`machines/turnstile/migration.json` is not sent: it migrates from a v1 to a v2 machine that has no IR, so each engine
replays it in its own tests.
