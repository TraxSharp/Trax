---
title: Generic state-machine mutations
description: "Reference for the six opt-in generic stateMachine mutations: the opt-in, their authorization, input and output fields, request ids and size limits."
type: reference
area: machines
nav_order: 22
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Mutations/GenericContracts.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Mutations/GenericTrains.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachineOptions.cs
---

# Generic state-machine mutations

Six mutations under `dispatch { stateMachine { ... } }` that take the machine as a runtime argument (the `machine`
field) and reach every registered user-owned machine. They are not in the schema unless the host opts in. A
machine's own typed operations are the [machines root](/docs/sdk-reference/graphql-api/machines); moving a client
from these to it is covered in
[Generic State-Machine Mutations](/docs/migration-guides/generic-state-machine-mutations).

## Opt-in

| Call | Effect |
| --- | --- |
| `StateMachineOptions.ExposeGenericStateMachineMutations(o => o.Policy = "...")`, in the `configure` argument of `AddStateMachines` | Serves the six mutations, each behind `GenericStateMachineMutationOptions.Policy`. |
| The same call with no `Policy`, or a blank one | `AddStateMachines` throws `InvalidOperationException`. |
| No call | None of the six is in the schema. The trains behind them are still mediator routes, so host code and a machine's typed surface dispatch them. |

While the opt-in is on, every user-owned machine is reachable through the mutations, so each must declare a rule for
every event, its start and its reads, under the same startup check as a machine marked `[TraxMachine]`.

## Authorization

| Check | Refusal |
| --- | --- |
| Each mutation's train carries `[TraxAuthorize]`, and the opt-in's policy gates its field | `TRAX_AUTHORIZATION` GraphQL error ("Not authorized."), before the mutation runs |
| `ISnapshotPrincipal.CurrentUserKey` is null | `unauthenticated` problem |
| The machine's own rule for the event, start or read | `forbidden` problem |

## Mutations

| Mutation | Train | Does |
| --- | --- | --- |
| `startSnapshot` | `IStartSnapshot` | Creates an instance from the machine's declared `start`. The server mints the id and builds the first snapshot. |
| `saveSnapshot` | `ISaveSnapshot` | Soft path: validates a whole client snapshot and stores it. Refused as `save-not-allowed` for a machine with server-side behaviour (a gated event or start, a timer, an invoked train, an effect). |
| `advanceSnapshot` | `IAdvanceSnapshot` | Authoritative path: fires a trigger on the stored draft, server-side. |
| `loadSnapshot` | `ILoadSnapshot` | Reads the caller's stored draft, held to the machine's `get` rule. A missing draft, and one the rule does not let the caller read, answer the same problem. |
| `sendSnapshot` | `ISendSnapshot` | Runs the machine's one irreversible effect exactly once and advances with its receipt. |
| `discardSnapshot` | `IDiscardSnapshot` | Deletes one of the caller's instances, cancelling a run its state invoked first. |

All six are mutations, `loadSnapshot` included. Each takes one `input` argument and answers `output { ... }`. The
snapshot crosses the wire as a string of canonical JSON in both directions. Each train withholds its output from the
lifecycle hooks, so the broadcaster and SignalR never carry an instance's snapshot.

## Inputs

`!` is required.

| Input type | Fields |
| --- | --- |
| `StartSnapshotInput` | `machine: String!`, `requestId: String!`, `input: String` (the start input as a JSON object), `sensitiveInput: String`, `schemaHash: String` |
| `SaveSnapshotInput` | `machine: String!`, `id: UUID!`, `snapshot: String!` (the whole snapshot as JSON), `schemaHash: String` |
| `AdvanceSnapshotInput` | `machine: String!`, `id: UUID!`, `trigger: String!` (a trigger name, `"Coin"`), `input: String` (the trigger input as JSON), `sensitiveInput: String`, `requestId: String`, `schemaHash: String`, `clientResult: String` |
| `LoadSnapshotInput` | `machine: String!`, `id: UUID!`, `schemaHash: String` |
| `SendSnapshotInput` | `machine: String!`, `id: UUID!`, `requestId: String`, `schemaHash: String` |
| `DiscardSnapshotInput` | `machine: String!`, `id: UUID!`, `requestId: String!` |

| Field | Meaning |
| --- | --- |
| `sensitiveInput` | Fields of the input recorded masked, as a JSON object merged into `input` before it is applied. Every copy of the run's input Trax keeps writes them as `{"_redacted": true}`. A field must not be in both. |
| `schemaHash` | The client's machine schema hash. A mismatch is refused as `schema-mismatch` before anything runs. Absent: no check. See [runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity). |
| `clientResult` | The snapshot the client's twin computed for the advance. A different server result is refused as `client-divergence` and nothing is written. Absent: no check. |

## Outputs

| Output | Fields |
| --- | --- |
| `startSnapshot` | `id`, `created` (false on a retry of the start that created it), `snapshot`, `revision`, `dueAt`, `problem` |
| `saveSnapshot`, `loadSnapshot` | `snapshot`, `revision`, `rowId`, `dueAt`, `problem` |
| `advanceSnapshot`, `sendSnapshot` | `snapshot`, `revision`, `rowId`, `dueAt`, `dueAtKnown`, `problem` |
| `discardSnapshot` | `discarded` (true when the instance is gone, already or by this request; another user's id answers the same), `problem` |

Exactly one of `snapshot` and `problem` is set (`discarded` and `problem` for a discard). When `problem` is set,
nothing was written. `revision` orders the writes of one `rowId`, which names one incarnation of the instance.

## Request ids

| Mutation | `requestId` |
| --- | --- |
| `startSnapshot` | required. The same id and input return the instance the first request created; the same id with another input is `request-id-reused`. |
| `advanceSnapshot` | optional. The same id with the same trigger replays the result instead of firing again; with another trigger, `request-id-reused`. |
| `sendSnapshot` | optional; `send:{id}` when absent. Shares one id space with `advanceSnapshot`, so an id an advance used is `request-id-reused`, before the effect runs. |
| `discardSnapshot` | required. |

A blank id, one holding a control character, or one longer than 200 characters is `invalid-request-id`. The full
matching rule is under
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched).

## Limits

A snapshot, a trigger or start input, a `clientResult`, or an advanced snapshot larger than 64 KiB
(`SnapshotLimits.MaxSnapshotBytes`) is `too-large`, and nothing is written.

## sendSnapshot

`sendSnapshot` takes no trigger. It fires the one transition the machine binds its effect to, and only from that
transition's source state; from any other state it is `no-transition`. The receipt the effect returns reaches the
transition's reducer as `input["receipt"]`. An effect that throws is `delivery-failed`, and the draft is not
advanced, so the send can be retried.

## Example

```graphql
mutation Advance($i: AdvanceSnapshotInput!) {
  dispatch { stateMachine { advanceSnapshot(input: $i) {
    output { snapshot problem { code message } }
  } } }
}
```

```json
{ "i": { "machine": "turnstile", "id": "33333333-3333-3333-3333-333333333333",
         "trigger": "Coin", "input": "{\"coin\":\"quarter\"}" } }
```

```json
{ "data": { "dispatch": { "stateMachine": { "advanceSnapshot": { "output": {
  "snapshot": "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}",
  "problem": null } } } } } }
```

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) | [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity)

## See also

- [Author and serve a machine](/docs/statemachine/authoring)
- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#options)
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
