# Tier-1 State Machines: codegen, multi-language, and scaffolding

Status: Draft / RFC (2026-08-07)

## Why this exists

The goal is to run large parts of a frontend off state machines wherever it makes sense, with
write-to-congress as the proof of concept. That surfaces a tension:

1. **Parity by construction.** The C# (server) and every frontend runtime must stay in lockstep on
   structure, data types, and behavior. Drift should fail a build or a test, never ship silently.
2. **Low-friction updates.** A developer adding a field or a step should not have to hand-edit the same
   thing in several languages, hand-pick edge cases, or remember a migration. Forgetting any of those must
   be impossible, not merely discouraged.

Today's baseline sits at the fully hand-written extreme: two hand-written twins, hand-authored
differential contexts, structure restated in `machine.json`. That maximizes friction and drift surface.
The `turnstile` and `checkout` machines demonstrate that baseline today, though it is already spread across
repos: the TypeScript twins, the `machine.json` per machine, and the hand-seeded differential live in this
repo, while the C# twins live in `Trax.Effect` test fakes and `Trax.Samples`. That cross-repo scatter is
itself part of the friction, and it is what a product machine like write-to-congress would inherit. This document describes the Tier-1 architecture that
resolves the tension: generate everything that is data, hand-write only the behavior that cannot be shared,
and make every category of drift a build failure.

This is a design/RFC, not an implementation plan for a single PR. It ends with a phased rollout that uses
write-to-congress to prove the pipeline before it is generalized.

## Decisions at a glance

The load-bearing decisions, so they are not buried under the caveats that follow:

1. **Tiering (§1).** A machine is Tier 1 (this stack) if and only if its state is persisted and re-driven by
   the server, or it drives an exactly-once effect. Everything else is Tier 2 and out of scope.
2. **C# is both source and oracle (§3).** Author the machine in C#, export a neutral IR, generate every
   frontend from it. C# will enumerate the golden corpus; frontends conform to it.
3. **Data generated, logic twinned (§2).** Structure/types/validators are generated. Guards get a small
   declarative vocabulary plus a server-only escape hatch; reducers get neither and must be pure
   `(context, input)`; effect-bound transitions are server-only (the client fires and waits).
4. **The differential is the parity guard (§7).** Every runtime replays the C# oracle's golden and must match
   byte-for-byte. It checks that hand-written logic *agrees* across runtimes, the one thing that cannot be
   generated. It does not check that the logic is *correct*, a reducer wrong identically in every runtime
   still passes (see §4).
5. **Full JCS is a prerequisite (§4).** Byte-exact wire needs real RFC 8785 serialization plus an independent
   conformance gate. The current serializers only key-sort, so this is unbuilt work, not a checkbox.
6. **A migration golden guards correctness (§8).** Migrations are verified against real stored vN drafts, not
   just checked for existence.

Status is the honest part: most of this is unbuilt. Today C# is verified *against* a TypeScript oracle, the
IR and its declarative layer do not exist, and probe generation is a goal, not a feature. Phase 0 (§12) builds
the source-and-oracle inversion; everything else is sequenced behind it.

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

## 2. The core split: data is generated, logic is twinned

A Tier-1 machine has two layers with very different economics.

- **Data (fully generatable).** States, triggers, the transition structure, the context shape (field names +
  types), the shape/type validators, the differential probe inputs, and the GraphQL mutation fan. All of
  this is pure data. It is single-sourced and generated into every runtime. Two different strengths of guard
  apply, and it is worth not conflating them: a **type** mismatch is a *compile error* in anything that
  consumes the generated types, which is genuinely construction-level; a **stale generated file** is caught by
  `--check`, a CI gate that is strong but not literally impossible (it fires only if CI runs and nobody
  hand-edits the output). The §11 table lists the honest failure mode for each. Both beat a runtime failure,
  but "impossible by construction" is accurate only for the typed half.
- **Logic (not generatable).** Guards and reducers, the actual behavior (`body.length >= 6`, the
  guided/unguided branch, "a `Sent` snapshot requires a receipt"). This cannot be shared across languages
  without serializing logic into data, which is a road with worse potholes (see §3, alternatives). So it is
  hand-written per runtime and kept honest by the differential.

Two levers shrink the hand-written logic surface, which matters a lot once there is more than one frontend
language:

- **A small declarative guard vocabulary.** Common guard shapes (`required`, `minLength`, `min`/`max`,
  `oneOf`, simple cross-field predicates) live in the schema and generate into every runtime, exactly as
  valibot/zod already do for shape. Only genuinely-custom guards fall back to named hand-written functions.
  Draw the line deliberately: a *small* vocabulary that covers the ~80% case. Let it grow to cover everything
  and you have re-invented the serialized-logic DSL you are trying to avoid.
- **Server-only guards (the escape hatch).** A guard too complex to express declaratively and not worth
  hand-writing in every frontend language can be omitted from the frontends entirely. The client optimistically
  allows the action; the server (authoritative) rejects it; the client renders the typed rejection. This
  bounds the per-language logic cost: you only hand-write in N languages the guards you want to run
  *optimistically on the client*. Everything else is server-authoritative and costs nothing on the frontend.

