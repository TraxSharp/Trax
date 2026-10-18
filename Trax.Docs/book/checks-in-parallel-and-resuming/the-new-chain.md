---
title: "14.1 The new chain"
description: "Chapter 14 of the Trax book, section 1: The new chain."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/the-machine-places-the-order/test-it
---

# 14.1 The new chain

```text
FulfilOrderInput
   ├── box:     ChooseBoxJunction        → Box
   └── courier: GetCourierRatesJunction  → CourierRates
BuyLabelJunction                         → ShippingLabel
Checkpoint<ShippingLabel>
PackOrderJunction                        → FulfilOrderOutput
```

The two branches run at once. `BuyLabelJunction` runs when both have finished, with what each
returned. The checkpoint stores the label. `PackOrderJunction` packs the order with the label on
it.

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [14.2 The courier and the warehouse](/docs/book/checks-in-parallel-and-resuming/the-courier-and-the-warehouse)
