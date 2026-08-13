# Tier-1 State Machines: codegen, multi-language, and scaffolding

Status: RFC, revised 2026-08-13 (originally 2026-08-07). The architecture below is decided and, for its
core, built: the C#-source + IR + interpret-at-runtime pipeline ships and has a real product consumer
(write-to-congress in nwyc). This revision reconciles the original draft with what actually landed. The
per-section "Status" lines and §11 table are the source of truth for what is built vs. outstanding; the
short version is that the source inversion, the declarative layer, the IR, full JCS, and the migration
golden are built, while the oracle flip, the `trax machine` CLI, probe generation, and the trust-boundary
enforcement are not.

## Why this exists

The goal is to run large parts of a frontend off state machines wherever it makes sense, with
write-to-congress as the proof of concept. That surfaces a tension:

1. **Parity by construction.** The C# (server) and every frontend runtime must stay in lockstep on
   structure, data types, and behavior. Drift should fail a build or a test, never ship silently.
2. **Low-friction updates.** A developer adding a field or a step should not have to hand-edit the same
   thing in several languages, hand-pick edge cases, or remember a migration. Forgetting any of those must
   be impossible, not merely discouraged.

The original baseline sat at the fully hand-written extreme: two hand-written twins, hand-authored
differential contexts, structure restated in `machine.json`. That maximized friction and drift surface. As
of 2026-08-13 that baseline is mostly retired. `turnstile` and the product machine `write-to-congress` are
on the generated pipeline (declarative C# source, exported IR, an interpreted TypeScript twin generated from
the IR); `checkout` is the last machine still on the old hand-written twin, pending the Increment E cleanup
(phase0-codegen-scope.md §0). This document describes the Tier-1 architecture that resolves the tension:
generate everything that is data, interpret the declarative logic from that same data, hand-write only the
rare genuinely-custom behavior, and make every category of drift a build failure or a test failure.

This is a design/RFC, not an implementation plan for a single PR. phase0-codegen-scope.md is the grounded,
increment-by-increment companion. This document ends with a phased rollout that used write-to-congress to
prove the pipeline before generalizing it.

## Decisions at a glance

The load-bearing decisions, so they are not buried under the caveats that follow:

1. **Tiering (§1).** A machine is Tier 1 (this stack) if and only if its state is persisted and re-driven by
   the server, or it drives an exactly-once effect. Everything else is Tier 2 and out of scope.
2. **C# is the source; the oracle flip to C# is planned, not done (§3).** Author the machine in C#, export a
   neutral IR, generate every frontend from it. The source inversion is built. The differential oracle is
   still TypeScript today (§7); flipping it to C# is a remaining increment, not a shipped fact.
3. **Data generated, logic interpreted, custom logic twinned (§2).** Structure/types/validators are
   generated. Declarative guards and reducers are *data* in the IR, run by a small per-language interpreter,
   so there is nothing per-machine to generate or hand-attach for them. Only a genuinely-custom named handler
   (the ~8% case) is twinned hand-written code, and the first product machine has zero of them.
4. **The differential is the parity guard (§7).** Every runtime replays a golden corpus and must match
   byte-for-byte. It checks that hand-written logic *agrees* across runtimes, the one thing that cannot be
   generated. It does not check that the logic is *correct*, a reducer wrong identically in every runtime
   still passes (see §4). Note it currently guards only interpreted/generated code, because no machine has a
   custom handler yet, so the one thing it exists to catch is unexercised.
5. **Full JCS is built (§4).** Byte-exact wire needs real RFC 8785 serialization plus an independent
   conformance gate. Both runtimes now implement the ECMAScript number/string rules and share an RFC 8785
   vector suite. This was the original draft's largest unbuilt prerequisite; it is done.
6. **A migration golden guards correctness (§8), but migrations are deferred (Decision E).** The golden
   machinery is built. Authoring-level migrations are deferred: a version-mismatched snapshot is rejected as
   a typed `version-mismatch` and the client starts fresh.
7. **Deploy topology: vendor the engine, check the artifacts in (§4).** The consuming repo vendors the
   TypeScript engine source at a pinned upstream commit and checks the generated twin, IR, and corpus into
   its own tree. This is the decided model, not an interim hack.

What remains unbuilt, so it is not lost under the "done" list: the oracle flip to C# (§7), the `trax machine`
CLI that replaces the fragile regen-via-golden-tests script (§9), probe generation (§7), and the
trust-boundary enforcement (`serverFilled` field-strip + reachability check in `Rehydrate`, §2). The last is
a *security* control, so it degrades to a vulnerability, not a failed build (§12).

## 1. Tiering: what Tier 1 is, and what it is not

The single most important decision is *which machines get this stack at all*. Conflating "model UI as a
state machine" with "persist a flow as a Trax snapshot machine" is what makes the whole thing feel heavy.

- **Tier 1 — server-authoritative machines.** Persisted, resumable across devices, re-driven authoritatively
  on the server, sometimes with exactly-once effects. Write-to-congress, checkout, onboarding, anything with
  an irreversible action or a draft that must survive a reload. These cross the client/server boundary, so
  the runtimes must agree. **Only Tier 1 uses the machinery in this document.**
- **Tier 2 — client-only UI machines.** A multi-step modal, a filter builder, async loading/error states, a
  wizard with no server draft. State-machine-shaped, but nothing reaches the backend, so there is nothing to
  keep in parity. Use a plain client state-machine library (XState or a small local one). No twin, no
  differential, no persistence, no migration. **Out of scope here.**

**The rule:** a machine is Tier 1 if and only if its state must be persisted and re-driven by the server (or
it drives an exactly-once effect). Everything else is Tier 2. Most "state machine opportunities" in a
frontend are Tier 2; keeping them out of this stack is what makes "whole frontend on machines" tractable.

## 2. The core split: data is generated, logic is interpreted, custom logic is twinned

A Tier-1 machine has two layers with very different economics.

