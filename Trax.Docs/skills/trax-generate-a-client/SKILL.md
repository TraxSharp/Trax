---
name: trax-generate-a-client
description: Use when a frontend or a Node script calls a Trax API, or when working with trax client, the client manifest (--trax-export-client, RunTraxAsync, TraxClientExport), trax.documents.json, .graphql documents, generated .g.ts files, configureTrax, the generated machine hooks (useCheckout, useRememberedCheckout and the like), useServerMachine, createMachineExecutor, createFormView, @traxsharp/state-machine, or a .NET frontend on Trax.StateMachine.Client (MachinePredictor, ServerMachineController, trax machine generate --lang csharp). Covers exporting the manifest, generating and checking the client, writing the documents a train needs, driving a machine from React, TypeScript or .NET, forms from a machine, persisted ids, and why a socket works from Node but gets 403 in the browser.
metadata:
  internal: true
---

# Generate and use a Trax client

A frontend never writes a GraphQL request by hand. The host exports a **client manifest**, `trax client` turns it
into typed TypeScript (a .NET frontend uses `trax machine generate --lang csharp` instead, see
[A .NET frontend](#a-net-frontend)), and every request goes out by a persisted id the host registered. A host with persisted
operations required (the default) refuses a document sent as text, so a hand-written `fetch` with a `query` string
fails however right it looks.

## How the pieces fit

| Who | Writes | Read by |
|---|---|---|
| The host, started with `--trax-export-client <path>` | `manifest.json`: every machine's documents and IR, every exposed train's field, arguments and types | `trax client` |
| The frontend | `.graphql` documents, one named operation each, for the trains it calls | `trax client --documents` |
| `trax client` | `src/trax/*.g.ts`, each machine's twin and hooks, `trax.documents.json` | the frontend; the host (`ClientDocuments`) |

The host generates documents only for machines and run progress. **A frontend reaches a train only through a
document it wrote** (ADR 0054): there is no generated function for a train until a `.graphql` file asks for one.

## 1. Let the host export its manifest

The namespaces are easy to guess wrong: `RunTraxAsync` is in `Trax.Api.GraphQL.PersistedOperations.Extensions`,
`TraxClientExport` in `Trax.Effect.Configuration.ClientExport`.

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Configuration.ClientExport;

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted =>
        persisted.SingleNode().ClientDocuments("trax.documents.json")));

var app = builder.Build();

if (!TraxClientExport.Requested)
{
    // Migrate or seed your own database here: an export opens none.
}

app.UseTraxGraphQL();
await app.RunTraxAsync(args);
```

- `RunTraxAsync` serves as `RunAsync` does, unless the process was started with `--trax-export-client`; then it runs
  the startup checks, writes the manifest and exits. A host that calls `app.Run()` instead refuses to start when
  asked to export.
- `ClientDocuments(path)` is relative to the content root. The host checks every document at startup and refuses to
  start, naming it, when the file is missing or a document no longer fits the schema. The book copies the file
  into the build output and passes `Path.Combine(AppContext.BaseDirectory, "trax.documents.json")`.
- Export with the production configuration: a machine's IR, and so its ids, can depend on it.

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run --project Api -- --trax-export-client ../web/src/trax/manifest.json
```

The path is relative to the project folder `dotnet run` starts in.

## 2. Write the documents a train needs

