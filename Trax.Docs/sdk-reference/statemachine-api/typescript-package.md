---
layout: default
title: TypeScript package
description: "Reference for @trax/state-machine: the engine twin, the typed view, the React hooks, the server clients and the rule interpreter, entry point by entry point."
parent: State Machine API
grand_parent: SDK Reference
nav_order: 14
---

# TypeScript package

`@trax/state-machine` is the TypeScript twin of `Trax.Effect.StateMachine`. It has three entry points:

| Entry point | Holds |
| --- | --- |
| `@trax/state-machine` | The engine, the typed view, the framework-free stores, the IR interpreter and the generators. Pure: no I/O. |
| `@trax/state-machine/client` | Everything that talks to a Trax server: the generic and typed executors, draft sessions, and the server-authoritative controller. |
| `@trax/state-machine/react` | `useMachine` and `useServerMachine`. React is an optional peer dependency. |

The package's exports are pinned by a committed baseline (`src/exports.test.ts`), so a new export is a reviewed
change. The operations each client sends are described on the [machines root](/docs/sdk-reference/graphql-api/machines)
page; the result codes on [Result codes](/docs/sdk-reference/statemachine-api/result-codes).

## The engine and the typed view

From `@trax/state-machine`:

| Export | What it is |
| --- | --- |
| `SnapshotMachine` | The reducer, the twin of the C# `SnapshotMachine`: `createInitialSnapshot`, `advance`, `applyOutcome`, `applyTimer`, `timer`, `rehydrate`, `serialize`, `canFire`, `availableTriggers`, `describe`. Every operation is total: `advance` answers with an `AdvanceResult`, `rehydrate` with a `RehydrationResult`. |
| `RejectionReasons`, `RehydrationErrorCodes` | The codes those results carry, identical to the C# constants. `RejectionReasons.TooLarge` (`too-large`) is answered only by a server. |
| `TypedMachine`, `typedMachineFromIr(ir, schemaHash?)` | The same engine with one machine's states, triggers and contexts as types (`MachineSpec`, `StateOf`, `TriggerOf`, `ContextOf`, `InputOf`). `typedMachineFromIr` builds one from an IR document; a generated twin calls it. |
| `machineFromIr(ir)` | Builds the untyped `MachineDefinition` an IR document describes, with its declarative guards, reducers, input schemas, outcomes and timers. |
| `MachineDefinition`, `TransitionDefinition`, `MachineTimer`, `Guard`, `Reduce`, `ContextValidator`, `InputValidator` | The definition `SnapshotMachine` runs. `InputValidator` is `(input) => string \| null`: the check of one trigger's input against its declared schema, null when it matches. |
| `IrDocument`, `IrTransition`, `IrOutcome`, `IrOutcomeEdge`, `IrTimer` | The [IR format](/docs/sdk-reference/statemachine-api/ir-format) as types. `IrTimer` is one state's timer: `state`, `trigger`, `target`, and either `after` or a context `field` with `min` and `max` (ISO-8601 durations). |
| `evaluateRule`, `applyReduction`, `validateSchema`, `validateInput` | The rule interpreter. `validateSchema(schema, context)` checks a context; `validateInput(schema, input)` checks a trigger's input as the C# engine does (no input reads as `{}`, anything but a plain object is refused). Both answer null when valid, else a message, and never throw. |
| `Rule`, `Reduction`, `ValueSource`, `SetStep`, `ContextSchema`, `FieldSchema`, `ValueSchema`, `RuleSource`, `JsonFieldType`, `CompareOp`, `CustomGuards`, `CustomReducers` | The [declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model). `ValueSchema` is the shape of one value (its `type`, `nullable`, an integer's `minimum` and `maximum`, a record's `fields`, a map's or list's `values`); `FieldSchema` adds the field's `name` and `constraints`. |
| `generateContextTypes`, `generateMachineFactory` | The generators that emit a twin from an IR. `trax machine generate` and the drift check call them. |
| `enumerate`, `serializeCorpus` | Enumerate a machine into the differential corpus the C# engine replays. |

## Driving a machine the client owns

When the browser holds the snapshot (an offline form, a machine with no server), use a store over a `TypedMachine`:

| Export | What it is |
| --- | --- |
| `MachineController` | A framework-free store: `send`, `can`, `available`, `reset`, `subscribe`, `getSnapshot`. It persists through `persist` after every successful transition, and a refused action still emits a view with its `problem`. |
| `useMachine(machine, { initial, persist?, observer?, devtools? })` | From `@trax/state-machine/react`: the controller as a hook. It returns `snapshot`, `state`, `context`, `send`, `can`, `available`, `lastProblem` and `reset`. |
| `createFormView(machine, { steps, toContext })` | For a UI that owns its data and asks the machine: maps each UI step to a state (one to one), and gives `advance`, `can`, `snapshotFor`, `serialize`, `resume` and `isCommitted` in the UI's own step names. |
| `problemFromAdvance`, `problemFromRehydration`, `Problem` | A refusal shaped for display. |
| `createDevLogger` | Logs transitions and refusals in development. |

## Talking to a server

From `@trax/state-machine/client`:

| Export | What it is |
| --- | --- |
| `createSnapshotClient(executor)` | A client for the six generic operations, `(machine, id)` on every call. Served only when the host [opts in](/docs/migration-guides/generic-state-machine-mutations). |
| `createHttpExecutor(endpoint, options?)` | The executor that sends the generic mutations' documents over HTTP. |
| `createMachineExecutor(endpoint, { machines, ... })` | The executor for machines the host exposes on the typed root, behind the same client and sessions. Given each machine's `documents` (`MachineDocument`), it sends persisted ids. |
| `createDraftSession({ client, machine, id })`, `startDraft({ client, machine, requestId, input? })` | One draft of one machine, every method total: a refusal is a `{ ok: false, code, message }`, an unreachable server is `TRANSPORT_ERROR` (`transport-error`). `startDraft` creates a draft under an id the server mints. |
| `createMachineServer(endpoint, { machine, documents, socket })` | One exposed machine's typed operations and its `machineChanged` stream, as the `MachineServer` a controller drives. `persistedMessageReplacer` is the graphql-ws `jsonMessageReplacer` it needs, and `DEFAULT_RESUBSCRIBE_DELAYS` (1, 2, then 5 seconds) the delays before a dropped stream is subscribed again. |
| `ServerMachineController` | The framework-free, server-authoritative store: the server applies every transition and the twin predicts. `start`, `send`, `retry` (which resends a start or event that failed in transport, or stayed `server-outdated` past every retry, under its first request id), `can`, `available`, `get`, `subscribe`, `dispose`. |
| `DEFAULT_RETRY_DELAYS` | The controller's default delays between retries of a `server-outdated` answer or a failed read: 250, 500, 1,000, 2,000 and 4,000 ms. Override with `retryDelays`. |
| `TRANSPORT_FAILED` | The problem code (`transport-error`) of a send or read the controller could not deliver. It is the same string as `TRANSPORT_ERROR`, the sessions' code, exported under both names. |

`useServerMachine(machine, { server, id?, startInput?, startRequestId?, startStorageKey?, newRequestId?, retryDelays? })`, from
`@trax/state-machine/react`, is `ServerMachineController` as a hook. Its options, its answer and an example are under
[Driving an instance from React](/docs/sdk-reference/graphql-api/machines#driving-an-instance-from-react).
