---
title: "14.4 Fulfil an order"
description: "Chapter 14 of the Trax book, section 4: Fulfil an order."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/checks-in-parallel-and-resuming/the-train
---

# 14.4 Fulfil an order

Start the hub and the worker, and place an order as `ada`. The worker's log, as it fulfils it:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.ChooseBoxJunction[0]
      Choosing a box for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.GetCourierRatesJunction[0]
      Asking the courier's rates for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.ChooseBoxJunction[0]
      Chose a Small box for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.GetCourierRatesJunction[0]
      The courier charges 4.00 and 1.50 a kilogram
info: Shopfront.Trains.FulfilOrder.Junctions.BuyLabelJunction[0]
      Bought label TRK-A1944EA7 for 5.50
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order a1944ea7-d757-412e-a1b2-439ea5a1ac11 as TRK-A1944EA7
```

The box and the rates were asked for together, and the label was bought when both had answered.

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [14.5 Kill the worker mid-pack, and resume](/docs/book/checks-in-parallel-and-resuming/kill-the-worker-mid-pack-and-resume)
