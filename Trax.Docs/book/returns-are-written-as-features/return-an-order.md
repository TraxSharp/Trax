---
title: "14.7 Return an order"
description: "Chapter 14 of the Trax book, section 7: Return an order."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/returns-are-written-as-features/serve-it
---

# 14.7 Return an order

Start the hub and the worker, and place an order as `ada` through a checkout, as in chapter 13.
Keep its id, which `{ discover { orders { nodes { id } } } }` gives her. Then start a return:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
G=http://localhost:5080/trax/graphql
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"mutation { machines { orderReturn { start(requestId: \"r-1\") { id state } } } }"}'
```

```json
{"data":{"machines":{"orderReturn":{"start":{"id":"0f6c1a9e-6a52-4a1f-8d0e-2c43b8f1e7a1","state":"Draft"}}}}}
```

Send it before saying why:

```bash
ID=<the return's id>
send() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { orderReturn { '"$1"' { state problem { code message } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
send 'send(id: $id, requestId: \"r-2\")'
```

```json
{"data":{"machines":{"orderReturn":{"send":{"state":null,"problem":{"code":"GUARD_FAILED","message":"Say why you are sending it back."}}}}}}
```

The message is the one the feature wrote. Fill it in and send it:

```bash
ORDER=<the order's id>
send 'describe(id: $id, input: { orderId: \"'$ORDER'\", reason: \"It arrived broken\" }, requestId: \"r-3\")'
send 'send(id: $id, requestId: \"r-4\")'
```

```json
{"data":{"machines":{"orderReturn":{"describe":{"state":"Draft","problem":null}}}}}
{"data":{"machines":{"orderReturn":{"send":{"state":"Checking","problem":null}}}}}
```

The return is `Checking`, and the worker runs `ReturnOrder`. A moment later the view shows where
it went:

```bash
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"{ views { returns { orderReturn { id status reason } } } }"}'
```

```json
{"data":{"views":{"returns":{"orderReturn":[{"id":"0f6c1a9e-6a52-4a1f-8d0e-2c43b8f1e7a1","status":"Accepted","reason":"It arrived broken"}]}}}}
```

Start a second return as `grace` and give it Ada's order: it ends `Refused`, since the order is not
hers. Ada's view never lists Grace's return, and Grace's never lists Ada's.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.8 The board](/docs/book/returns-are-written-as-features/the-board)
