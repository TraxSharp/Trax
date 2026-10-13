---
layout: default
title: Authoring a machine
description: How to author a state machine as a Machine subclass, wire it into a host, drive it over GraphQL, and keep the C# and TypeScript runtimes in parity.
parent: State Machines
nav_order: 1
---

# Authoring a machine

A machine is a class. You subclass `Machine<TState, TTrigger>`, declare its states, transitions, guards,
reducers, committed states, and its one irreversible effect inline, and a host discovers it with one line.
There is no per-machine registration and no effect wiring in the composition root.

## Declare the machine

`TState` and `TTrigger` are your own enums. Everything about a transition lives on the transition it belongs
to: the guard that admits it, the reducer that computes the next context, and the effect it fires.

```csharp
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine;

public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, Fresh);

        m.In(CheckoutState.Cart)
            .Holds(ctx => ItemsIsArray(ctx) && ReceiptEmpty(ctx) ? null : "Cart: items[] and no receipt.")
            .On(CheckoutTrigger.Next)
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .On(CheckoutTrigger.Pay)
            .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
            .Because("Checkout needs items and a receipt to be paid.")
            .RunsOnce<ICharge>("checkout:charge")
            .Reduce((ctx, input) => WithReceipt(ctx, Receipt(input)))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid).Committed();
    }
}
```

| Builder call | What it declares |
| --- | --- |
| `Id` / `Version` / `StartsAt` | the machine's stable name, its definition version, and the initial state plus a factory for its context |
| `In(state)` | opens a state to add its context rule and its outgoing transitions |
| `Holds(validator)` | the state's context rule: return `null` when valid, or a reason string. Enforced on the way in (rehydrate) and out (advance) |
| `On(trigger)` | starts a transition out of the current state |
| `When(guard)` / `Because(message)` | admits the transition only when the guard passes; the message is surfaced on a rejection |
| `Reduce(reducer)` | computes the next context. Return a fresh JSON object; never mutate the input |
| `RunsOnce<TEffect>(keyPrefix)` | binds an irreversible effect that fires exactly once when this transition is sent |
| `To(state)` | the destination |
| `Committed()` | marks a state a soft autosave must not overwrite (a completed order) |

Guards and reducers are named code, never serialized. The snapshot carries structure and data, never logic.

## Wire it into a host

One builder step. `AddStateMachines` discovers every machine and wires the store, the effect-claim ledger,
the exactly-once runner, and the registry, over the data context of the provider you configured in
`AddEffects`, and contributes the six generic mutation trains to the mediator scan (they
ship in the persistence package, not your assembly, so Trax can route them by input type). Call it before
`AddMediator`. The host binds only the two things a machine can't know: how to map its auth to a user key, and
each effect implementation.

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();
```

Abandoned drafts expire: a background sweep deletes a user's draft idle past `DraftTtl` (30 days unless the
`configure` overload sets another), so a forgotten form never lingers. A load never deletes.

```csharp
trax.AddStateMachines(
    o => o.DraftTtl = TimeSpan.FromDays(30),
    typeof(CheckoutMachine).Assembly);
```

`ISnapshotPrincipal` maps the current caller to the user key that scopes drafts. Binding it over Trax's own
`TraxCaller` is a one-liner:

```csharp
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

## Drive it over GraphQL

