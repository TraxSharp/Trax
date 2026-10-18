---
title: "13.5 Place an order through checkout"
description: "Chapter 13 of the Trax book, section 5: Place an order through checkout."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/the-machine-places-the-order/the-checkout-invokes-and-spawns
---

# 13.5 Place an order through checkout

Start the hub and the worker. As `ada`, start a checkout and take it to `Payment` as in chapter 12.
This chapter's `send` asks for the run a write started:

```bash
send() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { '"$1"' { state invokedRun { runId } problem { code message } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
get() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "query($id: UUID!) { machines { checkout { get(id: $id) { state context { orderId parcelId } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
send 'placeOrder(id: $id, requestId: \"r-6\")'
```

```json
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","invokedRun":{"runId":"db37bfbe148543dc9c9287b419ba4077"},"problem":null}}}}}
```

The checkout is in `Placing`, and `invokedRun` names the run it queued. A few seconds later the
worker has run it, the hub has applied its outcome, and `get` says so:

```bash
get
```

```json
{"data":{"machines":{"checkout":{"get":{"state":"Placed","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":null}}}}}}
```

The order is `ada`'s, because the invoked run knew whose checkout it was. Her orders over GraphQL:

```bash
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { orders { nodes { id customerId total } } } }"}'
```

```json
{"data":{"discover":{"orders":{"nodes":[{"id":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","customerId":"TraxJwt:ada","total":24.00}]}}}}
```

Ask to follow the parcel:

```bash
send 'track(id: $id, requestId: \"r-7\")'
get
```

```json
{"data":{"machines":{"checkout":{"track":{"state":"Shipping","invokedRun":{"runId":"6b3326b5f2e44e7896a7d97a26fbccbd"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"get":{"state":"Shipping","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":"e4bd886a-28e0-4938-9d11-ffe1b22b2b22"}}}}}}
```

`parcelId` is the parcel the checkout started, written by the server. Two minutes later the
parcel's timers have taken it to `Delivered`, which ends it, and its end has moved the checkout:

```bash
get
```

```json
{"data":{"machines":{"checkout":{"get":{"state":"Delivered","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":"e4bd886a-28e0-4938-9d11-ffe1b22b2b22"}}}}}}
```

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.6 When the order fails](/docs/book/the-machine-places-the-order/when-the-order-fails)
