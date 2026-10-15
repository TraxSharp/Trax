---
title: Invoke a train from a state
description: "Make a state machine state run a train with Invokes: route its outcomes, meet the host's requirements, choose an owner, and retry by entering the state again."
type: how-to
area: machines
nav_order: 3
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Invokes.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokedTrainLauncher.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokesStartupValidator.cs
---

# Invoke a train from a state

A state can run a train. Entering the state queues one run, and the run's outcome comes back as a trigger only that
entry of the state can apply. The state is a durable checkpoint between stages of long-running work (fetch,
normalise, resolve, embed), so a stage that fails is retried by entering its state again, not by rerunning
everything before it. The full behaviour, every outcome and every refusal, is in
[Invoked runs: outcomes and refusals](/docs/statemachine/invoked-runs).

## 1. Write the train to be invoked

The train is a `ServiceTrain` behind an interface, and the interface is the name the machine invokes it by.

- Make every junction an `EffectJunction`, inside a `Parallel` branch or a routing step's tracks too. Only an
  `EffectJunction` reads the run's cancel flag, so leaving the state can stop the run on another host.
- Make every junction safe to repeat. Queueing a run is exactly once, but a run can execute more than once. An
  irreversible step inside the train takes its own claim; nothing checks this for you.
- Give it an output record with properties, not a tuple. Its fields are what the outcome's guards and reductions
  read.
- Keep `[TraxSensitive]` members out of the output, and do not mark the train `[TraxBroadcast]` on a user-owned
  machine: the output is reduced into a context stored as plain JSON.

