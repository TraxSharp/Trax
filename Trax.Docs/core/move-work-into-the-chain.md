---
title: Move work around a chain into junctions
description: "Where work that sits before, after or beside a train's chain goes once Junctions() is the only declaration: setup, extra Memory values, failures, nested trains."
type: how-to
area: trains
nav_order: 3
---

# Move work around a chain into junctions

A train's chain is declared in `Junctions()` and nowhere else (see [why](/docs/core/trains-and-junctions#there-is-no-escape-hatch-on-purpose)), so work that would otherwise sit above or around the chain goes into the chain. This page says where each kind of work goes.

## Find the place for the work

| What you need | Where it goes |
|---|---|
| Logic before or after the chain | A junction at the head or the tail of it |
| Extra objects in Memory | A junction whose return value is that object, or `AddServices<IService>(value)` or `Extract<TIn, TOut>(value)` in the chain |
| Returning a failure | A junction that throws; the chain turns it into `Left` |
| Async setup | A junction, which is async already |
| Merging a nested train's result | A junction that calls `ITrainBus` and returns the merged value |
| Reading the input to decide what runs | A junction that takes the input, or a [Switch](/docs/sdk-reference/train-methods/switch) on a decision about it; `Junctions()` cannot read `TrainInput` |

## Run a nested train from a junction

1. Write a junction that takes `ITrainBus` through its constructor and calls `RunAsync` with the child's input, passing its `CancellationToken`.
2. Return the merged result from the junction.
3. Chain the junction where the child's result is needed.

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainBus;

public class ParentTrain : ServiceTrain<ParentInput, ParentResult>, IParentTrain
{
    protected override Task<Either<Exception, ParentResult>> Junctions() =>
        Chain<RunChildTrain>().Chain<ValidateParentResult>().Resolve();
}

public class RunChildTrain(ITrainBus trainBus) : Junction<ParentInput, ParentResult>
{
    public override async Task<ParentResult> Run(ParentInput input)
    {
        var child = await trainBus.RunAsync<ChildResult>(
            new ChildRequest(input.ChildData),
            CancellationToken
        );

        return new ParentResult(input.ParentData, child);
    }
}
```

The child runs as a train of its own and is not linked to the parent run. `RunAsync` takes an optional `Metadata`, but that is a pre-created `Pending` row for the child to run as, the way the scheduler uses it, not a parent: passing the running parent's metadata is refused with a `TrainException`.

## Related

- [Junctions](/docs/sdk-reference/train-methods/junctions), what the startup check refuses in a declaration
- [AddServices](/docs/sdk-reference/train-methods/add-services) and [Extract](/docs/sdk-reference/train-methods/extract)
- [TrainBus](/docs/sdk-reference/mediator-api/i-train-bus)
- [Removal of RunInternal and Activate](/docs/migration-guides/runinternal-and-activate)

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus)
