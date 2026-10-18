---
title: "4.8 Run it"
description: "Chapter 4 of the Trax book, section 8: Run it."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/the-shops-own-data/register-the-context-and-create-its-schema
---

# 4.8 Run it

Start the application and place an order:

```bash
dotnet run --urls http://localhost:5080
```

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":2},{"sku":"TEE","quantity":1}]}'
```

The stock and the order are in the `shop` schema:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront \
  -c 'select "Sku", "Stock" from shop."Products" order by "Sku"' \
  -c 'select "CustomerId", "Total", "TransactionId" from shop."Orders"'
```

```text
 Sku | Stock
-----+-------
 CAP |     0
 MUG |     8
 TEE |     2
(3 rows)

 CustomerId | Total | TransactionId
------------+-------+---------------
 ada        | 49.00 | tx_d8a78340
(1 row)
```

Stop the application and start it again. The stock is still eight mugs and two T-shirts, where
before every restart refilled the shelves.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.9 Test against a real database](/docs/book/the-shops-own-data/test-against-a-real-database)
