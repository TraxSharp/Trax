---
title: "15. A generated client"
description: "Chapter 15 of the Trax book: write the storefront's GraphQL documents and generate a typed React client for them with trax client."
type: tutorial
area: general
parent: The Trax book
nav_order: 25
snippet_context: none
follows: book/checks-in-parallel-and-resuming
---

# 15. A generated client

Customers have been using Shopfront through `curl`. In this chapter they get a storefront: a React
page that lists the catalogue, places an order, follows its run and lists the customer's orders.
The page calls none of that by hand. It writes the GraphQL documents it needs, and Trax generates
a typed function for each, plus the client that sends them.

This chapter starts from the end of [chapter 14](/docs/book/checks-in-parallel-and-resuming). It
needs Node.js 22 or later, as `trax machine generate` already did.

## How it fits together

```text
web/src/graphql/*.graphql ──┐
                            ├──► trax client ──► web/src/trax/   (typed functions, hooks)
hub --trax-export-client ───┘                     └── trax.documents.json ──► the hub registers it
```

1. **The page writes its documents**, one operation per `.graphql` file, asking for exactly the
   fields it shows.
2. **The hub exports its client manifest**: every train, query model and machine it serves, with
   their types. The hub writes it and exits, without serving.
3. **`trax client` reads both** and writes the client: a function per document, and the documents
   themselves in `trax.documents.json`.
4. **The hub registers `trax.documents.json` when it starts.** Each document gets an id from a hash
   of its text, and the page sends only that id and its variables, never the text. That is what
   persisted operations are: the server runs the documents it registered, and in chapter 17 it
   runs nothing else.

A document that asks for a field the hub does not serve, or one a train refuses, stops the hub at
start, naming the document and its file. A mistake in the storefront is found when the hub starts,
not when a customer clicks.

## The storefront's place

The storefront is `web/`, beside the hub, a Vite and React project in TypeScript.

```text
Shopfront/
├── Shopfront.Core/
├── Shopfront.Hub/
├── Shopfront.Worker/
├── machines/
├── tests/
└── web/
    ├── package.json
    ├── tsconfig.json
    ├── vite.config.ts
    ├── index.html
    └── src/
        ├── graphql/        the documents the page writes
        ├── trax/           what trax client generates; committed, never edited
        ├── connection.ts
        ├── main.tsx
        ├── App.tsx
        ├── Catalogue.tsx
        ├── OrderProgress.tsx
        └── MyOrders.tsx
```

```json file=web/package.json
{
  "name": "shopfront-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build"
  },
  "dependencies": {
    "@traxsharp/state-machine": "1.67.0",
    "graphql": "^16.10.0",
    "graphql-ws": "^6.0.0",
    "react": "^19.3.0",
    "react-dom": "^19.3.0"
  },
  "devDependencies": {
    "@types/react": "^19.3.0",
    "@types/react-dom": "^19.3.0",
    "@vitejs/plugin-react": "^6.1.2",
    "typescript": "~5.9.3",
    "vite": "^8.3.3"
  }
}
```

`@traxsharp/state-machine` is the runtime the generated client is built on, released with the
NuGet packages at the same version. `graphql-ws` is the socket a subscription runs on.

```json file=web/tsconfig.json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "verbatimModuleSyntax": true,
    "resolveJsonModule": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "skipLibCheck": true
  },
  "include": ["src"]
}
```

```ts file=web/vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The hub allows requests from http://localhost:5173 only, so the dev server never moves to another port.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
});
```

