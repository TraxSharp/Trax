# The client conformance kit and its testee protocol

The engine kit (`../PROTOCOL.md`) checks that an engine answers a trigger as the others do. This kit checks the other
half of a frontend: the client that drives a served machine over a host's typed `machines` root and its change
stream. Each transcript in `transcripts/` scripts a host, request by request and push by push, and says where a client
must end up: what it sent, what each send settled as, and what its view shows. Every operation is total, so a
refusal, a transport failure or a host that stops answering ends in a value, never a throw.

A client in any language joins by implementing a **testee**: a program that drives that language's client, through
its public API, as the runner tells it. The client talks to `host.mjs`, a real GraphQL host on a local port (HTTP
POST and a graphql-transport-ws socket), so its own HTTP stack, socket and JSON are what is tested.

## Running it

From `Trax.StateMachine/`:

```bash
node conformance/client/run.mjs --testee node ts/tools/conformance/client-testee-ts.mjs   # spawn a testee command
node conformance/client/run.mjs --stdio                                                   # be spawned by one
```

`--stdio` reverses the pipes as the engine runner's does: the runner writes requests on its stdout and reads answers on
its stdin, so a test in another language (the C# `ClientConformanceTests`) starts the runner and answers in process. The Rust
client's testee is a binary, `rust/client/src/bin/client_testee.rs`, which the runner spawns.
The runner writes its report on stderr and exits 0 when every transcript passed, 1 when any failed or the testee's
answer to `end` was an `error`, and 2 on a usage error, a `--only <prefix>` that selects no transcript, or anything
else it could not run. `--only <prefix>` runs the transcripts whose name starts with the prefix. The TypeScript
client's testee exits 3 when esbuild, which it bundles the client with, is not installed. The runner and the host use
node's own modules only.

## The protocol

One JSON object per line, UTF-8, both ways. A line ends at `\n` only: JSON writes U+2028 and U+2029 raw inside a
string, so a reader that also ends a line there splits an answer. Every request has an `id` and an `op`, and its answer carries the same
`id`. Unlike the engine kit, **answers come when they settle, not in order**: a `send` is answered once the host
answers it, and the runner goes on sending requests meanwhile. A testee hands each request to its client in the order
it arrives, so two sends reach the client in the order the runner sent them. A testee answers an `op` it does not implement with
`{ "id", "unsupported": true }`, and anything that went wrong outside the client (a throw it caught) with
`{ "id", "error": "..." }`. Either fails the transcript, whichever request it answers: one a step compares, one no
step looks at, and `dispose`. (A `view`'s `error` is the code that stopped the client, beside its `status`; it is not
one.)

**`open`** builds a client for one instance and holds it until the next `open`, `dispose` or `end`. It sends nothing.

```json
{ "id": 1, "op": "open", "machine": "turnstile", "ir": { ... }, "irHash": "40b0…", "documents": [ ... ],
  "endpoint": "http://127.0.0.1:41234/graphql", "socket": "ws://127.0.0.1:41234/graphql",
  "instance": "i-1", "startInput": null, "effectEvent": null, "retryDelays": [10, 10], "resubscribeDelays": [10] }
{ "id": 1, "opened": true }
```

- `ir` is the machine's IR as committed in `../machines/<m>/`, or in `machines/<m>.ir.json` here for a machine only
  this kit drives (one whose custom rules no client holds a handler for), which is the text the C# machine exports, and
  `irHash` the lowercase hex SHA-256 of that text: the hash a host's documents carry and a generated twin holds. A
  client that predicts with an engine of its own (C#) checks its machine against it.
- `documents` are the machine's persisted documents as a host's client manifest lists them (`id`, `machine`,
  `operation`, `irHash`, `document`). A client sends each operation by its `id`, with its values as variables, never
  a document's text.
- `instance` is the id of an instance to open, or null to start one on `start`.
- `retryDelays` and `resubscribeDelays` are in milliseconds: the delays between retries of `SERVER_OUTDATED` and of a
  failed read, and the delays before subscribing again after a stream that ended without saying why.
- **Request ids.** The client mints them `r1`, `r2`, … in the order it needs them, counting from each `open`. The
  host compares them, so they must be the ids the client actually sends.

**`start`** opens the client: with an `instance`, subscribes to its change stream and reads it; without one, starts an
instance under a fresh request id and subscribes to the id the host answers with. It is answered `{ "settled": true }`
once the start or the first read has settled, whatever its result.

**`send`** sends one event, after every earlier one has settled. `trigger` is the trigger's name; `input` is its
input, or absent. The client sends its prediction as `clientResult`, except for an event bound to the machine's
effect or one with an edge out of the state it fires from that rests on a custom rule it holds no handler for (a custom
guard, a custom reduction, or a custom requirement of the state it enters): it sends those with none, predicts nothing
after them until they answer, and sends one even when its own engine would refuse it. **`retry`** resends what last
failed under the same request id. It resends nothing, and answers `NOTHING_TO_RETRY`, once a later event has been
confirmed or while one is queued or in flight: the failed event would reach the host after it. Both are answered with
what the send came to:

```json
{ "id": 4, "outcome": "confirmed", "state": "Unlocked", "revision": 4 }
{ "id": 4, "outcome": "refused", "code": "GUARD_FAILED" }
{ "id": 4, "outcome": "failed", "code": "TRANSPORT_ERROR" }
{ "id": 4, "outcome": "stopped", "code": "CLIENT_OUTDATED" }
```

**`view`** answers with what the client shows now:

```json
{ "id": 5, "status": "ready", "instance": "i-1", "state": "Unlocked", "context": { "paidWith": "quarter" },
  "confirmed": { "state": "Locked", "context": {} }, "rowId": null, "revision": 3, "dueAt": null,
  "pending": true, "problem": null, "error": null, "deleted": null }
```

`status` is `loading`, `ready` or `stopped`. `dueAt` is the due time as an ISO-8601 instant with its offset, in
whatever offset and precision the client holds it; the runner compares it as the instant it names, so
`2026-10-22T12:10:00.000+02:00` and `2026-10-22T10:10:00Z` are one value, and one read without its offset is not. `state` and `context` are what to show (the prediction while one is
pending), `confirmed` what the host last confirmed. `problem` is the code of the last refusal or transport failure,
`error` the code that stopped the client (`GONE`, `CLIENT_OUTDATED`, or `UNAUTHENTICATED` and `FORBIDDEN` for a socket
or a stream the host refused), and `deleted` whether a `GONE` came from a
deletion (true) or a host that holds no such instance for the caller (false). A context holds no member whose value
is null: a typed answer cannot tell a member stored as null from one the state does not hold.

**`dispose`** closes the client: its stream, and every request it would still send. It is answered `{ "disposed":
true }`. **`end`** is the last request; the testee answers `{ "id" }` and exits.

## What the host does

The host answers nothing by itself. A request for an id that is not one of the transcript's documents is answered
with a GraphQL error and fails the transcript; so does a subscription that sends a document's text beside its id.
It acknowledges a socket's `connection_init` unless the transcript refuses that connection, in which case it closes
the socket with 4403 before acknowledging it. A socket the host closes with 4401 or 4403, before it is acknowledged or
while subscribed, is the host turning the caller away, and the client stops for good: `UNAUTHENTICATED` for 4401,
`FORBIDDEN` for 4403, and the same for an upgrade answered 401 or 403 where the client's socket can tell one. It
subscribes again after any other close.

A host can also refuse one subscription on a socket it accepted (an expired token, an anonymous socket it
acknowledged): it answers a `next` whose first GraphQL error carries the refusal in `extensions.code`, or an `error`
message carrying it. That is the same refusal, and the client stops for good without subscribing again:
`TRAX_AUTHORIZATION` and `AUTH_NOT_AUTHORIZED` stop it as `FORBIDDEN`, `AUTH_NOT_AUTHENTICATED` as `UNAUTHENTICATED`.
A `next` or `error` coded `CLIENT_OUTDATED` stops it as `CLIENT_OUTDATED`. A client that lets its caller name further
terminal codes (TypeScript's `terminalStreamErrors`, C#'s `TerminalStreamErrors`) stops on those as well, whether they
come in a `next` or an `error`, with the code as given. A stream error with any other code, or none, is subscribed
again after the resubscribe delay.

An answer is wrapped the way a host serves it, `{ "data": { "machines": { "<machine>": { "<operation>": … } } } }`,
and a push as `{ "data": { "<machine>Changed": … } }`, with the fields a host selects that the step does not give
filled in: `dueAt: null`, `dueAtKnown: true`, `problem: null` on an answer (and `id`, `revision`, `state`, `context`
null beside a `problem`), and `rowId: null`, `gone: false`, `reload: false` on a push.

## Transcripts

```json
{
  "about": "What the transcript shows.",
  "machine": "turnstile",
  "instance": "i-1",
  "documents": { "get": "$id: UUID!", "coin": "$id: UUID!, $input: CoinInput!, $requestId: String!, $clientResult: String", ... },
  "refuseConnections": 0,
  "retryDelays": [10, 10],
  "resubscribeDelays": [10],
  "steps": [ ... ]
}
```

`documents` names each operation the host serves and the variables its document declares; the runner writes the
documents from it. The steps run in order:

| Step | What the runner does |
| --- | --- |
| `{ "client": "<op>", ...args }` | Sends the request. With `"expect"`, waits for its answer and compares; with `"as": "<name>"`, goes on and a later `await` compares; with neither, goes on, and the answer must still come before the transcript ends. |
| `{ "await": "<name>", "expect": { ... } }` | Waits for the answer sent under that name and compares. |
| `{ "request": "<operation>", "variables": { ... }, <answer> }` | Waits for the next HTTP request, which must be that operation with exactly those variables (when given), and answers it with `"answer": {...}` (or `null`, no instance), `"errors": [...]`, `"status": 503` (with `"body": {...}`, the JSON it answers with, or none), or `"drop": true` (the connection is closed with no answer). |
| `{ "subscribe": { ...variables } }` | Waits for the next subscription, which must be the `changed` document by its id with those variables and no text. |
| `{ "push": { ... } }`, `{ "pushErrors": [...] }` | Sends one event, or an event of errors, on the last subscription. |
| `{ "complete": true }`, `{ "dropSocket": true }` | Completes the last subscription, or closes its socket (with 1011, or with the code given: `{ "dropSocket": 4401 }`). |
| `{ "view": { ... } }` | Asks for the view until it holds every member given, for up to three seconds. |
| `{ "quiet": 50 }` | Waits that many milliseconds; nothing may arrive meanwhile. |
| `{ "wait": 50 }` | Waits that many milliseconds, so the client has taken every request sent before it; what arrives meanwhile waits for a later step. |

A comparison holds when every member the step gives equals the answer's, canonically; members it does not give are
not compared. After the last step the runner waits briefly, and anything that arrived that no step took fails the
transcript: a client sends nothing the transcript does not expect.