- **Data (fully generatable).** States, triggers, the transition structure, the context shape (field names +
  types), the shape/type validators, the differential probe inputs, and the GraphQL mutation fan. All of
  this is pure data. It is single-sourced and generated into every runtime. Two different strengths of guard
  apply, and it is worth not conflating them: a **type** mismatch is a *compile error* in anything that
  consumes the generated types, which is genuinely construction-level; a **stale generated file** is caught by
  `--check`, a CI gate that is strong but not literally impossible (it fires only if CI runs and nobody
  hand-edits the output). The §11 table lists the honest failure mode for each. Both beat a runtime failure,
  but "impossible by construction" is accurate only for the typed half.
- **Declarative logic (data, interpreted, not twinned).** The original draft assumed guards and reducers were
  inherently hand-written per language. The shipped design proved most are not: a common guard/reducer
  vocabulary (`required`, `minLength`, `min`/`max`, `oneOf`, `count`, `and`, `set`, `reset`, ...) is
  represented as *data* in the IR, and a small per-language interpreter (`RuleEvaluator` in C#,
  `interpreter.ts` in TypeScript) runs it. So for the ~92% of logic the vocabulary covers there is nothing
  per-machine to generate and nothing to hand-attach: the frontend runs `machineFromIr(ir)` and the rules
  execute as data. This is the single biggest divergence from the original plan and it is what makes the
  first product machine 100% generated.
- **Custom logic (the ~8%, twinned).** A guard or reducer too specific to express in the vocabulary (a
  formula reducer, an idiosyncratic branch) is bound by **name** in the IR and hand-written per runtime that
  runs it. This is the only twinned, hand-written surface, and it is what the differential (§7) exists to
  keep honest. Write-to-congress has zero custom handlers, so this surface is currently unexercised.

Two levers shrink the twinned surface, which matters a lot once there is more than one frontend language:

- **The declarative vocabulary (above).** Sized empirically at ~8 primitives covering ~92% of real guards and
  reducers (phase0 §3). Draw the line deliberately: let it grow to cover everything and you have re-invented
  the serialized-logic DSL you are trying to avoid. The escape hatch is a named hand-written handler, not a
  bigger vocabulary.
- **Server-only guards (the escape hatch).** A guard too complex to express declaratively and not worth
  hand-writing in every frontend language can be omitted from the frontends entirely. The client optimistically
  allows the action; the server (authoritative) rejects it; the client renders the typed rejection. This
  bounds the per-language logic cost: you only hand-write in N languages the guards you want to run
  *optimistically on the client*. Everything else is server-authoritative and costs nothing on the frontend.

**Reducers get the vocabulary but not a "declarative reducer DSL."** A guard returns accept/reject; a
*reducer* transforms the context, and its output is the canonical wire that gets persisted and compared. The
vocabulary covers the common reducer shapes (`set`, `reset`, copy-input, clone-merge), but a genuinely-custom
reducer is N-plicate hand-written by default, and a reducer divergence is worse than a guard divergence: it
corrupts persisted state rather than flipping an accept/reject. Keep reducers thin and total; push branching
into guards where the vocabulary can absorb it.

### The client twin boundary: effect-bound transitions are server-only

The client twin only promises parity on transitions it can *actually compute*. That boundary has to be
explicit, because getting it wrong is where the "byte-exact parity" claim quietly breaks.

- **Reducers are pure functions of `(context, input)`. This is a hard rule.** A reducer never calls a clock,
  an RNG, an ID generator, or an effect. Every value that is not derivable from the current context and the
  trigger input arrives *as* input. This is what makes the server's own re-drive/resume deterministic and
  what lets the differential (§7) replay a golden and get byte-identical wire. A reducer that reads
  `now()` diverges from its own golden on replay, so it is a bug, not a style choice.
- **Effect-bound transitions are not twinned optimistically. The client fires the mutation and waits.** A
  transition whose input only the server can produce (a charge receipt, a server-assigned id, anything from an
  external system) cannot be run ahead by the client, because the client cannot fabricate that input. So the
  client does not advance its local machine for that edge; it invokes the mutation, the server runs the effect
  and returns the authoritative snapshot, and the client applies it. There is no client-side placeholder and
  therefore no parity hole. Write-to-congress's `Send -> Sent` is the canonical case: it binds
  `RunsOnce<IWriteToCongressLetterSend>("write-to-congress:send")`, the send runs first, its receipt flows in
  as reducer input, and `Sent` is unreachable until the send completes (`checkout`'s `Pay -> Paid` is the same
  shape). Crash between the effect and the transition is safe: re-drive reuses the existing receipt (the
  exactly-once claim blocks a second send) and finishes the move. The server was always going to wait for
  the real receipt; the design's only job is to stop the *client* from pretending it can skip that wait.
- **Optimistic-advance-with-reconcile is a rare, annotated exception, never the default and never for an
  irreversible action.** If a transition's outcome is *usually* predictable and the round-trip would feel
  sluggish (a generated share link, say), a machine may advance optimistically with a placeholder and
  reconcile on the server response, but only behind an explicit per-field `serverFilled` / `excludeFromParity`
  annotation, so every field the differential is *not* guarding is visible in the IR and reviewable. A payment
  or a letter send is never a candidate: nobody wants an optimistic "Sent" before the effect clears.

**The honest consequence:** the twin mirrors the client-computable transitions (form steps, local validation,
branching on entered data), and *by design* does not mirror server-only ones. The differential guards the
former; the latter produce a golden only on the server, with no client wire to compare. That is the true
boundary of what a twin can promise, and stating it is what keeps "parity by construction" from overclaiming.

### The trust boundary: the server never trusts the client twin

The optimistic-client / authoritative-server split is a trust boundary, and it has to be stated as a security
requirement, not just a parity mechanism, or someone will later "optimize" by trusting client-computed wire.

- **The server re-runs every guard and re-validates every context, unconditionally, on every transition.** It
  never accepts a client's assertion that a guard passed or that a context is valid; the client twin is a UX
  accelerator, never an authorization. The engine already does this (`Advance` re-runs guards and
  `ValidateContext` server-side), but the design must *require* it, because generated optimistic clients that
  advance and then submit their own snapshot are exactly the shape that invites a "trust the client wire"
  shortcut. No client guard is ever a security control.
