---
title: Machines root
description: "Reference for the machines GraphQL root: each exposed state machine served as its own typed start, events, discard and get, and machineChanged."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 12
source:
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/MachineTypeModule.cs
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/MachineSurface.cs
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/MachineChanged.cs
---

# Machines root

Each state machine marked `[TraxMachine]` is served as its own typed API under the `machines` field of both roots,
however the host registered it. Nothing is served for a machine that is not marked: the attribute is the only
switch, as `[TraxQuery]` and `[TraxMutation]` are for a train. Trains keep their own `[TraxMutation]` and `[TraxQuery]` fields beside it: a host serves machines and plain
trains together. The decision is
[ADR 0048](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md).
Moving a client off the generic `stateMachine` mutations, which are no longer served by default, is covered in
[Generic State-Machine Mutations](/docs/migration-guides/generic-state-machine-mutations).

## Members

| Field | Root | Arguments | Answers |
| --- | --- | --- | --- |
| `machines` | both | none | The namespace of every exposed machine. It and each machine's field group operations, so the [operation cap](/docs/sdk-reference/graphql-api/add-trax-graphql) counts the operations under them, not them. |
| `machines.<machine>` | both | none | One field per exposed machine, named from its kebab-case id in camelCase (`topic-map` is `topicMap`). |
| `start` | mutation | `input: <StartInput>!`, `requestId: String!` | Creates an instance for the caller from the machine's declared start. The server mints the id. `input` is the record `Start<TStartInput>` declares, never the context type, and is left out when that record has no field. A retry with the same `requestId` and input answers with the same instance; the same `requestId` with another input is `request-id-reused`. |
| `<event>` | mutation | `id: UUID!`, `input: <Input>!`, `requestId: String!`, `clientResult: String` | Sends one client event, named from its trigger in camelCase (`CancelBuild` is `cancelBuild`). `input` is the record the event's `WithInput<T>()` declares, and is absent for an event declared without one: never a JSON scalar. |
| `<effect event>` | mutation | `id: UUID!`, `requestId: String!` | An event bound to the machine's `RunsOnce` effect. The server runs the effect, once per instance however many retries arrive with one `requestId`, and records its result; the client sends no input, and no `clientResult`, since it cannot predict the effect's receipt. |
| `discard` | mutation | `id: UUID!`, `requestId: String!` | Deletes the caller's instance, held to the machine's `Discard(...)` rule. A missing id, and another user's, answers `discarded: true`. A row the server put in its state (an effect's record, an invoked train's outcome, an event gated by `Requires` or `RequiresPolicy`, carried on by a timer), an instance in an invoking state, and one with an effect pending on it are refused as `discard-not-allowed` and kept. Answers `MachineDiscardResult { id discarded problem }`. |
| `get` | query | `id: UUID!` | The caller's instance, or null. Another user's id, and one the machine's `Get` rule refuses the caller, read exactly as an unknown id. |

An outcome of an invoked train and a timer have no field: only the server fires them.

Every operation field carries `@authorize`, so an anonymous caller is refused before any resolver runs. Each event's
own rule (`Requires`, `RequiresPolicy`, `AnyAuthenticated`) is checked below the field, where every surface meets,
and a caller it refuses is answered with the problem `forbidden`, not an error.

### Answers

Every operation but `discard` answers with the machine's `<Machine>MachineResult`:

| Field | Type | Description |
| --- | --- | --- |
| `id` | `UUID` | The instance's id; on a refusal, the id the request named. |
| `revision` | `Long` | The revision the write left the instance at, bumped by the database on every write. It travels beside the snapshot, never inside the context. |
| `state` | `<Machine>MachineState` | The state, an enum whose values are the machine's state names as it declares them (`BuildFailed`). |
| `context` | `<Machine>MachineContext` | Every field any state's context record declares, by its JSON name, each nullable: a field the state the instance is in does not hold is null. Absent for a machine whose states declare no context record. |
| `dueAt` | `DateTime` | When the timer of the state the instance is in fires, from the row's `due_at`, or null when that state has no timer or the server cleared it. Every answer carries it, `start` and `get` included, beside the snapshot and never inside it. |
| `dueAtKnown` | `Boolean!` | False when the answer replays a request the instance has moved past since: the server records a request's result, not its due time, so `dueAt` is null and means nothing. Treat `dueAt` as absent then. True on every other answer. |
| `problem` | `SnapshotProblem` | `{ code message }` when the operation was refused, else null. When it is set, nothing was written. |

