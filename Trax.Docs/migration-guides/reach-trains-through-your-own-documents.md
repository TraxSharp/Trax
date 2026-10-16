---
title: Reach trains through your own documents
description: "Upgrade steps for the release that stopped generating a document per train: write one per train the frontend calls, register it, regenerate the client."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 0
---

# Reach trains through your own documents

Applies from: Trax 1.67.0.

A host no longer generates a persisted document for each exposed train, and `trax client` no longer writes a
`trains/<train>.g.ts` function for each one. A select-everything document grows with the number of paths through the
answer, so a train answering EF entities that reference each other either stopped the host at HotChocolate's
2048-field limit or started and was refused for cost on every call. A frontend now writes the documents it sends,
which the host already registers at boot ([Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs)).
Machine documents and the `runProgress` documents are unchanged.

## Write a document for each train the frontend calls

For each generated function the frontend imports (`startRun`, `ordersTabletCalls`), add a `.graphql` file naming its
operation the same way, Pascal case of the namespace and the field, so the function and its `<Operation>Result` type
keep their names and no import changes:

```graphql
# src/graphql/startRun.graphql
mutation StartRun($input: StartRunInput!) {
  dispatch {
    startRun(input: $input) {
      externalId
      output {
        runId
      }
    }
  }
}
```

Select what the page uses rather than the whole answer. To start from what the generated function sent, copy the
train's `documents[0].document` out of the previous `manifest.json` before you export it again.

## Register the documents on the host

Name the file `trax client --documents` writes in `ClientDocuments(path)`, resolved against the content root, and
make sure the host's build carries it:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(opts =>
        opts.RequirePersisted(true)
            .SingleNode()
            .ClientDocuments(Path.Combine(AppContext.BaseDirectory, "trax.documents.json"))
    )
);
```

## Export the manifest and regenerate the client

The manifest is now format 3: each train lists its arguments instead of a document. Export it again from the host and
run `trax client` with `--documents`, from the frontend's folder, with the same arguments in CI's `--check`:

```bash
trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql'
```

- **The `trains/` folder goes.** `trax client` deletes the `trains/*.g.ts` modules it wrote before, and writes
  `documents/<operation>.g.ts` in their place; `index.g.ts` exports the same names.
- **An older `trax client` refuses the new manifest**, naming both formats. Update the tool with the packages.

## Deploy the host first

Deploy the host with its documents before the frontend. A page built before the deploy still sends a `trax_train_`
id: the host answers `SERVER_OUTDATED` while a node of the earlier version may still serve it, and `CLIENT_OUTDATED`
once its row is collected, a grace period after no node registers it, so the page reloads into the new frontend. The
`trax_train_` prefix stays reserved, so nothing can be uploaded under it.