- **A client can always forge a snapshot.** The server must treat any inbound state/context as attacker-
  controlled: reject transitions that are not reachable, reject contexts that fail server validation, and
  never resurrect a draft into a state the guards would not allow. **This is partly unbuilt.** `Rehydrate`
  today validates the context and that the state token parses, but does no reachability-from-initial analysis,
  so a forged snapshot in any parseable state with a passing validator currently loads cleanly. Rejecting
  unreachable states is a graph check the engine does not have yet; it is a Phase-0 prerequisite (§12), not an
  assumption validation covers.
- **`serverFilled` / `excludeFromParity` fields are an authorization surface, not just a parity exception.**
  They are, by definition, fields the differential does not guard, so a forged client can write into them.
  The server must own them: overwrite them from the authoritative source on every transition, and authorize
  who may set them. A field being excluded from parity must imply it is server-written, never client-trusted.
  Enforce this in the engine, not by reducer discipline: the generic `Advance` must strip `serverFilled` fields
  from client-supplied input before the reducer runs, so a reducer *cannot* copy a client value forward even
  by mistake. **This is unbuilt and non-trivial:** today `Advance` passes client `input` straight into the
  reducer with no field-level filtering and no per-field metadata to filter on, so it requires the IR to carry
  which fields are `serverFilled` and the engine to consume that. And because it is a *security* control, not
  a parity one, it cannot degrade to a CI test that might not run: a missing strip is a forgery hole, not a
  failed build. Write-to-congress avoids the whole path (its server-produced value, the receipt, arrives via
  the effect-bound `RunsOnce` mechanism, not an optimistic `serverFilled` field), so this control is both
  unbuilt and unexercised by the first consumer, which is exactly why it needs its own concrete design (§12).
- **The generated mutation fan inherits user scoping.** Persistence is already per-user
  (`SnapshotEffectRunner.Run(userKey, ...)`), so the generator (§6) must thread the caller's identity into
  every emitted mutation and scope every load/save/advance to it. A generator that emits an unscoped mutation
  is an authz bug shipped at scale, so user-scoping is a generation requirement, not a per-machine reminder.

## 3. Source of truth: author in C#, export a neutral IR, generate the rest

### The decision

Author each machine's **data layer and declarative guards in C#**, have a C# tool export a **neutral
intermediate representation (IR)**, and generate every other runtime (TypeScript, Rust, ...) from the IR.
The IR, not any one language, is the interchange contract.

"C# as source" in practice: you never hand-author the IR JSON. You author a C# machine (real types, real
tooling), and `IrExporter.Export(machine)` produces the IR from the built machine model. Generators for each
frontend language consume the IR.

