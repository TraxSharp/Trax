# The operations conformance kit and its testee protocol

`trax client --operations` writes a typed client for a host's persisted operations in TypeScript (over `graphql-ws`
and `fetch`) and in C# (over `Trax.Client`). This kit holds both to one transport and error behaviour: each transcript
in `transcripts/` scripts a host, request by request and frame by frame, and says what the client's caller must see
for each document it sends or follows. The client kit beside it (`../client/PROTOCOL.md`) does the same for the
machine clients; this kit plays its transcripts on that kit's fake host, `../client/host.mjs`, a real GraphQL host on a
local port (HTTP POST and a graphql-transport-ws socket), so each client's own HTTP stack, socket and JSON are what is
tested.

A generated client joins by implementing a **testee**: a program that drives the client generated for `fixture/`,
through the functions it generates for each document, as the runner tells it.

| Client | Generated into | Testee | Run by |
| --- | --- | --- | --- |
| TypeScript | `ts/tools/conformance/operations/client/` | `ts/tools/conformance/operations-testee-ts.mjs` | `ts/tools/conformance/operations.test.mjs` (vitest) |
| C# | `Trax.Cli/tests/Trax.Cli.Tests/IntegrationTests/OperationsConformance/Generated/` | `OperationsTestee` beside it | `OperationsConformanceTests` (Trax.Cli) |

## Running it

From `Trax.StateMachine/`:

```bash
node conformance/operations/run.mjs --testee node ts/tools/conformance/operations-testee-ts.mjs   # spawn a testee
node conformance/operations/run.mjs --stdio                                                      # be spawned by one
```

The flags, the exit codes and `--only <prefix>` are the client kit's. The TypeScript testee bundles the generated
client with esbuild, so it needs `npm ci --ignore-scripts` in `ts/` first, and exits 3 without it.

## The fixture

`fixture/operations.graphql` is an operations schema rooted as a host that serves the console roots one, and
`fixture/documents/` holds a query (`Runs`, an enum variable), a mutation (`Retry`, an input with a defaulted enum and a
nullable field) and a subscription (`Changes`, an operator stream). `fixture/console-documents.json` is the file a host
registers them from; the fake host serves each under the id a host computes for it, `trax_console_<name>_<hash><check>`,
and fails a request for any other id or one that carries a document's text.

Both clients and `console-documents.json` are generated, never edited. `OperationsConformanceTests` in Trax.Cli fails
when any of them is not what the CLI generates today. To regenerate, from `fixture/`:

```bash
trax client --operations operations.graphql --documents "documents/*.graphql" \
  --out ../../../ts/tools/conformance/operations/client
trax client --operations operations.graphql --documents "documents/*.graphql" --lang csharp \
  --namespace Trax.Cli.Tests.IntegrationTests.OperationsConformance.Generated \
  --out ../../../../Trax.Cli/tests/Trax.Cli.Tests/IntegrationTests/OperationsConformance/Generated
cp ../../../ts/tools/conformance/operations/client/console-documents.json .
```

(`trax` is `dotnet run --project Trax.Cli/src/Trax.Cli --` from a checkout.)

## The protocol

One JSON object per line, UTF-8, both ways, ended by `\n` only; every request has an `id` and an `op`, and its answer
carries the same `id`. Answers come when they settle, not in order. A testee answers an `op` it does not implement with
`{ "id", "unsupported": true }`, and anything that went wrong outside the client with `{ "id", "error": "..." }`; either
fails the transcript.

**`open`** builds a client and holds it until the next `open`, `dispose` or `end`. It sends nothing.

```json
{ "id": 1, "op": "open", "endpoint": "http://127.0.0.1:41234/graphql", "socket": "ws://127.0.0.1:41234/graphql" }
{ "id": 1, "opened": true }
{ "id": 1, "opened": false, "code": "INSECURE_TRANSPORT" }
```

`maxResponseSize` (bytes), `requestTimeout` (milliseconds) and `allowInsecureTransport` are given only when a
transcript sets them, and go to the client's options for them (TypeScript's `maxResponseBytes`, `requestTimeout` and
`allowInsecureTransport` on `httpTransport`; C#'s `MaxResponseSize`, the `HttpClient`'s `Timeout` and
`AllowInsecureTransport`). A client that refuses to be built for its endpoint or socket answers `opened: false` with
`INSECURE_TRANSPORT`, having sent nothing. The C# testee builds its `HttpClient` on a handler that follows no redirect,
as `TraxHttpTransport` requires, with no base address.

