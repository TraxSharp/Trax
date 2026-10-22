# Trax.StateMachine

The state machines, in every language they ship in: the C# engine and its persistence
(`Trax.StateMachine`, `.Persistence`, `.Testing` on NuGet), the TypeScript engine and client
(`@traxsharp/state-machine` on npm), and the conformance kit that holds the two to the same
answers. It sits between `Trax.Effect`, whose trains a machine invokes and whose tables it is
stored in, and `Trax.Mediator`, which launches those trains, so a change here reaches Trax.Mediator,
Trax.Scheduler, Trax.Api, Trax.Cli and Trax.Samples. Trax.Effect is upstream and never sees it.

This file is the entry point. It routes; it does not restate the rules.

## Layout

The folder is laid out by language, with what spans the languages at its root.

| Path | What it holds |
| --- | --- |
| `dotnet/` | The C# engine, and `Trax.StateMachine.Client`, which drives a served machine from .NET. A .NET folder like any other: `Trax.StateMachine.slnx`, `Directory.Build.props`, `.config/dotnet-tools.json`, the NuGet `README.md`, `src/` and `tests/` (with `Trax.StateMachine.Tests.Meta`). |
| `ts/` | `@traxsharp/state-machine`: the engine (`src/`), the model every typed view is written from and an emitter per language (`src/codegen/`, TypeScript's in `src/rules/generateTypes.ts`), the client a page drives a served machine through (`src/client/`, `src/server/`, `src/react.ts`), and the node entrypoints `trax machine` shells out to (`tools/`), which `tools/bundle-cli.mjs` bundles into Trax.Cli's package. |
| `conformance/` | Engine-neutral data any engine replays: the shared machines (`machines/<m>/`, each machine's IR, fixtures, differential corpus and migration golden), the IR's JSON Schema (`ir.schema.json`, normative), the canonical-wire vectors, the refusal-code registry, the strict-input cases, and the runner (`run.mjs`, `kit.mjs`) that sends all of it to one engine over the protocol in `PROTOCOL.md`. A vector goes here, not in one engine's tests. |
| `conformance/client/` | The client kit: transcripts of a host, request by request and push by push (`transcripts/`), the fake host that plays them (`host.mjs`) and the runner (`run.mjs`) that drives one client's testee through them over the protocol in its `PROTOCOL.md`. The TypeScript client (`ts/tools/conformance/client-testee-ts.mjs`) and the C# one (`ClientConformanceTests`) pass it. |
| `docs/adr/` | This folder's decisions, for every language. |

Another language's engine gets a directory of its own beside `dotnet/` and `ts/`, and joins the kit by
implementing a testee for `conformance/run.mjs`.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. Read the relevant one before proposing to
change a rule, and if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| a state-machine draft's `requestId` replay, or `ISnapshotStore.UpdateWithRequest` | [0001](./docs/adr/0001-a-request-id-replays-only-the-request-it-recorded.md), an id replays only for the request it recorded, wherever the draft is now |
| what a state-machine draft's autosave or advance may write, what the effect runner commits or replays and when a reset releases its claim, `effect_claim.content_fingerprint`, `RunsOnce`, or `Committed()` | [0002](./docs/adr/0002-only-the-effect-runner-reaches-a-committed-state.md), only the effect runner puts a draft into a committed state or an effect's target |
| `trax.snapshot_transition`, `IDataContext.SnapshotTransitions`, or a new path that writes a state-machine snapshot | [0003](./docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md), every snapshot write through the store adds one history row in its transaction, and the history goes with the instance |
| a state that invokes a train, or where its outcome goes | central `docs/0046`, only the entry that started the run receives its outcome |
| what a client may send, or what the TypeScript twin may decide | central `docs/0048`, a machine is served as a typed API and the server applies every transition; a wrong prediction is a UX bug, not a correctness one |
| a state that spawns a child machine | central `docs/0052`, a child is spawned the way a train is invoked |

The tables the machines are stored in (`SnapshotDraft`, `EffectClaim`, their `Persistent*` pairs and
migrations 040 to 076) stay in Trax.Effect, with every other table (effect `0003`, central `0036`).
A schema change for a machine is a Trax.Effect change.

Decisions binding more than one folder live in the central corpus at `Trax.Docs/adr/`; the ones
tagged `statemachine` bind this folder.

## The rules, and where they are written

- **C# is the source of truth.** A machine is declared in C# and exported as an IR; the TypeScript
  engine interprets the IR's declarative guards and reducers rather than generating logic. A custom
  rule is bound by name and has no handler there, so the twin defers to the server on it. See
  [the codegen pipeline](https://traxsharp.net/docs/statemachine/codegen-pipeline) and
  [the IR format](https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-format).
- **The engines agree case for case.** The differential corpus under `conformance/machines/<m>/` is
  enumerated by the TypeScript engine and replayed by the C# one, and the conformance runner sends
  the whole kit to each engine. Only result codes and wire bytes are contract; messages may differ.
  The wire is canonical JSON (RFC 8785) in both
  ([canonical wire](https://traxsharp.net/docs/statemachine/canonical-wire)).
- **Every operation is total.** The engines and the clients answer a refusal or a transport failure
  as a value (`problem`, `TRANSPORT_ERROR`), never by throwing.
- **Generated files are never edited.** They carry an `AUTO-GENERATED` banner; regeneration and the
  drift check share one code path.
- **The public surfaces are committed baselines.** The C# packages' are in
  `dotnet/tests/Trax.StateMachine.Tests.Meta/PublicApi/`; the npm package's exports are in
  `ts/src/exports.test.ts`, and each one is documented on
  [the TypeScript package reference](https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package).
  Adding or removing an export changes both in the same pull request.

## Guards

`dotnet/tests/Trax.StateMachine.Tests.Meta/` holds the convention guards every .NET folder runs,
among them `DependencyDirectionTests`: a machine project may reference Trax.Core and Trax.Effect,
never the mediator or anything below it, and no Effect project may reference a machine project.
The census is on, as in every folder.

A C# test that runs or reads the TypeScript engine is marked `[Category(Categories.CrossEngine)]`.
CI runs those apart from the rest (below), so mark any new one, or a change to the TypeScript engine
alone will not run it.

## Running the tests

The C# engine, from `dotnet/`. The persistence suites need Postgres; `Trax.Effect`'s compose file
provides it (`docker compose -f ../../Trax.Effect/docker-compose.yml up -d`). The cross-engine tests
need node and the TypeScript engine's packages.

```bash
dotnet test
```

The TypeScript engine, from `ts/`:

```bash
npm ci --ignore-scripts
npm test          # vitest: conformance, totality, serialization, migration, codegen drift
npm run typecheck
```

The conformance kit through the TypeScript engine, from this folder (the C# engine answers it in
`ConformanceRunnerTests`):

```bash
node conformance/run.mjs --testee node ts/tools/conformance/testee-ts.mjs
node conformance/client/run.mjs --testee node ts/tools/conformance/client-testee-ts.mjs
```

## CI

This folder has no folder job. `.github/ci/packages.json` gives it three suites, each with its own
paths, so a change to one language does not rebuild the other:

| Suite | Runs for | Does |
| --- | --- | --- |
| `state-machine-dotnet` | `dotnet/`, `conformance/`, or Trax.Effect | the folder job's checks over `dotnet/Trax.StateMachine.slnx` (format, locks, deprecations, pack with package validation, coverage) and its tests, without the CrossEngine ones |
| `state-machine-ts` | `ts/`, `conformance/` | typecheck, vitest, build, `npm pack --dry-run`, audit |
| `state-machine-conformance` | anything in the folder, or Trax.Effect | the CrossEngine tests, then the kit through the TypeScript engine |

Trax.Mediator and Trax.Cli list this folder upstream, so a change here also runs them and the
folders below them.
