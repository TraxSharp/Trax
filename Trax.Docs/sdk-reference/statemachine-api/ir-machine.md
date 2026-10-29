---
title: IrMachine
description: "Reference for IrMachine, the C# engine built from a machine's IR instead of its C# code, as a client holding only the host's manifest builds it."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 18
package: Trax.StateMachine
namespace: Trax.StateMachine
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine/IrMachine.cs
---

# IrMachine

The C# engine built from a machine's [IR](/docs/sdk-reference/statemachine-api/ir-format) instead of from its C#
code. A client that holds the host's [client manifest](/docs/sdk-reference/statemachine-api/client-manifest) but not
the machine's assembly predicts with it, the way every other language's client builds its machine from the IR. It
answers as `SnapshotMachine` does for the machine that exported the IR, and the conformance kit holds the two to the
same codes and wire bytes.

## Signature

```csharp fragment
public sealed class IrMachine
{
    public IrMachine(
        JsonObject ir,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>>? customGuards = null,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, JsonObject>>? customReducers = null);

    public string Id { get; }
    public int Version { get; }
    public string InitialState { get; }
    public IReadOnlySet<string> CommittedStates { get; }
    public IReadOnlySet<string> EffectTriggers { get; }
    public IReadOnlySet<string> TimedStates { get; }

    public Snapshot CreateInitialSnapshot();
    public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input = null);
    public AdvanceResult ApplyOutcome(Snapshot snapshot, string trigger, JsonNode? input = null);
    public AdvanceResult ApplyTimer(Snapshot snapshot, string trigger);
    public RehydrationResult Rehydrate(string json);
    public RehydrationResult Rehydrate(string json, bool fromServer);
    public string Serialize(Snapshot snapshot);
    public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input = null);
    public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot);
    public PlanResult Plan(
        Snapshot snapshot,
        string target,
        PlannedEvent? fields,
        IReadOnlyList<string> navigation,
        int? maxDepth = null);
}

public sealed record PlannedEvent(string Trigger, JsonNode? Input = null);

public abstract record PlanResult
{
    public sealed record Planned(IReadOnlyList<PlannedEvent> Events) : PlanResult;
    public sealed record Unreachable : PlanResult;
    public sealed record Exceeded : PlanResult;
    public sealed record Undetermined(string Trigger, IReadOnlyList<PlannedEvent> Events) : PlanResult;
    public sealed record Invalid(string? Trigger = null, string? Target = null) : PlanResult;
}
```

## Members

| Member | What it is |
| --- | --- |
| `IrMachine(ir, customGuards, customReducers)` | Builds the machine an IR describes. The handlers bind the custom rules and reductions the IR names. An IR this engine cannot read throws `ArgumentException`; the `differential` block is ignored. |
| `Id`, `Version`, `InitialState` | The IR's id, definition version and initial state. |
| `CommittedStates`, `EffectTriggers`, `TimedStates` | The states marked `.Committed()`, which a soft autosave may not move a draft out of; the triggers whose edge runs the effect; and the states with a timer. |
| `CreateInitialSnapshot()` | The snapshot a new instance begins with: the IR's `initialContext`, or one derived from the initial state's schema. |
| `Advance(snapshot, trigger, input)` | A client's trigger, with the refusals `SnapshotMachine.Advance` gives, in its order. |
| `ApplyOutcome(snapshot, trigger, input)`, `ApplyTimer(snapshot, trigger)` | An invoked train's outcome (`Fetching.done`) and a state's timer (`Review.after`). The server applies both; a client uses them to replay one. |
| `Rehydrate(json)`, `Rehydrate(json, fromServer)` | Reads a snapshot. With `fromServer`, a custom state requirement with no handler counts as passed, since the server checked it. |
| `Serialize(snapshot)` | The canonical wire, byte for byte what every engine writes. |
| `CanFire(snapshot, trigger, input)`, `AvailableTriggers(snapshot)` | Whether a trigger fires, and the triggers with an edge out of the state. |
| `Plan(snapshot, target, fields, navigation, maxDepth)` | The events that take the snapshot to `target` with the page's fields applied. See [Planning](#planning). |
| `PlannedEvent` | One event of a plan: its `Trigger`, and its `Input` when it takes one. |
| `PlanResult` | What a plan came to: `Planned` with its `Events` (empty when the snapshot is there already), `Unreachable`, `Exceeded`, `Undetermined` with the `Trigger` it could not evaluate and the `Events` before it, or `Invalid` with the `Trigger` or the `Target` it cannot plan with. |

## Behavior

- Every operation is total: a refusal is an `AdvanceResult.Rejected` or a `RehydrationResult.Error`, never an
  exception.
- A custom guard with no handler does not hold, and a custom reduction with none keeps the context. Neither is the
  server's answer, which has the handlers. The .NET client does not use such a result: an event whose prediction
  depended on an unhandled custom guard, custom state requirement or custom reduction is sent unpredicted, and the view
  shows the server's answer.
- An IR carries no migrations, so `Rehydrate` refuses a snapshot of another version with `VERSION_MISMATCH`.

## Planning

A page that moves on its twin and saves on a debounce runs several events ahead of the host. `Plan` finds the events
that bring the host to where the page is: the state `target`, with `fields` (the event that writes the page's fields,
with its input) applied, moving through the `navigation` triggers. Every engine searches the same way, so they answer
the same plan, and the conformance kit's `plan` cases hold them to it:

- A `target` that is not a state, or a fields or navigation trigger the machine does not have, one bound to the
  effect, an outcome, a timer, or a navigation trigger that is also the fields event, answers `Invalid`.
- The goal is a snapshot in `target` that the fields event leaves as it is. A fields event the target refuses never
  reaches it. Contexts are compared without their null members, at any depth, since a host omits them.
- The search is breadth first, and from each snapshot it tries the fields event, then `navigation` in the order given.
  A refused edge is skipped and a snapshot already seen is not visited again, so the plan never depends on the order
  the IR lists its transitions in.
- A snapshot reached by `maxDepth` events (twice the machine's states by default) is not expanded, and the search
  stops after expanding 10 000. Either answers `Exceeded` when no plan was found.
- An edge that rests on a custom guard, a custom reduction or a custom state requirement with no handler is not tried.
  When nothing else reaches the target, the first one met answers `Undetermined`: send its `Events`, then its
  `Trigger` with no prediction, and plan again from the server's answer.

The [.NET client](/docs/sdk-reference/statemachine-api/dotnet-client)'s `SyncToAsync` does that loop.

## Example

```csharp compile
var manifest = ClientManifest.Read(await File.ReadAllTextAsync("manifest.json"));
var machine = new IrMachine(manifest.Machines.Single(m => m.Id == "checkout").Ir);

var start = machine.CreateInitialSnapshot();
var result = machine.Advance(start, "Next");
// AdvanceResult.Rejected with GUARD_FAILED while the cart is empty

var plan = machine.Plan(start, "Cart", fields: null, navigation: ["Next", "Back"]);
// PlanResult.Planned with no events: the snapshot is in Cart already
```

## See also

- [.NET client](/docs/sdk-reference/statemachine-api/dotnet-client), which predicts with it through `MachinePredictor.FromIr`.
- [IR format](/docs/sdk-reference/statemachine-api/ir-format), what it reads.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