One named `query` or `mutation` per `.graphql` file, rooted at `discover` (query trains, query models) or
`dispatch` (mutation trains), with the train's namespace if it has one. Select only what the component needs. A
filter or sort is written in the document, only its values are variables; no directives. The rules and examples are
in [Fetch only what a component needs](https://traxsharp.net/docs/api/fetch-only-what-a-component-needs).

## 3. Generate, commit, check

```bash
dotnet tool run trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql'
```

- Install the tool as a local tool so CI runs the pinned version: `dotnet new tool-manifest` once at the repository
  root, then `dotnet tool install Trax.Cli --version <the Trax version>`, and commit the `dotnet-tools.json` it
  writes. A fresh checkout runs `dotnet tool restore` first. It needs `node` 22 or later. The app installs
  `@traxsharp/state-machine` and `graphql-ws`, at the version of its `Trax.*` packages.
- Commit everything under `--out`, the manifest included. Never edit a `.g.ts` file.
- In CI, export the manifest again and run the same command with `--check`; it fails naming each stale file.
- After any change to a train, a query model, a machine or a document: export, generate, then build the host again
  so it picks up the new `trax.documents.json`. **Deploy the host before the frontend.**
- `trax client` writes each machine's twin itself (`<id>/<id>.machine.g.ts`). `trax machine generate` is for the IR
  and corpus on the host, and for the C# view of a .NET frontend; do not point its `--twin-out` into the same folder.

Every option and every file it writes: [trax client](https://traxsharp.net/docs/reference/cli#frontend-clients-trax-client).

## 4. Connect once, then call

Call `configureTrax` before any hook or function runs, and again when another user signs in. A credential goes in
both `headers` (HTTP) and `connectionParams` (the socket: a browser's WebSocket carries no custom headers). `user` is
the signed-in user's id; the remembered machines are kept under it.

```ts from=Trax.Samples/book/Shopfront/web/src/connection.ts
export async function signIn(user: string): Promise<void> {
// ...
  configureTrax({
    url: `${HUB}/trax/graphql`,
    socketUrl: `${HUB.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${token}` }),
    connectionParams: () => ({ authToken: token }),
    user,
  });
}
```

`socketUrl` is an absolute `ws://` or `wss://` URL. An absolute `http:` `url` to a host that is not loopback is
refused by every train function and machine server, since the headers would travel in cleartext, and so is a `ws:`
`socketUrl` to such a host unless it is the origin of a page served over `http:`: use `https:` and `wss:`, or a
relative url, or set `allowInsecureTransport: true` on a network you trust. Each document is then a function named after its operation,
exported from `src/trax/index.g`, taking the operation's variables and answering exactly the fields it selected
(`placeOrder(input)` for `mutation PlaceOrder($input: PlaceOrderInput!)`). Its doc comment says what a caller needs.

## 5. Drive a machine

From React, use the generated hooks: `use<Machine>({ id })` opens one instance or starts one, and
`useRemembered<Machine>()` keeps the signed-in user's instance across visits (it throws when `configureTrax` got no
`user`).

```tsx
const checkout = useRememberedCheckout();
const { status, error, state, context, pending, problem, send, can, forget } = checkout;

<button disabled={!can("SetAddress", { address })} onClick={() => send("SetAddress", { address })}>Continue</button>
```

- **The server decides; the twin predicts.** `send` shows the twin's prediction at once, the server applies the
  event and its answer replaces it. A refusal rolls back and sets `problem` (`GUARD_FAILED` carries the machine's
  `Because` text). `send` never throws.
- **`can` is a hint.** A custom rule (`new Rule.Custom(...)` on the server) has no handler in the twin and counts as
  passing, so decide such a button from the context or let the server refuse.
- **`error` means stopped for good:** `GONE`, `CLIENT_OUTDATED` (the page is older than the host: reload), or
  `CONNECTION_CHANGED` after `configureTrax` was called again.
- **A list or nested record in the context is typed loosely** (`unknown`): name its items with the input type from
  `types.g.ts`, as the book's `linesOf` does.
- Outside React (a Node script, a test), `createMachineExecutor(endpoint, { machines: [{ machine, documents }] })`
  from `@traxsharp/state-machine/client` sends the same operations by id, with the twin and the generated
  `<machine>Documents` array from `<id>.client.g.ts`.

A wizard whose form owns the data asks the twin instead of a server: `createFormView(twin, { steps, toContext })`
from `@traxsharp/state-machine`, then `can`, `advance`, `serialize` and `resume`
([Drive a form from a machine](https://traxsharp.net/docs/statemachine/drive-a-form-from-a-machine)). A snapshot
stored or compared is the [canonical wire](https://traxsharp.net/docs/statemachine/canonical-wire) that `serialize`
writes; never build one by hand.

## A .NET frontend

A .NET app (a desktop or mobile client, a worker) drives a served machine with `Trax.StateMachine.Client`, at the
version of the host's `Trax.*` packages. It needs the same manifest, and no reference to the host's assembly.

```bash
trax machine generate --manifest ./manifest.json --machine <id> --twin-out ./Machines --lang csharp --namespace MyApp.Machines
```

- One run per machine: `--machine` takes the machine's id with `--manifest`, and is required once the manifest
  serves two or more. It writes `<Machine>.g.cs`: the states and triggers as enums, a record per context and input,
  the IR with its hash, and `<Machine>Client`, a typed client over `ServerMachineController`. Commit it and run
  `trax machine check` with the same arguments in CI.
- The controller predicts with `MachinePredictor.FromIr(machine)`, the machine built from the IR
  ([`IrMachine`](https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-machine)). The same rule as the twin
  holds: the server decides. It has no handler for a custom guard, custom state requirement or custom reduction,
  so an event whose prediction depends on one is sent unpredicted and the view shows the server's answer.
  `MachinePredictor.For(machine)` predicts with the C# handlers, for an app that compiles the host's machine.
- A refusal or a failed request is a `SendOutcome`, never an exception. `Changed` is raised on the controller's own
  scheduler: marshal to the UI thread there.

Every type, option and behaviour: [.NET client](https://traxsharp.net/docs/sdk-reference/statemachine-api/dotnet-client),
and the C# view in [the codegen pipeline](https://traxsharp.net/docs/statemachine/codegen-pipeline#other-languages).

## Known mistakes

- **Copying a `documents.json` import from an older page.** The documents of a machine are the generated
  `<machine>Documents` export (or the generated hooks, which pass them). Nothing writes a `<machine>.documents.json`.
- **A socket that works from Node and gets `403` in the browser.** A browser sends `Origin` on the upgrade, and the
  host accepts only its own host or the origins it allows (its CORS default policy's `WithOrigins`, or
  `AllowSocketOrigins`). Node sends no `Origin`, so a Node test proves nothing about the page. Behind Vite's
  `server.proxy`, `changeOrigin: true` rewrites `Host` and every upgrade is refused: leave `changeOrigin` off for
  the GraphQL path. If the frontend really is on another origin, the host's allowlist names it; that is a host
  change, so ask the user ([Subscriptions](https://traxsharp.net/docs/sdk-reference/graphql-api/subscriptions)).
- **A text query because the id is unknown.** Write the `.graphql` document and regenerate. Never ask for persisted
  operations, introspection or masking to be turned off; the generated `schema.graphql` and `graphql.config.yml`
  give the editor what introspection would.
- **A frontend deployed first.** Its new ids are not registered yet. An old page keeps working through a deploy
  for the grace period, then gets `CLIENT_OUTDATED`.

## Next

- The machine itself: `trax-design-a-machine`. The host's auth, CORS and persisted operations:
  `trax-expose-over-api`.
- [Drive a machine instance from React](https://traxsharp.net/docs/statemachine/drive-a-machine-instance-from-react),
  [Call a machine from TypeScript](https://traxsharp.net/docs/statemachine/call-a-machine-from-typescript),
  [Client manifest](https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest),
  [TypeScript package](https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package),
  [.NET client](https://traxsharp.net/docs/sdk-reference/statemachine-api/dotnet-client), and the
  book's [A generated client](https://traxsharp.net/docs/book/a-generated-client).
