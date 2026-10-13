---
title: State Machine API
description: "Index of the State Machine API reference: registration, authoring, the declarative data model, IR format, effects, migrations, persistence and result codes."
type: landing
area: machines
parent: SDK Reference
has_children: true
section: SDK Reference
nav_order: 13
---

# State Machine API

Reference for authoring and hosting portable snapshot state machines. The concepts are on
[State machines](/docs/statemachine); the walkthroughs are [Authoring a machine](/docs/statemachine/authoring) and
[Declarative authoring](/docs/statemachine/declarative-authoring).

## Trax.Effect.StateMachine

- [Rules](/docs/sdk-reference/statemachine-api/rules): the string-free vocabulary for declarative guards and reducers.
- [Declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model): `Rule`, `Reduction`, `ContextSchema`, `DeclarativeModel` and the engine definition.
- [IR format](/docs/sdk-reference/statemachine-api/ir-format): the exported `machine.ir.json`.
- [Migrations](/docs/sdk-reference/statemachine-api/migrations): `MigrateFrom` and forward-migrating stored drafts.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes): the typed outcomes of advance, rehydrate and the draft operations.

## Trax.Effect.StateMachine.Persistence

- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines): discovering machines and wiring the subsystem in one call.
- [Machine](/docs/sdk-reference/statemachine-api/machine): `Machine<TState, TTrigger>` and its builders.
- [ISnapshotEffect](/docs/sdk-reference/statemachine-api/i-snapshot-effect): the side effect a transition runs once, and its receipt.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports): `ISnapshotPrincipal`, `ISnapshotStore`, `ISnapshotDraftService` and the stores.
- [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances): system-owned instances, created and advanced from code.
- [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity): the schema-hash handshake, divergence detection and the self-check.
- [AddTraxStateMachineSelfCheck](/docs/sdk-reference/statemachine-api/add-trax-state-machine-self-check): the self-check as an ASP.NET Core health check.

## Trax.Effect.StateMachine.Testing

- [MachineConformance](/docs/sdk-reference/statemachine-api/machine-conformance): checking a machine against its committed IR and corpus in a test.

## @trax/state-machine (npm)

- [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package): the engine twin, the stores and hooks, and the server clients.
