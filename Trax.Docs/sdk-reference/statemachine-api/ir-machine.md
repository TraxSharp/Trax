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
}
```

## Members

| Member | What it is |
| --- | --- |
| `IrMachine(ir, customGuards, customReducers)` | Builds the machine an IR describes. The handlers bind the custom rules and reductions the IR names. An IR this engine cannot read throws `ArgumentException`; the `differential` block is ignored. |
| `Id`, `Version`, `InitialState` | The IR's id, definition version and initial state. |
| `CommittedStates`, `EffectTriggers`, `TimedStates` | The states only the server's effect or train reaches, the triggers whose edge runs the effect, and the states with a timer. |
| `CreateInitialSnapshot()` | The snapshot a new instance begins with: the IR's `initialContext`, or one derived from the initial state's schema. |
| `Advance(snapshot, trigger, input)` | A client's trigger, with the refusals `SnapshotMachine.Advance` gives, in its order. |
| `ApplyOutcome(snapshot, trigger, input)`, `ApplyTimer(snapshot, trigger)` | An invoked train's outcome (`Fetching.done`) and a state's timer (`Review.after`). The server applies both; a client uses them to replay one. |
| `Rehydrate(json)`, `Rehydrate(json, fromServer)` | Reads a snapshot. With `fromServer`, a custom state requirement with no handler counts as passed, since the server checked it. |
| `Serialize(snapshot)` | The canonical wire, byte for byte what every engine writes. |
| `CanFire(snapshot, trigger, input)`, `AvailableTriggers(snapshot)` | Whether a trigger fires, and the triggers with an edge out of the state. |

## Behavior

- Every operation is total: a refusal is an `AdvanceResult.Rejected` or a `RehydrationResult.Error`, never an
  exception.
- A custom guard with no handler does not hold, and a custom reduction with none keeps the context, so the machine does
  not predict past them. The server, which has the handlers, decides.
- An IR carries no migrations, so `Rehydrate` refuses a snapshot of another version with `VERSION_MISMATCH`.

## Example

```csharp compile
var manifest = ClientManifest.Read(await File.ReadAllTextAsync("manifest.json"));
var machine = new IrMachine(manifest.Machines.Single(m => m.Id == "checkout").Ir);

var start = machine.CreateInitialSnapshot();
var result = machine.Advance(start, "Next");
// AdvanceResult.Rejected with GUARD_FAILED while the cart is empty
```

## See also

- [.NET client](/docs/sdk-reference/statemachine-api/dotnet-client), which predicts with it through `MachinePredictor.FromIr`.
- [IR format](/docs/sdk-reference/statemachine-api/ir-format), what it reads.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