A refusal is data. The codes are those of the [result codes](/docs/sdk-reference/statemachine-api/result-codes):
`invalid-input`, `guard-failed` (its message is the guard's `Because` text), `no-transition`, `forbidden`,
`request-id-reused`, `invalid-request-id`, `client-divergence`, `too-large`, `not-found` and the rest. A failure the
server did not expect, such as the database going away, is `internal-error` with a fixed message and a reference
the server logs the failure under; the answer carries no exception text, SQL or host.

## Behavior

Exposing a machine requires [persisted operations](/docs/persisted-operations): each machine's operations are
served as [persisted documents](#persisted-documents) that carry its version, and a node orders a machine's versions
across a rolling deploy through them. A host that exposes a machine without `UsePersistedOperations` refuses to start,
naming the machines and the call to add. `SingleNode()` suits one process; more than one node uses
`UseRabbitMqInvalidation(connectionString)`.

### Names

The input types are the CLR records, named as HotChocolate names them (`WizardStartInput`), and registered once
however many machines and trains take one record. Their fields, and the context's, carry the record's JSON names:
camelCase, or the `[JsonPropertyName]` a property declares (`note_text`). A `TimeSpan` crosses as the ISO-8601
duration a timer reads (`"PT5M"`), both in an input and in the context, so a timer whose duration the context holds is
armed from a typed event. The types the root generates carry the
machine's name and the word `Machine` (`TopicMapMachineState`, `TopicMapMachineContext`, `TopicMapMachineResult`,
`TopicMapMachineMutations`, `TopicMapMachineQueries`), so a CLR type the schema already holds, such as a machine's
own state enum a train returns, keeps its name.

The host refuses to build the schema, naming both, when two exposed machines' ids give one field (`map-2d` and
`map2d`), when two events of a machine give one field, and when an event is named `start`, `discard` or `get`. A
host's own field named `machines` on either root is refused at startup. Two registered machine types with one id,
served or not, refuse the host's start too, naming both: a subclass has its base's id, so a host serving a machine
through a marked subclass registers only the subclass
([AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#serving-a-machine)).

The machine's types come from `IMachine.Triggers` (each trigger's kind and input record), `IMachine.States` (each
state's `MachineState`: its `Name` and the `ContextType` its `Context<TContext>()` declares) and
`IMachine.StartInputType`, so any machine derived from `Machine<TState, TTrigger>` that has an IR can be exposed.

### How an operation runs

Each write dispatches the internal train it stands for through the mediator, as the generic mutations do:
`start` runs `IStartSnapshot`, an event `IAdvanceSnapshot`, an effect event `ISendSnapshot`, and `discard`
`IDiscardSnapshot`. So a typed event is authorized, recorded as a run, audited and mapped to a problem on the one
path every surface shares, and the machine's rules and input validation run below it. Those internal trains are
never published on the lifecycle subscriptions, whatever the host streams, and no lifecycle hook receives their
output: the broadcaster (RabbitMQ) and SignalR carry their runs' state changes with `output` null, so an instance's
context reaches its owner only through `get` and `machineChanged`. Their runs are recorded as any other train's.

- **One write per operation.** The fields under a namespace resolve in parallel, so an operation that selects more
  than one `start`, `discard` or event, on one machine or several, is refused at validation with
  `TRAX_ONE_MACHINE_EVENT` before any of them runs.
- **Reads share the request's data context.** `get` reads through the machine's draft service in the request's
  scope, one read of a request at a time, so two aliased `get`s are safe.
- **`requestId` is required** on `start`, `discard` and every event: 1 to 200 characters, not blank, with no control
  character (C0, DEL or C1) and no unpaired surrogate, or the answer is `invalid-request-id` and nothing runs. Two copies of one request racing each other answer with one result.
- **Input is bounded.** An input or a `clientResult` larger than 64 KiB is `too-large` before it is parsed.
- **Sensitive input is masked.** A field the input record marks `[TraxSensitive]`, or one whose type reaches a
  marked member, travels to the train in `AdvanceSnapshotInput.SensitiveInput` (`StartSnapshotInput.SensitiveInput`
  for a start), which every recorded copy of the run's input writes as `{"_redacted": true}`.
- **`clientResult`** is the canonical snapshot the client's twin predicts for the event. A different result is
  refused as `client-divergence`, nothing is written, and the `trax.machine.client_divergence` meter counts it; the
  server logs only its length and SHA-256 hash. It is compared canonically, with a null member equal to an absent
  one, so a prediction rebuilt from a typed answer matches. A retried request is compared with the result recorded
  for it.

### machineChanged

`subscription { machineChanged(machine: "topic-map", id: "...") { rowId revision state context dueAt gone reload problem { code message } } }`
pushes the caller's instance whenever any path on any host writes it: a step from another tab, an invoked train's
outcome applied on another node, a recovery. Over the socket only, by its persisted id when persisted operations are
on.

- **Verified once, routed by row.** The user key is the socket's principal (the host's `ISnapshotPrincipal`), never
  an argument, and the machine's `Watch` rule is checked once, at subscribe. A missing id, another user's, a system
  instance's and one the rule refuses all get the same answer: one event with the problem `not-found`, then the end.
  The stream then follows the row's `row_id`, so a row deleted and created again under the same id never reaches it.
- **Order.** The watch registers before the instance is read, and the first event is the instance as it is then.
  Each later event is a newer revision, whole, migrated and validated as `get` reads it; a burst may arrive as its
  latest revision only. Drop an event, or an answer to your own step, at or below the revision you hold: a skipped
  revision loses nothing. `reload` asks the client to read the instance again; the server sends whole snapshots and
  does not set it today. An event's `dueAt` is always the row's: null means the state has no timer, or the server
  cleared it while the instance stayed in it.
- **The end.** A delete or an expiry ends the stream with `gone: true`. A row written by a newer version of the
  machine than the node runs ends it with `client-outdated` (reload); one the node cannot read otherwise ends it with
  the rehydration code. Ending one stream ends no other. A stream also ends with its socket's credential.
- **Caps.** One principal holds at most 10 streams on one instance and 2,000 in all on a node; past either the
  answer is `too-many-subscriptions`, and a stream that ends frees its place. Each stream holds one of its socket's
  100 operations.
- **Cost.** A node holds one listening session however many streams it serves, and reads a written row once per
  burst of notices, only when something on the node watches it
  ([Snapshot change feed](/docs/sdk-reference/statemachine-api/add-state-machines)).

Operators watch every instance through `onMachineInstanceChanged` instead, behind the operations gate, which carries
an instance's state, revision and runs and never its context or owner
([Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#onmachineinstancechanged)).

### Persisted documents

With [persisted operations](/docs/sdk-reference/persisted-operations/use-persisted-operations) on, each exposed
machine's operations are persisted documents the host generates and registers itself, so a host that requires
persisted operations serves its machines with no upload, and an ad-hoc machine document is refused on every
transport like any other.

- **What is registered.** After the schema builds, and before the host serves anything, one document per operation:
  `start`, `discard`, every client event, `get` and `machineChanged` (never an outcome or a timer). The
  `machineChanged` document names its machine, so it takes only `$id`. A document takes its values as
  variables named like its field's arguments (`input`, `requestId`, `id`, `clientResult`) and selects every field of
  the answer. Its text is built from the schema alone, so every process building one schema writes the same bytes.
- **The id** is `trax_machine_<machine>_<operation>_<irHash><textHash>`: the machine's id, the operation's field,
  the 64 hex characters of the machine's IR hash, and 16 of the SHA-256 of the IR hash and the text. A change to the
  machine changes every id; a change to the GraphQL names alone changes the ids of the documents whose text changed.
  The documents live under the null tenant, and the `trax_machine_` prefix is reserved: upload, deactivate and
  restore refuse it (`RESERVED_ID`).
- **Registration** runs under one lock for every node. A document already stored as generated is left alone: no
  write, no history row, no broadcast. A stored body that does not match its id is written back, logged and counted
  on `trax.persisted_operations.machine_document_repairs` (meter `Trax.Api.PersistedOperations`). Each new set of a
  machine's documents is numbered (the row's `version`) one past every set stored before it.
- **A lookup verifies** the stored body against its id. A body that does not hash to its id is refused, logged and
  counted on `trax.persisted_operations.machine_document_mismatches`.
- **Each node runs only its own documents.** An id of an older set answers `client-outdated` (the client reloads);
  an id of a newer set, registered by a node running a newer version, answers `server-outdated` (retry: another node
  may run it). Both are GraphQL errors whose `extensions.code` is the code, and neither runs anything. Neither is a
  server error: `client-outdated` is HTTP 400 and `server-outdated` 409, so a load balancer counting 5xx answers does
  not count them, and a client reads the code, not the status. An id of a machine this node does not serve is
  answered from the store: `server-outdated` while the store holds it active (a node may serve it), `client-outdated`
  once it was collected. An id that is not shaped like a generated one answers `client-outdated` without a read. An
  id this node has not seen is answered at once from its last read of the store, never by waiting for one: until a
  read that began after the id was first asked for has finished, it answers `server-outdated`, and once such a read
  does not hold it, `client-outdated`. The store is read again in the background, at most once a second however many
  such ids arrive, and the ids waiting for a read are bounded (4096), the oldest forgotten first: a flood of more
  made-up ids than that within a second keeps a real stale client on `server-outdated` until the flood stops. A document id's machine is
  the segment between the `trax_machine_` prefix and the next underscore; a machine id never contains one.
- **Older documents are collected** once a node's own set has been stored for
  [`KeepOlderMachineDocumentsFor`](/docs/sdk-reference/persisted-operations/persisted-operations-builder) (seven
  days by default). Each running node registers its documents again every hour, under the same lock, and that is
  when it collects: a node starting inside the grace period deactivates nothing, and an older set is collected
  within the hour after the grace period ends, with no restart. A registration also marks the active documents of a
  machine the registering node does not serve as unexposed (their deprecation reason starts `unexposed:`), and a
  node that serves the machine clears the mark when it registers, so a machine any running node serves is never
  collected; one still marked after the grace period is collected, and its clients then reload. A collected document
  is made active again only when a node serving it starts (a rollback) or when no newer version of its machine is
  stored, so a node still running an older version past the grace period does not bring back what a newer one
  collected. A node that serves no machine registers too, with no documents of its own, so a deploy that stops
  serving every machine still collects the older documents after the grace period; a registration that fails there
  is logged and tried again an interval later, and does not stop the host.
- **The manifest** is `operations { machineDocuments { id machine operation irHash document } }`, behind the
  operations gate: the documents this node serves, which a client reads at build time.

The id carries the IR hash, so the typed surface needs no `schemaHash`: the optional handshake stays on the generic
mutations only.

### The TypeScript client

`@traxsharp/state-machine/client` drives this root with `createMachineExecutor`. [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) shows it.

### Driving an instance from React

`createMachineServer` and `useServerMachine` drive one instance from a React component, with the server applying every transition. [Drive a machine instance from React](/docs/statemachine/drive-a-machine-instance-from-react) shows them.

## Example

A host serving a machine:

```csharp fragment
[TraxMachine]
public sealed class TopicMapMachine : Machine<TopicMapState, TopicMapTrigger> { ... }

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(typeof(TopicMapMachine).Assembly)
        .AddMediator(typeof(TopicMapMachine).Assembly));
builder.Services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(po => po.SingleNode()));
```

The `topic-map` machine then has:

```graphql
mutation {
  machines {
    topicMap {
      start(requestId: "r-1") { id revision state context { fields } problem { code message } }
      chooseFields(id: "...", input: { fields: ["Hydrology"] }, requestId: "r-2") { revision state }
      discard(id: "...", requestId: "r-3") { id discarded problem { code message } }
    }
  }
}

query { machines { topicMap { get(id: "...") { id revision state context { fields mapId } } } } }
```

## See also

- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
- [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript)
- [Drive a machine instance from React](/docs/statemachine/drive-a-machine-instance-from-react)
