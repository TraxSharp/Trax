---
title: "7.5 Ask it"
description: "Chapter 7 of the Trax book, section 5: Ask it."
type: tutorial
area: general
parent: "7. Serve it over GraphQL"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/serve-it-over-graphql/serve-the-schema
---

# 7.5 Ask it

```bash
dotnet run --urls http://localhost:5080
```

The catalogue needs nobody signed in:

```bash
curl -s http://localhost:5080/trax/graphql -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { products(order: { sku: ASC }) { nodes { sku name price stock } } } }"}'
```

```json
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":0},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":3}]}}}}
```

Your stock is lower, after the orders of the earlier chapters. The row version is not a field: the
schema serves `IProductListing`, not `Product`.

Place an order as `ada`. The answer carries the run's `externalId`, which is how a run is named to
everything that can follow it:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' -d '{
  "query": "mutation Place($input: PlaceOrderInput!) { dispatch { placeOrder(input: $input) { externalId output { orderId total } } } }",
  "variables": { "input": { "paymentToken": "tok_visa", "lines": [{ "sku": "MUG", "quantity": 1 }] } }
}'
```

```json
{"data":{"dispatch":{"placeOrder":{"externalId":"f3e0f595ed6a472b926c46dbaf671e8d","output":{"orderId":"c5625240-1274-4e08-b2ff-2115a19fc9aa","total":12.00}}}}}
```

Read the run back, step by step:

```bash
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' -d '{
  "query": "query Progress($runId: String!) { runProgress(runId: $runId) { state steps { position name state } problem { code } } }",
  "variables": { "runId": "f3e0f595ed6a472b926c46dbaf671e8d" }
}'
```

```json
{"data":{"runProgress":{"state":"COMPLETED","steps":[{"position":0,"name":"IdentifyCustomerJunction","state":"COMPLETED"},{"position":1,"name":"PriceCartJunction","state":"COMPLETED"},{"position":2,"name":"AssessRiskJunction","state":"COMPLETED"},{"position":3,"name":"LooksFraudulent","state":"COMPLETED"},{"position":4,"name":"LooksFraudulent","state":"COMPLETED"},{"position":5,"name":"(withheld)","state":"COMPLETED"},{"position":6,"name":"(withheld)","state":"COMPLETED"},{"position":7,"name":"(withheld)","state":"COMPLETED"}],"problem":null}}}
```

Use your own `externalId`. The first three steps are the junctions before the fraud check. Then
the question is asked and routed on, as two steps. After that the names are withheld, and that is
deliberate: which junctions ran after a decision says what was decided, and a customer is not told
how the fraud check judged them. `[TraxRunProgress(Answers = true)]` would show them, and the
answer with them, for a train whose decisions its caller may see.

A run is followed by the caller who started it and by nobody else. Ask the same as `grace` and the
answer is `NOT_FOUND`, the same answer as for a run that does not exist, so it tells her nothing
about `ada`'s orders:

```json
{"data":{"runProgress":{"state":null,"steps":[],"problem":{"code":"NOT_FOUND"}}}}
```

A run started over a `mutation` returns once it has finished, so here the steps are all done. A
subscription to the same `runProgress` streams each step as it happens, which matters once runs
are queued and take longer than a request.

Errors come back as GraphQL errors. A plain `TrainException` keeps its message for the customer,
under the code `TRAX_TRAIN_ERROR`:

```json
{"errors":[{"message":"The card was declined.","path":["dispatch","placeOrder"],"extensions":{"code":"TRAX_TRAIN_ERROR"}}],"data":{"dispatch":{"placeOrder":null}}}
```

Anything else is masked, so the payment provider's timeout, with our provider's name in its
message, reaches the customer as:

```json
{"errors":[{"message":"Unexpected Execution Error","path":["dispatch","placeOrder"]}],"data":{"dispatch":{"placeOrder":null}}}
```

This is why chapter 1 threw `TrainException` for every message meant for the customer and nothing
else.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [7.6 Test it](/docs/book/serve-it-over-graphql/test-it)
