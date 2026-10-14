---
title: MachineConformance
description: "Reference for MachineConformance and DifferentialCorpus, the test helpers that check a machine against its committed IR and its differential corpus."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 15
package: Trax.Effect.StateMachine.Testing
namespace: Trax.Effect.StateMachine.Testing
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Testing/MachineConformance.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Testing/DifferentialCorpus.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/CorpusReplay.cs
---

# MachineConformance

The checks that keep a machine, its committed artifacts and its generated TypeScript twin in step, as a test runs
them. Each returns a list of human-readable problems, empty when everything agrees, and leaves the assertion to the
test framework.

## Signature

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
    public static IReadOnlyList<string> Replay<TState, TTrigger>(
        SnapshotMachine<TState, TTrigger> machine,
        string goldenJson)
        where TState : struct, Enum
        where TTrigger : struct, Enum;
}
```

## Members

| Member | Description |
|--------|-------------|
| `Check(machine, irPath, corpusPath)` | Runs `CheckIr`, `CheckCorpus` and the machine's `SelfCheck()`, and returns every problem. |
| `CheckIr(machine, irPath)` | The committed IR at `irPath` still equals `machine.ExportIr()`. A missing file is a problem, not an exception. |
| `CheckCorpus(machine, corpusPath)` | The committed corpus at `corpusPath` still equals `machine.Corpus`, the copy embedded in the build. A machine with no embedded corpus passes when nothing is committed, and fails when a corpus is. |
| `WriteIr(machine, irPath)` | Writes `machine.ExportIr()` to `irPath`, with a trailing newline. |
| `DifferentialCorpus.Replay(machine, goldenJson)` | Replays a differential corpus through the engine; one diff per case the engine does not reproduce. |

## Behavior

- The comparisons ignore a trailing newline: committed files carry one, the in-memory forms do not.
- `Replay` compares reason codes and canonical wire, never rejection detail. `CorpusReplay.Replay` in
  `Trax.Effect.StateMachine` is the same replay, shipped in the engine so a host can run it at startup;
  `IMachine.SelfCheck()` calls it.

## Example

```csharp compile
using Trax.Effect.StateMachine.Testing;

var problems = MachineConformance.Check(
    new CheckoutMachine(),
    irPath: "machines/checkout/checkout.ir.json",
    corpusPath: "machines/checkout/differential.json");

if (problems.Count > 0)
    throw new InvalidOperationException(string.Join("\n", problems));
```

## See also

- [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity), the same corpus replayed in production.
- [The codegen pipeline](/docs/statemachine/codegen-pipeline), what the IR and the corpus are generated for.