**`send`** sends one query or mutation, `operation` naming the document and `variables` its variables as JSON. A
testee reads them into the generated variables type (the C# records, with their enums and left-out fields), and
answers with what the caller saw:

```json
{ "id": 2, "outcome": "data", "data": { "operations": { ... } } }
{ "id": 2, "outcome": "refused", "code": "AUTH_NOT_AUTHORIZED" }
{ "id": 2, "outcome": "failed", "code": "TRANSPORT_ERROR", "status": 503 }
```

`data` is the result as the generated type writes it back to JSON. `refused` is the client's typed operation error
(TypeScript's `TraxOperationError`, C#'s answer with `Errors`), with the first error's `extensions.code`, or null.
`failed` is a request that got no GraphQL answer (TypeScript's `TraxTransportError`, C#'s `Failure`), with the HTTP
status when a response arrived. A TypeScript client that throws anything else is answered `{ "outcome": "threw" }`,
which no transcript expects.

**`subscribe`** follows `operation` with `variables`, replacing any stream the testee follows; it is answered
`{ "subscribed": true }` at once. **`events`** answers `{ "events": [...] }`, what the stream delivered so far:

| Event | TypeScript | C# |
| --- | --- | --- |
| `{ "next": data }` | `sink.next` | `TraxStreamEvent.Next` |
| `{ "ended": "complete" }` | `sink.complete` | `Ended` with no errors and no failure |
| `{ "ended": "refused", "code": ... }` | `sink.error` with a `TraxOperationError` | `Ended` with `Errors` |
| `{ "ended": "closed", "close": 4401 }` | `sink.error` with the socket's close event | `Ended` whose `Failure.CloseStatus` is set |
| `{ "ended": "failed" }` | `sink.error` with anything else | `Ended` with any other `Failure` |
| `{ "interrupted": true }`, `{ "resumed": true }` | | `Interrupted`, `Resumed` |

**`unsubscribe`** stops the stream. **`dispose`** closes the client, answered `{ "disposed": true }`. **`end`** is the
last request; the testee answers `{ "id" }` and exits.

## Transcripts

```json
{
  "about": "What the transcript shows.",
  "refuseConnections": 1,
  "open": { "maxResponseSize": 4096 },
  "opened": { "opened": false, "code": "INSECURE_TRANSPORT" },
  "steps": [ ... ]
}
```

`open` adds members to the `open` request, overriding the runner's `endpoint` and `socket`. `opened` compares the
answer to `open`; when it is not `opened: true` the transcript ends there, and nothing may have reached the host.
`refuseConnections` is how many socket connections the host closes with 4403 before acknowledging them.

| Step | What the runner does |
| --- | --- |
| `{ "client": "<op>", ...args }` | Sends the request; with `"expect"` waits for its answer and compares, with `"as": "<name>"` goes on and a later `await` compares. |
| `{ "await": "<name>", "expect": { ... } }` | Waits for the answer sent under that name and compares. |
| `{ "request": "<document>", "variables": { ... }, <answer> }` | Waits for the next HTTP request, which must be that document by its registered id, with no text and exactly those variables, and answers it: `"data"` and `"errors"` as a GraphQL answer, `"status"` with `"body"` (or none), `"raw"` (that text, as a 200 unless `"status"` says otherwise), `"redirect": true` (a 307 to a second host that counts what reaches it), or `"hang": true` (no answer ever comes). `"pad": n` adds n bytes of whitespace after a GraphQL answer. |
| `{ "subscribe": "<document>", "variables": { ... } }` | Waits for the next subscription, which must be that document by its id, with no text and those variables. |
| `{ "push": data }`, `{ "pushErrors": [...] }` | Sends one frame of data, or one of errors, on the last subscription. |
| `{ "streamError": [...] }` | Ends the last subscription with a graphql-transport-ws `error` message carrying those errors. |
| `{ "complete": true }`, `{ "dropSocket": 4401 }` | Completes the last subscription, or closes its socket with that code. |
| `{ "events": [ ... ] }` | Asks for the events until they are exactly those, for up to three seconds. |
| `{ "decoy": 0 }` | Checks how many requests reached the host a redirect points at. |
| `{ "quiet": 50 }`, `{ "wait": 50 }` | Waits that many milliseconds; with `quiet` nothing may arrive meanwhile. |

An `expect` holds when every member it gives equals the answer's, canonically. After the last step the runner waits
briefly, and anything that arrived that no step took fails the transcript.

## Where the clients differ, and why it is allowed

- A cleartext or scheme-relative endpoint is refused by both, for different reasons: TypeScript refuses
  `//host/path` because it would go over the page's scheme; C# has no page, and refuses an endpoint it cannot resolve
  without a base address (with one, the base's scheme decides, and an `http` one is refused as cleartext).
- A redirect is a failure in both, but only C# knows its status: `fetch` with `redirect: "error"` rejects before a
  response exists. The transcript does not compare it.
- A stream the socket drops for any reason but 4401 or 4403 is reconnected by `graphql-ws` silently in TypeScript, and
  resubscribed by `TraxClient` with `Interrupted` and `Resumed` in C#. No transcript drops a socket that way.
- The TypeScript machine client's `sendDocument` train functions (`trax client` without `--operations`) are not in the
  kit: they need a host's client manifest and the machine twins to generate.