**This was two inversions; one is built, one is not.** The original draft flagged that today's pipeline ran
the other way: a hand-authored `machine.json` was the structural source, a Node generator emitted the
TypeScript twin, TypeScript was the differential oracle, and the C# twin was verified against it. The
proposal flipped both the **source** (retire hand-authored `machine.json`; export the IR from the C#
machine) and the **oracle** (C# becomes the side the frontends must match).

- **The source flip is done.** C# now carries the declarative layer as data: a context-schema surface
  (`ContextSchema`, `SchemaReflection` from C# records + attributes), the guard/reducer vocabulary as
  inspectable rule objects (`Rule`, `Reduction`), named custom-handler bindings, and `IrExporter` emitting a
  full IR (identity, states, triggers, transitions with declarative guards/reducers, per-state context and
  input schemas, per-state invariants, committed states, effect bindings, and the differential block).
  `SnapshotMachine.Describe()` (the original down payment that emitted only the *structural* third) is
  subsumed. Proven on turnstile and, as a real product, write-to-congress.
- **The oracle flip is not done.** TypeScript's `enumerate()` still produces the golden corpus; C# replays it
  (`DifferentialCorpus.Replay`) and must match. This is backwards for a server-authoritative design and is a
  scheduled increment (§7, §12), but it is not a shipped fact. Do not read "C# is source and oracle" as
  present tense on the oracle half.

### Why C# is the right authoring surface here

- **Source of truth = the authoritative runtime.** The server is what actually persists and re-drives. Making
  its machine the hand-written, reviewed source (and generating the frontends to match) aligns "truth" with
  "authority". You review the real code that drives production; frontends conform to it.
- **Tooling.** Roslyn and C# source generators are a first-class, mature toolchain, and the built exporter
  works off the fluent machine model directly (no Roslyn needed for the current surface).
- **Team fit.** Trax is C#-centric; authoring machines in C# fits how the team already works.
- **The differential oracle flips the right way (once flipped).** With C# as source *and* oracle, C#
  enumerates the golden and every frontend matches *it*. "The frontend conforms to the server" is exactly the
  safety property you want. Today the enumerator lives in TS, so that property is aspirational (§7).

### What "C# as source" implies (the important part)

Positives are above. The costs to design around:

- **Custom logic is still hand-written per target language.** Generation and interpretation cover the data
  layer and the declarative vocabulary. A genuinely-custom named guard/reducer must exist in C#
  (authoritative) and in each frontend that runs it optimistically. With N frontend languages that is up to N
  hand-written implementations, differential-guarded. This is the dominant scaling cost, and it is why the
  vocabulary and the server-only escape hatch matter. Most machines should end up with few or zero
  truly-custom client-side handlers; write-to-congress has zero.
- **Frontend field changes couple to the C# build.** A frontend developer who wants a new field edits the C#
  source and regenerates; they cannot just edit TypeScript. For a C#-centric team this is fine and even
  desirable (one reviewed source). For a frontend-led org it is friction worth acknowledging.
- **The extractor is real infrastructure.** C# -> IR is a component with its own tests and versioning. The IR
  is a contract; changing it is a breaking change for every generator.
- **Generated frontend code must be excellent.** Frontend developers consume generated types and a typed
  factory that embeds the IR, and hand-write only the rare custom guard. The generated output has to be clean,
  typed, documented, and stable, or the DX is worse than hand-writing.

### Alternatives considered

- **TypeScript as source (the original oracle).** Good structural typing and the valibot ecosystem, but the
  source of truth would not be the authoritative runtime, and the C# server machine would be generated, which
  is the less reassuring direction for the side that owns persistence.
- **Neutral IDL as source (protobuf/smithy style).** Symmetric across all languages, but you hand-author a
  JSON/IDL with a poor editing surface, and expressing guards in it pushes hard toward the serialized-logic
  DSL. Authoring in a real language and *exporting* the neutral IR gets the symmetry (the IR is neutral)
  without the bad authoring DX.
- **Share/transpile the logic (WASM, or a logic DSL).** Eliminates the twins but loses native, reviewable,
  debuggable logic per language, couples deploys, and (for a DSL) becomes a language with N interpreters to
  maintain. The interpret-at-runtime approach (§2) is a bounded, deliberate version of this: a *small* fixed
  vocabulary interpreted per language, with a named escape hatch for anything past it, rather than an
  open-ended logic DSL.

## 4. Cost model: fixed per language, marginal per machine

This is the accounting that determines whether "many machines, several languages" is viable.

- **The engine is itself a per-language twin.** `advance` / `rehydrate` / `serialize` / canonicalization, plus
  the rule interpreter (§2), are implemented in C# and TypeScript today; a Rust frontend needs a Rust engine
  and interpreter too. This is written **once per language** and reused by every machine, and it is guarded by
  the totality + differential suites. It is the large fixed cost of adding a language.
- **Per new language target you build:** the engine + interpreter (once), a generator target (IR -> that
  language's types + a typed factory that embeds the IR), a differential `replay` harness, and a byte-exact
  canonical serializer (RFC 8785 JCS + the envelope ordering). All fixed, all reused across machines. C#
  already has a working `replay` harness (`DifferentialCorpus.Replay`), so flipping the oracle to C# (§7) is
  mostly moving the *enumerator* (today `enumerate()` in `differential.ts`) to C#, not building C#'s replay
  side from scratch.
- **Per new machine you build:** the C# source (data + declarative guards + any custom guards and reducers),
  then `generate` produces every runtime's types + typed factory, and you hand-write only the custom
  client-side handlers in each frontend language that needs them (often zero). Cheap when the vocabulary fits.

So the shape is **high fixed cost per language, low marginal cost per machine**. Many machines is cheap;
adding a language is a real investment that amortizes across all machines. Plan the language set
deliberately; do not add a runtime casually.

**Canonical serialization is built (this was the draft's largest unbuilt prerequisite).** Every runtime must
emit byte-identical canonical wire, so each language re-implements JCS canonicalization + the fixed envelope
order. As of this revision both runtimes do the *full* RFC 8785 rules, not just the key sort:
`CanonicalJson.cs` implements the ECMAScript `Number::toString` algorithm (the `1e+21` / `1e-7` / `5e-324` /
`-0`->`0` cases) and JSON.stringify-exact string escaping, and the TypeScript twin delegates to
`JSON.stringify`, which *is* the algorithm RFC 8785 defers to. There is an independent conformance gate: a
shared RFC 8785 vector suite (`CanonicalizationConformanceTests.cs` and its TypeScript mirror) that every
runtime including C# must pass, distinct from any machine golden. That gate is what verifies the oracle's own
serializer, which the differential cannot (a wrong oracle serializer is faithfully reproduced by every
frontend; byte-exactness proves *agreement*, not *correctness*).

One forward-looking consequence the draft called out and that still holds: a *future* change to the canonical
number/string rules would change stored snapshot bytes (and any hash/signature over them), and the shape
migration machinery (§8) cannot express a pure re-encoding. If that ever happens it needs a separate
wire/serializer version axis, orthogonal to machine `version`. It is not needed now (the serializers are
already spec-compliant), so it is a note, not a task.

### Deploy topology: vendor the engine, check the artifacts in (DECIDED)

The dominant *operational* cost is the cross-repo regen-and-ship loop, and the model is now decided by how the
first product consumer ships. **The consuming repo vendors the TypeScript engine source at a pinned upstream
commit and checks the generated artifacts into its own tree.** Concretely, in nwyc:

- The engine `src/` is mirrored into `libs/shared/frontend/state-machine/` and imported through a path alias
  (`@trax/state-machine` -> `libs/shared/frontend/state-machine/src/index.ts` in `tsconfig.base.json`). A
  `VENDORED.md` records the exact upstream commit the mirror is pinned to.
- The generated **twin** (`<machine>.contexts.g.ts` + `<machine>.machine.g.ts`) lives next to the frontend
  that consumes it (`apps/web/src/app/<machine>/`). The **IR** and the differential **corpus** live in a
  shared per-machine directory (`libs/shared/statemachine/machines/<machine>/`). All of it is committed.
- The C# side consumes Trax by NuGet package (`Trax.Effect.StateMachine`), pinned to an exact version.

Why vendoring rather than a published npm package: the engine and the machines co-evolve tightly, the
frontend needs to build against an arbitrary engine `src` (not a fixed package layout), and checking the
artifacts in makes the differential corpus present in the consumer's own CI (which is what closes the §7
"the parity suite silently skips" hole). The cost is a manual re-vendor step (bump the pin in `VENDORED.md`,
re-copy `src`, regenerate), which the `trax machine` CLI (§9) should make one command. The IR version, the
machine `version`, and the vendored-engine commit all have to stay coherent; the pin in `VENDORED.md` plus
the IR version stamped by the exporter are the coherence anchors.

## 5. The IR

One versioned, JSON-Schema'd document per machine, exported from the C# source. It captures everything that
is data. The blocks the exporter emits today, on the write-to-congress IR
(`libs/shared/statemachine/machines/write-to-congress/write-to-congress.ir.json`):

- Identity: `id`, `version`.
- Structure: `initialState`, `states`, `triggers`, `transitions` (from/trigger/to, plus which guard/reducer
  each edge binds, declaratively or by name).
- `initialContext`: the seed context for the initial state.
- Context schema (`context`): per-state field names, types, nullability, and enums.
- Input schema (`inputs`): per-trigger input field shapes.
- Declarative validators and per-state invariants (`invariants`): the vocabulary policy checks (`.Requires`).
- Named custom handlers: the *names* (not bodies) of any custom guards/reducers each edge binds, so every
  runtime knows which functions it must provide by hand. Write-to-congress binds none.
- `committedStates`: for the persistence layer.
- `differential`: the samples/seeds/probe contexts authored in C# via `.Differential(...)`, off which the
  harness enumerates. Today these are hand-authored; generated probes will live here when they exist (§7).

**Not yet in the IR:** a migrations block (deferred, Decision E / §8) and `serverFilled` per-field metadata
(needed for the trust-boundary strip, §2). Both are additive when they land.

The IR is the contract between authoring and every generator. It is versioned independently of any machine,
and because a schema change fans out to every generator in every repo, "evolves deliberately" is not enough:
the IR needs explicit compatibility *rules* so most changes never trigger a flag day.

- **Additive-only by default.** New optional fields, new enum members with a defined default, new sections a
  generator may ignore: these are minor IR versions. A generator built for vN must keep working against vN+1
  by ignoring what it does not understand, so additive changes roll out one repo at a time.
- **Semantic and subtractive changes are major, and gated.** Removing a field, changing the meaning or type
  of an existing one, or tightening a validator is a major IR version. During a migration, generators declare
  the IR major version(s) they accept, and the exporter can emit the older major until every generator has
  moved. What is *not* allowed is a silent semantic change inside a minor bump.
- **One coherent version story, not four.** The IR version, the vendored-engine commit, the machine
  `version`, and "the IR versions a generator accepts" all have to line up. The exporter stamps the IR version
  into the IR; the vendored pin (§4) anchors the engine; a generator fails loudly on an unaccepted IR major.
- **The IR schema itself is `--check`ed.** An IR emitted by a newer exporter than a generator understands must
  fail that generator's build with a clear "unsupported IR major" message, never produce subtly-wrong output.

## 6. Generators

One generator per target language, `IR -> code`, plus `--check` (regenerate to a temp location, diff against
the committed output, non-zero exit on drift for CI). Because declarative logic is *interpreted from the IR
at runtime* (§2), the generators emit far less than the original draft assumed: **types and a thin typed
factory, not logic.** Per language:

- state/trigger types (unions/enums) and the context/spec types (`generateContextTypes`)
- a typed factory that embeds the IR and constructs the running machine via `machineFromIr(ir)`
  (`generateMachineFactory`), wrapped in the typed facade
- stubs/interfaces for any *named custom handlers* the machine binds (so a missing one is a compile error) --
  currently unexercised, since no machine has one
- the differential `replay` entry point
- server target only: the GraphQL mutation fan + DI wiring (subsumed on the C# side, which runs the IR
  directly; the four generic mutations come from `AddStateMachines`)

There is **no per-machine C# artifact to generate.** C# is the source and runs the declarative rules directly
via `RuleEvaluator`, so the original "C# generator target" is subsumed by interpret-at-runtime. The generator
set is: the TypeScript twin today, and one target per future frontend language.

The starting precedent, `tools/state-machine-codegen/generate.mjs`, is the *old* pipeline: a Node script that
read the hand-authored `machine.json` and emitted TypeScript structure with a `--check` gate. It still exists
for the unmigrated `checkout`, but the migrated machines do not use it: they use `generateContextTypes` /
`generateMachineFactory` (in `src/rules/generateTypes.ts`) driven off the IR. Retiring `generate.mjs` and
`machine.json` for `checkout` is the Increment E cleanup.

### Round-trip: how hand-written logic attaches to generated code

For a fully-declarative machine there is **no** hand-written logic to attach: the guards and reducers are IR
data, and the generated typed factory runs them. This is why write-to-congress's twin is a generated
`*.machine.g.ts` plus a hand-owned *bridge* file (`*.machine.ts`) that adds UI-facing types and
state<->step mapping but **no guards or reducers**. The round-trip DX the draft agonized over does not bite
until a machine needs a genuinely-custom named handler, and none do yet.

When one does, the contract (still to be proven in practice) is:

- **Generated code owns structure and declares an interface of named handlers**, one entry per custom
  guard/reducer the IR binds by name. In C# this is a `partial` the human completes; in TypeScript it is a
  generated interface plus a registry the engine calls into, and the human provides an implementation object
  in a **separate, hand-owned file** the generator never writes.
- **The generated file is never hand-edited** (it carries the `AUTO-GENERATED / Do not edit by hand` banner
  the `.g.ts` files already use). All hand code lives in a sibling file that imports generated symbols.
- **A missing or misnamed handler is a compile error, not a silent no-op.** Adding a custom guard in C# and
  regenerating changes the generated interface, so every frontend that has not implemented the new handler
  fails to compile, pointing at the exact missing name.

The worked loop to hold the design to when the custom case first appears: *add a `promoCode` field with a
custom `promoValid` guard in the C# machine -> `trax machine generate` -> the C# `partial` now has an
unimplemented `promoValid`, and the TypeScript build fails on a missing `promoValid` in the handler object ->
implement it in both hand-files -> the differential (§7) confirms they agree.* Until a real machine needs
this, the mechanism is designed but **unexercised**; treat its first use as a de-risking milestone, not a
solved problem.

### `generate` is atomic and idempotent, or it corrupts the tree

The generator writes many files across repos, so its failure and concurrency behavior is part of the design.

- **Atomic per run.** A `generate` that writes three of five files and then throws must leave the tree either
  fully updated or untouched, never half-applied. Write to a staging location and swap. A half-regen that
  compiles is the worst case: it looks done and is wrong.
- **Idempotent.** Running `generate` twice with no source change produces byte-identical output and no diff.
  This is what makes `--check` meaningful.
- **The interim regen is a fragile chain of golden tests, and that is the argument for the CLI (§9).** Today,
  nwyc regenerates via `scripts/regen-state-machine.sh`, which runs three env-var-gated golden tests in order
  (`UPDATE_IR` writes the IR from a C# test, then `UPDATE_GEN` and `UPDATE_GOLDEN` write the twin and corpus
  from two Jest tests). It works but is brittle: driving generation through a test runner means an unrelated
  failing test can abort the regen under `set -e`, and the twin's three relative engine imports
  (`../../machine`, `../../rules/irMachine`, `../../typed`) are collapsed into one `@trax/state-machine` import
  by three hard-coded string `.replace()` calls in the codegen test (two deletions plus one rewrite), because
  `generateMachineFactory` has no import-style option. A real `trax machine generate` that runs
  the exporter and generators directly (§9) replaces the whole chain.
- **Regen races are a real hazard.** Two developers regenerating different machines into the shared artifact
  set will collide. With the vendoring topology (§4) the artifacts are committed per-machine, so the merge
  story is ordinary git on per-machine files, but a shared vendored engine bump touches everything at once and
  needs coordination like any dependency bump.

## 7. The differential at scale

- **The oracle is TypeScript today; C# is the target (the flip is planned, not done).** A TypeScript
  enumerator (`enumerate()` in `differential.ts`) drives the machine over the reachable space (BFS from
  initial + seeds) crossed with the IR's probe contexts, and records each outcome as canonical wire (on a
  transition) or a rejection code. That is the committed golden corpus, one per machine. C# replays it
  (`DifferentialCorpus.Replay`) and must match. **Increment D** moves the enumerator to C# so the server owns
  the golden; the engines are already proven-equivalent, so this is about which side owns the corpus, not
  correctness (§12).
- **Probe generation is a design goal that does not exist yet.** The state-space traversal is automated (BFS
  over reachable snapshots), but the *input values* it crosses are hand-authored (`.Differential(...)` in C#,
  exported into the IR's `differential` block). There is no boundary or property-based generation. The target
  is to derive a dense probe set from the context schema and the declared guard boundaries; that builds on the
  same declarative layer §3/§5 provide, so it is now *possible*, just unbuilt.
- **Generated probes will be weakest exactly where the differential matters most.** A property-based generator
  only knows the boundaries it can *see*, the declarative ones. A custom guard's real edges are opaque to it,
  so generated probes would be densest for declarative guards (also structurally checked, least likely to
  diverge) and sparsest for custom guards (the whole reason the differential exists). A machine must hand-add
  adversarial probes seeded *per custom guard*, and the corpus is incomplete for any custom guard or reducer
  that has none. This needs to be a gate, not a convention (§11).
- **The differential is currently unexercised for its core purpose.** Because write-to-congress has zero
  custom handlers, the corpus today only proves that the *interpreted* rules agree across runtimes, which is
  the part least likely to diverge (both runtimes run the same IR data). The first machine with a custom
  handler is what actually tests "hand-written logic agrees across languages." Until then, "parity is proven"
  means "the interpreter agrees with itself in two languages," which is weaker than the headline suggests.
- **Every runtime replays the golden and must match** byte-for-byte (wire) or code-for-code (rejection). A
  divergence names the exact case.
- **The oracle cannot verify its own serializer.** Replay proves runtimes *agree*, not that the oracle is
  *right*; canonicalization correctness is the separate RFC 8785 vector gate (§4), which is built.
- **CI regenerates and verifies.** The golden is regenerated from the oracle and the replay must be clean, so
  a corpus that is stale relative to the source fails the build.
- **A parity suite that can silently skip is not a guard, and the vendoring topology is what fixes it.** The
  upstream Trax.Effect differential locates the corpus by walking the filesystem for a sibling repo and
  `Assert.Ignore`s the whole suite if it is not found, so in an isolated CI checkout it does not actually run.
  The first product consumer resolves this by the §4 topology: it vendors the engine and **checks the corpus
  into its own repo** (`libs/shared/statemachine/machines/<machine>/differential.json`), so both the Jest and
  the C# replay tests find it and run unconditionally in that repo's CI, with no skip guard. The remaining
  work is to remove the skip-on-missing behavior *upstream* so Trax.Effect's own CI fails rather than skips;
  the consumer side is already correct.

### Corpus scale is a cost the golden makes concrete

"At scale" has to mean the corpus, not just many machines. The enumerator builds a cross-product (contexts x
reachable states, each fired against every trigger x input) plus the per-guard adversarial probes above. For
the toy machines that is tens of cases; write-to-congress's committed `differential.json` is already ~640 KB.
For a real machine it is combinatorial, committed to git as the golden, and replayed by every runtime on
every push. Bound it deliberately: cap the probe set per guard, shard the golden per machine, budget CI
replay time, and treat a large golden diff as a review artifact (a diff touching thousands of cases needs the
same scrutiny as the source change that caused it).

## 8. Migrations and schema evolution

Adding or changing a persisted context field breaks existing stored drafts unless handled. The migration
*correctness* machinery is built and the engine-level `MigrateFrom` authoring API works; what is **deferred**
(Decision E in phase0) is carrying migrations into the IR and the generated frontend.

- **Deferred posture (current).** The engine-level `MigrateFrom` authoring API exists and works: a
  delegate-based forward migration (`Func<string, JsonObject, MigrationResult>` registered per source version
  on the fluent builder), applied on `Rehydrate`, pinned by a migration golden. What is deferred is *carrying
  migrations into the IR*: `IrExporter` emits no migrations block, so migrations do not cross to the generated
  frontend, and the declarative product machines (turnstile, write-to-congress) are single-version. For those,
  a stored snapshot whose version does not match is rejected as a typed `version-mismatch`
  (`RehydrationErrorCodes`) and the client starts fresh, which is the engine's fallback when no migration is
  registered. Accepted cost: a schema change invalidates in-flight drafts for the single-version machines,
  revisited before a product with valuable persisted drafts.
- **When migrations are wired into the IR and the generated frontend, the tooling must make them hard to
  forget.** `generate`/`migrate` diffs the new IR
  context schema against the last committed one and scaffolds a forward-migration stub. Rename detection is a
  guess (a rename is structurally identical to a remove-plus-add), so the scaffolder may *suggest* a rename
  but must never silently assume one; treat every rename as human-confirmed, because a wrong guess silently
  drops persisted data.
- **The drift guard would catch a missing migration, not a wrong one.** A changed context schema with no
  migration (and no tolerant default) should be a build failure. But presence is not correctness: the
  differential (§7) tests machine *logic*, not that a real persisted vN draft forward-migrates to the right
  vN+1.
- **The migration golden is built.** Alongside the differential corpus, a migration golden per version bump (a
  set of stored vN snapshots and their expected vN+1 result, replayed through the real forward-migration path)
  is the only thing that verifies migration *correctness* against actual stored shapes. The machinery and the
  `turnstile` v2 migration golden (`machines/turnstile/migration.json`, driven by `TestTurnstileV2`) exist and
  stay green; deferral means not building *more* migration machinery, not removing what exists.

## 9. The CLI (Trax.Cli) -- the near-term priority

Trax.Cli is already a NuGet `dotnet tool` that turns a schema into generated Trax code (GraphQL/OpenAPI ->
trains, via `GenerateCommand` / `TraxProjectGenerator` / a code renderer). A `machine` command group is the
same architecture applied to state machines, and it is the **highest-value unbuilt item**: it replaces the
fragile regen-via-golden-tests script (§6) that a real team is fighting today, so it is scheduled *before* the
oracle flip (§12).

- `trax machine new <name>` — scaffold a new Tier-1 machine: the C# source skeleton (states/triggers/context
  + a couple of transitions), the differential wiring, and the mutation registration. The common case becomes
  a ~30-second scaffold, and consistency is by construction.
- `trax machine generate` — export the IR from the C# source and regenerate every target language, running the
  exporter and generators **directly** (not through a test runner), atomically (§6). Must take an output root
  per artifact (the IR/corpus and the twin live in different trees, §4), must run against an arbitrary engine
  `src` (the consumer vendors it, §4), and must take an **import style** option so the twin's imports collapse
  to the `@trax/state-machine` specifier without the hard-coded string `.replace()` the codegen test does now.
- `trax machine check` — `--check` drift across the IR, the generated outputs, and the golden corpus (CI).
- `trax machine migrate` — diff the context schema and scaffold a forward migration (when migrations are wired
  into the IR, §8; `MigrateFrom` already exists at the engine level).

phase0-codegen-scope.md §5 (Increment E) is the concrete build plan, grounded in exactly what
`regen-state-machine.sh` does today and the frictions it exposes.

## 10. Persistence and runtime (unchanged)

None of this changes how a machine *runs*. Trax snapshot persistence, the four generic `stateMachine`
mutations, draft TTL/expiry, and the exactly-once effect machinery are as-is. The generated types and the
IR-driven factory plug into the existing engine and persistence. What changed is the *authoring path around*
the runtime: the machine is authored declaratively in C#, the exporter emits the IR, and the frontend runs
the IR through the interpreter. "Runtime unchanged" is true for execution, not for the build and parity
tooling that wraps it.

Note the client twin does not mirror every transition: effect-bound edges use these same mutations but the
client fires and waits (§2). That is a boundary rule, not a runtime change.

## 11. Guard rails: drift is a failure, not a memory

The "Status" column is the honest part: a compile error is truly automatic, a `--check` test needs CI to run,
and several guards are still unexercised or unbuilt. Read it as current-state, not aspiration.

| Drift | Guard | Failure mode | Status |
|-------|-------|--------------|--------|
| Structure (state/trigger in one runtime, not another) | generated from the IR + interpreted | build fails (`--check`) / compile error | built for migrated machines (turnstile, write-to-congress); no C# artifact (interpret-at-runtime) |
| Data type (field is `string` here, `int` there) | generated types + schema validators | compile error | built (context/spec types generated) |
| Declarative guard (a boundary differs) | interpreted from the same IR data both sides | replay divergence, or compile error | built (one rule vocabulary, two interpreters) |
| Custom guard/reducer logic | differential replay | test fails, names the case | replay + corpus built; TS is the oracle; **unexercised** (no custom handler exists yet) |
| Reducer reads a clock/RNG/effect directly (nondeterministic wire) | pure-reducer rule; differential replay | test fails (diverges from its own golden) | convention (the rule) + test |
| Missing custom handler | generated stub/interface | compile error | designed; unexercised (no custom handler yet) |
| Context schema changed without a migration | schema-diff check | build fails | deferred (Decision E: declarative machines single-version, version-mismatch reject; `MigrateFrom` exists but is not in the IR) |
| Migration present but wrong (drops/mis-renames a field) | migration golden (stored vN -> expected vN+1) | test fails | golden built; `MigrateFrom` works; migrations not yet carried in the IR/declarative pipeline (§8) |
| Canonical wire diverges across languages | differential byte-exact compare + full JCS | test fails | built (full RFC 8785 both runtimes) |
| Canonicalizer itself is wrong (every runtime agrees on a bad encoding) | independent RFC 8785 vector gate | test fails | built (shared conformance vectors, C# included) |
| Forged snapshot loaded into an unreachable state | reachability-from-initial check in `Rehydrate` | load rejected | **unbuilt** (security control, §2/§12) |
| `serverFilled` field trusted from client input | engine strips it before the reducer | value overwritten server-side | **unbuilt** (security control, §2/§12) |

The compile-error rows are automatic by construction. The test rows get there only when the test is built
*and* CI runs it *and*, for custom guards/reducers, a probe actually exercises the divergent branch. The last
is still a convention until probe generation and per-guard adversarial seeding are enforced (§7), and the two
security rows do not get to be tests at all (§2). Naming the gaps is the point.

## 12. Rollout (phased, learning-gated)

The generator was grown from real machines, not built up front. Phase 0 is largely done; the remaining order
puts the developer-facing CLI ahead of the oracle flip, because the CLI is what a real team is fighting now.

**Prerequisites, revisited:**

1. **The codegen round-trip mechanism (§6).** *Resolved differently than planned.* For declarative machines
   there is no attachment at all (logic is interpreted from IR data); the partial/registry mechanism is
   designed but unexercised, and its first real use (a machine with a custom handler) is a de-risking
   milestone, not a blocker for the machines that exist.
2. **Full JCS + conformance gate (§4).** *Built.* Both runtimes implement the ECMAScript number/string rules
   and share the RFC 8785 vector suite.
3. **The migration golden (§8).** *Built,* and the engine-level `MigrateFrom` works. Carrying migrations into
   the IR/generated frontend is deferred (Decision E), a scope choice, not a missing prerequisite.
4. **The trust-boundary enforcement (§2).** *Still open, and the one true blocker for a security-sensitive
   product.* The engine-level `serverFilled` field-strip and the reachability check in `Rehydrate` are
   *security* controls, so they degrade to a vulnerability, not a failed build, and cannot rely on a test that
   might not run. They need a concrete design (per-field IR metadata + a reachability-from-initial check).
   Write-to-congress dodges the `serverFilled` path (it uses effect-bound `RunsOnce`), so the gap is currently
   latent, not exploited, but it must be closed before a machine ships an optimistic `serverFilled` field.

The phases below are in execution order; the letters in parentheses are the matching increments in
phase0-codegen-scope.md §5. Note the priority swap: the CLI (phase0 Increment E) is pulled ahead of the
oracle flip (phase0 Increment D).

- **Phase 0 — POC on the existing twins (phase0 Increment A/B). (Mostly done.)** turnstile's data layer is
  exported to the IR; the types + factory + probe contexts are generated for TypeScript from the IR; the
  interpreter runs the declarative rules; and a one-line source change propagates with the differential green.
  The source-and-oracle *source* half is built; the oracle half is deferred to Phase 3 below.
- **Phase 1 — machine #2, a real product (phase0 Increment E's checkout precursor). (Done.)**
  write-to-congress, a genuine Tier-1 flow (8 states, an effect-bound send, a guided/unguided branch), was
  built through the generator end to end and is the first product consumer. It surfaced the deploy-topology and
  CLI requirements folded into §4 and §9.
- **Phase 2 — the `trax machine` CLI (phase0 Increment E) (§9).** Replace `regen-state-machine.sh` with real
  `new`/`generate`/`check`/`migrate` commands that run the exporter and generators directly, take per-artifact
  output roots and an import-style option, and run against a vendored engine `src`. Also re-author `checkout`
  declaratively through the pipeline and retire `machine.json` + `generate.mjs`. **Scheduled before the oracle
  flip** because it removes friction a real team hits on every machine edit today.
- **Phase 3 — the oracle flip (phase0 Increment D) (§7).** Port `enumerate()` (the BFS corpus generator) to
  C#, so C# produces the golden and every runtime replays it. The engines are already proven-equivalent, so
  this is about which side owns the golden. Also remove the upstream `Assert.Ignore`-on-missing behavior so
  Trax.Effect's own CI fails rather than skips (§7).
- **Phase 4 — vocabulary + a third language.** Firm up the declarative vocabulary against a third machine's
  real needs, and add a second frontend target (Rust or similar) end to end: the engine + interpreter once,
  the IR generator, the replay, the canonical serializer. This is the real test of the fixed-cost-per-language
  model and proves the IR is genuinely language-neutral.

Establish the Tier-1/Tier-2 rule (§1) in parallel so nobody reaches for the heavy stack on a modal.

## 13. Open questions

The core direction (C# as source, a neutral IR, generate the frontends, interpret the declarative logic) is
decided and, for its core, built. What is genuinely still open:

- **The oracle flip timing and the enumerator port (§7, Phase 3).** Decided in principle (C# should own the
  golden); the port of `enumerate()` and the upstream skip-removal are the remaining work.
- **The trust-boundary enforcement design (§2, prerequisite 4).** The concrete per-field IR metadata for
  `serverFilled` and the reachability-from-initial check in `Rehydrate`. This is the one open item that is a
  security control, not a convenience.
- **Guard vocabulary scope (§2).** Exactly where the declarative/hand-written line sits, decided empirically
  against the third machine, not speculatively. Sized at ~8 primitives / ~92% today (phase0 §3).
- **IR schema format + negotiation plumbing (§5).** The compatibility *rules* are settled (additive-only is
  minor; semantic/subtractive is major and gated). What remains is the concrete JSON Schema and how a
  generator declares which IR majors it accepts.
- **The custom-handler round-trip, in practice (§6).** The partial/registry attachment is designed but
  unexercised. Confirm it on the first machine that needs a custom handler.

**Resolved since the original draft** (moved out of "open"):

- **Deploy topology (§4).** Decided: vendor the engine source at a pinned commit, check the IR/twin/corpus
  into the consuming repo. This also closes the §7 CI-skip problem on the consumer side.
- **`machine.json`'s fate.** Retired for migrated machines: the IR is the sole spec, exported from C#.
  `machine.json` survives only for the unmigrated `checkout`, pending Phase 2 cleanup.
- **Generator home.** Converge on `Trax.Cli` (§9), not the node `generate.mjs`.

## 14. Risks and tradeoffs

- **The generator becomes load-bearing infrastructure.** A bug in it breaks every machine at once. It needs a
  real test suite, excellent error messages, and a watch mode, or the DX collapses. The `trax machine` CLI
  (§9) is where this lives.
- **The custom-handler path is unproven.** The whole approach's fallback for the ~8% (named handlers, twinned,
  differential-guarded) has zero real uses. It is designed, not proven; the first machine that needs it is a
  real de-risking event.
- **N-language custom logic.** The irreducible cost, mitigated by the vocabulary and the server-only escape
  hatch. Keep the custom client-side handler set small on purpose.
- **The trust-boundary controls are unbuilt (§2).** `serverFilled` field-strip and reachability rejection are
  the one failure class that is a *vulnerability*, not a failed build. A wrong migration or a client forgery
  is not caught by the compiler or the differential.
- **The oracle is still the client runtime (§7).** For a server-authoritative design, having TypeScript own
  the golden is backwards; the flip (Phase 3) is scheduled but not done.
- **Over-machining.** Without the Tier-1/Tier-2 discipline, teams will machine-ify simple UI and drown in
  ceremony. The tiering rule is a first-class deliverable. There is also no defined *promotion* path when a
  Tier-2 machine later needs persistence: "we'll just save the draft" is how a Tier-2 machine quietly becomes
  Tier-1.
- **No production-divergence observability.** The differential catches client/server disagreement in CI, but
  the design introduces a new *production* failure mode it says nothing about: a real user's optimistic client
  view and the server's authoritative snapshot disagree in the wild (a stale twin, a serverFilled field).
  There is no story for diagnosing that: correlate the client-optimistic advance with the server's
  authoritative snapshot, log the server's reject reason back through the client, and record which twin
  computed what. The persistence layer already has structured logging (`trax.log`) to build on.
- **Trax's SM subsystem becomes foundational.** If much of the frontend depends on it, its API stability and
  quality matter far more than they do today. Budget for it being a product.

## 15. Non-goals

- Tier-2 client-only UI machines (use a plain client library).
- Transpiling arbitrary guard/reducer logic across languages, or a fully declarative logic DSL. The
  interpret-at-runtime vocabulary (§2) is a bounded, fixed set with a named escape hatch, deliberately not an
  open-ended DSL.
- Live multi-client collaboration / CRDT merge (a separate future layer over the snapshot engine, not this).