A machine is served to clients when its class is marked `[TraxMachine]` (from `Trax.Effect.Attributes`), as a train
is with `[TraxMutation]`; a machine without it is not served, and there is no host-side switch that serves it. The
attribute is read from every machine the host registers, by the `AddStateMachines` scan or as `IMachine` by hand.
To serve a machine you do not own, derive a class from it, mark that, and register only the subclass: a subclass has
its base's id, and a host that registers two machine types under one id refuses to start, naming both. So the base
must not be registered (do not scan its assembly, and do not call a registration of the library's that adds it), and
a sealed machine cannot be served this way. The same type registered twice is one machine. The attribute is not
inherited: a subclass of a marked machine is served only if it is marked itself, and a host registering an unmarked
one refuses to start. On anything else a scanned assembly holds that the scan cannot register (an abstract or open
generic class, or a class that is no machine) it refuses startup too; a closed generic machine the host registers
by its closed type, from an assembly it does not scan, is served. Each served machine is its own typed API under the [machines root](/docs/sdk-reference/graphql-api/machines): `start`,
one mutation per event taking the event's own input type, `discard` and `get`, each answering with the stored state,
its typed context and its revision, or a problem. The host must also call `UsePersistedOperations` in
`AddTraxGraphQL`: each operation is served as a persisted document that carries the machine's version, and a host
that serves a machine without them refuses to start.

```graphql
mutation { machines { checkout { addItem(id: "...", input: { item: "book" }, requestId: "r-1") {
  revision state context { items total } problem { code message } } } } }
```

### The generic mutations

A host that opts in with `ExposeGenericStateMachineMutations(o => o.Policy = "...")` also serves six generic mutations
for every registered machine under the `stateMachine` namespace, each behind the policy it names. The machine is a
runtime argument (the `machine` field), so there is no per-machine mutation to write. They were served by default
before; upgrading a host that used them is covered in [Generic State-Machine Mutations](/docs/migration-guides/generic-state-machine-mutations).

| Mutation | Trust level |
| --- | --- |
| `startSnapshot` | create: the server mints the id and builds the first snapshot from the machine's declared `start` |
| `saveSnapshot` | soft path: the client sends a whole snapshot, the server validates and stores it |
| `advanceSnapshot` | authoritative: the client sends a trigger, the server re-drives the stored draft |
| `loadSnapshot` | resume: read the caller's stored draft, held to the machine's `get` rule (a missing draft is normal, not an error, and a draft the rule does not let the caller read answers the same) |
| `sendSnapshot` | run the machine's one irreversible effect, exactly once and state-gated |
| `discardSnapshot` | delete one of the caller's own instances, cancelling a run its state invoked first |

```graphql
mutation {
  dispatch { stateMachine { sendSnapshot(input: {
    machine: "checkout", id: "…", requestId: "pay-1"
  }) { output { snapshot problem { code } } } } }
}
```

All six live under `dispatch { stateMachine { ... } }`, `loadSnapshot` included: it is a mutation, not a
query. Each takes one `input` argument and returns `output { snapshot problem { code message } }`, where exactly one
of `snapshot` and `problem` is set; `startSnapshot` adds the minted `id`, `created` and `revision`, and
`discardSnapshot` returns `output { discarded problem { code message } }`. The snapshot crosses the wire as a
**string** of canonical JSON in both directions, so a client `JSON.stringify`s the snapshot it saves and parses the one it gets back.

| Mutation | Input type | Fields (`!` is required) |
| --- | --- | --- |
| `startSnapshot` | `StartSnapshotInput` | `machine: String!`, `requestId: String!`, `input: String` (the start input as a JSON object), `schemaHash: String` |
| `saveSnapshot` | `SaveSnapshotInput` | `machine: String!`, `id: UUID!`, `snapshot: String!` (the whole snapshot as JSON), `schemaHash: String` |
| `advanceSnapshot` | `AdvanceSnapshotInput` | `machine: String!`, `id: UUID!`, `trigger: String!` (a trigger name, `"Coin"`), `input: String` (the trigger input as JSON, `"{\"coin\":\"quarter\"}"`), `requestId: String`, `schemaHash: String`, `clientResult: String` |
| `loadSnapshot` | `LoadSnapshotInput` | `machine: String!`, `id: UUID!`, `schemaHash: String` |
| `sendSnapshot` | `SendSnapshotInput` | `machine: String!`, `id: UUID!`, `requestId: String`, `schemaHash: String` |
| `discardSnapshot` | `DiscardSnapshotInput` | `machine: String!`, `id: UUID!`, `requestId: String!` |

`sendSnapshot` takes no trigger: it fires the one transition the machine binds its effect to, and only from that
transition's source state (from any other state it is `no-transition`). The receipt the effect returns reaches the
transition's reducer as `input["receipt"]`. `schemaHash` and `clientResult` are the
[runtime-integrity](/docs/sdk-reference/statemachine-api/runtime-integrity) checks; leave them out and nothing is
checked.

With variables, the way a browser client sends it:

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

Every rejection comes back as a typed `problem` in the data, never a thrown error across the boundary: an
unknown machine is `unknown-machine`, an invalid snapshot is `invalid-context`, a stale write is `conflict`.
A snapshot, a trigger input, or an advanced snapshot larger than 64 KiB is `too-large`, and nothing is written.

A `requestId` makes a retry safe: the same id with the same trigger returns the current snapshot instead of
firing again, and the same id with a different trigger is refused as `request-id-reused`. Advance and send
share one id space; a send with no `requestId` uses `send:{id}`. The full rule is under
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched).
The six snapshot mutations carry `[TraxAuthorize]`, so an unauthenticated caller is refused before the mutation
runs, with the `TRAX_AUTHORIZATION` GraphQL error ("Not authorized.") rather than a `problem`. A request that
reaches a mutation with no user key (`ISnapshotPrincipal.CurrentUserKey` is null) is refused as
`unauthenticated`.

A complete, runnable version of this (two machines, a GraphQL host, exactly-once over the wire, a forward
migration and a server-checked total) is the [State Machine sample](/docs/samples/state-machine).

## Keep the two runtimes in parity

A TypeScript twin is generated from the machine's [IR](/docs/sdk-reference/statemachine-api/ir-format), so it
needs a [declaratively authored](/docs/statemachine/declarative-authoring) machine; the delegate machine above
cannot be exported. The exhaustive [differential corpus](/docs/statemachine#two-runtimes-one-behavior) is
generated from the same IR. Its inputs are authored in C#, in `Configure`, with `.Differential(...)`:

- **Samples**: a few representative inputs per trigger (a no-input case is always added). A guard that accepts
  `quarter`/`dollar` wants one input that passes, one that fails, and an empty one.
- **Seeds**: a representative valid context for any state a trigger cannot reach (context that arrives via
  autosave rather than a transition). The initial state and everything reachable from it need no seed.

```csharp
m.Differential(d => d
    .Sample(CheckoutTrigger.Pay, new JsonObject { ["receipt"] = "rcpt_1" })
    .EmptySample(CheckoutTrigger.Pay)
    .Seed(CheckoutState.Review, new JsonObject
    {
        ["items"] = new JsonArray("book"),
        ["total"] = 5,
        ["receipt"] = null,
    }));
```

They are exported into the IR's `differential` block, and
[`trax machine generate --corpus-out`](/docs/reference/cli#trax-machine-generate) regenerates
`differential.json` from it; commit the result. There is no `machine.json` to edit: the CLI reads only the IR
it exports from your compiled machine. Both engines then replay the corpus and fail loudly if their guards or
reducers ever drift apart. The [IDifferentialBuilder](/docs/sdk-reference/statemachine-api/fluent-authoring#idifferentialbuilder)
reference lists the typed overloads and probe contexts.

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) | [Machine authoring](/docs/sdk-reference/statemachine-api/fluent-authoring) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
