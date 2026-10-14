---
title: Trax.Effect.StateMachine.Testing
description: "Reference for Trax.Effect.StateMachine.Testing: MachineConformance, which checks a machine against its committed IR and corpus, and DifferentialCorpus replay."
type: reference
area: testing
package: Trax.Effect.StateMachine.Testing
namespace: Trax.Effect.StateMachine.Testing
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Testing/MachineConformance.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Testing/DifferentialCorpus.cs
parent: Testing
grand_parent: SDK Reference
nav_order: 5
---

# Trax.Effect.StateMachine.Testing

Checks that keep a state machine, its committed artifacts and the TypeScript twin generated from them from drifting apart. Each returns a list of human-readable problems, empty when everything agrees, for any test framework to assert on.

## Signatures

```csharp fragment
namespace Trax.Effect.StateMachine.Testing;

public static class MachineConformance
{
    public static IReadOnlyList<string> Check(IMachine machine, string irPath, string corpusPath);
    public static IReadOnlyList<string> CheckIr(IMachine machine, string irPath);
    public static IReadOnlyList<string> CheckCorpus(IMachine machine, string corpusPath);
    public static void WriteIr(IMachine machine, string irPath);
}

public static class DifferentialCorpus
{
    public static IReadOnlyList<string> Replay<TState, TTrigger>(SnapshotMachine<TState, TTrigger> machine, string goldenJson)
        where TState : struct, Enum
        where TTrigger : struct, Enum;
}
```

## Members

| Member | Description |
|--------|-------------|
| `CheckIr(machine, irPath)` | The committed `*.ir.json` at `irPath` equals `machine.ExportIr()`, trailing newlines aside. A missing file is reported. |
| `CheckCorpus(machine, corpusPath)` | The committed `differential.json` at `corpusPath` equals the corpus embedded in the machine's assembly (`machine.Corpus`). A machine with no embedded corpus passes when nothing is committed, and is reported when something is. |
| `Check(machine, irPath, corpusPath)` | `CheckIr`, `CheckCorpus`, and `machine.SelfCheck()`: the embedded corpus replays clean through this build's engine. Each self-check diff is reported as `self-check: the embedded corpus no longer replays: <diff>`. |
| `WriteIr(machine, irPath)` | Writes `machine.ExportIr()` to `irPath`, with a trailing newline |
| `DifferentialCorpus.Replay(machine, goldenJson)` | Replays a corpus produced by the `@trax/state-machine` oracle through the C# engine and returns one diff per case it does not reproduce. Only reason codes and canonical wire are compared, not rejection detail. |

`IMachine` is in `Trax.Effect.StateMachine.Persistence`; see [State Machine API](/docs/sdk-reference/statemachine-api).

## Example

```csharp compile
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Testing;

public static class ConformanceCheck
{
    public static void Run(IMachine machine, string irPath, string corpusPath)
    {
        var problems = MachineConformance.Check(machine, irPath, corpusPath);

        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join("\n", problems));
    }
}
```

## See also

- [State Machine API](/docs/sdk-reference/statemachine-api)
- [Testing](/docs/sdk-reference/testing)
