---
layout: default
title: State Machines
nav_order: 8
has_children: true
section: Packages
---

# State Machines

Trax.Effect.StateMachine represents a multi-step flow (a wizard, an application form, a checkout) as a
small, language-neutral snapshot, so a C# backend and a TypeScript client agree on where a user is in the
flow and the data that goes with it. The whole runtime state of an instance is a serializable snapshot that
can be stored in Postgres and rebuilt from any point, so a user can start on one device and finish on
another. Illegal (state, data) combinations are made unrepresentable, and neither the API nor the client can
produce an unhandled exception.

To author a machine and wire it into a host, start with [Authoring a machine](/docs/statemachine/authoring).

## The two-document model

Keep these two things separate. Conflating them is the classic mistake.

| | Definition | Snapshot |
| --- | --- | --- |
| What it is | the machine's states, transitions, guards, and per-state context rules | one instance's current position and data |
| Where it lives | in code (a C# or TypeScript `MachineDefinition`) | in data (a Postgres row, a GraphQL payload) |
| How often it changes | rarely (a deploy) | constantly (every user action) |
| Crosses the wire | no | yes |

The definition is the program. The snapshot is the value it operates on. A snapshot has exactly four fields,
always in this order: `machine`, `version`, `state`, `context`. `state` is the current step. `context` is
the data for that step, and only that step, so its shape is discriminated by `state`.

## The three guarantees

1. **Resumable from any point.** A snapshot fully determines an instance. Rebuilding it is just a
   `Rehydrate` of the stored JSON. There is no hidden in-memory state to reconstruct.
2. **Illegal states unrepresentable.** Each state validates its own context on the way in (rehydrate) and on
   the way out (advance). A snapshot that claims a state but carries the wrong data is rejected as a typed
   error, never quietly accepted.
3. **No unhandled exceptions.** Every operation is total. `Advance` returns a transitioned or rejected
   result. `Rehydrate` returns an ok or error result. An unpermitted trigger, a failed guard, or malformed
   stored JSON all become typed values, never a throw.

## Two runtimes, one behavior

The engine is implemented twice, once in C# (`Trax.Effect.StateMachine`) and once in TypeScript
(`@trax/state-machine`). They are kept identical not by generating one from the other but by a shared set of
language-neutral conformance fixtures that both engines drive and must agree on. Only the result codes
(`no-transition`, `guard-failed`, `invalid-context`, `malformed`, `unknown-machine`, `version-mismatch`,
`unknown-state`) are contract. Human-readable detail text is free to differ.

Guards and reducers are named code on each side, never serialized expressions. The snapshot carries
structure and data, never logic, so there is nothing to evaluate twice. A machine's structure (its states,
triggers, and edges) is generated for both languages from a single `machine.json`, and a drift check fails
the build if a committed generated file goes stale.

## Persistence and exactly-once effects

The persistence layer is generic over a machine's (state, trigger) pair and stores the context in a real
`jsonb` column. A draft is scoped to its owner by a composite key, and every authoritative write carries an
app-managed optimistic-concurrency token, so concurrent writers get a typed conflict rather than a lost
update or a thrown exception. Two write paths have different trust levels. Autosave is the soft path: the
client sends a whole snapshot, the server validates it and stores it. Advance is the authoritative path: the
client sends only a trigger, and the server re-drives the transition from the stored snapshot, never
trusting a client-computed state.

Some transitions carry an irreversible side effect: charge a card, send a letter, provision a resource.
Those must run exactly once per intent, even under retries, two devices, or a crash mid-flight. The core
provides a generic exactly-once runner keyed on an intent that names the action, not its content. It claims
the intent before running the effect, so two concurrent sends deliver once and a crash-retry replays the
recorded result. A lease with a fence token keeps it live: if a runner wins the claim and then dies, the
lease expires and the next caller reclaims the key, and a revived stuck runner is fenced out of completing
the new claimant's work. A scheduled sweeper releases abandoned claims as a backstop.

## The frontend surface

A React engineer works through one hook. It owns the snapshot, persists after every successful step, and
re-renders on every change, including a declined action so the reason is never silently swallowed. Types
flow from the machine's spec: the current step is a typed string, the context is discriminated by that step,
and a trigger's input is required or forbidden at the type level. Persistence is transition-driven, not a
decoupled timer, which is what prevents a save from landing after the state has already advanced past a
terminal step.

## Where the code lives

| Piece | Location |
| --- | --- |
| C# engine (pure, total, dependency-free) | Trax.Effect.StateMachine |
| C# persistence, exactly-once effects, mutation trains | Trax.Effect.StateMachine.Persistence |
| TypeScript engine, typed facade, React hook | the @trax/state-machine package |
| Shared conformance fixtures and the structure generator | the shared machines directory and the codegen tool |
