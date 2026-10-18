---
title: "15.3 The documents"
description: "Chapter 15 of the Trax book, section 3: The documents."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/a-generated-client/the-storefronts-place
---

# 15.3 The documents

Each file holds one named operation. The roots are the ones the API already has: `discover` for
queries, `dispatch` for trains run as mutations.

```graphql file=web/src/graphql/Catalogue.graphql
query Catalogue {
  discover {
    products(order: { sku: ASC }) {
      nodes {
        sku
        name
        price
        stock
      }
    }
  }
}
```

```graphql file=web/src/graphql/PlaceOrder.graphql
mutation PlaceOrder($input: PlaceOrderInput!) {
  dispatch {
    placeOrder(input: $input) {
      externalId
      output {
        orderId
        total
      }
    }
  }
}
```

`externalId` is the run's id, which the page follows. `runProgress` is not written here: the hub
generates that document itself, for every train marked `[TraxRunProgress]`.

```graphql file=web/src/graphql/MyOrders.graphql
query MyOrders {
  discover {
    orders {
      nodes {
        id
        total
        placedAt
      }
    }
  }
}
```

A document asks for what its page shows and nothing more. Every rule from the earlier chapters
still applies to it: the query filter scopes `orders` to the caller, and `placeOrder` admits only
customers.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.4 The hub serves the storefront](/docs/book/a-generated-client/the-hub-serves-the-storefront)
