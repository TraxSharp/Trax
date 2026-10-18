---
title: "6.7 Try it"
description: "Chapter 6 of the Trax book, section 7: Try it."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/customers-and-staff/sign-in-and-run-trains-as-the-caller
---

# 6.7 Try it

```bash
dotnet run --urls http://localhost:5080
```

Ordering without signing in is refused:

```bash
curl -i -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```text
HTTP/1.1 401 Unauthorized
```

Sign in as `ada` and order a mug:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -X POST http://localhost:5080/orders -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

Sign in as `grace` and do the same. Then list the orders as each:

```bash
GRACE=$(curl -s http://localhost:5080/dev/token/grace)
curl -s http://localhost:5080/orders -H "Authorization: Bearer $ADA"
curl -s http://localhost:5080/orders -H "Authorization: Bearer $GRACE"
```

```json
[{"id":"3179f9c4-07ba-4f64-ac64-e7026995cc89","customerId":"TraxJwt:ada","total":12.00,"placedAt":"2026-10-18T19:30:06.689317Z"}]
[{"id":"fb90a6f5-f5e0-4a89-b6cc-d76141452907","customerId":"TraxJwt:grace","total":12.00,"placedAt":"2026-10-18T19:30:06.837553Z"}]
```

Each sees their own. A member of staff sees both, and every order from the chapters before this
one, but cannot place an order:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/orders -H "Authorization: Bearer $LIN"
curl -i -X POST http://localhost:5080/orders -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```text
HTTP/1.1 403 Forbidden
```

The orders from earlier chapters belong to `ada` and `grace` without a scheme in front of them,
so no customer who signs in now sees them. They are staff's to read.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.8 What it refuses](/docs/book/customers-and-staff/what-it-refuses)
