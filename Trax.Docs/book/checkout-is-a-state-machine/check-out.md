---
title: "12.7 Check out"
description: "Chapter 12 of the Trax book, section 7: Check out."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/checkout-is-a-state-machine/serve-it
---

# 12.7 Check out

Start the hub and the worker as before, and sign in as `ada`. Start a checkout:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
G=http://localhost:5080/trax/graphql
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"mutation { machines { checkout { start(requestId: \"r-1\") { id state dueAt } } } }"}'
```

```json
{"data":{"machines":{"checkout":{"start":{"id":"687a7aaa-0807-467d-9093-6e88eb0b4ac0","state":"Cart","dueAt":"2026-10-18T22:46:29.565068Z"}}}}}
```

The checkout is in `Cart`, and `dueAt` is when the two-hour timer fires. Every write carries a
`requestId` of the caller's choosing, so a request sent twice is applied once. Try to give an
address with nothing in the cart:

```bash
ID=<the id>
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"r-2\") { state problem { code message } } } } }",
  "variables": { "id": "'$ID'" }
}'
```

```json
{"data":{"machines":{"checkout":{"setAddress":{"state":null,"problem":{"code":"GUARD_FAILED","message":"Put something in the cart and give an address to send it to."}}}}}}
```

The guard refused it with its reason, as data, and the checkout stayed in `Cart`. Put two mugs in
the cart, then give the address, choose how to pay and place the order:

```bash
send() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { '"$1"' { state context { lines { sku quantity } address paymentToken } problem { code } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
send 'setLines(id: $id, input: { lines: [{ sku: \"MUG\", quantity: 2 }] }, requestId: \"r-3\")'
send 'setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"r-4\")'
send 'choosePayment(id: $id, input: { paymentToken: \"tok_visa\" }, requestId: \"r-5\")'
send 'placeOrder(id: $id, requestId: \"r-6\")'
```

```json
{"data":{"machines":{"checkout":{"setLines":{"state":"Cart","context":{"lines":[{"sku":"MUG","quantity":2}],"address":null,"paymentToken":null},"problem":null}}}}}
{"data":{"machines":{"checkout":{"setAddress":{"state":"Address","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":null},"problem":null}}}}}
{"data":{"machines":{"checkout":{"choosePayment":{"state":"Payment","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":"tok_visa"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":"tok_visa"},"problem":null}}}}}
```

The checkout is in `Placing`, where it waits for the next chapter. As `grace`, the same checkout's
`get` answers `null`, as for a checkout that does not exist: it is `ada`'s.

The hub warns once, at start, that persisted operations are not enforced, and logs each inline
document shadow mode lets through:

```text
warn: Trax.Api.GraphQL.PersistedOperations[0]
      Persisted-operation enforcement is OFF, with shadow logging on. Every inline query this endpoint serves will execute, so per-type [TraxAuthorize] is the only boundary in front of it. If enforcement is keyed on the environment, an environment holding production data but running as Development is running without it.
...
      Trax persisted-operations observed inline query (operationName=(null), willReject=False)
```

`willReject=False` says the host would have run it even with enforcement on. That is the number
to watch while shadow mode lasts.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.8 Export the definition](/docs/book/checkout-is-a-state-machine/export-the-definition)
