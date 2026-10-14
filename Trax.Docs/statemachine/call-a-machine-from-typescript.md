---
title: Call a machine from TypeScript
description: "Send a state machine's typed GraphQL operations from TypeScript with createMachineExecutor, by document or by persisted id."
type: how-to
area: machines
nav_order: 20
---

# Call a machine from TypeScript

The [machines root](/docs/sdk-reference/graphql-api/machines) serves each exposed machine as typed
operations. `@trax/state-machine/client` sends them.

## Build an executor

`@trax/state-machine/client` drives the typed root with `createMachineExecutor(endpoint, { machines })`, an executor
for the same `createSnapshotClient`, `createDraftSession` and `startDraft` the generic mutations use. Each machine is
listed with its twin, the `context` selection to read back, and its `effectEvent` when it has one:

```ts
const client = createSnapshotClient(
  createMachineExecutor('/trax/graphql', {
    headers: () => ({ 'X-Api-Key': key }),
    machines: [{ machine: topicMap, context: 'fields fromYear toYear mapId papers topicPairs coCitationTrack' }],
  }),
);
```

It writes every value into the document as a literal, rebuilds the twin's snapshot from the answer with the null
context fields left out, answers a missing instance as `not-found` (which a session reads as no draft) and a save as
`save-not-allowed` without asking the server.

## Send persisted documents

Given the machine's `documents` (the [manifest](/docs/sdk-reference/graphql-api/machines#persisted-documents) entries, `MachineDocument`), it sends each
operation by its persisted id with its values as variables and never a document, and `context` is not read: each
document selects the whole answer. It refuses, when it is built, documents of another machine or of another IR hash
than the twin's, and returns `client-outdated` and `server-outdated` as problems with their codes:

```ts
import documents from './topic-map.documents.json';

createMachineExecutor('/trax/graphql', { machines: [{ machine: topicMap, documents }] });
```

## SDK Reference

> [Machines root](/docs/sdk-reference/graphql-api/machines) | [@trax/state-machine](/docs/sdk-reference/statemachine-api/typescript-package)
