---
title: "15.7 Run it"
description: "Chapter 15 of the Trax book, section 7: Run it."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/a-generated-client/the-page
---

# 15.7 Run it

Start the hub and the worker, and the storefront:

```bash
cd web
npm run dev
```

Open `http://localhost:5173`. Buy a mug, and the order's steps appear under it; buy a cap and the
page says `CAP is out of stock.` What the functions answered, from the first call down:

```text
catalogue    {"nodes":[{"sku":"CAP","name":"Cap","price":18.5,"stock":0},{"sku":"MUG","name":"Mug","price":12,"stock":10},{"sku":"TEE","name":"T-shirt","price":25,"stock":3}]}
placeOrder   {"externalId":"f8ec30074c93467dbd65fe25819cb77c","output":{"orderId":"c6213f82-1da0-49ec-9e90-1d38a7c59cf4","total":12}}
placeOrder   TraxOperationError TRAX_TRAIN_ERROR CAP is out of stock.
myOrders     {"nodes":[{"id":"c6213f82-1da0-49ec-9e90-1d38a7c59cf4","total":12,"placedAt":"2026-10-18T21:29:00.004777Z"}]}
```

The hub ran every one of them by id. Its log has no `observed inline query` line for them: shadow
mode logs only documents sent as text, and the page sent none.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.8 Keep it current](/docs/book/a-generated-client/keep-it-current)
