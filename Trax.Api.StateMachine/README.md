# @trax/state-machine

The TypeScript half of the Trax portable snapshot state-machine: a small engine that represents a
multi-step flow's state as language-neutral JSON so a browser client and a C# backend
(`Trax.Effect.StateMachine`) agree on it. Illegal `(state, context)` pairs are unrepresentable and every
operation is total (it never throws).

This repo owns four things:

| Piece | Location |
| --- | --- |
| The `@trax/state-machine` package (engine, rule interpreter, typed facade, React hook) | [`src/`](src) |
| The `@trax/state-machine/client` subpath (the four generic snapshot mutations, machine-agnostic) | [`src/client/`](src/client) |
| The shared, language-neutral fixtures both engines drive (the cross-language oracle) | [`machines/`](machines) |
| The IR-driven generators (`generateContextTypes` / `generateMachineFactory`) and the node entrypoints the `trax machine` CLI shells out to | [`src/rules/generateTypes.ts`](src/rules/generateTypes.ts), [`tools/`](tools) |

## The contract

A **snapshot** is the only thing that crosses the wire or lands in a row:

```json
{ "machine": "turnstile", "version": 1, "state": "Locked", "context": {} }
```

- `state` is the current step (a string-literal union member).
- `context` is the per-state data, discriminated by `state`.

