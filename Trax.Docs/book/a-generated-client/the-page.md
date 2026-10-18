---
title: "15.6 The page"
description: "Chapter 15 of the Trax book, section 6: The page."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/a-generated-client/generate-the-client
---

# 15.6 The page

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

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.7 Run it](/docs/book/a-generated-client/run-it)
