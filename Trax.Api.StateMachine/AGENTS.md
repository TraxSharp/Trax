# Trax.Api.StateMachine

`@traxsharp/state-machine` on npm: the TypeScript twin of `Trax.Effect.StateMachine`, the client a page drives a
served machine through, and the generators `trax machine generate` runs. It is its own npm project, not a .NET
folder, and releases with every Trax release at the same version (`Trax.Docs/adr/0042`).

This file is the entry point. It routes; it does not restate the rules.

## Layout

| Path | What it holds |
| --- | --- |
| `src/` | The engine (`SnapshotMachine`), the typed view (`typedMachineFromIr`), the in-memory stores (`MachineController`, `createFormView`) and the rule interpreter (`src/rules/`). Pure: no I/O. |
| `src/client/` | `@traxsharp/state-machine/client`: the executor over persisted documents, draft sessions, `createMachineServer` and `ServerMachineController`. |
| `src/server/`, `src/react.ts` | The server-authoritative controller, and `@traxsharp/state-machine/react` (`useServerMachine`, `useRememberedMachine`). |
| `src/rules/generateTypes.ts` | `generateContextTypes` and `generateMachineFactory`, which turn an IR into a twin. |
| `tools/` | The node entrypoints the `trax machine` commands shell out to (`generate-twin.mjs`, `generate-corpus.mjs`), and `bundle-cli.mjs`, which bundles them with the engine compiled in for Trax.Cli's package. |
| `machines/` | The shared fixtures both engines drive: each machine's IR, its generated twin and its differential corpus. |
| `docs/design/` | The design notes the codegen was built from. Historical: the published docs are the rule. |

## The rules, and where they are written

- **C# is the source of truth.** A machine is declared in C# and exported as an IR; this package interprets the IR's
  declarative guards and reducers rather than generating logic. A custom rule is bound by name and has no handler
  here, so the twin defers to the server on it. See [the codegen pipeline](https://traxsharp.net/docs/statemachine/codegen-pipeline)
  and [the IR format](https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-format).
- **The two engines agree case for case.** The differential corpus under `machines/<machine>/` is enumerated here
  and replayed by the C# engine; only result codes are contract, messages may differ. The wire is canonical JSON
  (RFC 8785) in both ([canonical wire](https://traxsharp.net/docs/statemachine/canonical-wire)).
- **Every operation is total.** The engine and the clients answer a refusal or a transport failure as a value
  (`problem`, `TRANSPORT_ERROR`), never by throwing.
- **The server applies every transition** (`Trax.Docs/adr/0048`): the twin only predicts, and the client sends only
  persisted ids from the host's manifest.
- **Generated files are never edited.** They carry an `AUTO-GENERATED` banner; regeneration and the drift check share
  one code path.
- **The exports are a committed baseline** (`src/exports.test.ts`), and each one is documented on
  [the TypeScript package reference](https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package).
  Adding or removing an export changes both in the same pull request.

## Running the tests

```bash
npm ci --ignore-scripts
npm test          # vitest: conformance, totality, serialization, migration, codegen drift
npm run typecheck
```

The Trax.Effect conformance tests and the Trax.Cli byte-parity tests read `machines/`, which is why CI lists this
folder upstream of both.
