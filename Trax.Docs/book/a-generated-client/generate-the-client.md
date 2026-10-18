---
title: "15.5 Generate the client"
description: "Chapter 15 of the Trax book, section 5: Generate the client."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/a-generated-client/the-hub-serves-the-storefront
---

# 15.5 Generate the client

Build the hub, and export its manifest into the storefront:

```bash
dotnet build
dotnet run --project Shopfront.Hub -- --trax-export-client ../web/src/trax/manifest.json
```

```text
info: Trax.Api.GraphQL.PersistedOperations.Extensions.TraxClientManifestExtensions[0]
      Wrote the client manifest to /home/you/Shopfront/web/src/trax/manifest.json.
```

The path is relative to the hub's project folder, where `dotnet run` starts it. Then, from `web/`,
install the packages and generate the client:

```bash
cd web
npm install
dotnet trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql'
```

```text
Generated 15 file(s) in /home/you/Shopfront/web/src/trax.
```

```text
web/src/trax/
├── index.g.ts            re-exports everything below
├── client.g.ts           configureTrax, TraxOperationError, the socket
├── documents/
│   ├── catalogue.g.ts
│   ├── myOrders.g.ts
│   └── placeOrder.g.ts
├── progress.g.ts         readRunProgress, followRunProgress, useRunProgress
├── checkout/             the checkout machine's client, for the next chapter
├── types.g.ts            the input types and enums
├── trax.documents.json   the documents, which the hub registers
├── manifest.json         the manifest it was generated from
├── schema.graphql        for an editor's GraphQL extension
└── ...
```

Commit all of it. Each document becomes a function named after its operation:

```ts
/** Requires a signed-in caller, holding one of the roles Customer. The host checks it either way. */
export function placeOrder(input: PlaceOrderInput): Promise<PlaceOrderResult> {
  return sendDocument(placeOrderDocumentId, { input }, ["dispatch", "placeOrder"]);
}
```

It takes each required variable as a parameter, answers with the field the document asked for,
typed from what it selected, and says in its comment who may call it. `placeOrderDocumentId` is the
id the hub registered, `trax_client_PlaceOrder_` and a hash of the text.

Build the hub again, so it copies `trax.documents.json` beside itself. The first build had no file
to copy, and an export does not need one; a hub that serves does, and refuses to start without it.

```bash
cd ..
dotnet build
```

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.6 The page](/docs/book/a-generated-client/the-page)