```html file=web/index.html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Shopfront</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

```text file=web/.gitignore
node_modules
dist
```

## The documents

Each file holds one named operation. The roots are the ones the API already has: `discover` for
queries, `dispatch` for trains run as mutations.

```graphql file=web/src/graphql/Catalogue.graphql
query Catalogue {
  discover {
    products(order: { sku: ASC }) {
      nodes {
        sku
        name
        price
        stock
      }
    }
  }
}
```

```graphql file=web/src/graphql/PlaceOrder.graphql
mutation PlaceOrder($input: PlaceOrderInput!) {
  dispatch {
    placeOrder(input: $input) {
      externalId
      output {
        orderId
        total
      }
    }
  }
}
```

`externalId` is the run's id, which the page follows. `runProgress` is not written here: the hub
generates that document itself, for every train marked `[TraxRunProgress]`.

```graphql file=web/src/graphql/MyOrders.graphql
query MyOrders {
  discover {
    orders {
      nodes {
        id
        total
        placedAt
      }
    }
  }
}
```

A document asks for what its page shows and nothing more. Every rule from the earlier chapters
still applies to it: the query filter scopes `orders` to the caller, and `placeOrder` admits only
customers.

## The hub serves the storefront

Three things change in the hub. It registers the storefront's documents, it lets the storefront's
origin call it, and it can write its client manifest.

`Shopfront.Hub/Program.cs`:

{{csharp compile=app file=Shopfront.Hub/Program.cs}}

What changed:

- **`ClientDocuments(...)`** registers the documents in `trax.documents.json` at start. The file is
  read from beside the hub's build, where the project copies it.
- **`AddCors` and `UseCors()`.** The page is served from `http://localhost:5173` and calls the hub
  on `http://localhost:5080`, so the browser asks the hub first whether that origin may call it.
  The origin is configuration, so a deployed storefront names its own. The socket checks the same
  list: a WebSocket from an origin the policy does not name is refused.
- **`RunTraxAsync(args)`** in place of `RunAsync()`. Started with `--trax-export-client <path>`, the
  hub runs its startup checks, writes the manifest to the path and exits. Otherwise it serves, as
  before.
- **`TraxClientExport.Requested`** says whether this start is an export. An export opens no
  database, so the schema is not created during one.

`Shopfront.Hub/Shopfront.Hub.csproj`:

```xml file=Shopfront.Hub/Shopfront.Hub.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The host tests start the hub. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <!-- The storefront's documents, registered at start. trax client writes the file. -->
    <None
      Include="..\web\src\trax\trax.documents.json"
      Link="trax.documents.json"
      CopyToOutputDirectory="PreserveNewest"
      Condition="Exists('..\web\src\trax\trax.documents.json')"
    />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Api.Auth.Jwt" />
    <PackageReference Include="Trax.Api.Dashboard" />
    <PackageReference Include="Trax.Api.GraphQL" />
    <PackageReference Include="Trax.Api.GraphQL.PersistedOperations" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

`Shopfront.Hub/appsettings.json`:

```json file=Shopfront.Hub/appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider": "Debug"
    }
  },
  "Storefront": {
    "Origin": "http://localhost:5173"
  },
  "AllowedHosts": "*"
}
```

## Generate the client

Build the hub, and export its manifest into the storefront:

```bash
dotnet build
dotnet run --project Shopfront.Hub -- --trax-export-client ../web/src/trax/manifest.json
```

```text
info: Trax.Api.GraphQL.PersistedOperations.Extensions.TraxClientManifestExtensions[0]
      Wrote the client manifest to /home/you/Shopfront/web/src/trax/manifest.json.
