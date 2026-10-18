---
title: "15.9 Test it"
description: "Chapter 15 of the Trax book, section 9: Test it."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/a-generated-client/keep-it-current
---

# 15.9 Test it

The hub's tests start it, and it now reads `trax.documents.json` as it starts, so they show the
storefront's documents are valid: a document the hub refuses fails every host test. Nothing in
`tests/` changes.

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    74, Skipped:     0, Total:    74 - Shopfront.Tests.dll (net10.0)
```

## What we have

A storefront that calls the hub through typed functions generated from its own documents, which
the hub checked and registered when it started. It sends ids, not text.

The next chapter takes the customer through checkout in the browser, with the client `trax
client` already generated for the checkout machine.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [16. Checkout in the browser](/docs/book/checkout-in-the-browser)
