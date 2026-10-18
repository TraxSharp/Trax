---
title: "8.6 A customer gets nothing"
description: "Chapter 8 of the Trax book, section 6: A customer gets nothing."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/the-operations-console/open-it
---

# 8.6 A customer gets nothing

The console's requests are ordinary GraphQL, so the gate is easy to see from `curl`. As staff:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 2) { items { name trainState failureJunction } } } }"}'
```

```json
{"data":{"operations":{"executions":{"items":[{"name":"Shopfront.Trains.PlaceOrder.IPlaceOrderTrain","trainState":"COMPLETED","failureJunction":null},{"name":"Shopfront.Trains.PlaceOrder.IPlaceOrderTrain","trainState":"FAILED","failureJunction":"TakePaymentJunction"}]}}}}
```

As a customer, the same request is refused:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 2) { items { name } } } }"}'
```

```json
{"errors":[{"message":"Not authorized.","path":["operations"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],"data":{"operations":null}}
```

The refusal names no role and no field below `operations`: a caller who may not see the namespace
learns nothing about what is in it.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)

---

Next: [8.7 Test it](/docs/book/the-operations-console/test-it)
