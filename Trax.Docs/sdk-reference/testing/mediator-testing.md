---
title: Trax.Mediator.Testing
description: "Reference for Trax.Mediator.Testing: the train guard that requires every train to have its own train interface, as a checker and as an NUnit fixture."
type: reference
area: testing
package: Trax.Mediator.Testing
namespace: Trax.Mediator.Testing
source:
  - Trax.Mediator/src/Trax.Mediator.Testing/TrainGuards.cs
  - Trax.Mediator/src/Trax.Mediator.Testing/TrainGuardFixture.cs
parent: Testing
grand_parent: SDK Reference
nav_order: 3
---

# Trax.Mediator.Testing

The train guard package: one check, that every train has its own train interface, as a checker returning a [GuardResult](/docs/sdk-reference/testing/core-testing#guardresult) and as an NUnit fixture. It depends on Trax.Core.Testing.

## Signatures

```csharp fragment
namespace Trax.Mediator.Testing;

public static class TrainGuards
{
    public static GuardResult EveryTrainHasInterface(IEnumerable<Assembly> assemblies);
}

[TestFixture]
public abstract class TrainGuardFixture
{
    protected abstract IReadOnlyList<Assembly> TrainAssemblies { get; }

    [Test] public void Every_train_has_a_companion_interface();
}
```

## Members

| Member | Description |
|--------|-------------|
| `EveryTrainHasInterface(assemblies)` | Checks every train in the assemblies: a concrete class that derives from `ServiceTrain<,>` or implements `IServiceTrain<,>` directly. Each needs exactly one most-derived, non-generic interface deriving `IServiceTrain<TIn, TOut>`; its `FullName` is the train's canonical name. Flags a train with no such interface, and a train with two of which neither extends the other, which the train registry refuses. `Inspected` is the number of trains found. |
| `TrainAssemblies` | The assemblies whose trains `TrainGuardFixture` checks. Abstract, so a subclass supplies it. |
| `Every_train_has_a_companion_interface` | Runs `EveryTrainHasInterface(TrainAssemblies)`. Fails when the assemblies hold no train, then on any offender. |

The rule is the one the train registry applies when it scans; the interface is conventionally named `I{Name}`, but any name passes.

## Examples

```csharp compile
using Trax.Mediator.Testing;

var result = TrainGuards.EveryTrainHasInterface([typeof(Program).Assembly]);

if (!result.Passed)
    throw new InvalidOperationException(result.FailureMessage);
```

The fixture, subclassed in an NUnit test project:

```csharp fragment
[TestFixture]
public sealed class TrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies => [typeof(MyAssemblyMarker).Assembly];
}
```

## See also

- [Architecture Guards](/docs/reference/architecture-guards)
- [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), and why a train is resolved through its interface
