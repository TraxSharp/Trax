---
title: Drive a machine instance from React
description: "Bind a state machine's typed operations and machineChanged stream with createMachineServer, and drive one instance from a component with useServerMachine."
type: how-to
area: machines
nav_order: 21
---

# Drive a machine instance from React

`createMachineServer(endpoint, { machine, documents, socket })`, from `@trax/state-machine/client`, binds one exposed
machine's typed operations and its [`machineChanged`](/docs/sdk-reference/graphql-api/machines#machinechanged) stream into the `MachineServer` that
`useServerMachine` (from `@trax/state-machine/react`) and its framework-free `ServerMachineController` drive an instance
through. The server applies every transition, stores it and answers what it stored; the twin only predicts.

## Bind the server and use the hook

```ts
import { createClient } from 'graphql-ws';
import { createMachineServer, persistedMessageReplacer } from '@trax/state-machine/client';
import { useServerMachine } from '@trax/state-machine/react';
import documents from './topic-map.documents.json';

const server = createMachineServer('/trax/graphql', {
  headers: () => ({ 'X-Api-Key': key }),
  machine: topicMap,
  documents,
  socket: createClient({
    url: 'wss://host/trax/graphql',
    connectionParams: { apiKey: key },
    lazy: true,
    retryAttempts: Infinity,
    jsonMessageReplacer: persistedMessageReplacer,
  }),
});

function Wizard({ id }: { id?: string }) {
  const map = useServerMachine(topicMap, { server, id });   // without id, it starts an instance
  if (map.error) return <p>{map.error.code}</p>;           // gone, or client-outdated: reload
  return <button disabled={!map.can('Back')} onClick={() => map.send('Back')}>Back</button>;
}
```

## What createMachineServer does

- **Sends every operation by its persisted id,** through the same executor as `createMachineExecutor`, so the same
  document checks apply. `documents` must hold the `machineChanged` document, or it throws when it is built.
- **Subscribes by id over the socket you pass,** with an empty document: graphql-ws requires one, and a host that
  runs persisted documents refuses one beside an id, so pass `persistedMessageReplacer` as the client's
  `jsonMessageReplacer` and it leaves the empty `query` out. The package does not depend on graphql-ws; any socket
  with its `subscribe(payload, sink)` shape (`PersistedSubscriptionSocket`) does.
- **Maps the stream:** a snapshot with its `rowId` and `revision`; `gone: true`, and the `not-found` a missing id is
  answered with, as gone; `client-outdated` as client-outdated; `reload: true` as a read. A stream that ends any
  other way (its host stopped, the socket gave up, `too-many-subscriptions`) is subscribed again after
  `resubscribeDelays` (1, 2, then every 5 seconds); the new stream's first event is the instance as it is then, so
  nothing is read again.
- **Adds `discard(id, requestId)`**, for a page that throws an instance away.

## What useServerMachine does

`useServerMachine(machine, { server, id?, startInput?, startRequestId?, startStorageKey?, newRequestId?, retryDelays? })`:

- **Opens or starts.** With `id` it subscribes, then reads the instance. Without one it calls `start` once, under
  `startRequestId` when you keep one (across a reload, so a start that was in flight answers with the instance it
  created) and a minted one otherwise; StrictMode's second mount reuses it. A new `startRequestId` starts a new
  instance. A start request id the hook minted is kept in `sessionStorage` under `startStorageKey` (by default
  `trax-start:<machine id>`; `null` keeps nothing) until the start answers, so a reload while it is unanswered, or
  after it failed, sends the same id and gets the instance the first request created. Give each hook that starts the
  same machine on one page its own key. Without `sessionStorage` nothing is kept.
- **Predicts, then confirms.** `send(trigger, input)` queues the event behind any earlier one of the instance, sends
  it under its own requestId with the twin's prediction as `clientResult`, and shows the prediction until the answer
  or a push replaces it. A refusal or a transport failure rolls it back and sets `problem`; `client-divergence`
  also reads the instance again. `server-outdated` is retried under the same requestId after each of `retryDelays`.
- **Orders by revision.** Anything at or below the revision held is dropped, the hook's own answers included; it
  reads the instance again only after `reload`, an incarnation change or a `client-divergence`.
- **Stops for good** on `gone` or `client-outdated`: `status` is `stopped`, `error.code` says which, and nothing more
  is sent. `dueAt` is shown, never fired: the server runs a state's timer and pushes the move. It is taken from
  every answer and push the hook accepts, and null always clears it: the state has no timer, or the server gave up
  on it and cleared it. An answer that does not carry the field, or cannot know it (`dueAtKnown: false`, a replay
  whose recorded revision the row has moved past, which `createMachineServer` delivers with no `dueAt`), keeps the
  due time held while the state stays the same; in a timed state it otherwise reads the instance once.
- **Retries on request.** A transport failure is not retried by itself: `retry()` resends the failed start, or the
  last event that failed (in transport, or `server-outdated` past every retry), under its first request id, so the
  server replays its result rather than applying it twice. A later event the server confirms supersedes the failed
  one: resending it then would apply it after the later one, so `retry()` answers `nothing-to-retry`. A refused
  start is not resent. `send` again would be a new request.
- **Mints request ids** with `newRequestId`, `crypto.randomUUID` by default, which never makes one the server
  refuses. A custom one must keep to the server's rule: 1 to 200 characters, not blank, with no control character
  and no unpaired surrogate; anything else is answered `invalid-request-id`.

It returns `status`, `id`, `snapshot`, `state`, `context`, `revision`, `dueAt`, `pending`, `problem`, `error`, `send`,
`retry`, `can` and `available`. The Recovery sample's topic map is built on it
([Recovery](/docs/samples/recovery)).

## SDK Reference

> [Machines root](/docs/sdk-reference/graphql-api/machines) | [@trax/state-machine](/docs/sdk-reference/statemachine-api/typescript-package)