The host refuses to start, naming the machine, the state and the train, junction or member at fault, when the train
breaks one of these; the list is under [Host refusals](/docs/statemachine/invoked-runs#host-refusals).

## 2. Declare the invoking state

`Invokes<TTrain, TInput, TOutput>` names the train by its interface, and the lambda builds the run's input from the
context the state was entered with. Every invoking state says where each outcome goes:

```csharp compile=ingest
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

public enum Stage
{
    Idle,
    Fetching,
    NeedsReview,
    Fetched,
    FetchFailed,
    Cancelled,
}

public enum IngestTrigger
{
    Fetch,
    Abandon,
    Retry,
}

public sealed record SourceInput
{
    public string Source { get; init; } = "";
}

public sealed record SourceContext
{
    public string Source { get; init; } = "";
}

public sealed record FetchedContext
{
    public string Source { get; init; } = "";

    public string Fingerprint { get; init; } = "";
}

public sealed class IngestMachine : Machine<Stage, IngestTrigger>
{
    protected override void Configure(IMachineBuilder<Stage, IngestTrigger> m)
    {
        m.Id("ingest").Version(1).StartsAt(Stage.Idle, () => new JsonObject());

        m.In(Stage.Idle)
            .Context()
            .On(IngestTrigger.Fetch)
            .WithInput<SourceInput>()
            .Reduce(Set((SourceContext c) => c.Source).FromInput((SourceInput i) => i.Source))
            .To(Stage.Fetching);

        m.In(Stage.Fetching)
            .Context<SourceContext>()
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(ctx =>
                new FetchInput(ctx["source"]!.GetValue<string>()))
            .OnDone(Stage.NeedsReview,
                when: Input((FetchOutput o) => o.Unsure).IsTrue(),
                reduce: Set((FetchedContext c) => c.Fingerprint).FromInput((FetchOutput o) => o.Fingerprint))
            .OnDone(Stage.Fetched,
                reduce: Set((FetchedContext c) => c.Fingerprint).FromInput((FetchOutput o) => o.Fingerprint))
            .OnFailed(Stage.FetchFailed)
            .OnCancelled(Stage.Cancelled)
            .On(IngestTrigger.Abandon)
            .Reduce(Clear())
            .To(Stage.Idle);

        m.In(Stage.NeedsReview).Context<FetchedContext>();
        m.In(Stage.Fetched).Context<FetchedContext>();
        m.In(Stage.FetchFailed)
            .Context<SourceContext>()
            .On(IngestTrigger.Retry)
            .To(Stage.Fetching);
        m.In(Stage.Cancelled).Context<SourceContext>();
    }
}
```

- `OnDone`, one or more, tried in the order declared: the first whose guard holds for the output is taken, so an
  unguarded `OnDone` after the guarded ones is the fallback. The guard reads the output as the outcome's input, and
  the reduction copies what the next state needs into the context. A train that can be unsure says so in its output,
  and a guarded `OnDone` routes it, as `NeedsReview` does here.
- `OnFailed`, exactly once. A run the scheduler reaps arrives here too.
- `OnCancelled`, exactly once, and required.

`Build` refuses an invoking state that leaves one out, and an outcome that goes to the state the machine starts at or
to its effect's target. The builder's rules are listed under
[IInvokeBuilder](/docs/sdk-reference/statemachine-api/machine#iinvokebuilder).

## 3. Meet the host's requirements

The host that registers the machine needs Postgres or SQLite (InMemory has no transactions to queue the run in),
`AddJunctionProgress()` (it registers the junction effect that reads the cancel flag) and the mediator:

```csharp compile=ingest
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJunctionProgress())
        .AddStateMachines(typeof(IngestMachine).Assembly)
        .AddMediator(typeof(IngestMachine).Assembly));
```

Leave the mediator's own `ITrainExecutionService` in place, and give the invoked train no `OnQueue` hook or
`DeferQueuePromotion`: the run is queued in the transaction that enters the state, and each of those commits on its
own.

A scheduler dispatches the run. Entering the state only queues the run in `trax.work_queue`, through the mediator,
which enqueues and does nothing more. Only `Trax.Scheduler`'s job dispatcher (registered by `AddScheduler()` on a host
with a database provider) takes queued work and runs it, so there must be
a scheduler on this host or another host sharing the database. The host does not check for one at startup, since
the scheduler may live on a dedicated host; without one, the run stays queued and no outcome arrives. The instance
stays in the invoking state until something else moves it: an event the state declares, or a timer. An `After(...)`
on the invoking state fires with or without a scheduler, because timers are fired by the outcome reconciler that
every host registering machines runs, and leaving the state cancels the queued run.

## Who a run belongs to

Decide whether the machine is user-owned (the default) or declares `SystemOwned()`, because that decides how its runs
are authorized.

- **User-owned.** The train is authorized against the user entering the state, at entry, through the host's train
  authorization; a user the train refuses cannot enter the state (`INVOKE_FORBIDDEN`). When the train's
  `[TraxAuthorize]` names roles or a policy, every event that enters the state (and the start, when the initial state
  invokes) must require at least as much, or the host refuses to start. An outcome may not enter another invoking
  state, and neither may a timer, since no
  user is present to authorize that run: chain stages through an event the user sends (a "continue" or "retry" edge
  into the next stage), as `Retry` does above.
- **System-owned.** Only [`IMachineInstances.Start`](/docs/sdk-reference/statemachine-api/machine-instances)
  creates an instance, and only its runs' outcomes and `IMachineInstances.Advance` move it. Its train is authorized
  inside Trax's trusted execution scope, as a scheduled manifest run is, so the train may not declare
  `[TraxAuthorize]`. Only a system-owned machine chains runs through outcomes.

One user holds at most 10 live invoked runs across every machine; a machine sets its own limit with
`InvokedRunLimit(n)`. Past it, entering the state is refused as `INVOKE_LIMIT_REACHED`. A run counts until it ends,
even when its instance was discarded or swept in the meantime: each run records the user as its starter. System owners
are not capped here: the dispatcher's `MaxActiveJobs` bounds them.

## Retry by entering the state again

The scheduler never retries or dead-letters an invoked run, and an operator cannot requeue one. Declare an edge from
the failure state back into the invoking state (`FetchFailed -Retry-> Fetching` above): entering it queues a new
run, and the old run's late completion is then a `NO_TRANSITION`.

## How an outcome comes back

The host that ran the train delivers the outcome as soon as the run's terminal write commits, and every host that
registers machines runs an outcome reconciler that sweeps for ended runs, which covers a crashed host, a reaped run
and a train run on a host that registers no machines. The outcome is applied once, by a conditional update on the
instance's invoke token, however many hosts deliver it. To change how often the sweep runs (5 seconds by default):

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJunctionProgress())
        .AddStateMachines(o => o.InvokeOutcomeSweepInterval = TimeSpan.FromSeconds(10), assembly)
        .AddMediator(assembly));
```

[Delivery](/docs/statemachine/invoked-runs#delivery) describes both paths, and
[AddStateMachines options](/docs/sdk-reference/statemachine-api/add-state-machines#options) the intervals.

## Reduce pointers, not data

The outcome is reduced into the context, which is stored with the snapshot and returned to the client. Reduce
pointers into it, such as fingerprints and dataset URIs, never rows, so the database writes grow with the number of
steps rather than with the data each step handles. A snapshot past 64 KiB is refused, and the run goes to `OnFailed`.

## Operators

Operators see every instance and the runs it invoked, never its context, and can cancel only a system-owned
instance's run. What each operator surface shows and refuses is under
[Operator actions](/docs/statemachine/invoked-runs#operator-actions).

## SDK Reference

> [Invokes](/docs/sdk-reference/statemachine-api/machine#istatebuilder) | [OnDone / OnFailed / OnCancelled](/docs/sdk-reference/statemachine-api/machine#iinvokebuilder) | [SystemOwned / InvokedRunLimit](/docs/sdk-reference/statemachine-api/machine#imachinebuilder) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances) | [InvokeOutcomeSweepInterval](/docs/sdk-reference/statemachine-api/add-state-machines#options) | [OutcomeSample](/docs/sdk-reference/statemachine-api/machine#idifferentialbuilder)
