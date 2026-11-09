---
title: Call a machine from TypeScript
description: "Send a state machine's typed GraphQL operations from TypeScript with createMachineExecutor, by the persisted ids its host registered."
type: how-to
area: machines
nav_order: 20
---

# Call a machine from TypeScript

The [machines root](/docs/sdk-reference/graphql-api/machines) serves each exposed machine as typed
operations. `@traxsharp/state-machine/client` sends them.

## Build an executor

`@traxsharp/state-machine/client` drives the typed root with `createMachineExecutor(endpoint, { machines })`. Each
machine is listed with its twin, its `documents` (the entries the host's
[client manifest](/docs/sdk-reference/statemachine-api/client-manifest) lists for it, `MachineDocument`) and its
`effectEvent` when it has one. Every operation is sent by its persisted id with its values as variables, never a
document, and each document selects the whole answer. [`trax client`](/docs/reference/cli#frontend-clients-trax-client) writes both
halves: the twin, and the documents as `topicMapDocuments` in `topic-map/topic-map.client.g.ts` under its `--out`
(`src/trax` here, beside the page):

```ts
import { createMachineExecutor, startDraft } from '@traxsharp/state-machine/client';
import { topicMap, topicMapDocuments } from './trax/topic-map/topic-map.client.g';

const executor = createMachineExecutor('/trax/graphql', {
  headers: () => ({ Authorization: `Bearer ${token}` }),
  machines: [{ machine: topicMap, documents: topicMapDocuments }],
});
const started = await startDraft({ executor, machine: topicMap, requestId: crypto.randomUUID() });
```

It refuses, when it is built, a machine with no documents and documents of another machine or of another IR hash
than the twin's. It rebuilds the twin's snapshot from the answer with the null context fields left out, answers a
missing instance as `NOT_FOUND` (which a session reads as no draft) and a save as `SAVE_NOT_ALLOWED` without asking
the server, and returns `CLIENT_OUTDATED` and `SERVER_OUTDATED` as problems with their codes.

## A page on another origin

The executor sends requests only, over HTTP, so a page on another origin than the host needs the host's CORS policy
to allow it. A page that also follows an instance's push over a socket, as
[Drive a machine instance from React](/docs/statemachine/drive-a-machine-instance-from-react#a-page-on-another-origin)
does, needs its origin allowed for the socket as well: see
[Allowed origins](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins).

## SDK Reference

> [Machines root](/docs/sdk-reference/graphql-api/machines) | [@traxsharp/state-machine](/docs/sdk-reference/statemachine-api/typescript-package)
