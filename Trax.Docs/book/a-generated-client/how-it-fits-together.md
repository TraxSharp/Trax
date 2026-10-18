---
title: "15.1 How it fits together"
description: "Chapter 15 of the Trax book, section 1: How it fits together."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/checks-in-parallel-and-resuming/test-it
---

# 15.1 How it fits together

```text
web/src/graphql/*.graphql ──┐
                            ├──► trax client ──► web/src/trax/   (typed functions, hooks)
hub --trax-export-client ───┘                     └── trax.documents.json ──► the hub registers it
```

1. **The page writes its documents**, one operation per `.graphql` file, asking for exactly the
   fields it shows.
2. **The hub exports its client manifest**: every train, query model and machine it serves, with
   their types. The hub writes it and exits, without serving.
3. **`trax client` reads both** and writes the client: a function per document, and the documents
   themselves in `trax.documents.json`.
4. **The hub registers `trax.documents.json` when it starts.** Each document gets an id from a hash
   of its text, and the page sends only that id and its variables, never the text. That is what
   persisted operations are: the server runs the documents it registered, and in chapter 17 it
   runs nothing else.

A document that asks for a field the hub does not serve, or one a train refuses, stops the hub at
start, naming the document and its file. A mistake in the storefront is found when the hub starts,
not when a customer clicks.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.2 The storefront's place](/docs/book/a-generated-client/the-storefronts-place)
