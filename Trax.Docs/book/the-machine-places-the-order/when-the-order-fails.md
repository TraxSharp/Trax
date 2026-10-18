---
title: "13.6 When the order fails"
description: "Chapter 13 of the Trax book, section 6: When the order fails."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/the-machine-places-the-order/place-an-order-through-checkout
---

# 13.6 When the order fails

Start a second checkout, take it to `Payment` with `tok_chargeDeclined`, and place the order:

```bash
send 'placeOrder(id: $id, requestId: \"d-6\")'
get
```

```json
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","invokedRun":{"runId":"68617bc0e1cb4059948881b0f8479b68"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"get":{"state":"Failed","context":{"orderId":null,"parcelId":null}}}}}}
```

The run failed at the payment, and the checkout took `OnFailed`. `Retry` runs `PlaceOrder` again
from its first junction, as a new run:

```bash
send 'retry(id: $id, requestId: \"d-7\")'
```

```json
{"data":{"machines":{"checkout":{"retry":{"state":"Placing","invokedRun":{"runId":"c3aab85fdb7d43f3939ea05d3acad073"},"problem":null}}}}}
```

With a card that declines, it fails again and the checkout is back in `Failed`. A try that was
charged and then failed, which the fake cannot do but a real system can, is answered from the
first charge on the next try: the customer is charged once however often they retry.

A worker that stops in the middle of an invoked run has its run failed, when the run is found
stale or the scheduler next starts, and the checkout takes `OnFailed` in the same way. A
checkpoint does not resume it. The customer retries, and the key keeps the retry from charging
twice.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.7 Test it](/docs/book/the-machine-places-the-order/test-it)
