---
title: Move off the generic state-machine mutations
description: "The generic stateMachine mutations are now opt-in: what stops working on upgrade, how to move to each machine's typed API, and what opting back in requires."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 1
---

# Move off the generic state-machine mutations

Applies from: Trax 1.63.1.

The generic `stateMachine` mutations are no longer served by default. Each machine a host names is served as its own
typed API instead, under the [machines root](/docs/sdk-reference/graphql-api/machines). The decision is
[ADR 0048](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md).

## What stops working

Before, `AddStateMachines` put these under `dispatch { stateMachine { ... } }` for every registered machine:
`saveSnapshot`, `advanceSnapshot`, `loadSnapshot` and `sendSnapshot`. After the upgrade none of them, nor the new
`startSnapshot` and `discardSnapshot`, is in the schema unless the host opts in. A client that still sends
them gets a GraphQL validation error, so nothing runs. That includes the TypeScript client built with
`createSnapshotClient(createHttpExecutor(...))` and the `createDraftSession` on top of it, which send these
documents.

The trains behind the mutations are still registered as mediator routes, so host code that dispatches them keeps
working. Only the GraphQL fields are gone.

You have two ways forward: move each machine to its typed API (recommended), or opt back in to the generic
mutations, which now come with conditions.

## Moving to the typed API

### 1. Expose the machine

Mark each machine a client may reach with `[TraxMachine]`. Nothing is served for a machine that is not marked: the
attribute is the only switch, as `[TraxQuery]` and `[TraxMutation]` are for a train, and it is read from every
machine the host registers, whether `AddStateMachines` found it in a scanned assembly or the host registered it as
`IMachine` itself. To serve a machine you do not own, derive a class from it, mark that, and register only the
subclass. A subclass has its base's id, so a host that also registers the base (by scanning the library's assembly,
or through a registration of the library's) refuses to start, naming both. A sealed machine cannot be served this way.

```csharp fragment
[TraxMachine]
public sealed class TopicMapMachine : Machine<TopicMapState, TopicMapTrigger> { ... }

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(typeof(TopicMapMachine).Assembly)
        .AddMediator(typeof(TopicMapMachine).Assembly));
```

An exposed machine must be declarative (built with the fluent builder, so it has an IR) and user-owned. A machine
built from delegates, or one that declares `SystemOwned()`, refuses startup when exposed; host code still reaches it.

### 2. Turn on persisted operations

Exposing a machine requires [persisted operations](/docs/sdk-reference/persisted-operations/use-persisted-operations).
The host generates and registers each exposed machine's documents itself, so there is nothing to upload:

```csharp fragment
builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted => persisted.SingleNode()));
```

