---
title: "7. Serve it over GraphQL"
description: "Chapter 7 of the Trax book: serve the catalogue, PlaceOrder and each customer's orders over GraphQL, and let a customer follow the run of their order."
type: tutorial
area: general
parent: The Trax book
nav_order: 17
---

# 7. Serve it over GraphQL

Shopfront's API is two hand-written endpoints, and every new thing a client needs would be another.
In this chapter Trax generates the API instead: a GraphQL schema with the catalogue for everyone,
`PlaceOrder` for customers and each customer's own orders, from the trains and entities that are
already there and the rules already on them. A customer can also follow the run of their order,
junction by junction.

This chapter starts from the end of [chapter 6](/docs/book/customers-and-staff).

## In this chapter

1. [Packages](/docs/book/serve-it-over-graphql/packages)
2. [PlaceOrder becomes a mutation](/docs/book/serve-it-over-graphql/placeorder-becomes-a-mutation)
3. [The catalogue and the orders become queries](/docs/book/serve-it-over-graphql/the-catalogue-and-the-orders-become-queries)
4. [Serve the schema](/docs/book/serve-it-over-graphql/serve-the-schema)
5. [Ask it](/docs/book/serve-it-over-graphql/ask-it)
6. [Test it](/docs/book/serve-it-over-graphql/test-it)

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [7.1 Packages](/docs/book/serve-it-over-graphql/packages)
