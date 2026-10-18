---
title: "17.3 What a caller sees"
description: "Chapter 17 of the Trax book, section 3: What a caller sees."
type: tutorial
area: general
parent: "17. Require persisted operations"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/require-persisted-operations/make-the-hub-refuse
---

# 17.3 What a caller sees

Start the hub, and send the catalogue's document as text, as chapter 7 did:

```bash
curl -s -w ' %{http_code}' http://localhost:5080/trax/graphql -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { products(order: { sku: ASC }) { nodes { sku name price stock } } } }"}'
```

```text
{"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]} 400
```

The same document by the id the storefront's client holds runs:

```bash
CAT=$(grep -o 'trax_client_Catalogue_[a-z0-9]*' web/src/trax/documents/catalogue.g.ts)
curl -s http://localhost:5080/trax/graphql -H 'Content-Type: application/json' -d '{"id":"'$CAT'"}'
```

```text
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":0},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":3}]}}}}
```

Introspection is refused the same way. The hub says at start which mode it is in:

```text
info: Trax.Api.GraphQL.PersistedOperations[0]
      Persisted-operation enforcement is ON. An inline query is refused unless it is allowlisted, introspection, or selects only the persisted-operations management surface. Enforcement shapes requests and is not an authorization boundary: per-type [TraxAuthorize] still decides who may run what.
```

The storefront works as it did: open it, check out and buy now. So does the console: sign in as
`lin` and it lists the runs, because each of its pages sends a document the hub registered. Its
entry page names the ids:

```text
71 documents; Executions -> trax_console_Executions_ea98447121dc28f08cf3d51a272418b4611c...
```

## SDK Reference

> [Turn on persisted operations](/docs/api/turn-on-persisted-operations) | [Persisted operations](/docs/persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)

---

Next: [17.4 What stops working](/docs/book/require-persisted-operations/what-stops-working)
