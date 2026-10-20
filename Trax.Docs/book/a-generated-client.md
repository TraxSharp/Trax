---
title: "15. A generated client"
description: "Chapter 15 of the Trax book: write the storefront's GraphQL documents and generate a typed React client for them with trax client."
type: tutorial
area: general
parent: The Trax book
nav_order: 25
---

# 15. A generated client

Customers have been using Shopfront through `curl`. In this chapter they get a storefront: a React
page that lists the catalogue, places an order, follows its run and lists the customer's orders.
The page calls none of that by hand. It writes the GraphQL documents it needs, and Trax generates
a typed function for each, plus the client that sends them.

This chapter starts from the end of [chapter 14](/docs/book/checks-in-parallel-and-resuming). It
needs Node.js 22 or later, as `trax machine generate` already did.

## In this chapter

1. [How it fits together](/docs/book/a-generated-client/how-it-fits-together)
2. [The storefront's place](/docs/book/a-generated-client/the-storefronts-place)
3. [The documents](/docs/book/a-generated-client/the-documents)
4. [The hub serves the storefront](/docs/book/a-generated-client/the-hub-serves-the-storefront)
5. [Generate the client](/docs/book/a-generated-client/generate-the-client)
6. [The page](/docs/book/a-generated-client/the-page)
7. [Run it](/docs/book/a-generated-client/run-it)
8. [Keep it current](/docs/book/a-generated-client/keep-it-current)
9. [Test it](/docs/book/a-generated-client/test-it)

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.1 How it fits together](/docs/book/a-generated-client/how-it-fits-together)