**Reducers are the other half of twinned logic, and they do not get these levers.** The declarative
vocabulary and the server-only escape hatch are both guard-shaped. A guard returns accept/reject; a *reducer*
transforms the context, and its output is the canonical wire that gets persisted and compared. There is no
"declarative reducer vocabulary" here, so any non-trivial reducer is N-plicate hand-written by default, and a
reducer divergence is worse than a guard divergence: it corrupts persisted state rather than flipping an
accept/reject. The differential (§7) catches a divergence, but the authoring cost of N-plicate reducers is
real and must be counted in the cost model (§4) alongside custom guards, not folded silently into "guards".
Keep reducers thin and total; push branching into guards where the vocabulary can absorb it.

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
  therefore no parity hole. `checkout`'s `Pay -> Paid` is the canonical case: it binds
  `RunsOnce<ICheckoutCharge>("checkout:charge")`, the charge runs first, its receipt flows in as reducer
  input, and `Paid` is unreachable until the charge completes. Crash between charge and transition is safe: re-drive reuses the existing receipt (the
  exactly-once claim blocks a second charge) and finishes the move. The server was always going to wait for
  the real receipt; the design's only job is to stop the *client* from pretending it can skip that wait.
- **Optimistic-advance-with-reconcile is a rare, annotated exception, never the default and never for an
  irreversible action.** If a transition's outcome is *usually* predictable and the round-trip would feel
  sluggish (a generated share link, say), a machine may advance optimistically with a placeholder and
  reconcile on the server response, but only behind an explicit per-field `serverFilled` / `excludeFromParity`
  annotation, so every field the differential is *not* guarding is visible in the IR and reviewable. A payment
  is never a candidate: nobody wants an optimistic "Paid" before the charge clears.

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
  never resurrect a draft into a state the guards would not allow. Optimism is a rendering strategy; the
  server re-derives the truth. Note the reachability half is unbuilt: `Rehydrate` today validates the context
  and that the state token parses, but does no reachability-from-initial analysis, so a forged snapshot in any
  parseable state with a passing validator currently loads cleanly. Rejecting unreachable states is a graph
  check the engine does not have yet; state the requirement and design it, do not assume validation covers it.
- **`serverFilled` / `excludeFromParity` fields are an authorization surface, not just a parity exception.**
  They are, by definition, fields the differential does not guard, so a forged client can write into them.
  The server must own them: overwrite them from the authoritative source on every transition, and authorize
  who may set them. A field being excluded from parity must imply it is server-written, never client-trusted.
  Enforce this in the engine, not by reducer discipline: the generic `Advance` must strip `serverFilled` fields
  from client-supplied input before the reducer runs, so a reducer *cannot* copy a client value forward even
  by mistake. This is unbuilt and non-trivial: today `Advance` passes client `input` straight into the reducer
  with no field-level filtering and no per-field metadata to filter on, so it requires the IR to carry which
  fields are `serverFilled` and the engine to consume that. And because it is a *security* control, not a
  parity one, it cannot degrade to a CI test that might not run: a missing strip is a forgery hole, not a
  failed build.
- **The generated mutation fan inherits user scoping.** Persistence is already per-user
  (`SnapshotEffectRunner.Run(userKey, ...)`), so the generator (§6) must thread the caller's identity into
  every emitted mutation and scope every load/save/advance to it. A generator that emits an unscoped mutation
  is an authz bug shipped at scale, so user-scoping is a generation requirement, not a per-machine reminder.

## 3. Source of truth: author in C#, export a neutral IR, generate the rest

### The decision

Author each machine's **data layer and declarative guards in C#**, have a C# tool export a **neutral
intermediate representation (IR)**, and generate every other runtime (TypeScript, Rust, ...) from the IR.
The IR, not any one language, is the interchange contract.

"C# as source" in practice means: you never hand-author the IR JSON. You author a C# machine (real types,
real tooling), and the IR falls out of the built machine model or a Roslyn source generator. Generators for
each frontend language consume the IR. "Add an exporter" is true only for the *structural* third, though; the
declarative layer needs an authoring surface that does not exist yet, which is the real long pole (next
paragraph).

