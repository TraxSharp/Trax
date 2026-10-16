---
title: Fetch only what a component needs
description: "Write a .graphql document that selects part of a train's answer, generate its typed function with trax client --documents, and register it on the host."
type: how-to
area: api
nav_order: 6
source:
  - Trax.Cli/src/Trax.Cli/Clients
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/ClientDocuments.cs
---

# Fetch only what a component needs

Every exposed train already has a generated document and a typed function in the client `trax client` writes. That
document selects the whole answer, short of a field that leads back to a type already in it. When a component needs
less, write a document of your own: the host resolves only what it selects, and the generated function's type is
exactly what comes back. These steps assume a host with
[persisted operations](/docs/api/turn-on-persisted-operations) and a client generated with `trax client`.

## 1. Write the document

Put a `.graphql` file anywhere in the frontend's source. It holds one named `query` or `mutation`, rooted where the
train sits: `discover` for a query train, `dispatch` for a mutation train, and the train's namespace if it has one.

```graphql
# src/trax-documents/orderCard.graphql
query OrderCard($input: FindOrderInput!) {
  discover {
    findOrder(input: $input) {
      title
      customer {
        ...CustomerBadge
      }
    }
  }
}
```

A shape several components share goes in a named fragment, in a file of its own or beside the operation:

```graphql
# src/trax-documents/customerBadge.graphql
fragment CustomerBadge on Customer {
  name
}
```

What a document may hold:

- **Trains only.** Under `discover` or `dispatch`, only trains and their namespaces. A query may hold several trains;
  a mutation runs exactly one. The operations surface, machines and `runProgress` keep their own generated documents.
- **No directives.** `@include`, `@skip`, `@defer` and `@stream` are refused, so every field in the type is always
  there.
- **Any depth the host allows.** A document may follow a field the generated one leaves out, such as an entity's
  reference back to its parent; the host's `MaxExecutionDepth` and operation cap still apply.

## 2. Generate its function

Run `trax client` with `--documents`, from the frontend's root:

```bash
trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/**/*.graphql'
```

It checks every document against the host's types, as the host will, and refuses with the file, line and column of
each problem. For each operation it writes `documents/<operation>.g.ts`, exported from `index.g.ts`:

```ts
export type OrderCardResult = {
  title: string;
  customer: {
    name: string;
  };
};

export function orderCard(input: FindOrderInput): Promise<OrderCardResult>;
```

Each named fragment becomes a type in `fragments.g.ts`. A component typed with it takes any answer that has those
fields, the train's generated one included:

```tsx
import { orderCard, type CustomerBadge } from "./trax/index.g";

function Badge({ customer }: { customer: CustomerBadge }) {
  return <b>{customer.name}</b>;
}

const order = await orderCard({ id: 7 });
<Badge customer={order.customer} />;
```

It also writes `trax.documents.json`, the documents as the host registers them, and `schema.graphql` with
`graphql.config.yml`, so the GraphQL editor extension completes and checks the `.graphql` files. Commit all of them.
`trax client --check` with the same `--documents` fails when any is stale.

## 3. Register the documents on the host

Name the file in `ClientDocuments`, relative to the host's content root:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(opts =>
        opts.RequirePersisted(true)
            .SingleNode()
            .ClientDocuments("../web/src/trax/trax.documents.json")
    )
);
```

The host checks every document again when it starts and runs it through its own validation. A document that reaches
anything but trains, names a field the host no longer has, or is deeper than its `MaxExecutionDepth` refuses the
start, naming the document and its file. Each train's own authorization still applies to every call.

## 4. Deploy the host first

The host must hold a document before a page sends it, so deploy the host, then the frontend. A page built before
the deploy keeps working: a version of a document the frontend no longer holds keeps running, on every node, while
it still validates, until no node has registered it for the grace period
([`KeepOlderMachineDocumentsFor`](/docs/sdk-reference/persisted-operations/persisted-operations-builder), seven days
by default). After that, or once a schema change breaks it, the host answers `CLIENT_OUTDATED` and the page reloads.

## SDK Reference

> [ClientDocuments](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [`trax client`](/docs/reference/cli#frontend-clients-trax-client)
