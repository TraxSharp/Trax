---
name: trax-design-a-machine
description: Use before writing or changing a Trax state machine (a Machine subclass, Trax.StateMachine, Trax.StateMachine.Persistence), or when a process spans several requests or users, waits on people, or walks a user through steps such as a checkout, a wizard, an approval or a game turn. Covers train or machine, the builder and host rules that decide the machine's layout, invoking trains from a state, Rule and Reduction (SetAll, new Rule.Custom), spawning child machines, undoing an effect and where machine files go.
metadata:
  internal: true
---

# Design a Trax machine

A machine is a thing that waits: it sits in a named state until someone sends it an event, and the server stores it
after every move. A train is one operation that runs to its end. Most of what goes wrong with a machine is a layout
the builder refuses, found after the states were drawn. Read the rules below before drawing any state.

## First: train or machine

- **Nobody waits in the middle:** write a train (`trax-write-a-train`). A long train is not a reason for a machine;
  [checkpoints](https://traxsharp.net/docs/core/checkpoints-and-branches) resume it.
- **A person or another system acts several times, some actions only in some states:** a machine.
- **Both, usually:** the machine decides when the work happens, a train does it. A state invokes the train.

[Train or machine](https://traxsharp.net/docs/statemachine/train-or-machine) has the full comparison.

## Decide before writing states

1. **Who owns an instance.** User-owned (the default): a user starts it and sends its events. `SystemOwned()`: only
   host code (`IMachineInstances.Start`) creates one and only outcomes and `IMachineInstances.Advance` move it; it
   cannot be served to clients.
2. **Served or host-only.** `[TraxMachine]` serves it under the `machines` GraphQL root. A served machine is all
   data (no delegate guards or reducers), declares a rule on every event and on `Start`, and declares `Get`, `Watch`
   and `Discard` owner-only or stricter. The host refuses to start until it does
   ([Author and serve a machine](https://traxsharp.net/docs/statemachine/authoring)).
3. **Does a state run work.** A state that invokes a train needs Postgres or SQLite (not InMemory),
   `AddJunctionProgress()`, `AddMediator(...)`, and a scheduler on this host or one sharing the database. Without a
   scheduler the run stays queued and no outcome arrives; the host does not check. The `trax-hub` template has all
   of it; `trax-api` has none.
4. **One irreversible effect, or trains.** `RunsOnce<TEffect>()` binds the machine's one exactly-once effect (a
   charge) to one edge. Work with several stages is invoked trains.

## The rules that shape the machine

`Build()` refuses a machine that breaks any of these the first time anything reads it: the host's startup checks, or
`new RunMachine().ExportIr()` in a test. The ones marked *startup* are checked only by a host that registers the
machine, so a test that only builds the machine misses them; start the host in a test too.

| Rule | Checked |
|---|---|
| An ordinary event may not enter a state an outcome enters. A self-loop is fine, and so is an edge into an outcome target that itself invokes a train. | `Build` |
| An outcome may not go to the start state, or to the `RunsOnce` effect's target. | `Build` |
| An event any caller may send (no rule, `AnyAuthenticated()`, `Anonymous()`) may not copy from its input a field the server writes: an outcome's fields, the effect's receipt, a child's id, a field a role-gated event writes. A custom reducer counts as copying every field. | `Build` |
| Every invoking state has at least one `OnDone`, exactly one `OnFailed` and exactly one `OnCancelled`. One train or child per state. The train is named by its interface; its output is a record with properties, not a tuple. | `Build` |
| An event has the same rule on every edge. A role-gated event may not enter the start state from another state. | `Build` |
| At most one `RunsOnce`; only its own edge enters its target; its trigger is sent on that edge only; it neither runs from nor lands in an invoking state. | `Build` |
| On a user-owned machine, no outcome and no timer enters a state that invokes a train or spawns a child. | *startup* |
| On a user-owned machine, every event entering an invoking state requires at least the roles and policies of the train's `[TraxAuthorize]`. | *startup* |

Together they force one layout on a user-owned machine that invokes trains: **each user action is its own event,
from a resting state into a working state that invokes the train for it, and the outcome brings the instance back to
a resting state.** The start state is a plain state the user leaves with an event. A failure goes to a state with a
`Retry` event back into the working state, because the scheduler never retries an invoked run.

```csharp compile
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

public enum RunState { New, Loading, Turn, Resolving, Broken, Over }

public enum RunTrigger { Begin, Play, Retry }

public sealed record RunContext
{
    public string RunId { get; init; } = "";
    public int? Version { get; init; }
    public int? Slot { get; init; }
    public int? Target { get; init; }
}

public sealed record EmptyStart;

public sealed record BeginInput
{
    public string RunId { get; init; } = "";
}

public sealed record PlayInput
{
    public int Slot { get; init; }
    public int Target { get; init; }
}

[TraxMachine]
public sealed class RunMachine : Machine<RunState, RunTrigger>
{
    protected override void Configure(IMachineBuilder<RunState, RunTrigger> m)
    {
        m.Id("run")
            .Version(1)
            .StartsAt(RunState.New, () => new JsonObject())
            .Start<EmptyStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        // The start state is plain: the user leaves it with an event into a working state.
        m.In(RunState.New)
            .Context()
            .On(RunTrigger.Begin)
            .AnyAuthenticated()
            .WithInput<BeginInput>()
            .Reduce(Set((RunContext c) => c.RunId).FromInput((BeginInput i) => i.RunId))
            .To(RunState.Loading);

        m.In(RunState.Loading)
            .Context<RunContext>()
            .Invokes<ILoadRunTrain, LoadRunInput, LoadRunOutput>(ctx =>
                new LoadRunInput(ctx["runId"]!.GetValue<string>()))
            .OnDone(RunState.Turn,
                reduce: Set((RunContext c) => c.Version).FromInput((LoadRunOutput o) => o.Version))
            .OnFailed(RunState.Broken)
            .OnCancelled(RunState.Broken);

        // A resting state. The user's action is its own event into the state that runs it.
        m.In(RunState.Turn)
            .Context<RunContext>()
            .On(RunTrigger.Play)
            .AnyAuthenticated()
            .WithInput<PlayInput>()
            .Reduce(SetAll(
                Set((RunContext c) => c.Slot).FromInput((PlayInput i) => i.Slot),
                Set((RunContext c) => c.Target).FromInput((PlayInput i) => i.Target)))
            .To(RunState.Resolving);

        m.In(RunState.Resolving)
            .Context<RunContext>()
            .Invokes<IPlayCardTrain, PlayCardInput, PlayCardOutput>(ctx =>
                new PlayCardInput(
                    ctx["runId"]!.GetValue<string>(),
                    ctx["version"]!.GetValue<int>(),
                    ctx["slot"]!.GetValue<int>(),
                    ctx["target"]!.GetValue<int>()))
            .OnDone(RunState.Over,
                when: Input((PlayCardOutput o) => o.Finished).IsTrue(),
                reduce: Set((RunContext c) => c.Version).FromInput((PlayCardOutput o) => o.Version))
            .OnDone(RunState.Turn,
                reduce: Set((RunContext c) => c.Version).FromInput((PlayCardOutput o) => o.Version))
            .OnFailed(RunState.Broken)
            .OnCancelled(RunState.Broken);

        // The scheduler never retries an invoked run: the user does, by entering the state again.
        m.In(RunState.Broken).Context<RunContext>().On(RunTrigger.Retry).AnyAuthenticated().To(RunState.Loading);
        m.In(RunState.Over).Context<RunContext>();
    }
}
```

`Play` copies `slot` and `target`, which no outcome writes; the outcomes write `version`, which no open event
copies. A system-owned machine may chain outcomes straight into the next invoking state instead.

## Known mistakes

```csharp fragment
// Wrong: Rule.Custom is a record, not a factory method. Same for Reduction.Custom.
.When(Rule.Custom("can-afford"))
// Right: construct it, and bind the C# handler on the machine builder.
.When(new Rule.Custom("can-afford"))
m.CustomGuard("can-afford", (context, input) => /* ... */ true);
```

```csharp fragment
// Wrong: a custom reducer to copy several input fields. On an open event it counts as copying every
// field, so Build refuses it as soon as an outcome writes any field.
.Reduce(new Reduction.Custom("stash-play"))
// Right: SetAll combines Set reductions, each field once. There is no other combinator for reductions.
.Reduce(SetAll(Set((RunContext c) => c.Slot).FromInput((PlayInput i) => i.Slot),
               Set((RunContext c) => c.Target).FromInput((PlayInput i) => i.Target)))
```

```csharp fragment
// Wrong: Turn is an outcome target, so no event from another state may enter it (Build refuses);
// and on a user-owned machine an outcome may not enter Resolving, which invokes a train (startup refuses).
m.In(RunState.Broken).On(RunTrigger.Retry).To(RunState.Turn);
.OnDone(RunState.Resolving)
// Right: Retry enters Loading, which invokes the train that brings the instance back to Turn;
// Play goes from Turn into Resolving, and Resolving's outcome comes back to Turn.
```

- **A custom rule in the browser.** The generated twin has no handler for a custom rule: it counts it as passing on
  a snapshot the server sent and predicts nothing that needs it. The server's answer decides. Use the vocabulary
  where it fits; it has no arithmetic and cannot compare two fields, which is what custom rules are for.
- **A delegate on a served machine.** `When(Func...)`, `Reduce(Func...)` and `Holds` run but cannot be exported, so
  the machine has no IR, no twin and no typed API. Use them only on a host-only machine with no twin
  ([Delegates and declarative rules](https://traxsharp.net/docs/statemachine/delegates-and-declarative-rules)).
- **A refusal is not an exception.** A guard that declines answers a typed `problem` (`GUARD_FAILED` with the
  `Because` text); nothing is thrown and nothing is written.

## Invoking a train

The train is a `ServiceTrain` behind an interface. Every junction is an `EffectJunction` (it reads the cancel flag
when the instance leaves the state) and is safe to run twice: the run is queued once but can execute more than once.
The output is a record, never holds a `[TraxSensitive]` member, and the train is not `[TraxBroadcast]` on a
user-owned machine. The host refuses each of these at startup.

The run has no caller: `TraxPrincipal` cannot be injected. A junction that acts on the user's data injects
`InvokedRun` and checks the data against its `UserKey`, the owner of the instance that queued the run. Reduce
pointers (ids, versions, fingerprints) into the context, not rows: a snapshot past 64 KiB is refused and the run goes
to `OnFailed`. [Invoke a train from a state](https://traxsharp.net/docs/statemachine/invoking-trains) and
[Invoked runs](https://traxsharp.net/docs/statemachine/invoked-runs#host-refusals) list every refusal.

## Rules, reductions and context

- Guards: `When(Rule)` from `Input(...)` (the event's input) and `Field(...)` (the context), with `All` and `Any`.
  Reductions: `Keep()`, `Clear()`, `Reset()`, `Set(...).FromInput(...)` or `.ToValue(...)`, and `SetAll(...)`.
  Fields are member expressions, never strings
  ([Rules](https://traxsharp.net/docs/sdk-reference/statemachine-api/rules)).
- Each state's context and each event's input is a record (`Context<T>()`, `WithInput<T>()`); the JSON names are
  camelCase. An event's input is checked as strictly as its type, a nested record included, so several values for a
  train go in one input record. A context is described one level deep, so the twin types a nested record or a list
  of records loosely. `object`, `JsonNode` and `[Flags]` enums are refused
  ([Context and input schemas](https://traxsharp.net/docs/statemachine/context-schemas)).

## Children and undoing

- **A sub-flow with its own events:** `Spawns<TChild, TContext, TOutput>(c => c.ChildId)` on the parent's state,
  with the same `OnDone`/`OnFailed`/`OnCancelled`. The child marks its end states `Ends()` and `Fails()`, its start
  state waits for an event, and it is owned the way its parent is
  ([Spawn a child machine](https://traxsharp.net/docs/statemachine/spawning-child-machines)).
- **Undo an effect that ran** (a refund): a second machine whose one effect is the undo, started from the receipt
  the first recorded. Before the effect runs, discard or reset instead
  ([Undo an effect](https://traxsharp.net/docs/statemachine/undo-an-effect)).

## Where the files go

`Machines/<Name>/` beside the trains: the machine, its state and trigger enums and its context at the top, every
input and context part in `Models/`. `trax machine generate --assembly <dll> --ir-out machines/<id> --corpus-out
machines/<id>` writes the IR and the differential corpus under `machines/<id>/` at the solution root; commit them,
embed the corpus, and run `trax machine check` with the same arguments in CI. The frontend's twin comes from
`trax client` (`trax-generate-a-client`).
[Organize machines in a project](https://traxsharp.net/docs/statemachine/organize-machines) has the tree.

## Next

- Serve it and secure it: `trax-expose-over-api`. Call it from a frontend: `trax-generate-a-client`. Test it:
  `trax-test-your-app`.
- Every builder method: [Machine](https://traxsharp.net/docs/sdk-reference/statemachine-api/machine). Every result
  code: [Result codes](https://traxsharp.net/docs/sdk-reference/statemachine-api/result-codes).