**Be honest that this is two inversions at once, and both are unbuilt.** Today the pipeline runs the other
way: a hand-authored `machine.json` is the structural source, the Node generator emits the TypeScript twin
from it, TypeScript is the differential oracle, and the C# twin is the side *verified against* the oracle.
This proposal flips the **source** (retire hand-authored `machine.json`; export the IR from the C# machine)
*and* the **oracle** (C# becomes the oracle the frontends must match). Those two flips are coupled and
neither exists yet.

The one existing down payment is `SnapshotMachine.Describe()`, which already exports the *structural* third of
the IR (id, version, states, triggers, edges, canonically sorted) from the C# side and is asserted against a
committed golden. But it stops exactly where the hard part starts. It carries no context schema, no
declarative validators, and no handler bindings, because the C# machine today expresses guards and context
validation as opaque delegates (`When(Func<JsonObject, JsonNode?, bool>)`, `Holds(Func<...>)`), not as
declarative data. **The IR's entire declarative layer (§5) cannot be exported until C# gains a declarative
context-schema and guard-vocabulary authoring surface, and that surface does not exist today.** §2 and §5
assume it. So Phase 0 is really building three things: the declarative C# authoring surface, the full IR
export on top of it, and the oracle flip. Framing it as generalizing an existing pipeline understates the
work; the structural exporter is the easy 20%.

### Why C# is the right authoring surface here

- **Source of truth = the authoritative runtime.** The server is what actually persists and re-drives. Making
  its machine the hand-written, reviewed source (and generating the frontends to match) aligns "truth" with
  "authority". You review the real code that drives production; frontends conform to it.
- **Tooling.** Roslyn and C# source generators are a first-class, mature toolchain for extracting a model
  from C# and generating code at build time.
- **Team fit.** Trax is C#-centric; authoring machines in C# fits how the team already works.
- **The differential oracle flips the right way.** Today TypeScript is the oracle and the C# server is
  verified against it, which is backwards for a server-authoritative design. With C# as source, C# becomes
  the oracle: it enumerates the golden corpus and every frontend must match *it*. "The frontend conforms to
  the server" is exactly the safety property you want.

### What "C# as source" implies (the important part)

Positives are above. The costs to design around:

- **Custom logic is still hand-written per target language.** Generation covers the data layer and the
  declarative guards. A genuinely-custom guard/reducer must exist in C# (authoritative) and in each frontend
  that runs it optimistically. With N frontend languages that is up to N hand-written implementations of that
  one guard, differential-guarded. This is the dominant scaling cost, and it is why the declarative
  vocabulary (§2) and the server-only escape hatch (§2) matter: they keep the N-plicate set as small as
  possible. Most machines should end up with few or zero truly-custom client-side guards.
- **Frontend field changes couple to the C# build.** A frontend developer who wants a new field edits the C#
  source and regenerates; they cannot just edit TypeScript. For a C#-centric team this is fine and even
  desirable (one reviewed source). For a frontend-led org it is friction worth acknowledging.
- **The extractor is real infrastructure.** C# → IR (via the built machine model or Roslyn) is a component
  with its own tests and versioning. The IR is a contract; changing it is a breaking change for every
  generator.
- **Generated frontend code must be excellent.** Frontend developers consume generated types and validators
  and hand-write only custom guards against a generated skeleton. The generated output has to be clean,
  typed, documented, and stable, or the DX is worse than hand-writing.

### Alternatives considered

- **TypeScript as source (today's oracle).** Good structural typing and the valibot ecosystem, but the source
  of truth would not be the authoritative runtime, and the C# server machine would be generated, which is the
  less reassuring direction for the side that owns persistence.
- **Neutral IDL as source (protobuf/smithy style).** Symmetric across all languages, but you hand-author a
  JSON/IDL with a poor editing surface, and expressing guards in it pushes hard toward the serialized-logic
  DSL. Authoring in a real language and *exporting* the neutral IR gets the symmetry (the IR is neutral)
  without the bad authoring DX.
- **Share/transpile the logic (WASM, or a logic DSL).** Eliminates the twins but loses native, reviewable,
  debuggable logic per language, couples deploys, and (for a DSL) becomes a language with N interpreters to
  maintain. Rejected for anything past a purely-structural machine. Write-to-congress has data-dependent
  guards, so it is past that line.

## 4. Cost model: fixed per language, marginal per machine

This is the accounting that determines whether "many machines, several languages" is viable.

- **The engine is itself a per-language twin.** `advance` / `rehydrate` / `serialize` / canonicalization are
  implemented in C# and TypeScript today; a Rust frontend needs a Rust engine too. This is written **once per
  language** and reused by every machine, and it is guarded by the totality + differential suites. It is the
  large fixed cost of adding a language.
- **Per new language target you build:** the engine (once), a generator target (IR → that language's
  types/structure/validators/declarative-guards), a differential `replay` harness, and a byte-exact canonical
  serializer (RFC 8785 JCS + the envelope ordering). All fixed, all reused across machines. Note this list
  slightly overstates the *oracle-flip* cost specifically: C# already has a working `replay` harness
  (`DifferentialCorpus.Replay`, with its own tests), so flipping the oracle to C# is mostly moving the
  *enumerator* (today `enumerate()` in `differential.ts`) to C#, not building C#'s replay side from scratch.
- **Per new machine you build:** the C# source (data + declarative guards + any custom guards and reducers),
  then `generate` produces every runtime's data layer, and you hand-write only the custom client-side guards
  and any non-declarative reducers in each frontend language that needs them (often zero guards; reducers are
  easy to undercount). Cheap when reducers stay thin.

So the shape is **high fixed cost per language, low marginal cost per machine**. Many machines is cheap;
adding a language is a real investment that amortizes across all machines. Plan the language set
deliberately; do not add a runtime casually.

**The canonical-serialization caveat:** every runtime must emit byte-identical canonical wire, so each
language re-implements JCS canonicalization + the fixed envelope order. It is per-language correctness work
that must not be underestimated, and today it is only *half done in the two runtimes that exist*. Both
current `Canonicalize` implementations (`SnapshotMachine.Canonicalize` in C#, `canonicalize` in `machine.ts`)
implement only RFC 8785 §3.2.3, the recursive key sort, and then defer value emission to the platform
(`JsonNode.ToJsonString()` / `JSON.stringify`). Neither does §3.2.2.3 number serialization (the ECMAScript
`Number.prototype.toString` / Ryū algorithm) or §3.2.2.2 string escaping. They agree *today* only because the
demo contexts carry strings and small integers, where `System.Text.Json` and V8 happen to coincide; on a real
number (`0.1`, `1e21`, `-0`, a large long, any non-integer double) they will diverge. So "the canonical
serializer is a solved, reusable fixed cost" is not true even for C# and TypeScript yet.

Two consequences the plan must absorb. First, the differential's byte-exact comparison catches divergence
*between* runtimes, but not a bug in canonicalization itself: the golden is produced by the C# oracle (§7), so
a wrong oracle serializer is faithfully reproduced by every frontend. Byte-exactness proves *agreement*, not
*correctness*. The only thing that verifies the oracle's own serializer is a separate, independent JCS
conformance gate, the published RFC 8785 test vectors checked by **every** runtime including C#, as its own CI
step distinct from any machine golden. Second, adopting that gate is not a checkbox: the current serializers
would *fail* the vectors on numbers and non-ASCII strings, so this is real implementation work in each runtime
(a spec-compliant number/string emitter, replacing the platform hand-off) and it *changes the canonical wire*
for any snapshot carrying non-trivial numbers. That is a breaking change to stored snapshot bytes (and to any
hash/signature over them), and the existing migration machinery cannot express it: the forward-migration (§8)
is keyed by machine `version` and maps `(state, context)` *shapes*, not bytes, so it has no hook for
re-encoding the same logical context under new serialization rules. This needs a *separate* wire/serializer
version axis, orthogonal to machine `version`, plus a story for who re-signs stored hashes and whether old
signatures are grandfathered. Treat it as a data-migration project that touches every stored snapshot at once,
with its own versioning, not a code swap and not a shape migration.

### Deploy topology: where generated frontend artifacts live

The dominant *operational* cost is not the N-language logic, it is the cross-repo regen-and-ship loop, and it
needs an explicit answer. With C# as source, a frontend field change is: edit the C# machine in the server
repo, run the generator, and land the regenerated TypeScript in the frontend repo, both together. That is the
same multi-repo lockstep this workspace's `pack-local` / pinned-version machinery already manages for NuGet,
now extended to the frontend. Decide and document: do the generated frontend artifacts live checked-in in the
frontend repo, or ship as a versioned npm package published from the C# build? If a package, you have
recreated the local-feed / exact-pin dance on the JS side (version bump, lockfile, a local override for
same-workspace development), and the IR version, the package version, and the machine `version` all have to
stay coherent. This topology is a first-class part of the design, not an afterthought; pick it before Phase 0
so the round-trip has a concrete home.

## 5. The IR

One versioned, JSON-Schema'd document per machine, exported from the C# source. It captures everything that
is data:

- Identity: `id`, `version`.
- Structure: `states`, `triggers`, `initialState`, `transitions` (from/trigger/to, plus which guard/reducer
  each edge binds by name).
- Context schema: field names, types, nullability, and enums.
- Declarative validators: shape (from the context schema) and the declarative-vocabulary policy checks.
- Named custom handlers: the names (not bodies) of custom guards/reducers each edge and state binds, so every
  runtime knows which functions it must provide by hand.
- Committed states, effect bindings, and `committedStates` for the persistence layer.
- Migrations: the version history and the field-level diffs (see §8).
- Differential inputs: seeds and, once probe generation lands (§7), the parameters for generated probe
  contexts. Today these are hand-authored sample/seed lists; the IR is where generated probes will live when
  they exist.

The IR is the contract between authoring and every generator. It is versioned independently of any machine,
and because a schema change fans out to every generator in every repo, "evolves deliberately" is not enough:
the IR needs explicit compatibility *rules* so most changes never trigger a flag day.

- **Additive-only by default.** New optional fields, new enum members with a defined default, new sections a
  generator may ignore: these are minor IR versions. A generator built for vN must keep working against vN+1
  by ignoring what it does not understand, so additive changes roll out one repo at a time.
- **Semantic and subtractive changes are major, and gated.** Removing a field, changing the meaning or type
  of an existing one, or tightening a validator is a major IR version. These are the only changes allowed to
  break generators, and they are exactly the ones the version-negotiation window (§13) exists for: during a
  migration, generators declare the IR major version(s) they accept, and the exporter can emit the older major
  until every generator has moved. What is *not* allowed is a silent semantic change inside a minor bump.
- **One coherent version story, not four.** The IR version, the generated-artifact package version, the
  machine `version`, and "the IR versions a generator accepts" all have to line up. Pick a single source of
  truth (the exporter stamps the IR version into the IR; the package version derives from it; a generator
  fails loudly on an unaccepted IR major) rather than letting four axes drift independently.
- **The IR schema itself is `--check`ed.** An IR emitted by a newer exporter than a generator understands must
  fail that generator's build with a clear "unsupported IR major" message, never produce subtly-wrong output.

## 6. Generators

One generator per target language, each `IR -> code`, plus `--check` (regenerate to a temp location, diff
against the committed output, non-zero exit on drift for CI). Each generator emits, for its language:

- state/trigger types (enums or unions) and the transition table
- the context type
- the shape validator and the declarative-vocabulary guards
- stubs/interfaces for the named custom handlers the machine binds (so a missing custom guard is a compile
  error, not a silent no-op)
- the differential `replay` entry point
- server target only: the GraphQL mutation fan + DI wiring

The existing precedent is narrower than it sounds. `tools/state-machine-codegen/generate.mjs` is a **Node**
script (not C#), it reads the hand-authored `machine.json`, and today it emits only the **TypeScript**
structure (state/trigger unions + the edge table) plus a check that `structure.json` is derivable from the
spec. It does not emit validators, and the C# output is explicitly not wired yet (`csOut is wired when the C#
demo machines land`). So the precedent proves "one spec, deterministic per-language structure with a
`--check` drift gate," and nothing more. This design generalizes that to `IR -> N languages` *and* inverts
the source (IR exported from C#, rather than hand-authored JSON), both of which are new work (see §3).

### Round-trip: how hand-written logic attaches to generated code

This is the DX bet the whole approach rests on, so it needs a concrete mechanism, not just "hand-write custom
guards against a generated skeleton." The problem: custom guards and reducers are hand-written and must
survive regeneration without being clobbered, in languages that (unlike C#) have no partial classes. The
contract is:

- **Generated code owns structure and declares an interface of named handlers** it needs, one entry per
  custom guard/reducer the IR binds by name. In C# this is a `partial` the human completes; in TypeScript it
  is a generated interface plus a generated registry the engine calls into, and the human provides an
  implementation object in a **separate, hand-owned file** the generator never writes.
- **The generated file is never hand-edited** (it carries the `AUTO-GENERATED / Do not edit` banner the
  current `.g.ts` already uses). All hand code lives in a sibling file that imports generated symbols.
- **A missing or misnamed handler is a compile error, not a silent no-op.** Adding a field or a custom guard
  in C# and regenerating changes the generated interface, so every frontend that has not implemented the new
  handler fails to compile, pointing at the exact missing name. That compile break is the feature: it is how
  "someone forgot to update a runtime" becomes impossible rather than discovered in production.

The worked loop to hold the design to: *add a `promoCode` field with a custom `promoValid` guard in the C#
machine → `trax machine generate` → the C# `partial` now has an unimplemented `promoValid`, and the
TypeScript build fails on a missing `promoValid` in the handler object → implement it in both hand-files →
differential (§7) confirms they agree.* If that loop is not clean and obvious, the generated DX is worse than
hand-writing and the approach does not pay for itself.

### `generate` is atomic and idempotent, or it corrupts the tree

The generator writes many files across repos, so its failure and concurrency behavior is part of the design,
not an implementation detail.

- **Atomic per run.** A `generate` that writes three of five files and then throws must leave the tree either
  fully updated or untouched, never half-applied. Write to a staging location and swap, or write-all-or-roll-
  back. A half-regen that compiles is the worst case: it looks done and is wrong.
- **Idempotent.** Running `generate` twice with no source change produces byte-identical output and no diff.
  This is what makes `--check` meaningful.
- **Regen races are a real hazard, not a hypothetical.** Two developers regenerating different machines into a
  shared generated-artifact set (or the same npm package, per the §4 deploy topology) will collide. The IR
  export is per-machine, but the generated output and its version are shared, so the merge story for
  concurrently-regenerated artifacts has to be decided with the deploy topology, not left to whoever pushes
  last. This is the same class of pain the workspace already sees when a partial local pack leaves a
  half-updated feed.

## 7. The differential at scale

- **C# is the oracle.** A C# enumerator drives the machine over the reachable space (BFS from initial +
  seeds) crossed with probe contexts, and records each outcome as canonical wire (on a transition) or a
  rejection code. That is the committed golden corpus, one per machine.
- **Probe generation is a design goal, and it does not exist yet. Say so.** Today the *state-space traversal*
  is automated (BFS over reachable snapshots), but the *input values* it crosses are entirely hand-authored:
  `enumerate()` reads `samples`/`seeds`/`contexts` straight from `machine.json`, whose own comment says "keep
  the set small and shape-valid." There is no boundary or property-based generation in the codebase. The
  target is to derive a dense probe set from the context schema and the declared guard boundaries, so the
  developer stops having to think of every edge case, but that is the same unbuilt declarative layer §3/§5
  depend on (you cannot generate boundary probes off a schema that does not exist), and it must be planned
  with the same rigor, not assumed.
- **Even once built, generated probes are weakest exactly where the differential matters most, and today the
  whole burden is on the author.** A property-based generator only knows the boundaries it can *see*, the
  declarative-vocabulary ones. A custom guard's real edges (a guided/unguided branch, "a Sent snapshot
  requires a receipt") are opaque to it, so generated probes would be densest for declarative guards (also
  structurally checked, least likely to diverge) and sparsest for custom guards (the whole reason the
  differential exists). Until generation lands, *every* probe is hand-seeded, so this gap is total, not
  partial. Either way it must be closed explicitly: a machine hand-adds adversarial probes seeded *per custom
  guard*, and the corpus is considered incomplete for any custom guard or reducer that has none. That
  incompleteness needs to be a gate, not a convention (see the §11 note).
- **Every runtime replays the golden and must match** byte-for-byte (wire) or code-for-code (rejection). A
  divergence names the exact case. This is the guard for the one thing that cannot be generated: that the
  hand-written custom guards and reducers agree across languages.
- **The oracle cannot verify its own serializer.** Replay proves runtimes *agree*, not that the oracle is
  *right*; canonicalization correctness is a separate gate (§4).
- **CI regenerates and verifies.** The golden is regenerated from the oracle and the replay must be clean, so
  a corpus that is stale relative to the source fails the build.
- **A parity suite that can silently skip is not a guard, and right now it skips in CI.** The conformance
  tests locate the corpus by walking up the filesystem for the sibling repo (`FixturePaths.Find`) and
  `Assert.Ignore` the whole suite if it is not found (an "isolated build"); the `[TestCaseSource]` variants
  `yield break` to *zero* cases, not even a visible skip. Worse, the skip message points at a
  `Trax.StateMachine.Fixtures` package "supplied in CI" that **does not exist** anywhere in the workspace, and
  Trax.Effect's CI checks out only its own repo, so today the differential is not actually running there. This
  is not a latent risk, it is the current state, and the design's stated remedy is fictional. The real fix is
  the §4 deploy-topology question (how the oracle's golden reaches every consumer's CI), and until it is
  answered the §11 row "custom guard/reducer logic → differential replay → test fails" is aspirational. The
  suite must *fail*, not skip, when the corpus it should check is absent in CI; allow the skip only behind an
  explicit local-dev opt-out that CI never sets.

### Corpus scale is a cost the golden makes concrete

"At scale" has to mean the corpus, not just many machines. The enumerator builds a cross-product (contexts ×
reachable states, each fired against every trigger × input) plus the per-guard adversarial probes above. For
the toy machines that is tens of cases; for a real machine it is combinatorial, committed to git as the
golden, and replayed by every runtime on every push. Bound it deliberately: cap the probe set per guard,
shard the golden per machine, budget CI replay time, and treat a large golden diff as a review artifact (a
diff touching thousands of cases needs the same scrutiny as the source change that caused it).

## 8. Migrations and schema evolution

Adding or changing a persisted context field breaks existing stored drafts unless handled. The tooling makes
this a first-class, hard-to-forget step:

- **Schema-diff-driven scaffolding.** `generate`/`migrate` diffs the new IR context schema against the last
  committed one and scaffolds a forward migration stub (added/removed/renamed fields), rather than relying on
  a developer to remember. The engine already forward-migrates on load.
- **Rename detection is a guess, and the scaffold must say so.** A field rename is structurally identical to a
  remove-plus-add; a schema diff cannot distinguish `foo -> bar` from "dropped `foo`, added `bar`." The
  scaffolder can offer a rename as a *suggestion* the developer confirms, but it must never silently assume
  one, because a wrong guess silently drops persisted data. Treat every rename as human-confirmed.
- **Version discipline.** A context-shape change bumps the machine `version`; a migration maps vN to vN+1. A
  low-stakes additive field may instead use a tolerant default, declared in the schema.
- **The drift guard catches a missing migration, not a wrong one.** A changed context schema with no
  corresponding migration (and no tolerant default) is a build failure, so "I forgot the migration" cannot
  ship. But presence is not correctness: the differential corpus (§7) tests machine *logic*, not that a real
  persisted vN draft forward-migrates to the right vN+1. That needs its own guard.
- **A migration golden.** Alongside the differential corpus, commit a migration golden per version bump: a set
  of stored vN snapshots and their expected vN+1 result, replayed through the real forward-migration path.
  This is the only thing that verifies migration *correctness* against actual stored shapes, and it is where a
  bad rename or a dropped field gets caught before it corrupts user drafts. Without it, the migration story
  guarantees a stub exists, not that it works.

## 9. The CLI (Trax.Cli)

Trax.Cli is already a NuGet `dotnet tool` that turns a schema into generated Trax code (GraphQL/OpenAPI ->
trains, via `GenerateCommand` / `TraxProjectGenerator` / a code renderer). A `machine` command group is the
same architecture applied to state machines, and is the natural home for scaffolding:

- `trax machine new <name>` — scaffold a new Tier-1 machine: the C# source skeleton (states/triggers/context
  + a couple of transitions), both twin stubs' custom-handler files, the differential wiring, and the mutation
  registration. The common case becomes a ~30-second scaffold, and consistency is by construction.
- `trax machine generate` — export the IR from the C# source and regenerate every target language.
- `trax machine check` — `--check` drift across the IR, the generated outputs, and the golden corpus (CI).
- `trax machine migrate` — diff the context schema and scaffold a forward migration.

Keeping this in the CLI (rather than only the node `generate.mjs`) means the same tool that authors Trax API
projects also authors machines, and it ships as a versioned, testable `dotnet tool`.

## 10. Persistence and runtime (unchanged)

None of this changes how a machine *runs*. Trax snapshot persistence, the four generic `stateMachine`
mutations, draft TTL/expiry, and the exactly-once effect machinery are as-is. The generated data layer plugs
into the existing engine and persistence. What does change is the *authoring path around* the runtime: the
fluent builder gains an IR exporter, and the differential oracle flips from TypeScript to C# (§3). "Runtime
unchanged" is true for execution, not for the build and parity tooling that wraps it, so this is not a
zero-surface-area change even though the engine is untouched.

Note the client twin does not mirror every transition: effect-bound edges use these same mutations but the
client fires and waits (§2). That is a boundary rule, not a runtime change.

## 11. Guard rails: drift is a failure, not a memory

The "Status" column is the honest part: a compile error is truly automatic, a `--check` test needs CI to run,
and several guards are unbuilt or still depend on a human seeding a probe. Do not read this table as "all
green today."

| Drift | Guard | Failure mode | Status |
|-------|-------|--------------|--------|
| Structure (state/trigger in one runtime, not another) | generated from the IR | build fails (`--check`) / compile error | partly built (TS structure gen exists; C# gen + IR unbuilt) |
| Data type (field is `string` here, `int` there) | generated validators + generated types | compile error | automatic once generated types exist; types unbuilt |
| Declarative guard (a boundary differs) | generated from the IR | build fails (`--check`) | unbuilt (needs the declarative layer, §3) |
| Custom guard/reducer logic | differential replay vs the C# oracle | test fails, names the case | **convention** — only if a probe hits the branch (§7); replay exists, oracle-in-C# unbuilt |
| Reducer reads a clock/RNG/effect directly (nondeterministic wire) | pure-reducer rule; differential replay | test fails (diverges from its own golden) | convention (the rule) + test |
| Missing custom handler | generated stub/interface | compile error | automatic once generators emit stubs; unbuilt |
| Context schema changed without a migration | schema-diff check | build fails | unbuilt |
| Migration present but wrong (drops/mis-renames a field) | migration golden (stored vN -> expected vN+1) | test fails | unbuilt (§8) |
| Canonical wire diverges across languages | differential byte-exact compare | test fails | built, but only key-sorting today (§4) |
| Canonicalizer itself is wrong (every runtime agrees on a bad encoding) | independent JCS / RFC 8785 vector gate | test fails | unbuilt; current serializers would fail it (§4) |

The *aspiration* is that none of these rely on a developer remembering. The compile-error rows get there by
construction; the test rows get there only when the test is built *and* CI runs it *and*, for custom
guards/reducers, a probe actually exercises the divergent branch. That last one is still a convention until
probe generation and per-guard adversarial seeding are enforced (§7). Naming the gap is the point; pretending
it is already closed is the failure mode this very table is meant to prevent.

## 12. Rollout (phased, learning-gated)

Do not build the generator up front. Grow it from real machines.

**Settle three things before Phase 0** (each is designed above, and each is load-bearing enough that Phase 0
is not honest without it):

1. **The codegen round-trip mechanism** (§6): the concrete way hand-written guards/reducers attach to
   generated code per language, proven on the worked "add a field, regenerate, watch the build break" loop.
   This is the DX the whole approach rests on.
2. **Full JCS in every runtime, plus an independent conformance gate** (§4): the current serializers only
   key-sort and defer number/string emission to the platform, so this means *implementing* spec-compliant
   number/string serialization (not just adding a test), gating it on the RFC 8785 vectors checked by every
   runtime including the C# oracle, and shipping the resulting wire change as a versioned snapshot migration.
   This is upstream of the whole byte-exact safety argument, so it comes first.
3. **The migration golden** (§8): stored vN -> expected vN+1, so migration *correctness* is guarded, not just
   the presence of a stub.
4. **The trust-boundary enforcement mechanism** (§2): the engine-level field-strip for `serverFilled` inputs
   and the reachability check in `Rehydrate`. Unlike the other three this is a *security* control, so it
   degrades to a vulnerability, not a failed build, and cannot rely on a test that might not run. It needs a
   concrete design (per-field IR metadata plus a reachability-from-initial check), not a status line.

- **Phase 0 — POC on the existing twins.** Use the in-repo `turnstile` and `checkout` machines as the
  substrate (they already have the hand-written twins, `machine.json`, and differential). Export their data
  layer into the IR, generate the data layer + shape validators + probe contexts for C# and TypeScript from
  the IR, and prove a one-line source change (add a state/field) propagates to every runtime with the
  differential still green. This de-risks the pipeline for a product machine like write-to-congress; it does
  not depend on that machine existing yet. Note this is the phase that first builds the source-and-oracle
  inversion described in §3, so budget for it as new construction.
- **Phase 1 — machine #2.** Build a genuinely different Tier-1 flow (checkout-shaped: multi-key context, a
  branch, an effect) *through the generator*. The second machine is where the generator's real requirements
  appear; the first always looks easy.
- **Phase 2 — vocabulary + CLI scaffold.** Fix the declarative guard vocabulary and the `trax machine new`
  scaffold, driven by what Phases 0-1 actually needed, not speculatively.
- **Phase 3 — a third language.** Add Rust (or another frontend target) end to end: the engine once, the IR
  generator, the replay, the canonical serializer. This is the real test of the fixed-cost-per-language model
  and the thing that proves the IR is genuinely language-neutral.

Each phase is gated on the previous one's learnings. Establish the Tier-1/Tier-2 rule (§1) in parallel so
nobody reaches for the heavy stack on a modal.

## 13. Open questions

The core direction (C# as source and oracle, a neutral IR, generate the frontends) is decided in §3 and
revisited only if Phase 0 fails. What is genuinely still open:

- **Guard vocabulary scope:** exactly where the declarative/hand-written line sits. Decided empirically in
  Phase 2.
- **Client vs server-only logic:** per machine, which guards run optimistically on the client (hand-written N
  times) vs server-only (deferred). A default posture plus per-guard override.
- **IR schema + versioning:** the compatibility *rules* are now settled in §5 (additive-only is minor;
  semantic/subtractive is major and gated; one coherent version story; the schema is `--check`ed). What
  remains open is the concrete *format* (the JSON Schema itself) and the negotiation *plumbing*: how a
  generator declares which IR majors it accepts and where that declaration lives. Decide alongside the deploy
  topology, since they share the version story.
- **Deploy topology:** where generated frontend artifacts live (checked-in in the frontend repo vs a
  versioned npm package published from the C# build) and how the IR version, package version, and machine
  `version` stay coherent (§4). Pick before Phase 0.
- **Round-trip attachment:** the exact per-language form of handler attachment (C# `partial` vs a generated
  interface + registry + separate hand-file in TypeScript) (§6). Confirmed on Phase 0.
- **Generator home:** the node `generate.mjs` vs the C# `Trax.Cli` vs both (one for CI `--check`, one for
  authoring). Prefer converging on the CLI.

## 14. Risks and tradeoffs

- **The generator becomes load-bearing infrastructure.** A bug in it breaks every machine at once. It needs a
  real test suite, excellent error messages, and a watch mode, or the DX collapses.
- **N-language custom logic.** The irreducible cost. Mitigated by the declarative vocabulary and the
  server-only escape hatch, but real; keep the custom client-side guard set small on purpose.
- **Per-language canonical serialization.** A correctness burden per language, easy to get subtly wrong, and
  only partly built today (§4).
- **Migration correctness, not just presence.** The drift guard proves a migration exists; only the migration
  golden (§8) proves it is right against real stored drafts. A wrong migration silently corrupts persisted
  user state, the one failure class neither the compiler nor the differential covers.
- **Cross-repo regen topology.** Landing regenerated frontend artifacts in lockstep with the C# source is the
  dominant operational cost (§4), easy to underestimate next to the more visible N-language logic cost.
- **Over-machining.** Without the Tier-1/Tier-2 discipline, teams will machine-ify simple UI and drown in
  ceremony. The tiering rule is a first-class deliverable, not a footnote. There is also no defined
  *promotion* path when a Tier-2 machine later needs persistence: rebuilding a client-only machine into this
  stack is real work, and "we'll just save the draft" is how a Tier-2 machine quietly becomes Tier-1.
- **No production-divergence observability.** The differential catches client/server disagreement in CI, but
  the design introduces a new *production* failure mode it says nothing about: a real user's optimistic client
  view and the server's authoritative snapshot disagree in the wild (a stale twin, a guard the client didn't
  run, a serverFilled field). There is no story for diagnosing that: correlate the client-optimistic advance
  with the server's authoritative snapshot, log the server's reject reason back through the generated client,
  and record which twin computed what. For a system meant to run large parts of a frontend, that diagnosis
  path should exist before, not after, the first production divergence. The persistence layer already has
  structured logging (`trax.log`) to build on.
- **Trax's SM subsystem becomes foundational.** If much of the frontend depends on it, its API stability and
  quality matter far more than they do today. Budget for it being a product.

## 15. Non-goals

- Tier-2 client-only UI machines (use a plain client library).
- Transpiling arbitrary guard/reducer logic across languages, or a fully declarative logic DSL.
- Live multi-client collaboration / CRDT merge (a separate future layer over the snapshot engine, not this).
