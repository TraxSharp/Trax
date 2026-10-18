---
title: "2.7 Run them"
description: "Chapter 2 of the Trax book, section 7: Run them."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/testing-place-order/guard-the-projects-shape
---

# 2.7 Run them

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 816 ms - Shopfront.Tests.dll (net10.0)
```

Eleven are ours. The other five are the guards, which the fixtures bring with them.

Break something to see a test catch it. Change the empty-cart message in `PriceCartJunction` and
run the tests again:

```text
  Failed Run_EmptyCart_Throws [13 ms]
  Error Message:
   Expected exception message to match the equivalent of "The cart is empty.", but "Your cart is empty." does not.
```

Put the message back before going on.

## What we have

Shopfront has a test project that checks each junction on its own, the train as a whole and the
application over HTTP, and the guards check its shape. Every later chapter adds to it.

The record the train test reads lives in memory and is gone when the process ends. In chapter 3 we
move it to Postgres, make the payment provider fail now and then, and read back what happened.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [3. Every order is recorded](/docs/book/every-order-is-recorded)
