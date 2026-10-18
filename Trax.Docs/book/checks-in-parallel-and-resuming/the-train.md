---
title: "14.3 The train"
description: "Chapter 14 of the Trax book, section 3: The train."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/checks-in-parallel-and-resuming/the-courier-and-the-warehouse
---

# 14.3 The train

`Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Trains.FulfilOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public class FulfilOrderTrain : ServiceTrain<FulfilOrderInput, FulfilOrderOutput>, IFulfilOrderTrain
{
    protected override Task<Either<Exception, FulfilOrderOutput>> Junctions() =>
        Parallel(p =>
                p.Branch("box", b => b.Chain<ChooseBoxJunction>())
                    .Branch("courier", b => b.Chain<GetCourierRatesJunction>())
            )
            .Chain<BuyLabelJunction>()
            // A fulfilment that fails after this resumes here, with the label it bought.
            .Checkpoint<ShippingLabel>()
            .Chain<PackOrderJunction>()
            .Resolve();

    // One order is fulfilled one queued run at a time, however often it is queued.
    protected override string? QueueSubjectKey(Metadata metadata) => $"order:{TrainInput.OrderId}";
}
```

What is new:

- **`Parallel`** declares a step whose **branches** run side by side, each named. A branch is
  written on the parameter it is handed, `b.Chain<...>()`, the way a track of a `Switch` is. Each
  branch starts from a copy of the run's Memory and has a dependency injection scope of its own,
  so two branches never share a database context. When both have finished, what each returned
  joins the run's Memory, and `BuyLabelJunction` reads both. The box and the rates take two
  seconds each, and the step takes two.
- **What a branch may do** is checked when the process starts, like every chain. Two branches may
  not return the same type, since the join would have two values for one type, and a branch may
  not read what a sibling returns, since it cannot see it. A branch computes and returns a value;
  anything that must be all or nothing is written after the join, because each branch commits on
  its own.
- **A failed branch** cancels the other and fails the step, with a `BranchesFailedException`
  naming each branch that failed. The failure is transient only when every branch's was.
- **`Checkpoint<ShippingLabel>()`** stores the `ShippingLabel` in Memory when the run reaches it.
  Only that value is stored, not the rest of Memory, so the startup check can tell from the chain
  alone that every step after it finds what it needs: `PackOrderJunction` needs the input, which a
  resumed run has, and the label, which the checkpoint gives back.

A run that completes deletes its checkpoints. A run that fails keeps them until a resume of it
completes or metadata cleanup takes the run.

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [14.4 Fulfil an order](/docs/book/checks-in-parallel-and-resuming/fulfil-an-order)