A client reads the documents' ids from `operations { machineDocuments { ... } }` at build time (see
[Persisted documents](/docs/sdk-reference/graphql-api/machines#persisted-documents)).

### 3. Declare a rule on everything a client can do

An exposed machine declares who may do each thing, and the host refuses to start, naming every gap in one
exception, until it does:

| What | Declared with | Allowed rules |
| --- | --- | --- |
| each client or effect-bound event | `.On(trigger)` followed by the rule | `Requires(roles)`, `RequiresPolicy(name)`, `AnyAuthenticated()` |
| `start` | `Start<TStartInput>(s => s.Reduce(...).<rule>())` | `Requires`, `RequiresPolicy`, `AnyAuthenticated` |
| `get` | `Get(r => ...)` | owner-only or stricter |
| `machineChanged` | `Watch(r => ...)` | owner-only or stricter |
| `discard` | `Discard(r => ...)` | owner-only or stricter |

`Anonymous()` on an event or the start of a user-owned machine refuses startup, and so does a policy the host does not
register. There is no machine-level default: an event added later without a rule stops the host from starting rather
than inheriting one.

```csharp fragment
m.Id("topic-map")
    .Version(1)
    .StartsAt(TopicMapState.ChoosingFields, () => new JsonObject())
    .Start<TopicMapStart>(s => s.Reduce(Keep()).AnyAuthenticated())
    .Get(r => r.OwnerOnly())
    .Watch(r => r.OwnerOnly())
    .Discard(r => r.OwnerOnly());

m.In(TopicMapState.ChoosingRange)
    .On(TopicMapTrigger.Back).AnyAuthenticated().To(TopicMapState.ChoosingFields)
    .On(TopicMapTrigger.Build).Requires(RecoveryRoles.Operator).WithInput<RangeChoice>().To(TopicMapState.Building);
```

The rules are checked below the resolver, where every surface meets, so the same rule holds for host code acting for
a client. The [authoring page](/docs/sdk-reference/statemachine-api/add-state-machines) lists every startup check.

Whatever the `discard` rule, an instance in a state only the server puts it in (a committed state, an effect's
target, a state that invokes a train, an invoked train's outcome target, or the target of an event gated by
`Requires` or `RequiresPolicy`), or one an effect is running on, is not discarded: the answer is
`discard-not-allowed`. A flow that discarded such a draft to start over declares an event back to its first step
instead.

### 4. Map the operations

| Before (generic) | After (typed, under `machines { <machine> { ... } }`) |
| --- | --- |
| creating a draft with the first `saveSnapshot` under an id the client chose | `start(input, requestId)`; the server mints the id |
| `advanceSnapshot(machine, id, trigger, input, requestId)` | one mutation per event: `<event>(id, input, requestId)` |
| `sendSnapshot(machine, id, requestId)` | the effect-bound event's own mutation: `<event>(id, requestId)` |
| `loadSnapshot(machine, id)` | `query { machines { <machine> { get(id) } } }` |
| `discardSnapshot(machine, id, requestId)` | `discard(id, requestId)` |
| `saveSnapshot(machine, id, snapshot)` | none: every change is a declared event |
| polling `loadSnapshot` for server-made changes | `subscription { machineChanged(...) }`, pushed on every write |

`requestId` is required on every event, `start` and `discard`; a retry with the same id answers what the first
request recorded. Each event takes the record its `WithInput<T>()` declares, typed in the schema, and an event without
one takes no input. A typed answer carries the instance's `revision`, so a client can order what it receives.

There is no save on the typed API. A flow that autosaved a client-computed snapshot moves to events: give each step
that changes the context a declared event with an input record, and let the server apply it.

### 5. Move the client

In TypeScript, `createMachineExecutor(endpoint, { machines })` drives the typed API through the same
`createSnapshotClient`, `createDraftSession` and `startDraft` as before, and `createMachineServer` with
`useServerMachine` drives one instance from React, with the server applying every transition and pushing each
change. Both are described under [The TypeScript client](/docs/statemachine/call-a-machine-from-typescript)
and [Driving an instance from React](/docs/statemachine/drive-a-machine-instance-from-react).

## Opting back in

A host that cannot move yet serves the generic mutations again with `ExposeGenericStateMachineMutations`, which takes
the name of an authorization policy every caller of the six mutations must satisfy:

```csharp fragment
builder.Services.AddAuthorization(o =>
    o.AddPolicy("StateMachineClients", p => p.RequireAuthenticatedUser()));

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(
            m => m.ExposeGenericStateMachineMutations(g => g.Policy = "StateMachineClients"),
            typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));
```

The opt-in brings three conditions:

- **A policy is required.** Calling it without `Policy` refuses startup.
- **Every user-owned machine needs its rules.** While the opt-in is on, the generic mutations reach every user-owned
  machine, so each one is held to the same startup check as an exposed machine: a rule on every event, the start,
  `get`, `machineChanged` and `discard`, as in step 3 above. System-owned machines are not reachable and need none.
- **The generic save serves only machines with no server-side behaviour.** `saveSnapshot` answers
  `save-not-allowed`, and writes nothing, for a machine with an event or start whose rule restricts callers
  (`Requires` or `RequiresPolicy`), a timer, an invoked train or an effect. Such a machine moves only through its
  declared events: `advanceSnapshot` and `sendSnapshot` still serve it, under each event's rule. A client that
  autosaved one must send events instead.

The opt-in and exposed machines can be combined, for instance while a client moves one machine at a time.

## Defaults an upgraded host gets

These apply to every host that calls `AddStateMachines`, whichever surface serves its machines. Set each on
`StateMachineOptions` to change it (see [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#options)):

| Option | Default | Effect |
| --- | --- | --- |
| `DraftTtl` | 30 days | A background sweep deletes a user's draft idle that long. It keeps a draft waiting on an invoked run, one with a timer pending, one an effect is running on, and an instance in a state only the server puts it in. Set `DraftTtl = null` to keep idle drafts. |
| `MaxDraftLifetime` | 90 days | The sweep deletes a user's draft created longer ago, whatever its timers or activity, cancelling a run it waits on first; it keeps an instance in a state only the server puts it in. Set it to `null` for no bound; with `DraftTtl = null` as well, drafts live until they are discarded. A `DraftTtl` you set longer than it makes `AddStateMachines` throw. |
| `MaxDraftsPerUser` | 50 | A `start`, or a save that creates a draft, past it is refused as `too-many-drafts`. Drafts idle past `DraftTtl` and system-owned instances do not count. Set it to `null` for no cap. |
| `ChangeFeed` | `Notify` | On Postgres, each host listens for the change notices migration `078` sends. `Poll` reads the watched rows instead. |

The upgrade also applies migrations `076` to `080` (SQLite `038` to `040`), which add the revision, the start
request id and request ring, the change notices, the timers' due time and the switch that turns the notices off.
Each is described, with what a rolling deploy sees, in [Schema migrations](/docs/reference/schema-migrations#state-machine-revision-076-sqlite-038).

## Related

- [Machines root](/docs/sdk-reference/graphql-api/machines): the typed API, its answers, the TypeScript client and the
  persisted documents.
- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines): the options and the startup
  checks.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes): `forbidden`, `save-not-allowed`,
  `request-id-reused` and the rest.
