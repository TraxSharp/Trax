---
title: "5.5 Run it"
description: "Chapter 5 of the Trax book, section 5: Run it."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/does-this-order-look-like-fraud/register-shopfront-in-one-place
---

# 5.5 Run it

```bash
dotnet run --urls http://localhost:5080
```

`grace` has never ordered. Her first order comes to 74.00, under the live rule's 100:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"grace","paymentToken":"tok_visa","lines":[{"sku":"TEE","quantity":2},{"sku":"MUG","quantity":2}]}'
```

It goes through. `hopper` has never ordered either, and orders nine mugs, 108.00:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"hopper","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":9}]}'
```

```json
"We could not take this order without a check. You have not been charged."
```

Nothing was reserved and nobody was charged: the train stopped at the gate.

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.6 Read the decisions](/docs/book/does-this-order-look-like-fraud/read-the-decisions)