```

The path is relative to the hub's project folder, where `dotnet run` starts it. Then, from `web/`,
install the packages and generate the client:

```bash
cd web
npm install
dotnet trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql'
```

```text
Generated 15 file(s) in /home/you/Shopfront/web/src/trax.
```

```text
web/src/trax/
├── index.g.ts            re-exports everything below
├── client.g.ts           configureTrax, TraxOperationError, the socket
├── documents/
│   ├── catalogue.g.ts
│   ├── myOrders.g.ts
│   └── placeOrder.g.ts
├── progress.g.ts         readRunProgress, followRunProgress, useRunProgress
├── checkout/             the checkout machine's client, for the next chapter
├── types.g.ts            the input types and enums
├── trax.documents.json   the documents, which the hub registers
├── manifest.json         the manifest it was generated from
├── schema.graphql        for an editor's GraphQL extension
└── ...
```

Commit all of it. Each document becomes a function named after its operation:

```ts
/** Requires a signed-in caller, holding one of the roles Customer. The host checks it either way. */
export function placeOrder(input: PlaceOrderInput): Promise<PlaceOrderResult> {
  return sendDocument(placeOrderDocumentId, { input }, ["dispatch", "placeOrder"]);
}
```

It takes each required variable as a parameter, answers with the field the document asked for,
typed from what it selected, and says in its comment who may call it. `placeOrderDocumentId` is the
id the hub registered, `trax_client_PlaceOrder_` and a hash of the text.

Build the hub again, so it copies `trax.documents.json` beside itself. The first build had no file
to copy, and an export does not need one; a hub that serves does, and refuses to start without it.

```bash
cd ..
dotnet build
```

## The page

`connection.ts` signs in and points the client at the hub. In Development the token comes from the
hub's `/dev/token` endpoint, as it has for `curl`. A socket cannot carry a header, so the token goes
in the socket's first message as well.

```ts file=web/src/connection.ts
import { configureTrax } from "./trax/index.g";

const HUB = "http://localhost:5080";

// Signs in as a customer with a Development token from the hub, and points the generated client at
// it. Every request carries the token; the socket, which cannot carry a header, sends it when it
// opens.
export async function signIn(user: string): Promise<void> {
  const token = await (await fetch(`${HUB}/dev/token/${user}`)).text();
  configureTrax({
    url: `${HUB}/trax/graphql`,
    socketUrl: `${HUB.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${token}` }),
    connectionParams: () => ({ authToken: token }),
    user,
  });
}
```

```tsx file=web/src/main.tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { signIn } from "./connection";

signIn("ada").then(() =>
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
);
```

```tsx file=web/src/App.tsx
import { useState } from "react";
import { Catalogue } from "./Catalogue";
import { MyOrders } from "./MyOrders";
import { OrderProgress } from "./OrderProgress";

export function App() {
  // The run of the order placed last, followed until it ends.
  const [runId, setRunId] = useState<string | null>(null);

  return (
    <main>
      <h1>Shopfront</h1>
      <Catalogue onPlaced={setRunId} />
      {runId && <OrderProgress runId={runId} />}
      <MyOrders key={runId} />
    </main>
  );
}
```

The catalogue lists the products and buys one. A refusal a train meant the customer to read, a
`TrainException`, arrives as a `TraxOperationError` with the train's message; anything else stays
on the server, as it always has.

```tsx file=web/src/Catalogue.tsx
import { useEffect, useState } from "react";
import { catalogue, placeOrder, TraxOperationError, type CatalogueResult } from "./trax/index.g";

// GraphQL lets every level be null, and the generated type says so.
type Product = NonNullable<NonNullable<NonNullable<CatalogueResult>["nodes"]>[number]>;

export function Catalogue({ onPlaced }: { onPlaced: (runId: string) => void }) {
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    catalogue().then((page) => setProducts(page?.nodes?.filter((p) => p !== null) ?? []));
  }, []);

  async function buy(sku: string) {
    setError(null);
    try {
      const placed = await placeOrder({ lines: [{ sku, quantity: 1 }], paymentToken: "tok_visa" });
      if (placed) onPlaced(placed.externalId);
    } catch (e) {
      // A refusal the train meant the customer to read, such as "CAP is out of stock."
      setError(e instanceof TraxOperationError ? e.message : "Something went wrong.");
    }
  }

  return (
    <section>
      <h2>Catalogue</h2>
      <ul>
        {products.map((p) => (
          <li key={p.sku}>
            {p.name}, {p.price} ({p.stock} left){" "}
            <button onClick={() => buy(p.sku)}>Buy one</button>
          </li>
        ))}
      </ul>
      {error && <p role="alert">{error}</p>}
    </section>
  );
}
```

`useRunProgress` follows a run: it reads what has happened, subscribes to the rest, and re-reads if
it misses an event. Only the caller who started the run can follow it. `PlaceOrder` runs while the
mutation waits, so by the time the page follows it, it has finished; the page still shows every
step, as `runProgress` did in chapter 7, the fraud check's steps withheld.

```tsx file=web/src/OrderProgress.tsx
import { useRunProgress } from "./trax/index.g";

