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
The runner writes its report on stderr and exits 0 when every transcript passed, was skipped or failed as the testee's
expected failures list it, 1 when any other failed, the list did not hold or the testee's answer to `end` was an
`error`, and 2 on a usage error, a `--only <prefix>` that selects no transcript, or anything else it could not run.
`--only <prefix>` runs the transcripts whose name starts with the prefix, and `--name <testee>` names the testee for
its [expected failures](#expected-failures). The report ends `N of N transcripts passed, s skipped, k expected
failures.`, where N leaves out the transcripts skipped and the ones the list excused. The TypeScript
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
{ "id": 1, "opened": true, "client": "ts" }
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
  failed read, and the delays before subscribing again after a stream that ended without saying why. A request
  without them leaves the client's own defaults in force.
- `maxResponseSize` (bytes), `requestTimeout` (milliseconds) and `allowInsecureTransport` are given only when a
  transcript sets them, and go to the client's own options of those names: the largest answer or socket message it
  reads, how long it waits for an answer, and whether it may talk cleartext to a host that is not this machine.
- `connectTimeout` and `idleTimeout` (milliseconds) are given only when a transcript sets them, and go to the
  socket's options: how long a socket may take to connect, and to stay silent (C#'s `ConnectTimeout` and
  `KeepAliveTimeout`, Rust's `connect_timeout` and `idle_timeout`). A client whose page builds its socket lacks them
  (`socket-timeouts`).
- `throwingRequestId: n`, when given, has the testee build the client with a request id minter whose call numbered
  `n` (from 1) throws (panics, in Rust) and mints nothing; the ids it does mint still count `r1`, `r2`, ….
  `throwingListener: true` has it subscribe a change listener that throws on every change; a client with no listener
  to call lacks it (`change-listener`).
- Every answer to `open` names its client in `client`: `ts`, `dotnet` or `rust`, or the name a new client is given
  in [the allowlist](#what-a-client-may-lack).
- A client that refuses to be built because its endpoint or socket is cleartext to a host that is not this machine
  answers `{ "id": 1, "opened": false, "client": "ts", "code": "INSECURE_TRANSPORT" }`, having sent nothing.
- `opened` may carry `lacks`, the capabilities named in transcripts' `needs` that this client cannot have. A
  transcript that needs one is skipped, and reported as skipped. A client may lack only what
  [the allowlist](#what-a-client-may-lack) grants it: an answer that names no client, or lacks anything else, fails
  the transcript it opened, so a client cannot skip a transcript it fails by saying it cannot run it.
- **Request ids.** The client mints them `r1`, `r2`, … in the order it needs them, counting from each `open`. The
  host compares them, so they must be the ids the client actually sends.

**`start`** opens the client: with an `instance`, subscribes to its change stream and reads it; without one, starts an
instance under a fresh request id and subscribes to the id the host answers with. It is answered `{ "settled": true }`
once the start or the first read has settled, whatever its result.

**`send`** sends one event, after every earlier one has settled. `trigger` is the trigger's name; `input` is its
input, or absent. The client sends its prediction as `clientResult`, except for an event bound to the machine's
effect or one with an edge out of the state it fires from that rests on a custom rule it holds no handler for (a custom
guard, a custom reduction, or a custom requirement of the state it enters): it sends those with none, predicts nothing
after them until they answer, and sends one even when its own engine would refuse it. A trigger the machine does
not declare as an event is refused where it is sent, with the code its engine's `advance` gives (`NO_TRANSITION` for
one it lacks, `OUTCOME_BOUND` for an outcome, `TIMER_BOUND` for a timer), and never reaches the host, whatever its name: an event is never sent as the `start`, `get`, `discard` or `changed` operation.
A `CLIENT_DIVERGENCE` answer starts a read of the instance, and the queue holds behind it: the next queued event is
sent only once that read has settled, and is predicted from what the read says.
A send before `start` waits for it; once a start or a first read has ended without an instance (refused, or past
every retry), every send waiting and every later one settles `failed` as `NOT_READY`. **`retry`** resends what last
failed under the same request id: a start that failed, or, for an instance opened by id, a first read that failed
past every retry, which it reads again and settles `confirmed` with what the read says. It resends nothing, and answers `NOTHING_TO_RETRY`, once a later event has been
confirmed or while one is queued or in flight: the failed event would reach the host after it. Both are answered with
what the send came to:

```json
{ "id": 4, "outcome": "confirmed", "state": "Unlocked", "revision": 4 }
{ "id": 4, "outcome": "refused", "code": "GUARD_FAILED" }
{ "id": 4, "outcome": "failed", "code": "TRANSPORT_ERROR" }
{ "id": 4, "outcome": "stopped", "code": "CLIENT_OUTDATED" }
```

**`sync`** brings the host to where a page that ran ahead of it is: the state `target`, with the fields event
`fields` (`trigger` and `input`, or null) applied, moving through the triggers in `navigation`. It plans with the
engine's planner (`../PROTOCOL.md`, `plan`) from the snapshot the host last confirmed, sends the first event of the
plan as `send` does, and plans again from each answer until the plan is empty. A refusal (`GUARD_FAILED`,
`NO_TRANSITION`, `CLIENT_DIVERGENCE`, `CONFLICT`) reads the instance once and plans again (a `CLIENT_DIVERGENCE`
starts that read itself); a second refusal in the same sync settles it `refused`. A sync plans only when nothing is
queued, so an event sent meanwhile goes out first. An `undetermined` plan sends its `events`, then its `trigger` unpredicted.
One such plan the planner cannot get past: the fields event resting on a custom rule at the target, which makes every
plan from the target `undetermined` on the fields trigger with no `events`. So when the host has confirmed the fields
event this sync sent, and the next plan, from a snapshot in `target` with nothing confirmed since (the revision held is
the one that answer carried), is that one, the sync settles `synced`: the host applied the fields at the target. A sync started while another
runs supersedes it: the older settles `superseded` once the event it has in flight answers, and sends nothing more;
one still waiting when the client stops or is disposed settles `superseded` too. No sync sends more than twice the
machine's states in events, the unpredicted ones counted; past that it settles `exceeded`.

```json
{ "id": 6, "op": "sync", "target": "Review", "fields": { "trigger": "Update", "input": { ... } }, "navigation": ["Begin", "Next", "Back"] }
{ "id": 6, "outcome": "synced", "state": "Review", "revision": 4 }
{ "id": 6, "outcome": "superseded" }
{ "id": 6, "outcome": "unreachable" }
```

and `refused`, `failed` and `stopped` with their `code`, as for `send`, and `exceeded` and `invalid` (with the
`trigger` or `target` it cannot plan with, or the `code` of an input or context nested too deep). `tooDeep: "input"`, when
given, has the testee build 600 arrays nested one in another, in memory, and sync with them as the fields input, as the
engine kit's `plan` does (`../PROTOCOL.md`): no reader need take a text nested that deep.

**`view`** answers with what the client shows now:

```json
{ "id": 5, "status": "ready", "instance": "i-1", "state": "Unlocked", "context": { "paidWith": "quarter" },
  "confirmed": { "state": "Locked", "context": {} }, "rowId": null, "revision": 3, "dueAt": null,
  "pending": true, "problem": null, "error": null, "deleted": null, "unsaved": false }
```

`status` is `loading`, `ready` or `stopped`. `dueAt` is the due time as an ISO-8601 instant with its offset, in
whatever offset and precision the client holds it; the runner compares it as the instant it names, so
`2026-10-22T12:10:00.000+02:00` and `2026-10-22T10:10:00Z` are one value, and one read without its offset is not. `state` and `context` are what to show (the prediction while one is
pending), `confirmed` what the host last confirmed. `problem` is the code of the last refusal or transport failure,
`error` the code that stopped the client (`GONE`, `CLIENT_OUTDATED`, or `UNAUTHENTICATED` and `FORBIDDEN` for a socket
or a stream the host refused), and `unsaved` whether a sync is running or the last one
did not settle `synced` (a page holds a leave guard on it), and `deleted` whether a `GONE` came from a
deletion (true) or a host that holds no such instance for the caller (false). A context holds no member whose value
is null: a typed answer cannot tell a member stored as null from one the state does not hold.

**`can`** asks whether the client allows `trigger` (with `input`, or absent) from what it shows now, the prediction
while one is pending: TypeScript's `can`, C#'s `CanSend`, Rust's `can_send`. **`available`** asks for the triggers it
allows from there, which the testee sorts by code unit. Both are answered at once, from the view:

```json
{ "id": 7, "op": "can", "trigger": "Coin", "input": { "coin": "quarter" } }
{ "id": 7, "can": true }
{ "id": 8, "op": "available" }
{ "id": 8, "triggers": ["Coin"] }
```

Before the first snapshot and once stopped, `can` is false and `available` empty. Otherwise `available` lists every
trigger with an edge out of the shown state, guarded or not, and `can` reads the guard of the edge with the input
given. An edge that rests on a custom rule the client holds no handler for is the host's to judge, so `can` says only
whether it leaves the shown state. An event bound to the machine's effect takes an input only the host's effect has,
so `can` is asked without one and does not read the guard's rules on the input. (TypeScript also reads that guard's
rules on the context; C# and Rust judge its edge alone. No transcript asks `can` of an effect trigger whose guard's
context rules fail, until the three agree.)

**`discard`** deletes the instance the client holds, under the next request id. No client discards through its
controller: each discards through the server it built the controller over (TypeScript's `createMachineServer` answer,
C#'s `GraphQLMachineServer.DiscardAsync`, Rust's `GraphQLMachineServer::discard`), which is what the testee calls. It
is answered with what the discard came to, and leaves the view alone: a discarded instance stops the client when the
stream says it is gone.

```json
{ "id": 9, "outcome": "discarded" }
{ "id": 9, "outcome": "refused", "code": "DISCARD_NOT_ALLOWED" }
{ "id": 9, "outcome": "failed", "code": "TRANSPORT_ERROR" }
```

An answer that neither says the instance was discarded nor gives a problem does not say it is gone: it is
`TRANSPORT_ERROR`, as a failed request is.

The kit drives no separate effect send. Each client sends the event bound to the effect as any other, through `send`;
only TypeScript has a second path, the snapshot transport's `send` (`sendSnapshot`) a draft session uses, which is not
the controller's and has no C# or Rust counterpart.

**`dispose`** closes the client: its stream, and every request it would still send. It is answered `{ "disposed":
true }`.

**`build`** builds a client from the last `open` with the members given over it (`requestTimeout`,
`maxResponseSize`, `retryDelays`, `resubscribeDelays`, `connectTimeout`, `idleTimeout`), and drops it unstarted. It
sends nothing and leaves the open client alone. It is answered `{ "built": true }`, or `{ "built": false, "code":
"INVALID_OPTION" }` when the client refused an option ([Defaults](#defaults)), or `"INSECURE_TRANSPORT"` as for
`open`.

**`headers`** has the headers each HTTP request asks for throw, from now on, with `"throws": true`, and stop
throwing with `false`: TypeScript's `headers`, Rust's `headers` (a provider that panics), and for C#, which takes its
headers on the `HttpClient`, a delegating handler before the client's that throws before it sends. It is answered
`{ "throws": <bool> }`. **`end`** is the last request; the testee answers `{ "id" }` and exits.

## What a client may lack

A client may lack a capability only where this block grants it, by capability and client, with the reason; the runner
reads it from here. A reason names what the client's platform does not give it: a capability the client could have
and does not yet is a bug in the client, not an entry here.

```json lacks
[
  {
    "capability": "upgrade-status",
    "client": "ts",
    "reason": "A WebSocket in a browser or in node does not say what status a failed upgrade was answered with."
  },
  {
    "capability": "socket-message-limit",
    "client": "ts",
    "reason": "The page builds the graphql-ws client given as socket and its WebSocket, which read every message whole, so the client never sees a message before it is read and cannot cap its size."
  }
]
```

## Expected failures

A client in a new language joins the kit before it passes all of it, as an engine does (`../PROTOCOL.md`, "Expected
failures"). The transcripts it does not pass yet are listed in `expected-failures/<testee>.json` here, read when the
runner is run with `--name <testee>`, the name its answer to `open` gives as `client`; an answer that names another
client fails the transcript, so a testee cannot run under another client's list. `ts`, `dotnet` and `rust` each list
what they fail today.

```json
[{ "prefix": "sync-", "reason": "syncTo is not built yet", "message": "the testee's answer to sync: unsupported" }]
```

An entry names a transcript by its file name without `.json` (`case`), or every transcript whose name starts with
`prefix`, says why in `reason`, and may give `message`, the start of the text the runner fails the transcript with. The
rules are the engine kit's: a failing transcript no entry names, a listed one that passes or that the client skips, one
that fails with another message, an entry that names no transcript, and a transcript two entries name each fail the run.

An expected failure is not a lack. A lack is granted above, for what a client's platform cannot give it, and lasts as
long as the platform does; an expected failure is what a client has not built yet, and its entry goes when it is built.

## Defaults

A client built without these options has these values, in every language. A testee sets only the options a transcript
gives, so a transcript that gives none sees the defaults: `response-size-default` holds the response size. The runner
gives every transcript short retry and resubscribe delays unless it is marked `"defaults": true`, so
`retry-delays-default` and `resubscribe-delays-default` hold the first of each. The rest are too long for a
transcript to wait out, and each client's unit tests hold them by what the client does
(TypeScript's `machineServer.test.ts`, C#'s `DefaultsParityTests`, Rust's `tests/defaults.rs`). The values are written
down once, in `defaults.json` beside this file: each client's tests hold its constants to it, and TypeScript's
`defaults.conformance.test.ts` holds this table to it. A change to one is a change there, here and in all three.

| Option | Default | What it bounds |
| --- | --- | --- |
| Request timeout | 30 s | One request, from sending it to the last byte of its answer. Past it the request fails as `TRANSPORT_ERROR`. C#'s also yields to a shorter `HttpClient.Timeout`. |
| Retry delays | 250, 500, 1000, 2000, 4000 ms | The waits before each retry of a `SERVER_OUTDATED` answer and of a read the transport failed: one retry per delay, then the event fails and `retry` resends it. An empty list retries nothing. |
| Resubscribe delays | 1000, 2000, 5000 ms | The steps of the waits before subscribing again after a stream ended without saying why. The last repeats for as long as the stream is wanted, and the schedule starts over once a stream has delivered its second push, since every stream's first is the instance as it is. An empty list is the default. Each wait is jittered, below. |
| Response size | 16 MiB | The largest answer body read; a larger one fails its request as `TRANSPORT_ERROR` and is not read past the limit. C# and Rust hold a socket message to the same limit. |

Each resubscribe wait is drawn uniformly between 50% and 100% of its step: `step × (1 − r / 2)`, with `r` uniform in
[0, 1), drawn afresh for every wait, so the clients a host restart or a refusal like `TOO_MANY_SUBSCRIPTIONS` dropped
together do not subscribe again in lockstep. Retry delays are not jittered. The random source is the language's
ordinary one and each client's unit tests replace it (TypeScript's spies on `Math.random`), so a draw of 0 waits the
whole step. The kit does not pin the random source: `resubscribe-backoff` bounds each wait by half its step, the
shortest a draw can make it, so it holds the order and growth of the waits whatever is drawn.

The socket's own timing is not pinned, because the TypeScript client does not own its socket: a page builds its
graphql-ws client and its options with it, and graphql-ws waits for an acknowledgement and stays silent for as long
as it is not told otherwise. The C# socket gives up a connection not acknowledged within 10 seconds and pings at the
WebSocket level every 15 seconds, closing one whose pong does not come within 15 more. The Rust one gives up a
connection after the same 10 seconds, and an acknowledged socket silent for 60, sending a graphql-transport-ws `ping`
halfway through the silence.

## What the host does

The host answers nothing by itself. A request for an id that is not one of the transcript's documents is answered
with a GraphQL error and fails the transcript; so does a subscription that sends a document's text beside its id.
It acknowledges a socket's `connection_init` unless the transcript refuses that connection, in which case it closes
the socket with 4403 before acknowledging it. A socket the host closes with 4401 or 4403, before it is acknowledged or
while subscribed, is the host turning the caller away, and the client stops for good: `UNAUTHENTICATED` for 4401,
`FORBIDDEN` for 4403, and the same for an upgrade answered 401 or 403 where the client's socket can tell one. It
subscribes again after any other close, graphql-transport-ws's 4408 (not initialised in time) and 4429 (too many
initialisation requests) among them. C# and Rust hold a socket message to the response size: one larger fails the
socket, unread past the limit, and the client subscribes again on a new one.

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
null beside a `problem`), and `rowId: null`, `gone: false`, `reload: false` on a push. A step with `"bare": true` sends its answer or push as
given, without them. An answer or push that leaves out `dueAtKnown` or `dueAt` does not know the due time: the
client reads it as `dueAtKnown: false`, never as a known null.

A context nests at most 64 levels, the snapshot that holds it counting as one: no store writes a snapshot deeper,
and every engine refuses to read one. One that deep is read, however deep the answer or socket message around it, so
a client's JSON reader takes the whole envelope; one deeper is not one a store wrote, and is malformed (below).

**Malformed answers and pushes.** An answer the client cannot take as one a host wrote fails its request
`TRANSPORT_ERROR` and changes nothing: one that is not an object, or whose `revision` is not a whole number from zero
(a fraction, a string, a negative), whose `dueAt` is not an instant, whose `state` is not a string or is missing
beside no problem, or whose context is not an object or nests too deep. A push like that, or one whose `gone` is not
a boolean, is dropped and the instance read again. Neither stops the client, and neither is taken unchecked.

## Transcripts

```json
{
  "about": "What the transcript shows.",
  "machine": "turnstile",
  "instance": "i-1",
  "documents": { "get": "$id: UUID!", "coin": "$id: UUID!, $input: CoinInput!, $requestId: String!, $clientResult: String", ... },
  "refuseConnections": 0,
  "refuseUpgrades": [401],
  "open": { "maxResponseSize": 4096 },
  "opened": { "opened": false, "code": "INSECURE_TRANSPORT" },
  "retryDelays": [10, 10],
  "resubscribeDelays": [10],
  "defaults": false,
  "steps": [ ... ]
}
```

`retryDelays` and `resubscribeDelays` default to `[10, 10]` and `[10]`, which keep a run fast. `"defaults": true` sends
neither, so the client's own [defaults](#defaults) are in force; such a transcript may not set them or `open`, and
keeps to the first few delays so the run stays fast. `refuseUpgrades` lists the HTTP statuses the first socket
upgrades are answered with instead of switching protocols, one each. `open` adds members to the `open` request, overriding the runner's (`endpoint` and `socket` among them).
`opened` compares the answer to `open`; when it is not `opened: true`, the transcript ends there.

`documents` names each operation the host serves and the variables its document declares; the runner writes the
documents from it. The steps run in order:

| Step | What the runner does |
| --- | --- |
| `{ "client": "<op>", ...args }` | Sends the request. With `"expect"`, waits for its answer and compares; with `"as": "<name>"`, goes on and a later `await` compares; with neither, goes on, and the answer must still come before the transcript ends. |
| `{ "await": "<name>", "expect": { ... } }` | Waits for the answer sent under that name and compares. |
| `{ "request": "<operation>", "variables": { ... }, <answer> }` | Waits for the next HTTP request, which must be that operation with exactly those variables (when given), and answers it with `"answer": {...}` (or `null`, no instance), `"errors": [...]`, `"status": 503` (with `"body": {...}`, the JSON it answers with, or none), `"drop": true` (the connection is closed with no answer), `"hang": true` (no answer ever comes), `"redirect": true` (a 307, or the status given, whose `Location` is a second host that counts what reaches it) or `"raw": "..."` (a 200 with that text as its body). `"pad": n` adds n bytes of whitespace after an answer's JSON, and `"padTo": n` pads the body to exactly n bytes; `"bare": true` sends the answer without the fields a host fills in. With `"hold": "<name>"` instead of an answer, it answers nothing yet. |
| `{ "respond": "<name>", <answer> }` | Answers the request held under that name, as a `request` step would. |
| `{ "subscribe": { ...variables } }` | Waits for the next subscription, which must be the `changed` document by its id with those variables and no text. `"notBefore": ms` and `"before": ms` bound how long after the host last ended a stream (`complete`, `streamError`, `dropSocket`) it came. |
| `{ "push": { ... } }`, `{ "pushErrors": [...] }` | Sends one event, or an event of errors, on the last subscription; `"pad": n` beside a `push` adds n bytes of whitespace to its frame, and `"bare": true` sends it without the fields a host fills in. |
| `{ "pushRaw": "..." }` | Sends that text as one frame on the last subscription's socket, JSON or not. A later `subscribe` step's bounds count from it. |
| `{ "clientClosed": 4400 }` | Waits for the client to close the last subscription's socket, and checks the code its close frame carried. |
| `{ "unsettled": "<name>" }` | Checks that the answer sent under that name has not come yet. It asks for the view first and checks once that answer is in, so an answer the testee wrote before it has been read; put it after a fence (below). |
| `{ "streamError": [...] }` | Ends the last subscription with a graphql-transport-ws `error` message carrying those errors. |
| `{ "ping": true }` | Sends a graphql-transport-ws `ping` on the last subscription's socket and waits for the client's `pong`. |
| `{ "decoy": 0 }` | Checks how many requests reached the second host a redirect points at. |
| `{ "complete": true }`, `{ "dropSocket": true }` | Completes the last subscription, or closes its socket (with 1011, or with the code given: `{ "dropSocket": 4401 }`). |
| `{ "view": { ... } }` | Asks for the view until it holds every member given, for up to three seconds. With `"once": true`, asks once: what must stay as it is is checked after a fence, not by waiting. |
| `{ "quiet": 50 }` | Waits that many milliseconds; nothing may arrive meanwhile. |
| `{ "wait": 50 }` | Waits that many milliseconds, so the client has taken every request sent before it; what arrives meanwhile waits for a later step. |

A comparison holds when every member the step gives equals the answer's, canonically; members it does not give are
not compared.

A step that checks something did not happen (a stale push not applied, a send not settled) does not wait for it to
happen late. It follows a **fence**: something the client sends only once it has taken what came before, such as the
read a `reload` push makes it send or the subscription it opens after a socket failed. The fence's request is held
(`hold`), the check made (`"once": true`, `unsettled`), and the request answered (`respond`). After the last step the runner waits briefly, and anything that arrived that no step took fails the
transcript: a client sends nothing the transcript does not expect.

## The client surface

The transcripts hold what the clients do; this section holds what they are called. Each row is one thing a page or an
app reaches for, with its name in each client: TypeScript (`@traxsharp/state-machine`, its `client` and `react`
entry points), C# (`Trax.StateMachine.Client`) and Rust (`trax-state-machine-client`, and the engine
`trax-state-machine` where the engine is what predicts). A test in each language reads these tables and fails when a
name a row gives for it is not on its public surface, or when a public member of a type the second table covers is in
no row: `ClientSurfaceTests` in `dotnet/tests/Trax.StateMachine.Tests.Meta`, `ts/src/clientSurface.test.ts` and
`rust/client/tests/client_surface.rs`. So a member added to one client fails until it has a row, and the row says
what the other two call it, or why they have none. Each test reads the column headed with its language's name, so
another client is another column and a test of its own, and the headers name the languages the testees in
`conformance/testees.json` are written in.

A cell is one name in backticks, `Type.member` for a member and a bare `Name` for an export of its own (a type, a
function, a constant), or `—` and the reason that language has none. A member of a type the first table does not cover
is named like any other, so it is checked too; a reason is never only a name, which would leave it unchecked. A type's
members are the ones a caller reaches on it, inherited ones included (C#'s and TypeScript's alike). A type is the name the language exports it
under, and a member is written as the language spells it, a Rust field or method alike with a dot. The reason is
for people; the tests only check it is there. Outcomes, problem codes and stream messages are not here: the
transcripts compare those by value.

| Type | TypeScript | C# | Rust |
| --- | --- | --- | --- |
| The controller | `ServerMachineController` | `ServerMachineController` | `ServerMachineController` |
| How it is built | `ServerMachineOptions` | `ServerMachineOptions` | `ServerMachineOptions` |
| What it shows | `ServerMachineView` | `MachineView` | `MachineView` |
| Why it stopped | `ServerMachineError` | `MachineError` | `MachineError` |
| A refusal or a transport failure | `Problem` | `MachineProblem` | `MachineProblem` |
| The seam to a host | `MachineServer` | `IMachineServer` | `MachineServer` |
| The host over GraphQL | `TypedMachineServer` | `GraphQLMachineServer` | `GraphQLMachineServer` |
| How it reaches the host | `MachineServerOptions` | `GraphQLMachineServerOptions` | `GraphQLMachineServerOptions` |
| What predicts | — the twin given as `machine` predicts, and the engine's surface is its own | `IMachinePredictor` | — the engine's `Machine`, built from `ir`, predicts, and the engine's surface is its own |

| Member | TypeScript | C# | Rust |
| --- | --- | --- | --- |
| Build a controller | `ServerMachineController` | `ServerMachineController` | `ServerMachineController.new` |
| Open the instance, or start one | `ServerMachineController.start` | `ServerMachineController.StartAsync` | `ServerMachineController.start` |
| Send an event | `ServerMachineController.send` | `ServerMachineController.SendAsync` | `ServerMachineController.send` |
| Resend what failed | `ServerMachineController.retry` | `ServerMachineController.RetryAsync` | `ServerMachineController.retry` |
| Bring the host to where the page is | `ServerMachineController.syncTo` | `ServerMachineController.SyncToAsync` | `ServerMachineController.sync_to` |
| Whether a trigger fires from what is shown | `ServerMachineController.can` | `ServerMachineController.CanSend` | `ServerMachineController.can_send` |
| The triggers that fire from what is shown | `ServerMachineController.available` | `ServerMachineController.AvailableTriggers` | `ServerMachineController.available_triggers` |
| The current view | `ServerMachineController.get` | `ServerMachineController.View` | `ServerMachineController.view` |
| Hear of every change of the view | `ServerMachineController.subscribe` | `ServerMachineController.Changed` | `ServerMachineController.watch` |
| Close the stream and drop late answers | `ServerMachineController.dispose` | `ServerMachineController.Dispose` | `ServerMachineController.dispose` |
| The default retry delays | `DEFAULT_RETRY_DELAYS` | `ServerMachineController.DefaultRetryDelays` | `DEFAULT_RETRY_DELAYS` |
| Options with every choice left to its default | — an object literal | — an object initializer | `ServerMachineOptions.new` |
| The machine | `ServerMachineOptions.machine` | `ServerMachineOptions.Machine` | `ServerMachineOptions.ir` |
| The IR's hash | — the generated twin given as `machine` carries it | — the manifest entry given as `Machine` carries it | `ServerMachineOptions.ir_hash` |
| What predicts | — the twin given as `machine` | `ServerMachineOptions.Predictor` | — the engine built from `ir`, which binds no custom handler |
| The seam to the host | `ServerMachineOptions.server` | `ServerMachineOptions.Server` | `ServerMachineOptions.server` |
| An instance to open | `ServerMachineOptions.id` | `ServerMachineOptions.Id` | `ServerMachineOptions.id` |
| The start's input | `ServerMachineOptions.startInput` | `ServerMachineOptions.StartInput` | `ServerMachineOptions.start_input` |
| The start's request id | `ServerMachineOptions.startRequestId` | `ServerMachineOptions.StartRequestId` | `ServerMachineOptions.start_request_id` |
| How request ids are minted | `ServerMachineOptions.newRequestId` | `ServerMachineOptions.NewRequestId` | `ServerMachineOptions.new_request_id` |
| Retry delays | `ServerMachineOptions.retryDelays` | `ServerMachineOptions.RetryDelays` | `ServerMachineOptions.retry_delays` |
| The runtime the controller runs on | — a page has one event loop | — continuations run on the thread pool | `ServerMachineOptions.runtime` |
| Status | `ServerMachineView.status` | `MachineView.Status` | `MachineView.status` |
| Instance id | `ServerMachineView.id` | `MachineView.Id` | `MachineView.id` |
| What to show | `ServerMachineView.snapshot` | `MachineView.Snapshot` | `MachineView.snapshot` |
| What the host last confirmed | `ServerMachineView.confirmed` | `MachineView.Confirmed` | `MachineView.confirmed` |
| Row incarnation | `ServerMachineView.rowId` | `MachineView.RowId` | `MachineView.row_id` |
| Revision | `ServerMachineView.revision` | `MachineView.Revision` | `MachineView.revision` |
| Timer due time | `ServerMachineView.dueAt` | `MachineView.DueAt` | `MachineView.due_at` |
| An event in flight or queued | `ServerMachineView.pending` | `MachineView.Pending` | `MachineView.pending` |
| The last refusal or transport failure | `ServerMachineView.problem` | `MachineView.Problem` | `MachineView.problem` |
| Why it stopped | `ServerMachineView.error` | `MachineView.Error` | `MachineView.error` |
| A sync running or unsettled, for a leave guard | `ServerMachineView.unsaved` | `MachineView.Unsaved` | `MachineView.unsaved` |
| Stop code | `ServerMachineError.code` | `MachineError.Code` | `MachineError.code` |
| Stop message | `ServerMachineError.message` | `MachineError.Message` | `MachineError.message` |
| Deleted, or not found | `ServerMachineError.deleted` | `MachineError.Deleted` | `MachineError.deleted` |
| Refusal or transport failure | `Problem.kind` | `MachineProblem.Kind` | `MachineProblem.kind` |
| Problem code | `Problem.code` | `MachineProblem.Code` | `MachineProblem.code` |
| Problem message | `Problem.message` | `MachineProblem.Message` | `MachineProblem.message` |
| Seam: start an instance | `MachineServer.start` | `IMachineServer.StartAsync` | `MachineServer.start` |
| Seam: send an event | `MachineServer.event` | `IMachineServer.EventAsync` | `MachineServer.event` |
| Seam: read the instance | `MachineServer.get` | `IMachineServer.GetAsync` | `MachineServer.get` |
| Seam: follow the instance | `MachineServer.subscribe` | `IMachineServer.Subscribe` | `MachineServer.subscribe` |
| Seam: a rejected request that ends the controller | `MachineServer.endedBy` | — a request that throws is a transport failure; terminal codes end it through the stream | — a request that fails is a transport failure; terminal codes end it through the stream |
| Build the host over GraphQL | `createMachineServer` | `GraphQLMachineServer` | `GraphQLMachineServer.new` |
| Start an instance | `TypedMachineServer.start` | `GraphQLMachineServer.StartAsync` | `GraphQLMachineServer.start` |
| Send an event | `TypedMachineServer.event` | `GraphQLMachineServer.EventAsync` | `GraphQLMachineServer.event` |
| Read the instance | `TypedMachineServer.get` | `GraphQLMachineServer.GetAsync` | `GraphQLMachineServer.get` |
| Follow the instance | `TypedMachineServer.subscribe` | `GraphQLMachineServer.Subscribe` | `GraphQLMachineServer.subscribe` |
| A rejected request that ends the controller | `TypedMachineServer.endedBy` | — terminal codes end it through the stream | — terminal codes end it through the stream |
| Discard the instance | `TypedMachineServer.discard` | `GraphQLMachineServer.DiscardAsync` | `GraphQLMachineServer.discard` |
| The default resubscribe delays | `DEFAULT_RESUBSCRIBE_DELAYS` | `GraphQLMachineServer.DefaultResubscribeDelays` | `DEFAULT_RESUBSCRIBE_DELAYS` |
| The default request timeout | `DEFAULT_REQUEST_TIMEOUT` | `GraphQLMachineServer.DefaultRequestTimeout` | `DEFAULT_REQUEST_TIMEOUT` |
| Leave the empty query out of a socket message | `persistedMessageReplacer` | — `GraphQLSubscriptionSocket` writes its own messages | — the server writes its own socket's messages |
| Options with every choice left to its default | — an object literal | — an object initializer | `GraphQLMachineServerOptions.new` |
| The machine | `MachineServerOptions.machine` | `GraphQLMachineServerOptions.Machine` | `GraphQLMachineServerOptions.machine` |
| Its persisted documents | `MachineServerOptions.documents` | — the manifest entry given as `Machine` carries them | `GraphQLMachineServerOptions.documents` |
| The endpoint | — the first argument of `createMachineServer` | `GraphQLMachineServerOptions.Endpoint` | `GraphQLMachineServerOptions.endpoint` |
| The socket | `MachineServerOptions.socket` | `GraphQLMachineServerOptions.Socket` | `GraphQLMachineServerOptions.socket` |
| The HTTP client | `MachineServerOptions.fetch` | `GraphQLMachineServerOptions.Http` | `GraphQLMachineServerOptions.http` |
| Say the HTTP client's handler follows no redirects | — every request is sent with `redirect: 'error'` | `GraphQLMachineServerOptions.HandlerFollowsNoRedirects` | — the server builds the builder given as `http` with `redirect(Policy::none())`, whatever it set |
| Headers on every request | `MachineServerOptions.headers` | — set on the `HttpClient` given as `Http` | `GraphQLMachineServerOptions.headers` |
| The socket's `connection_init` payload | — the page's graphql-ws client given as `socket` sends it | `GraphQLSubscriptionSocketOptions.ConnectionParams` | `GraphQLMachineServerOptions.connection_params` |
| allowInsecureTransport | `MachineServerOptions.allowInsecureTransport` | `GraphQLMachineServerOptions.AllowInsecureTransport` | `GraphQLMachineServerOptions.allow_insecure_transport` |
| maxResponseSize | `MachineServerOptions.maxResponseSize` | `GraphQLMachineServerOptions.MaxResponseSize` | `GraphQLMachineServerOptions.max_message_size` |
| requestTimeout | `MachineServerOptions.requestTimeout` | `GraphQLMachineServerOptions.RequestTimeout` | `GraphQLMachineServerOptions.request_timeout` |
| How long a socket may take to connect | — the page's graphql-ws client given as `socket` decides | `GraphQLSubscriptionSocketOptions.ConnectTimeout` | `GraphQLMachineServerOptions.connect_timeout` |
| How long a socket may stay silent | — the page's graphql-ws client given as `socket` decides | `GraphQLSubscriptionSocketOptions.KeepAliveTimeout` | `GraphQLMachineServerOptions.idle_timeout` |
| resubscribeDelays | `MachineServerOptions.resubscribeDelays` | `GraphQLMachineServerOptions.ResubscribeDelays` | `GraphQLMachineServerOptions.resubscribe_delays` |
| terminalStreamErrors | `MachineServerOptions.terminalStreamErrors` | `GraphQLMachineServerOptions.TerminalStreamErrors` | `GraphQLMachineServerOptions.terminal_stream_errors` |
| The runtime subscriptions run on | — a page has one event loop | — continuations run on the thread pool | `GraphQLMachineServerOptions.runtime` |
| A predictor from the IR | — the generated twin is the predictor | `MachinePredictor.FromIr` | `Machine.from_ir` |
| A predictor from a C# machine | — machines are declared in C# | `MachinePredictor.For` | — machines are declared in C# |
| The IR's hash | `TypedMachine.schemaHash` | `IMachinePredictor.IrHash` | `ir_hash` |
| Predict an event | `TypedMachine.advance` | `IMachinePredictor.Advance` | `Machine.advance` |
| Whether a trigger fires | `TypedMachine.can` | `IMachinePredictor.CanFire` | `Machine.can_fire` |
| The triggers that fire | `TypedMachine.available` | `IMachinePredictor.AvailableTriggers` | `Machine.available_triggers` |
| Plan a sync | `TypedMachine.plan` | `IMachinePredictor.Plan` | `Machine.plan` |
| Read a stored snapshot | `TypedMachine.rehydrate` | `IMachinePredictor.Rehydrate` | `Machine.rehydrate` |
| Write a snapshot's canonical wire | `TypedMachine.serialize` | `IMachinePredictor.Serialize` | `Machine.serialize` |
| Drive one instance from a React component | `useServerMachine` | — React hooks are TypeScript only; a .NET page builds the controller and listens to `Changed` | — React hooks are TypeScript only; an app builds the controller and reads `watch` |
| The same, remembering the instance across reloads | `useRememberedMachine` | — React hooks are TypeScript only | — React hooks are TypeScript only |
