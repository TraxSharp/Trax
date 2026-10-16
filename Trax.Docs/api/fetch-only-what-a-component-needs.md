---
title: Fetch only what a component needs
description: "Write a .graphql document selecting what a component needs from a train, generate its typed function with trax client --documents, and register it."
type: how-to
area: api
nav_order: 6
source:
  - Trax.Cli/src/Trax.Cli/Clients
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/ClientDocuments.cs
---

# Fetch only what a component needs

A frontend reaches a train through a document it writes. The host generates none for a train, because only the
component knows what it needs from an answer: an entity whose navigations lead to other entities has no finite
"everything" worth fetching. Write a `.graphql` document selecting what the component needs: the host resolves only
that, and the generated function's type is exactly what comes back. These steps assume a host with
[persisted operations](/docs/api/turn-on-persisted-operations) and a client generated with `trax client`.

## 1. Write the document

Put a `.graphql` file anywhere in the frontend's source. It holds one named `query` or `mutation`, rooted where the
train sits: `discover` for a query train or a query model, `dispatch` for a mutation train, and the namespace if it
has one.

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

A list of a query model is paged, filtered and sorted in the document; only the values vary:

```graphql
# src/trax-documents/openOrders.graphql
query OpenOrders($customer: String, $after: String) {
  discover {
    orders(
      first: 20
      after: $after
      where: { state: { eq: "open" }, customerName: { eq: $customer } }
      order: { placedAt: DESC }
    ) {
      totalCount
      nodes {
        title
        placedAt
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}
```

The query model is projected, so the host reads only `title` and `placedAt` from the database.

A shape several components share goes in a named fragment, in a file of its own or beside the operation:

```graphql
# src/trax-documents/customerBadge.graphql
fragment CustomerBadge on Customer {
  name
}
```

What a document may hold:

- **Trains and query models.** Under `discover` or `dispatch`, only the trains and the
  [query models](/docs/api/expose-an-entity-as-a-query-model) the host registered, and their namespaces. A query may
  hold several, trains and query models together; a mutation runs exactly one train. A field someone added to
  `discover` by hand is refused. The operations surface, machines and `runProgress` keep their own generated documents.
- **Filters and sorts in the document.** Write a query model's `where` and `order` in the document and pass only
  their values as variables (`where: { state: { eq: $state } }`, `order: { title: $direction }`). A variable whose
  type is a filter or sort input (`$where: OrderFilterInput`) is refused, so the id fixes which filter runs.
- **No directives.** `@include`, `@skip`, `@defer` and `@stream` are refused, so every field in the type is always
  there.
- **Any depth the host allows.** A document may follow any field, an entity's reference back to its parent included;
  the host's `MaxExecutionDepth`, cost limit and operation cap still apply.

## 2. Generate its function

Run `trax client` with `--documents`, from the frontend's root:

```bash
trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/**/*.graphql'
```

It checks every document against the host's types, as the host will, and refuses with the file, line and column of
each problem: an unknown field or argument, an inline filter or sort naming a field the model does not have or a
value of the wrong type, a query model in a mutation, and a filter or sort passed as a variable. For each operation it
writes `documents/<operation>.g.ts`, exported from `index.g.ts`:

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
fields, from any document:

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
anything but its trains and query models, passes a filter or sort as a variable, names a field the host no longer
has, or is deeper than its `MaxExecutionDepth` refuses the start, naming the document and its file. Each train's and
query model's own authorization still applies to every call.

## 4. Deploy the host first

The host must hold a document before a page sends it, so deploy the host, then the frontend. A page built before
the deploy keeps working: a version of a document the frontend no longer holds keeps running, on every node, while
it still validates, until no node has registered it for the grace period
([`KeepOlderMachineDocumentsFor`](/docs/sdk-reference/persisted-operations/persisted-operations-builder), seven days
by default). After that, or once a schema change breaks it, the host answers `CLIENT_OUTDATED` and the page reloads.

## SDK Reference

> [ClientDocuments](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [`trax client`](/docs/reference/cli#frontend-clients-trax-client)