// Follows the run that placed the order, step by step, as the hub records it.
export function OrderProgress({ runId }: { runId: string }) {
  const run = useRunProgress(runId);

  return (
    <section>
      <h2>Your order: {run.state ?? "..."}</h2>
      <ol>
        {run.steps.map((step) => (
          <li key={step.position}>
            {step.name}: {step.state}
          </li>
        ))}
      </ol>
      {run.failure && <p role="alert">{run.failure}</p>}
    </section>
  );
}
```

```tsx file=web/src/MyOrders.tsx
import { useEffect, useState } from "react";
import { myOrders, type MyOrdersResult } from "./trax/index.g";

type Order = NonNullable<NonNullable<NonNullable<MyOrdersResult>["nodes"]>[number]>;

export function MyOrders() {
  const [orders, setOrders] = useState<Order[]>([]);

  useEffect(() => {
    myOrders().then((page) => setOrders(page?.nodes?.filter((o) => o !== null) ?? []));
  }, []);

  return (
    <section>
      <h2>Your orders</h2>
      <ul>
        {orders.map((o) => (
          <li key={o.id}>
            {new Date(o.placedAt).toLocaleString()}: {o.total}
          </li>
        ))}
      </ul>
    </section>
  );
}
```

## Run it

Start the hub and the worker, and the storefront:

```bash
cd web
npm run dev
```

Open `http://localhost:5173`. Buy a mug, and the order's steps appear under it; buy a cap and the
page says `CAP is out of stock.` What the functions answered, from the first call down:

```text
catalogue    {"nodes":[{"sku":"CAP","name":"Cap","price":18.5,"stock":0},{"sku":"MUG","name":"Mug","price":12,"stock":10},{"sku":"TEE","name":"T-shirt","price":25,"stock":3}]}
placeOrder   {"externalId":"f8ec30074c93467dbd65fe25819cb77c","output":{"orderId":"c6213f82-1da0-49ec-9e90-1d38a7c59cf4","total":12}}
placeOrder   TraxOperationError TRAX_TRAIN_ERROR CAP is out of stock.
myOrders     {"nodes":[{"id":"c6213f82-1da0-49ec-9e90-1d38a7c59cf4","total":12,"placedAt":"2026-10-18T21:29:00.004777Z"}]}
```

The hub ran every one of them by id. Its log has no `observed inline query` line for them: shadow
mode logs only documents sent as text, and the page sent none.

## Keep it current

The client is generated from the hub, so any change to a train, a query model, a machine or a
document means exporting and generating again. Forgetting to is caught in CI: `--check` writes
nothing and fails, naming each file, when what is committed is not what the manifest generates.

```bash
dotnet run --project Shopfront.Hub -- --trax-export-client ../web/src/trax/manifest.json
cd web
dotnet trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql' --check
```

```text
The client in /home/you/Shopfront/web/src/trax is what the manifest generates.
```

Deploy the hub before the storefront: a page whose documents the hub has not registered yet is
refused.

## Test it

The hub's tests start it, and it now reads `trax.documents.json` as it starts, so they show the
storefront's documents are valid: a document the hub refuses fails every host test. Nothing in
`tests/` changes.

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    74, Skipped:     0, Total:    74 - Shopfront.Tests.dll (net10.0)
```

## What we have

A storefront that calls the hub through typed functions generated from its own documents, which
the hub checked and registered when it started. It sends ids, not text.

The next chapter takes the customer through checkout in the browser, with the client `trax
client` already generated for the checkout machine.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)
