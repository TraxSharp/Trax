# Trax.StateMachine

The state machines, in every language they ship in: the C# engine, its persistence and the .NET
client (`Trax.StateMachine`, `.Persistence`, `.Testing`, `.Client` on NuGet), the TypeScript engine
and client (`@traxsharp/state-machine` on npm), the Rust engine and client (`trax-state-machine`,
`trax-state-machine-client`, not published yet), and the conformance kits that hold them to the same
answers: the engine kit holds the three engines and the C# `IrMachine`, the client kit the three
clients. It sits between `Trax.Effect`, whose trains a machine invokes and whose tables it is
stored in, and `Trax.Mediator`, which launches those trains, so a change here reaches Trax.Mediator,
Trax.Scheduler, Trax.Api, Trax.Cli and Trax.Samples. Trax.Effect is upstream and never sees it.

This file is the entry point. It routes; it does not restate the rules.

## Layout

The folder is laid out by language, with what spans the languages at its root.

| Path | What it holds |
| --- | --- |
| `dotnet/` | The C# engine, `Trax.StateMachine.Client`, which drives a served machine from .NET, `Trax.StateMachine.Gherkin`, which derives machines from features and a glossary and runs them in memory, and `Trax.StateMachine.Gherkin.Reqnroll`, the Reqnroll bindings that run those features against a host. A .NET folder like any other: `Trax.StateMachine.slnx`, `Directory.Build.props`, `.config/dotnet-tools.json`, the NuGet `README.md`, `src/` and `tests/` (with `Trax.StateMachine.Tests.Meta`). |
| `ts/` | `@traxsharp/state-machine`: the engine (`src/`), the model every typed view is written from and an emitter per language (`src/codegen/`, TypeScript's in `src/rules/generateTypes.ts`), the client a page drives a served machine through (`src/client/`, `src/server/`, `src/react.ts`), and the node entrypoints `trax machine` shells out to (`tools/`), which `tools/bundle-cli.mjs` bundles into Trax.Cli's package. |
| `rust/` | `trax-state-machine`, the engine in Rust that the bindings for other languages are built on (not published yet): the engine (`src/`, no dependencies, no I/O) and its conformance testee (`src/bin/testee.rs`), and `trax-state-machine-client` (`client/`), the client that drives a served machine from Rust over tokio, with its client-kit testee (`client/src/bin/client_testee.rs`). A Cargo workspace with a committed `Cargo.lock`; its toolchain is pinned in `rust-toolchain.toml`. |
| `conformance/` | Engine-neutral data any engine replays: the shared machines (`machines/<m>/`, each machine's IR, fixtures, differential corpus and migration golden), the IR's JSON Schema (`ir.schema.json`, normative), the schemas of the glossary and host file a machine derived from features is written in (`glossary.schema.json`, `host.schema.json`, held to their C# readers by the corpus under `formats/`; central ADR 0058), the canonical-wire vectors, the hostile values (`values/`), the differences the fuzzer found (`found/`), the refusal-code registry, the strict-input cases, the runner (`run.mjs`, `kit.mjs`) that sends all of it to one engine over the protocol in `PROTOCOL.md`, and the expected failures an engine not built out yet runs under (`expected-failures/`, read by `expected-failures.mjs`, which the client and operations kits share). A vector goes here, not in one engine's tests. |
| `conformance/client/` | The client kit: transcripts of a host, request by request and push by push (`transcripts/`), the machines only it drives (`machines/`), the fake host that plays them (`host.mjs`) and the runner (`run.mjs`) that drives one client's testee through them over the protocol in its `PROTOCOL.md`. The TypeScript client (`ts/tools/conformance/client-testee-ts.mjs`), the C# one (`ClientConformanceTests`) and the Rust one (`rust/client`) pass it. |
| `conformance/operations/` | The operations kit: transcripts that hold the operations clients `trax client --operations` writes, TypeScript and C#, to one transport and error behaviour, played on the client kit's host. Its fixture's TypeScript client is committed under `ts/tools/conformance/operations/client/` and run by vitest; the C# one is in Trax.Cli's tests (`OperationsConformanceTests`), which also check both are what the CLI generates. See its `PROTOCOL.md`. |
| `docs/adr/` | This folder's decisions, for every language. |

Another language's engine gets a directory of its own beside `dotnet/`, `ts/` and `rust/`, and a row in
`conformance/testees.json`, the roster of every language folder and the testee each kit runs it through (or none, and
why). It joins the kit by implementing a testee for `conformance/run.mjs`, and its client the client kit with a testee
for `conformance/client/run.mjs`; what neither passes yet goes in its expected failures (`conformance/PROTOCOL.md`).
`TesteeRosterTests` fails on a folder with no row, a row whose suite or job CI does not run, and a testee command
`ci.yml` does not run.

## Adding a language

The behaviour is written down; read it before any code. `conformance/PROTOCOL.md` and `conformance/client/PROTOCOL.md`
are the spec, `conformance/client/defaults.json` and `conformance/limits.json` hold every number, and ADR 0004 says
what "the same client" means across languages. Where the spec is silent, the answer is not whichever existing client
you read first: settle it in PROTOCOL.md with a transcript, then build it.

A new language lands in this order, each step in the same pull request as its tests:

1. **The roster row and the expected failures.** Add the folder's row to `conformance/testees.json` and an
   `expected-failures/<name>.json` in each kit listing every case, each with its reason. The kits then run green from
   the first commit, and the list is the work left: a case that passes while listed fails the run, so each fix removes
   its entry.
2. **The CI job.** A suite in `.github/ci/packages.json` and a job in `ci.yml` that runs both testees with `--name`, in
   the `CI result` gate's `needs`. `TesteeRosterTests` checks all three.
3. **The engine testee,** through the language's public API, not the core directly, so marshalling is what is tested.
   Templates: `rust/src/bin/testee.rs`, `ts/tools/conformance/testee-ts.mjs`.
4. **The client and its testee.** A binding wraps the Rust client and implements only its transport on the
   platform's own stack ([0005](./docs/adr/0005-a-binding-wraps-the-rust-client-over-its-own-transport.md)). Template:
   `rust/client/src/bin/client_testee.rs`. The testee builds the client the
   way an app would, on the library's defaults; a transcript sets an option only when it is what it tests. A platform
   that cannot do something (a browser has no upgrade status) gets a `lacks` entry with its reason, never an expected
   failure.
5. **The surface.** A column in the client surface tables of `conformance/client/PROTOCOL.md` (the three parsers read
   columns by header, and the column must match the roster's `surfaceColumn`), a committed public-surface baseline,
   and a page under `Trax.Docs/sdk-reference/`.
6. **The parity tests:** the language's defaults and limits read from the two JSON files, and the refusal codes from
   `refusal-codes.json`, never typed in.
7. **The typed view,** an emitter on `ts/src/codegen/model.ts`, listed in `ts/tools/twin-languages.json`.

Every client so far has had bugs of the same few kinds, so check a new one for each before review:

- **Numbers.** A float read as an integer truncates (`4.5` to `4`), a huge one saturates or traps (`1e19`), NaN passes
  every comparison, a timer past 2^31-1 ms throws or fires at once, a bool is an int. `conformance/values/` sends these.
- **Strings and keys.** Lone surrogates, U+2028, NUL, duplicate keys (refused, not last-wins), `__proto__` and
  `constructor` as names, comparison and sort order by UTF-16 units as RFC 8785 says, not by the platform's collation.
- **The platform's defaults.** HTTP stacks follow redirects with the auth headers, accept `ws://`, have no request
  timeout or a different one, cap socket messages below 16 MiB. Each one has a transcript; turn every one of them off.
- **Callbacks.** A listener, a headers provider or a request-id minter that throws or panics settles its operation and
  leaves the controller running.
- **Ordering.** State read before an await is stale after it; a divergence, a reload or a new incarnation can land
  between any two steps.
- **Malformed host answers.** A field of the wrong type is `TRANSPORT_ERROR` on an answer and a dropped push with a
  read, never a stop and never taken as is.
- **Bounds.** Every parse and every recursion has a depth or size limit from `limits.json`, checked before the value is
  copied.

`conformance/fuzz.mjs` sends random cases to every engine testee and fails on any difference; run its long form
(`conformance/PROTOCOL.md`) against a new engine before calling it done.

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
| a member of one machine client, or the client surface table | [0004](./docs/adr/0004-the-machine-clients-share-one-surface-table.md), the three clients share one surface table and each language is held to it |
| a client for a new language, or a binding of the Rust client | [0005](./docs/adr/0005-a-binding-wraps-the-rust-client-over-its-own-transport.md), a binding wraps the Rust client and brings only its platform's transport |

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
  Adding or removing an export changes both in the same pull request. The Rust crates' are
  `rust/tests/public-api.txt` and `rust/client/tests/public-api.txt`, read from the source by
  `rust/tests/support/surface.rs` (`TRAX_UPDATE_PUBLIC_API=1 cargo test --workspace` rewrites them), and both crates
  build with `#![deny(missing_docs)]`.
- **The three clients name the same things.** The table under "The client surface" in
  `conformance/client/PROTOCOL.md` gives each client member its TypeScript, C# and Rust name, or `—` and the reason
  a language has none. A new public member of a client type the table covers gets its row, with all three
  languages, in the same pull request: `ClientSurfaceTests` (C#), `ts/src/clientSurface.test.ts` and
  `rust/client/tests/client_surface.rs` fail on a member the table does not name and on a name the client lacks.

## Guards

`dotnet/tests/Trax.StateMachine.Tests.Meta/` holds the convention guards every .NET folder runs,
among them `DependencyDirectionTests`: a machine project may reference Trax.Core and Trax.Effect,
never the mediator or anything below it, and no Effect project may reference a machine project.
The census is on, as in every folder. `ClientSurfaceTests` there holds the C# client to the client surface
table, as `ts/src/clientSurface.test.ts` and `rust/client/tests/client_surface.rs` hold the other two.

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

The Rust engine, from `rust/`, in a container (nothing here runs on the host; `rust/README.md` has the commands):

```bash
cargo fmt --all --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
cargo build --release --workspace   # the testees: target/release/trax-state-machine-testee and -client-testee
```

The conformance kit through the TypeScript engine, from this folder (the C# engine answers it in
`ConformanceRunnerTests`):

```bash
node conformance/run.mjs --name ts --testee node ts/tools/conformance/testee-ts.mjs
node conformance/client/run.mjs --name ts --testee node ts/tools/conformance/client-testee-ts.mjs
node conformance/run.mjs --name rust --testee rust/target/release/trax-state-machine-testee
node conformance/client/run.mjs --name rust --testee rust/target/release/trax-state-machine-client-testee
node conformance/operations/run.mjs --name ts --testee node ts/tools/conformance/operations-testee-ts.mjs
node conformance/fuzz.mjs --seed 1 --cases 400 --irs --testee rust/target/release/trax-state-machine-testee --testee node ts/tools/conformance/testee-ts.mjs
```

The last holds the engines to each other on generated requests rather than to the kit; `conformance/PROTOCOL.md`
("Fuzzing the engines against each other") has the long run and the C# side.

## CI

This folder has no folder job. `.github/ci/packages.json` gives it four suites, each with its own
paths:

| Suite | Runs for | Does |
| --- | --- | --- |
| `state-machine-dotnet` | `dotnet/`, `conformance/`, or Trax.Effect | the folder job's checks over `dotnet/Trax.StateMachine.slnx` (format, locks, deprecations, pack with package validation, coverage) and its tests, without the CrossEngine ones |
| `state-machine-ts` | `ts/`, `conformance/` | typecheck, vitest, build, `npm pack --dry-run`, audit |
| `state-machine-rust` | `rust/`, `conformance/`, and the TypeScript engine the fuzz runs (`ts/src/`, its testee and loader, its package and tsconfig files) | `cargo fmt --check`, clippy with warnings as errors, `cargo test`, then the engine kit and the client kit through the Rust testees, and a short fuzz of the Rust engine against the TypeScript one |
| `state-machine-conformance` | anything in the folder, or Trax.Effect | the CrossEngine tests (among them the engine kit through the C# engine, a short fuzz of it against the TypeScript one, `FuzzTests`, and the client kit through the C# client, `ClientConformanceTests`), then the engine kit through the TypeScript engine and the client kit through the TypeScript client |

The three language suites leave each other alone, but for the TypeScript engine the Rust suite's
fuzz runs, and the folder is still one unit to the change detector: any change in it runs `state-machine-conformance`, which builds and runs the C# tests, and
selects the folder itself. Trax.Mediator and Trax.Cli list this folder upstream, so a change anywhere
in it, a `rust/`-only one included, also runs them and the folders below them.
