# The conformance runner and its testee protocol

`run.mjs`, in this directory, turns every case in `machines/` and the tables beside it into requests, sends them to one
engine, the **testee**, and compares what it answers with what the case expects. An engine in any language joins by
implementing the testee side of this protocol over its public API, not over its internals, so a bug in how a binding
marshals a value is caught too. Only codes and wire bytes are compared; messages are never sent.

## Running it

From `Trax.StateMachine/`:

```bash
node conformance/run.mjs --testee node ts/tools/conformance/testee-ts.mjs   # spawn a testee command
node conformance/run.mjs --stdio                                            # be spawned by one
```

The runner and `kit.mjs` use only node's own modules, so they run without an `npm ci`.

With `--testee`, the runner starts the command and talks to it over the command's stdin and stdout. With `--stdio`,
the roles on the pipes are reversed: the runner writes requests on its own stdout and reads answers on its stdin, so a
test in another language (the C# `ConformanceRunnerTests`) can start the runner and answer from inside its own
process. Either way the runner writes its report on stderr and exits 0 when every case passed, 1 when any failed.

`--only <prefix>` runs the cases whose name starts with the prefix, and every `load`. A prefix no case has is an error
(exit 2), not an empty pass.

## The protocol

One JSON object per line, UTF-8, in both directions. A line ends at `\n` and nowhere else: JSON.stringify writes
U+2028 and U+2029 literally inside a string, so a reader that also breaks lines there (node's readline does) cuts a
request in two. The runner sends a request and waits for its answer before
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

A testee that builds machines from an IR binds one handler to every machine it loads: a custom guard named `throws`,
which throws (panics, in Rust), so a case can pin `INTERNAL_ERROR`. It binds no other handler, so every other custom
rule is one the engine cannot evaluate: a guard that does not hold, a reduction that keeps the context, a state
requirement that fails except on a snapshot the server sent. The `vault` machine pins that. A testee whose machines are
compiled has every handler bound, so for `vault` it checks the IR against its compiled machine and then serves it from
the IR, with only `throws` bound, as an IR testee does.

Some loads must be refused: the IRs in `unreadable-ir/cases.json` each name one construct no engine knows (a rule, a
reduction, a field type, a comparison or a rule source) or one field no engine holds (a field, a rule's path or a set
step's source named `__proto__`, `constructor` or `prototype` in any segment), and a testee that builds machines from an
IR answers each with `error`. Any `error` passes, since the message is the engine's own; `loaded` fails. A testee that only has compiled
machines answers `error` because it has no machine of that name, which passes but proves nothing: a compiled machine's
rules are types in its language and cannot name an unknown construct, so these cases hold the engines that read IRs.

**`advance`**, **`outcome`** and **`timer`** apply a trigger to a snapshot through the entry point that owns it: a
client's trigger through advance, an invoked train's outcome (`Fetching.done`) through the outcome path, a state's
timer (`Review.after`) through the timer path. `snapshot` is an object (`machine`, `version`, `state`, `context`);
`input` is absent or `null` when the trigger takes none. The answer is the canonical wire after the step, or the
refusal code. A case may send a trigger through an entry point that does not own it, to pin the refusal
(`OUTCOME_BOUND`, `TIMER_BOUND`, `NO_TRANSITION`).

```json
{ "id": 2, "op": "advance", "machine": "turnstile", "snapshot": { ... }, "trigger": "Coin", "input": { "coin": "quarter" } }
{ "id": 2, "outcome": "transitioned", "wire": "{\"machine\":\"turnstile\",...}" }
{ "id": 2, "outcome": "rejected", "reason": "GUARD_FAILED" }
```

**`rehydrate`** reads stored text as a snapshot of `machine`. With `"fromServer": true` it reads a snapshot the server
sent, which counts a custom state requirement with no handler as passed; a testee whose engine has every handler bound
reads it as any other. The answer is the snapshot's canonical wire, or the refusal code.

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

**`plan`** finds the events that take `snapshot` of `machine` to the state `target` with the page's fields applied:
what a client sends to bring the server to where a page that ran ahead of it is. `fields` is the event that writes the
page's fields (`trigger` and its `input`), or null; `navigation` is the triggers, without input, that move between
states; `maxDepth` is the longest plan to look for, twice the machine's states when absent.

```json
{ "id": 6, "op": "plan", "machine": "wizard", "snapshot": { ... }, "target": "Review",
  "fields": { "trigger": "Update", "input": { "name": "Ada", "email": "ada@example.com" } }, "navigation": ["Begin", "Next", "Back"] }
{ "id": 6, "result": "plan", "events": [{ "trigger": "Begin" }, { "trigger": "Update", "input": { ... } }, { "trigger": "Next" }] }
{ "id": 6, "result": "unreachable" }
{ "id": 6, "result": "exceeded" }
{ "id": 6, "result": "undetermined", "trigger": "Skip", "events": [] }
{ "id": 6, "result": "invalid", "trigger": "Send" }
```

Every engine answers the same plan, so the search is fixed:

- **Invalid first.** A `target` that is not a state answers `invalid` with `target`. Then the fields trigger, then each
  navigation trigger in order: one the machine does not have, one bound to the effect, an outcome, a timer, or a
  navigation trigger equal to the fields trigger answers `invalid` with that `trigger`.
- **The goal.** A snapshot is the goal when its state is `target` and, with `fields` given, advancing it by the fields
  event transitions to the same wire. A fields event the target state refuses never reaches the goal.
- **Breadth first, in the caller's order.** From each snapshot the edges are tried in a fixed order: the fields event
  first, then `navigation` as given, each through `advance`. A refused edge is skipped. A snapshot whose wire was seen
  is not visited again. The start is the goal test's first case; after it, each new snapshot is tested as it is
  reached, and the first that is the goal ends the search with the events on its path. A plan never depends on the
  order an engine lists its transitions in.
- **Depth.** A snapshot reached by `maxDepth` events is not expanded, and the search answers `exceeded` rather than
  `unreachable` when it ends without a plan after leaving one unexpanded, or after expanding 10 000 snapshots.
- **Custom rules.** An edge whose transition rests on a custom rule the engine holds no handler for (a custom guard, a
  custom reduction, or a custom requirement of the state it enters) is not tried. The first such edge the search meets
  is kept: when the search ends without a plan, the answer is `undetermined` with that `trigger` and `events`, the
  path to the snapshot it leaves from. A client sends those, then that event unpredicted, and plans again from the
  answer.

**`end`** is the last request. The testee answers `{ "id" }` and exits.

## What the runner sends

| Source | Requests |
| --- | --- |
| `machines/<m>/<m>.ir.json` | `load` |
| `machines/<m>/differential.json` | one `advance`, `outcome` or `timer` per case, by the trigger's kind in the IR |
| `machines/<m>/fixtures/advance/*.json` | `advance`, `outcome` or `timer` by the trigger's kind, or the fixture's `op`, which sends a trigger through another entry point |
| `machines/<m>/fixtures/rehydrate/*.json` | `rehydrate`, with the fixture's `fromServer` |
| `machines/<m>/wire-handoff.json` | `serialize` |
| `conformance/strict-input/` | `load`, then one `advance` per case |
| `conformance/plan/` | `load` of `wizard.ir.json`, then one `plan` per case in `cases.json` |
| `conformance/unreadable-ir/cases.json` | one `load` per case, which must be refused |
| `conformance/canonical-wire.json` | `canonical` |

`machines/turnstile/migration.json` is not sent: it migrates from a v1 to a v2 machine that has no IR, so each engine
replays it in its own tests.