Two engines (this one and the C# one) implement the same behavior. They are kept identical by a shared
**differential corpus** under `machines/<machine>/`: one runtime enumerates every reachable case into a
committed golden and every other runtime replays it and must produce the same outcome. Only the result
*codes* (`no-transition`, `guard-failed`, `invalid-context`, `malformed`, ...) are contract; human-readable
detail text is free to differ. Byte-exact wire is guaranteed by a full RFC 8785 (JCS) canonical serializer in
each runtime, pinned by a shared conformance-vector suite.

## Data is generated, logic is interpreted

The generated context types read the IR's **constraints**, not just its field types: a field constrained
`arrayOf number` is emitted as `number[]`, and one constrained `oneOf ["a","b"]` as `"a" | "b"`. The IR
already describes the context exactly, so a consumer never has to hand-maintain a more precise copy of it.

**C# is the source of truth.** A machine is authored declaratively in C# (`Trax.Effect.StateMachine`), and
`IrExporter.Export` emits a neutral, versioned **IR** (`<machine>.ir.json`) carrying identity, structure, the
per-state context/input schemas, the declarative guards/reducers and invariants, committed states, effect
bindings, and the differential seeds.

The generators here turn that IR into the frontend twin: `generateContextTypes` emits the state/trigger/context
types, and `generateMachineFactory` emits a typed factory that embeds the IR, exports as `irHash` the SHA-256
of the `*.ir.json` file with trailing newlines stripped (the version-skew handshake token, and not the hash
of the embedded constant, which differs), and constructs the running machine via `typedMachineFromIr(ir, irHash)`,
which wraps `machineFromIr` in the typed facade. There is **no per-language logic to hand-write** for the ~92%
of guards/reducers the declarative vocabulary covers: they are data in the IR, run by the rule interpreter
(`src/rules/interpreter.ts` here, `RuleEvaluator` in C#). A genuinely-custom guard/reducer is bound by *name*
in the IR and hand-written per runtime (the ~8% escape hatch); no machine here has one.

The generated files carry an `AUTO-GENERATED / Do not edit by hand` banner; any hand-written UI glue lives in a
separate sibling *bridge* file. Regeneration and the drift check share one code path, so they cannot disagree.

```bash
npm test                # vitest: conformance, totality, serialization, migration, codegen drift
```

Consumers drive regeneration through `trax machine generate`, the Trax.Cli `machine` command group, which
exports the IR in process and shells `node` at the entrypoints in [`tools/`](tools) for the twin and the
corpus; the exporter is also callable directly on the C# side. Every machine here is on that pipeline. The old
hand-authored `machine.json` and the `generate.mjs` node generator that read it are deleted.

## Driving a UI that owns its own data

`useMachine` / `MachineController` assume the machine owns the snapshot. A wizard built on a form
library doesn't — the form is the source of truth, and the machine is an oracle: *given these values at
this step, may I fire this, and where does it land?* `createFormView` derives that whole bridge from
the only two things an app can supply:

```ts
const wizard = createFormView(writeToCongress, {
  steps:     { welcome: 'Welcome', topic: 'Topic', compose: 'Compose', confirmation: 'Sent' },
  toContext: (values, options = {}) => ({ subject: values.compose.subject, /* … */ }),
});

wizard.advance('topic', values, 'Next');   // → { ok: true, step: 'compose' }
wizard.can('topic', values, 'Next');       // → false while the guard declines
wizard.serialize('topic', values);         // → canonical JSON for an autosave
wizard.resume(storedJson);                 // → { step, snapshot } | null
```

The step map is given in **one** direction; `stepFor` is derived, so the two can't drift (a many-to-one
map is rejected at construction, where the inverse would be ambiguous). Snapshots are stamped with the
machine's own id and version. `resume` returns null for a COMMITTED run, and which states those are
comes from the machine's IR (`.Committed()` in C#) — never a state name repeated in the app.

## Talking to the server — `@trax/state-machine/client`

The engine above is pure: a reducer and a rule interpreter, no I/O. Persisting a draft is a separate
subpath, so importing the engine never pulls a transport into a bundle that only wanted the reducer.

`Trax.Effect.StateMachine.Persistence` exposes **one** set of operations for **every** machine —
`saveSnapshot`, `advanceSnapshot`, `loadSnapshot`, `sendSnapshot` under `dispatch { stateMachine { … } }`,
each carrying a `machine` discriminator the server resolves in its registry. So there is one client, not
one per machine:

```ts
import { createSnapshotClient, createHttpExecutor, createDraftSession } from '@trax/state-machine/client';

const client  = createSnapshotClient(createHttpExecutor('/graphql'));
const session = createDraftSession({ client, machine: writeToCongress, id: DRAFT_ID });

await session.save(machine.serialize(snapshot));   // soft autosave
await session.advance('Next');                     // authoritative transition
await session.send(requestId);                     // the exactly-once effect
```

- **`createSnapshotClient(executor)`** — the four operations, machine-agnostic: `(machine, id)` on every
  call. The `executor` is the seam to the host's GraphQL stack. An app with generated typed documents
  writes a small one against those; an app without can use `createHttpExecutor`, which builds the four
  documents itself.
- **`createDraftSession({ client, machine, id })`** — one machine's one draft, with `machine` and
  `schemaHash` read off the `TypedMachine` so they cannot disagree with the machine actually running.
  Every method is **total**: a refusal arrives as a typed `problem` and an unreachable server as
  `transport-error`, so no call site needs a try/catch. Fallback copy is overridable via `messages`.

The `schemaHash` threaded through every call is the version-skew handshake: a client whose machine has
drifted from the deployed one is refused with `schema-mismatch` and reloads, rather than writing under an
outdated contract. `advance` also accepts `clientResult` — the snapshot the local twin computed for that
same advance — so the server can refuse a `client-divergence` instead of storing a result the two engines
disagree on.

## Status

Every machine is on the IR pipeline and proven in agreement by the differential: the `turnstile` proof machine,
the richer `checkout` (multi-key context, count guards, an exactly-once effect), and the `write-to-congress`
product machine (the first real consumer, in nwyc). The `trax machine` CLI that drives regeneration has
shipped. What is left: TypeScript is still the differential oracle, so the flip to a C# enumerator (Increment
D) is the main remaining increment, and the derived `structure.json` golden still needs retiring. See
[`docs/design/tier1-machine-codegen.md`](docs/design/tier1-machine-codegen.md) and
[`docs/design/phase0-codegen-scope.md`](docs/design/phase0-codegen-scope.md).
