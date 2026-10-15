---
title: AddStateMachines
description: "Reference for AddStateMachines, which discovers every Machine in the given assemblies and registers the snapshot store, effect claims and mutation trains."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 1
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachinesBuilderExtensions.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachineOptions.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/MachineCensus.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachineInstruments.cs
redirect_from:
  - /docs/sdk-reference/statemachine-api/add-trax-state-machines
---

# AddStateMachines

Registers state-machine persistence as a step in the `AddTrax` builder chain. One call discovers every
`Machine<TState, TTrigger>` in the given assemblies and wires the subsystem: the snapshot store, the effect-claim
ledger, the exactly-once runner, the machine registry, and the six generic `stateMachine` mutation trains, which it
contributes to the mediator scan so Trax routes them by input type. The stores reach their tables through the
`IDataContext` the database provider configured in `AddEffects` registers (`IDataContext.SnapshotDrafts` and
`IDataContext.EffectClaims`).

## Signatures

```csharp fragment
namespace Trax.Effect.StateMachine.Persistence;

public static class StateMachinesBuilderExtensions
{
    public static TraxBuilderWithEffects AddStateMachines(
        this TraxBuilderWithEffects builder,
        params Assembly[] assemblies
    );

    public static TraxBuilderWithEffects AddStateMachines(
        this TraxBuilderWithEffects builder,
        Action<StateMachineOptions>? configure,
        params Assembly[] assemblies
    );
}
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `assemblies` | `Assembly[]` | No | The assemblies to scan for concrete `Machine<TState, TTrigger>` subclasses; an abstract or open generic class is not discovered. Throws `InvalidOperationException` if `[TraxMachine]` is on a type in them that is not a concrete, non-generic machine class. A machine the host registers as `IMachine` itself (`services.AddSingleton<IMachine, CheckoutMachine>()`, an instance or a factory) is one of the subsystem's machines too, so a host may scan nothing; the scan does not register again a type the host registered before the call. A host that registers no machine either way refuses to start with an `InvalidOperationException`. |
| `configure` | `Action<StateMachineOptions>?` | No | Sets host-level options (see below). The `assemblies`-only overload passes `null`. |

### Options

`StateMachineOptions` carries host-level settings read when the registry builds a machine's draft service.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DraftTtl` | `TimeSpan?` | 30 days (`StateMachineOptions.DefaultDraftTtl`) | How long a user's draft survives without activity (a sliding window on the row's last update). Drafts expire after it by default, and a background sweep on every host that registers machines deletes them, releasing their settled effect claims. A load never deletes a draft. The sweep's delete is conditional on the revision it read, so a draft written after the read is kept. It leaves alone a draft whose state is waiting on an invoked run, a draft whose timer is still pending (up to `MaxDraftLifetime`), and a draft an effect is running on, and never touches a system-owned instance. It keeps, however long it is idle, a row the server put in its state (an effect's record, an invoked train's outcome, an event gated by a role or a policy, carried on by a timer), as `MaxDraftLifetime` does: that is the record of what the server did. A row from before that was recorded is kept while its state is one only the server puts it in. `null` turns idle expiry off; `MaxDraftLifetime` still applies, so set both to `null` for drafts to live until they are discarded. Must be positive. A value the host sets longer than `MaxDraftLifetime` makes `AddStateMachines` throw `InvalidOperationException` naming both. |
| `MaxDraftLifetime` | `TimeSpan?` | 90 days (`StateMachineOptions.DefaultMaxDraftLifetime`) | The longest a user's draft may live, counted from its creation. A draft idle past `DraftTtl` is kept while its state's timer is still pending; the draft sweep deletes a user's draft created longer ago than this, whatever its timers or its activity say. One whose state waits on an invoked run has the run cancelled first. It keeps an instance in a state only the server puts it in (a committed state, an effect's target, an invoked train's outcome target, a gated event's target): that is the record of what the server did, and how long it stays is the host's to decide. It skips a draft an effect is running on until the effect settles. A draft written before its creation time was recorded counts its age from its last write. Set below the default `DraftTtl` on its own, it caps every draft. `null` sets no bound. Must be positive. |
| `MaxDraftsPerUser` | `int?` | 50 (`StateMachineOptions.DefaultMaxDraftsPerUser`) | The most drafts one user may hold, across every machine. A `start`, or a save that creates a new draft, past it is refused with `TOO_MANY_DRAFTS` and writes nothing. A retried `start` returns the instance it created, a save over a draft the user already holds is not a new one, a draft idle past `DraftTtl` no longer counts, a row the server put in its state is a record rather than a draft and never counts, and system-owned instances never count. The count and the insert are one transaction under a lock on the user, so concurrent starts cannot pass it together. `null` sets no cap. Must be positive. On `UseInMemory` the default is not applied, and setting a cap refuses to start: InMemory has no transaction to hold it in. |
| `InvokeOutcomeSweepInterval` | `TimeSpan` | 5 seconds | How often this host's outcome reconciler sweeps for [invoked runs](/docs/statemachine/invoking-trains#how-an-outcome-comes-back) that have ended and applies each one's outcome. The run's own host applies an outcome as soon as it is recorded, and on Postgres a notification wakes every host's reconciler sooner, so this bounds only how long an outcome waits when both miss it: a crash after the run, a run the scheduler reaped or cancelled, a cancel before dispatch. It is also the sweep's pace whenever the notifications are not being heard. Must be positive. |
| `ChangeFeed` | `StateMachineChangeFeed` | `Notify` | How this host hears that an instance changed, to push it to the clients watching it. Postgres only: SQLite and InMemory always poll. `Notify` hears the database's notice of every write at once; every notifying transaction takes one lock shared by the whole cluster and holds it through its commit, so concurrent writes commit one at a time. `Poll` opens no listening session and reads the watched rows' revisions once a second while something watches: a change arrives up to a second later, and each host with an open watch reads the watched rows each second. It takes the snapshot notice off the writes only once the database stops sending it, database-wide (`ALTER DATABASE <name> SET trax.snapshot_notify = 'off'`, migration [`080`](/docs/reference/schema-migrations#state-machine-change-notify-switch-080); a role, connection or `SET LOCAL` switch is not supported while any host listens), and a write in a transaction that sends another notice still takes the lock. A `Notify` host refuses to start when the database, a role or its own session turns the notices off, or the trigger is not enabled for ordinary sessions; a `Poll` host whose database still sends them starts and logs a warning. See [Change feed](#change-feed). |
| `InvokeOutcomeSweepIntervalWhileListening` | `TimeSpan` | 1 minute | How often the reconciler sweeps while it hears Postgres's notification of invoked runs ending. Each notification delivers its run at once, and the reconciler sweeps in full each time it subscribes again after losing them, so this sweep only catches what both miss. Never shorter than `InvokeOutcomeSweepInterval`. Must be positive. |

| Method | Description |
|--------|-------------|
| `ExposeGenericStateMachineMutations(Action<GenericStateMachineMutationOptions> configure)` | Serves the six generic mutations (`startSnapshot`, `saveSnapshot`, `advanceSnapshot`, `loadSnapshot`, `sendSnapshot`, `discardSnapshot`) under `dispatch { stateMachine { ... } }`, each behind `GenericStateMachineMutationOptions.Policy`, the authorization policy a caller must satisfy. Without this call none of them is in the schema. The opt-in without a policy is refused where `AddStateMachines` runs. |

A machine's own typed operations are served by marking it, not by an option: see [Serving a machine](#serving-a-machine).

## Returns

`TraxBuilderWithEffects`, so the chain continues into `AddMediator(...)`.

## Behavior

Call it after `AddEffects(...)` (it needs a data provider) and before `AddMediator(...)` (the mediator builds its
route registry when it runs, so the mutations must be contributed first). Called after `AddMediator(...)`, it does
not compile: the error is `Call AddStateMachines(...) before AddMediator(...).`

Throws `InvalidOperationException` if no data provider was configured in `AddEffects` (the store needs a
database), if `AddMediator` has already run (the mutations would arrive too late to be dispatchable), or if
`AddStateMachines` was already called: call it once, passing every assembly and configuring every option in that
one call.

When the host starts, before any hosted service runs, every registered machine is built and its IR hash
(`IMachine.SchemaHash`) computed. Each write to a machine's instance records that hash, so a machine that cannot be
built, or whose hash fails for any other reason, refuses the host's start with an `InvalidOperationException`
naming it, rather than failing every write it would make. A machine whose IR is not exportable by design has no
hash, which is not a failure: a raw-delegate machine, and a declarative one that still has a delegate guard
(`When(Func...)`), reducer (`Reduce(Func...)`) or validator (`Holds(...)`). Its writes record no hash.

Every machine is looked up by its id (`IMachine.Name`), so a host that registers two machine types under one id
refuses to start with an `InvalidOperationException` naming both. A subclass has its base's id: when one of the two
derives from the other, the refusal says to register only the subclass. The same type registered twice (by the scan
and by the host, or twice by the host) is one machine and is served once.

### Registered services

| Service | Lifetime | Implementation |
|---------|----------|----------------|
| `IMachine` | Singleton | Each discovered machine type |
| `StateMachineOptions` | Singleton | The options `configure` set |
| `ISnapshotStore` | Scoped | `EfSnapshotStore` over the provider's `IDataContext` and `ISqlDialect` |
| `IEffectClaimStore` | Scoped | `EfEffectClaimStore`, the same way |
| `IdempotentEffect` | Scoped | The exactly-once runner over the claim store |
| `ISnapshotMachineRegistry` | Scoped | Builds each machine's draft service and effect runner, and answers `SchemaHash` |
| `IMachineInstances` | Scoped | [System-owned instances](/docs/sdk-reference/statemachine-api/machine-instances) |
| `ISnapshotChangeListener` | Singleton | The [change feed](#change-feed) |
| `ISaveSnapshot`, `IAdvanceSnapshot`, `ILoadSnapshot`, `ISendSnapshot`, `IStartSnapshot`, `IDiscardSnapshot` | Scoped | The six generic mutation trains, as mediator routes |
| Hosted services | | The startup checks (machine ids, IR hashes, rules, start declarations, invoked trains, change feed), the outcome reconciler, the draft sweep and the change listener |

The six trains behind the generic mutations are registered as mediator routes whether or not they are served, so
host code and a machine's own surface dispatch them; `ExposeGenericStateMachineMutations` decides only whether a
client reaches them through GraphQL. `startSnapshot` (`IStartSnapshot`, `StartSnapshotInput` to
`StartSnapshotOutput`) creates an instance through `ISnapshotDraftService.Start`, returning the id the server minted,
`created` and the revision, and `discardSnapshot` (`IDiscardSnapshot`, `DiscardSnapshotInput` to
`DiscardSnapshotOutput`) deletes one through `ISnapshotDraftService.Discard`, answering `discarded: true` for an
instance that is already gone or is another user's. Both take a required `requestId` and refuse with a `problem` in
the output, never a GraphQL error. `saveSnapshot`, `advanceSnapshot`, `loadSnapshot` and `sendSnapshot` answer
`SaveSnapshotOutput`, `AdvanceSnapshotOutput`, `LoadSnapshotOutput` and `SendSnapshotOutput`: the snapshot, its
`Revision`, `RowId` and `DueAt`, or a `Problem` with its code. `StateMachineMutations.Assembly` is the assembly
the trains are declared in.

The host still registers two things a machine cannot know:

| Registration | What it is |
|--------------|-----|
| `ISnapshotPrincipal` | Maps the current caller to the user key that scopes drafts. A host binds it over its own auth, for example over Trax's `TraxCaller`. |
| Each effect implementation | Every effect a machine references with `RunsOnce<TEffect>` is resolved from the container when its transition is sent. |

The stores use the `IDataContext` and `ISqlDialect` that `UsePostgres`, `UseSqlite` or `UseInMemory` registers.
`UseInMemory` registers no dialect, so there a concurrent create of one draft throws instead of losing the race. The
in-memory provider has no set-based update and no triggers, so there each conditional update is a read and a save
guarded by the concurrency token, and the store bumps the revision itself.

### Serving a machine

`[TraxMachine]` (from `Trax.Effect.Attributes`) on a machine's class serves it through its own typed operations
under the [machines root](/docs/sdk-reference/graphql-api/machines): `start`, one mutation per event, `discard`,
`get` and its push. It is to a machine what `[TraxQuery]` and `[TraxMutation]` are to a train, and as with
trains it is the only switch: there is no option that serves an unmarked machine, or every machine. The attribute
is read from every machine the host registers, however it was registered: found by the `AddStateMachines` scan, or
registered as `IMachine` by the host itself. It is not inherited: a subclass of a marked machine is served only if
it is marked itself. To serve a machine you do not own, derive a class from it, mark that, and register only the
subclass: the base has the same id, so registering it too (by scanning its assembly, or through a registration of
the library's) refuses the host's start, and a sealed machine cannot be served this way. A closed generic machine
the host registers by its closed type is served by the attribute on its generic definition, as long as the host does
not scan the assembly that declares it, where the attribute on an open generic class refuses startup. A host that serves a machine must call `UsePersistedOperations` in `AddTraxGraphQL`, or it
refuses to start. `AddTraxGraphQL` decides whether the schema gets the machines root from the marked machines
registered before it by their type or as an instance; a marked machine it could not see (registered later, or
through a factory) is served when another marked machine gave the schema its root, and refuses the host's start
otherwise.

A machine a client can reach, one marked `[TraxMachine]` or, while the generic mutations are opted in,
every user-owned machine, must declare who may do everything a client can do to it. The host refuses to start,
listing every problem in one exception, when such a machine:

- leaves a client or effect-bound event, its `start`, `get`, push or `discard` without a rule;
- declares `Anonymous()` on an event or its start (a user's instance needs an owner);
- declares a `get`, push or `discard` rule looser than owner-only (`AnyAuthenticated()` or
  `Anonymous()`);
- declares a push rule that admits a caller its `get` rule refuses (`Get(r => r.OwnerOnly().Requires("Viewer"))`
  beside `Watch(r => r.OwnerOnly())`): the push carries the whole instance, so `Watch` needs every requirement `Get` has;
- names a policy the host does not register, checked through `ICallerAuthorization.IsPolicyRegistered`;
- declares a start whose reduction copies its input into a field an invoked train's outcome, the effect or an event
  gated by `Requires` or `RequiresPolicy` writes.

A marked machine is also refused when it is built from delegates (it has no IR), is system-owned, or uses a
delegate guard or reducer beside declarative ones, and the refusal says to declare it or remove `[TraxMachine]`
from it. A host refuses to start when it registers an unmarked concrete subclass of a marked machine: mark the
subclass. A machine nothing serves, with the opt-in off, needs no rules. Independently of being served, a machine refuses to build when an event open to any caller (no rule,
`AnyAuthenticated()` or `Anonymous()`) copies from its input a field an event gated by `Requires` or
`RequiresPolicy` writes; a custom reducer counts as copying every field.

### Census and instruments

`MachineCensus.Describe(machines)` lists every trigger and read each machine declares as `MachineCensusEntry`
records (`Machine`, `Name`, `Kind`, `Rule`, `Surfaces`), so a host can review what clients and its own code may do.
A client or effect-bound event names its rule (`MachineCensus.Undeclared` when it has none) and the surfaces that
send it; on a system-owned machine that is host code through `IMachineInstances`, which no rule is checked against.
An outcome or timer trigger is listed as the server's own; a timer with the union of the rules of everything that enters its state, which it acts for. `start`, `get` and the push are listed with
the kind `Read`, and `discard` with the kind `Discard` and the surfaces that reach it: the typed `discard`, the generic
`discardSnapshot`, and host code acting for a client through `ISnapshotDraftService.Discard`.

`StateMachineInstruments` names the meter (`MeterName`, `Trax.StateMachine`) and the instruments the subsystem
reports through: `ClientDivergence` (`trax.machine.client_divergence`), counted once for each advance refused as
`CLIENT_DIVERGENCE` and tagged with the machine, `TimerFireFailed` (`trax.machine.timer.fire_failed`),
`TimerFireErrors` (`trax.machine.timer.fire_errors`), counted once for each attempt to fire a due timer that threw,
`SnapshotNoticeMalformed` (`trax.machine.snapshot_notice.malformed`), counted once for each change notice whose
payload is not a row id, `EffectsAbandoned` (`trax.machine.effect.abandoned`), counted on each sweep for each effect
found pending past its claimant's lease, tagged with the machine, and `NotificationQueueUsage` (`trax.machine.snapshot_notice.queue_usage`), a gauge of how
full the Postgres notification queue is, from 0 to 1, as the change listener last read
`pg_notification_queue_usage()`, which it reads every 30 seconds while it listens, busy or idle. Postgres stops
delivering notices when the queue is full, so alert well before 1.
`TimerFireFailed` is counted once for each due timer that could not be fired (its target refused the context, the
run it would queue could not be, or its fire threw on every attempt), tagged with the machine; its due time is
cleared, so it is not tried again.

### Timers

The outcome reconciler fires due timers too, on every host that registers machines, hosts whose machines have timers
and no invoked trains included. Each sweep reads the rows whose `due_at` has passed on the database's clock, soonest
first, and fires each in a transaction of its own, which first locks the row; on Postgres a row another host's sweep
holds is skipped (`FOR UPDATE SKIP LOCKED`). Each timer is fired by a conditional update on the row's state and
revision, so a client event, an outcome or another host that wrote the row first wins and the fire matches nothing. A
timer leaving an invoking state clears the run's token and cancels the run in the same transaction.

A row with an effect pending on it is passed over: its timer waits until the effect is recorded (and the state, with
its timer, is left) or fails. Before it fires timers, each sweep finishes the effects whose claimant's lease has passed
on the database's clock, through the machine's effect runner on a host that registers the effect, so a claimant that
stopped mid-effect holds the timer for its lease and one sweep at most. The mark is set by a conditional write on the
row's revision, and the fire's update matches only a row with none, so a timer and an effect never both apply. A claim
from the earlier effect-claim ledger holds the timer the same way while it is in flight within its lease, or completed
for the content the instance holds, until the next send records or drops it.

A row whose state no longer has a timer (a deploy removed it) has
its due time cleared, never fired; a row this host cannot read (a newer definition wrote it) is left to a host that
can. A fire that throws, such as a run its timer queues that cannot be stored, rolls back that row alone and is
counted on `TimerFireErrors`; it is tried again after a back-off that doubles from 30 seconds, and after five attempts
its due time is cleared and it is counted on `TimerFireFailed`. A fire that throws because the database failed (a
timeout, or a provider error whose SQLSTATE class is 08, 40, 53 or 57, or that has none, anywhere in the exception's
chain) is backed off and counted the same way but is not an attempt, so a failing database never clears a timer; one
about the row's data, such as a constraint violation, is an attempt. The attempts and back-offs are kept in memory on each host: each
host counts up to five of its own, and a restart starts the count over. Between
sweeps the reconciler wakes for the next due timer rather than waiting out `InvokeOutcomeSweepInterval`, counting a
row it is backing off from when its back-off ends.

### Change feed

Every committed write to `snapshot_draft` is announced, so a host can push an instance to the clients watching it.
On Postgres a trigger (migration `078`) sends `NOTIFY trax_snapshot_changed` with the row's `row_id` and nothing
else, whichever path wrote the row: Trax's own, an outcome applied on another host, a sweep, a custom store or raw
SQL. The payload is a hint. The host re-reads the revision, the state and whether the row still exists before it
sends anything on, so a notice sent by hand moves nothing, and a notice whose payload is not a row id is counted on
`SnapshotNoticeMalformed` and dropped.

`AddStateMachines` registers one change listener per host. On Postgres it holds one session, named
`trax_snapshot_listener` in `pg_stat_activity`, whatever the number of subscribers; it never holds a transaction,
reads `pg_notification_queue_usage()` for `NotificationQueueUsage`, and after losing its session it listens again
and has every row its subscribers watch read again, so a write made while it was away still arrives. On SQLite and
InMemory, which have no notifications, it polls revisions every second instead: only the rows the host's
subscribers watch, and every row of the machine an open operator feed watches (of every machine for a feed on all
of them). Nothing is opened or polled until something on the
host watches an instance, and polling stops when the last watch ends. A watched row the poll finds missing is
reported once, whether or not an earlier poll saw it, so a row watched and deleted between two polls ends its watches
with `gone`, also while an operator feed watches every row. A Postgres host whose `ChangeFeed` is `Poll` polls the
same way and opens no listening session. A row is re-read once for all its subscribers and once per
burst of notices (notices that arrive within 10 ms of each other are one burst, waiting at most 100 ms), coalesced
to its latest revision, and only when this host has a subscriber for it. Two readers sit on it: the
[push](/docs/sdk-reference/graphql-api/machines#the-push) subscription, which follows one
instance for its owner, and `IOperationsService.WatchMachineInstancesAsync`, the operator feed the dashboard and
`onMachineInstanceChanged` read.

### Tables

`snapshot_draft` and `effect_claim` (both in the `trax` schema) ship as migrations in the core data
providers and apply automatically when you register one. `UsePostgres(...)` runs
`040_state_machine_snapshots.sql`, `048_snapshot_draft_request_scope.sql`,
`051_snapshot_draft_machine_key.sql` and `053_effect_claim_content_fingerprint.sql`; `UseSqlite(...)` runs `006`,
`013`, `015` and `018` of the same names. A host
that calls `AddStateMachines` gets the tables for free; one that does not just carries two empty tables. You do
not create or migrate them yourself, and there is no `EnsureCreated` step. Their models are
`Trax.Effect.Models.SnapshotDraft.SnapshotDraft` and `Trax.Effect.Models.EffectClaim.EffectClaim`, mapped on the
core data context like every other Trax table, so on SQLite the data context strips the `trax` schema, maps the
`jsonb` context column to `TEXT`, and stores each timestamp as fixed-width UTC text that sorts in time order.

A draft is keyed by its user, its machine and its id, so two machines can each give one user a draft under
the same well-known id without touching each other's.

## Examples

```csharp compile
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();

public sealed class StripeCharge(IPaymentGateway gateway) : ICharge
{
    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        gateway.Charge(snapshot.Context, $"checkout:{snapshot.State}", cancellationToken);
}
```

With options:

```csharp compile
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(
            o =>
            {
                o.DraftTtl = TimeSpan.FromDays(7);
                o.MaxDraftsPerUser = 200;
            },
            typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));
```

## See also

- [State machines](/docs/statemachine), the concepts.
- [Authoring a machine](/docs/statemachine/authoring) and [Invoking a train](/docs/statemachine/invoking-trains).
- [Machine](/docs/sdk-reference/statemachine-api/machine), the builder a machine declares itself with.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports), what the draft service checks on each path.
- [The machines root](/docs/sdk-reference/graphql-api/machines), the typed operations a marked machine is served through.
- [Generic state-machine mutations](/docs/migration-guides/generic-state-machine-mutations), for a host upgrading from the generic mutations served by default.
