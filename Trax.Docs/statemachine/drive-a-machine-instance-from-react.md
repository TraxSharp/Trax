---
title: Drive a machine instance from React
description: "Bind a state machine's typed operations and push stream with createMachineServer, and drive one instance from a component with useServerMachine."
type: how-to
area: machines
nav_order: 21
---

# Drive a machine instance from React

`createMachineServer(endpoint, { machine, documents, socket })`, from `@traxsharp/state-machine/client`, binds one exposed
machine's typed operations and its [push](/docs/sdk-reference/graphql-api/machines#the-push) stream into the `MachineServer` that
`useServerMachine` (from `@traxsharp/state-machine/react`) and its framework-free `ServerMachineController` drive an instance
through. The server applies every transition, stores it and answers what it stored; the twin only predicts.

## Bind the server and use the hook

```ts
import { createClient } from 'graphql-ws';
import { createMachineServer, persistedMessageReplacer } from '@traxsharp/state-machine/client';
import { useServerMachine } from '@traxsharp/state-machine/react';
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
  if (map.error) return <p>{map.error.code}</p>;           // gone, client-outdated (reload), or CONNECTION_CHANGED
  return <button disabled={!map.can('Back')} onClick={() => map.send('Back')}>Back</button>;
}
```

## What createMachineServer does

- **Sends every operation by its persisted id,** through the same executor as `createMachineExecutor`, so the same
  document checks apply. `documents` must hold the push document (`changed`), or it throws when it is built.
- **Subscribes by id over the socket you pass,** with an empty document: graphql-ws requires one, and a host that
  runs persisted documents refuses one beside an id, so pass `persistedMessageReplacer` as the client's
  `jsonMessageReplacer` and it leaves the empty `query` out. The package does not depend on graphql-ws; any socket
  with its `subscribe(payload, sink)` shape (`PersistedSubscriptionSocket`) does.
- **Maps the stream:** a snapshot with its `rowId` and `revision`; `gone: true` as gone, and the `NOT_FOUND` a missing
  id is answered with as gone with `notFound`; `CLIENT_OUTDATED` as client-outdated; `reload: true` as a read. A socket error whose
  `code` is in `terminalStreamErrors` ends the stream for good: it reaches the controller as `ended`, and is not
  subscribed again. A request rejected with one of those codes is not a transport failure either: the server's
  `endedBy` names it, and the controller stops on it the same way. The generated client lists `CONNECTION_CHANGED`
  there, which a server made before the last `configureTrax` refuses every request and stream with. A stream that ends any
  other way (its host stopped, the socket gave up, `TOO_MANY_SUBSCRIPTIONS`) is subscribed again after
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
  or a push replaces it. A refusal or a transport failure rolls it back and sets `problem`; `CLIENT_DIVERGENCE`
  also reads the instance again. `SERVER_OUTDATED` is retried under the same requestId after each of `retryDelays`.
  The twin holds no handler for a custom rule, so it predicts nothing that needs one: such an event is sent without a
  prediction, and the answer is what the hook shows.
  An effect-bound event (its edge runs an effect, such as a charge) is sent with no input and no prediction, since
  the effect produces what its edge records, and nothing queued after it is predicted until it answers; `can` reads
  its guard with every rule on the input, and every custom rule, counted as passing, so a rule on the context still
  decides.
- **Trusts the server on custom rules.** A snapshot the server sends is checked for its shape and every built-in
  rule; a custom rule the twin has no handler for counts as passed, because the server evaluated it. A snapshot that
  fails a check it can make stops the hook with `CLIENT_OUTDATED`.
- **Orders by revision.** Anything at or below the revision held is dropped, the hook's own answers included; it
  reads the instance again only after `reload`, an incarnation change or a `CLIENT_DIVERGENCE`.
- **Stops for good** on `gone`, `CLIENT_OUTDATED`, `ended`, or a request the server's `endedBy` names: `status` is
  `stopped`, `error.code` says which (with `ended` or `endedBy`, the code given, such as `CONNECTION_CHANGED`), nothing
  more is sent, `retry()` answers `stopped`, and a start request id the hook kept is forgotten. With `GONE`, `error.deleted` is true when the stream said the instance was deleted or expired, and false
  when the server answered `NOT_FOUND`, which it answers alike for a missing instance and another caller's. `dueAt` is shown, never fired: the server runs a state's timer and pushes the move. It is taken from
  every answer and push the hook accepts, and null always clears it: the state has no timer, or the server gave up
  on it and cleared it. An answer that does not carry the field, or cannot know it (`dueAtKnown: false`, a replay
  whose recorded revision the row has moved past, which `createMachineServer` delivers with no `dueAt`), keeps the
  due time held while the state stays the same; in a timed state it otherwise reads the instance once.
- **Retries on request.** A transport failure is not retried by itself: `retry()` resends the failed start, or the
  last event that failed (in transport, or `SERVER_OUTDATED` past every retry), under its first request id, so the
  server replays its result rather than applying it twice. A later event the server confirms supersedes the failed
  one: resending it then would apply it after the later one, so `retry()` answers `NOTHING_TO_RETRY`. A refused
  start is not resent. `send` again would be a new request.
- **Moves with its options.** A new `server`, `machine`, `id` or `startRequestId` closes the old instance's stream
  and opens the new one. From that render on the hook is `loading` and sends nothing, so a page whose server changed
  (another user signed in) never shows the instance before it under the new caller.
- **Mints request ids** with `newRequestId`, `crypto.randomUUID` by default, which never makes one the server
  refuses. A custom one must keep to the server's rule: 1 to 200 characters, not blank, with no control character
  and no unpaired surrogate; anything else is answered `INVALID_REQUEST_ID`.

It returns `status`, `id`, `snapshot`, `state`, `context`, `revision`, `dueAt`, `pending`, `problem`, `error`, `send`,
`retry`, `can` and `available`. The Recovery sample's topic map is built on it
([Recovery](/docs/samples/recovery)).

## SDK Reference

> [Machines root](/docs/sdk-reference/graphql-api/machines) | [@traxsharp/state-machine](/docs/sdk-reference/statemachine-api/typescript-package)
