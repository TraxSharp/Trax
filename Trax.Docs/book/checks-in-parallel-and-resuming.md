---
title: "14. Checks in parallel, and resuming"
description: "Chapter 14 of the Trax book: look up fulfilment's box and courier rates side by side, and checkpoint the label so a resume never buys another."
type: tutorial
area: general
parent: The Trax book
nav_order: 24
---

# 14. Checks in parallel, and resuming

Fulfilment packs the order, and that is all it does. A real one does more first: it picks a box
for the order, asks the courier what it charges, and buys a label. The box and the rates do not
depend on each other, so they are looked up side by side. The label costs money, so once it is
bought, a fulfilment that fails afterwards picks up from there rather than buying a second one.

At the end of [chapter 11](/docs/book/a-separate-worker) a member of staff re-queued a fulfilment
whose worker died. A re-queue runs the train from its first junction. With a label to buy, that
would buy it twice. In this chapter staff **resume** it instead.

This chapter starts from the end of [chapter 13](/docs/book/the-machine-places-the-order).

## In this chapter

1. [The new chain](/docs/book/checks-in-parallel-and-resuming/the-new-chain)
2. [The courier and the warehouse](/docs/book/checks-in-parallel-and-resuming/the-courier-and-the-warehouse)
3. [The train](/docs/book/checks-in-parallel-and-resuming/the-train)
4. [Fulfil an order](/docs/book/checks-in-parallel-and-resuming/fulfil-an-order)
5. [Kill the worker mid-pack, and resume](/docs/book/checks-in-parallel-and-resuming/kill-the-worker-mid-pack-and-resume)
6. [Test it](/docs/book/checks-in-parallel-and-resuming/test-it)

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)
