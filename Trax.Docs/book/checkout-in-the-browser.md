---
title: "16. Checkout in the browser"
description: "Chapter 16 of the Trax book: drive the checkout machine from React with the generated hook, which predicts each move with the machine's twin."
type: tutorial
area: general
parent: The Trax book
nav_order: 26
snippet_context: none
follows: book/a-generated-client
---

# 16. Checkout in the browser

The storefront buys one product at a time. In this chapter it gets a cart, and the customer goes
through the checkout machine from chapter 12 in the browser: fill the cart, give an address,
choose how to pay, place the order and follow the parcel.

Nothing new is generated. `trax client` already wrote the checkout's client in the last chapter,
because the hub serves the machine: `web/src/trax/checkout/` holds it.

This chapter starts from the end of [chapter 15](/docs/book/a-generated-client).

## The twin and the hook

`checkout.machine.g.ts` is the machine's **twin**: the same states, events, guards and reducers,
in TypeScript, generated from the definition the hub exported. It never decides anything. It
predicts, so the page can answer a click at once:

1. The page sends an event with `send`. The twin computes where the checkout goes, and the page
   shows that straight away.
2. The event goes to the hub by its persisted id, with the prediction.
3. The hub applies the event itself, with the rules on the server, and stores the result. It
   answers with what it stored, which replaces the prediction. If the hub refuses the event, a
   guard say, the prediction is rolled back and the refusal is shown. If the hub's result differs
   from the prediction, it refuses with `CLIENT_DIVERGENCE` and stores nothing, and the page reads
   the checkout again.
4. Changes the page did not make, a timer firing or the run that places the order ending, arrive
   on the socket as they are stored.

`useRememberedCheckout` is the hook over all of that. It keeps the customer's checkout across
visits: it opens the one it remembers in `localStorage`, for the signed-in user, or starts one.
Every event carries a request id, so an event sent twice, by a retry or a double click, is applied
once.

| It returns | What it is |
|---|---|
| `status` | `loading` until the first snapshot, `ready`, or `stopped` for good |
| `state`, `context`, `snapshot` | the prediction while an event is pending, otherwise what the hub stored |
| `pending` | an event is in flight |
| `problem` | the last refusal, with the guard's `Because` text as its message |
| `send(event, input)` | sends an event; never throws |
| `can(event, input)` | whether the twin allows it from what is shown |
| `forget()` | starts a new checkout; the old one stays on the hub |
| `error` | why the hook stopped: the checkout is gone, or the page is out of date |

## The checkout

`web/src/Checkout.tsx`:

```tsx file=web/src/Checkout.tsx
import { useState } from "react";
import type { CartLineInput, useRememberedCheckout } from "./trax/index.g";

export type CheckoutView = ReturnType<typeof useRememberedCheckout>;

// The cart's lines. The twin types the items of a list loosely, so the page names them.
export function linesOf(checkout: CheckoutView): CartLineInput[] {
  return (checkout.context?.lines ?? []) as CartLineInput[];
}

// Puts one more of a product in the cart. SetLines replaces the whole list, as the machine says.
export function addToCart(checkout: CheckoutView, sku: string) {
  const lines = linesOf(checkout);
  const line = lines.find((l) => l.sku === sku);
  return checkout.send("SetLines", {
    lines: line
      ? lines.map((l) => (l === line ? { ...l, quantity: l.quantity + 1 } : l))
      : [...lines, { sku, quantity: 1 }],
  });
}

export function Checkout({ checkout }: { checkout: CheckoutView }) {
  const [address, setAddress] = useState("");
  const { status, error, snapshot, state, pending, problem, send, can, forget } = checkout;

  if (status === "loading") return <p>Loading your checkout...</p>;
  if (error?.code === "CLIENT_OUTDATED") return <p role="alert">Shopfront has changed. Reload the page.</p>;
  if (error) return <button onClick={forget}>Start a new checkout</button>;

  return (
    <section>
      <h2>
        Checkout: {state}
        {pending && " (saving)"}
      </h2>
      <ul>
        {linesOf(checkout).map((l) => (
          <li key={l.sku}>
            {l.quantity} x {l.sku}
          </li>
        ))}
      </ul>

      {state === "Cart" && (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            send("SetAddress", { address });
          }}
        >
          <input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="Where to send it" />
          <button disabled={!can("SetAddress", { address })}>Continue</button>
        </form>
      )}
      {state === "Address" && (
        <>
          <button onClick={() => send("ChoosePayment", { paymentToken: "tok_visa" })}>Pay by card</button>
          <button onClick={() => send("ChoosePayment", { paymentToken: "tok_chargeDeclined" })}>
            Pay with a test card that declines
          </button>
          <button onClick={() => send("Back")}>Back</button>
        </>
      )}
      {state === "Payment" && (
        <>
          <button onClick={() => send("PlaceOrder")}>Place order</button>
          <button onClick={() => send("Back")}>Back</button>
        </>
      )}
      {state === "Placing" && <p>Placing your order...</p>}
      {snapshot?.state === "Placed" && (
        <>
          <p>Order {snapshot.context.orderId} is placed.</p>
          <button onClick={() => send("Track")}>Follow the parcel</button>
        </>
      )}
      {state === "Failed" && (
        <>
          <p>The order did not go through.</p>
          <button onClick={() => send("Retry")}>Try again</button>
        </>
      )}
      {state === "Shipping" && <p>Your parcel is on its way.</p>}
      {state === "Delivered" && (
        <>
          <p>Your parcel has arrived.</p>
          <button onClick={forget}>Start a new order</button>
        </>
      )}
      {state === "Abandoned" && <button onClick={() => send("StartOver")}>Start over</button>}

      {problem && <p role="alert">{problem.message}</p>}
    </section>
  );
}
```

Each state shows what the customer can do from it, and each button sends one event:

- **`can("SetAddress", { address })`** asks the twin whether the guard would let it through, with
  what is typed so far. The button is disabled until the cart has something in it and the address
  is not blank, the same rule the hub applies. The hub still applies it: the twin decides what the
  page offers, never what is stored.
- **`SetLines` replaces the whole list**, as the machine declares, so adding a product sends every
  line with one more of it.
- **`Placing` shows no button.** The hub moves the checkout on when the run that places the order
  ends, and the page hears it on the socket.
- **`snapshot?.state === "Placed"`** narrows the snapshot, so its context is the `Placed` state's,
  which has the order's id. `state` alone does not narrow `context`.
- **The twin types a list's items loosely**, as `unknown`, so `linesOf` names them as the
  `CartLineInput` the generated types already have.
- **`CLIENT_OUTDATED`** means the hub serves a newer version of the machine than the page was
  built for. The page has to reload to get it.

The catalogue gets an **Add to cart** button beside **Buy one now**, enabled while the cart can
take a line.

`web/src/Catalogue.tsx`:

```tsx file=web/src/Catalogue.tsx
import { useEffect, useState } from "react";
import { catalogue, placeOrder, TraxOperationError, type CatalogueResult } from "./trax/index.g";

// GraphQL lets every level be null, and the generated type says so.
type Product = NonNullable<NonNullable<NonNullable<CatalogueResult>["nodes"]>[number]>;

export function Catalogue({
  onPlaced,
  onAdd,
}: {
  onPlaced: (runId: string) => void;
  // Puts the product in the cart, when the cart can take it.
  onAdd?: (sku: string) => void;
}) {
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
            <button onClick={() => buy(p.sku)}>Buy one now</button>{" "}
            <button disabled={!onAdd} onClick={() => onAdd?.(p.sku)}>
              Add to cart
            </button>
          </li>
        ))}
      </ul>
      {error && <p role="alert">{error}</p>}
    </section>
  );
}
```

`web/src/App.tsx`:

```tsx file=web/src/App.tsx
import { useState } from "react";
import { Catalogue } from "./Catalogue";
import { addToCart, Checkout } from "./Checkout";
import { MyOrders } from "./MyOrders";
import { OrderProgress } from "./OrderProgress";
import { useRememberedCheckout } from "./trax/index.g";

export function App() {
  // The run of the order bought last with "Buy one now", followed until it ends.
  const [runId, setRunId] = useState<string | null>(null);
  // The customer's checkout, the same one across visits.
  const checkout = useRememberedCheckout();

  return (
    <main>
      <h1>Shopfront</h1>
      <Catalogue
        onPlaced={setRunId}
        onAdd={checkout.can("SetLines", { lines: [] }) ? (sku) => addToCart(checkout, sku) : undefined}
      />
      <Checkout checkout={checkout} />
      {runId && <OrderProgress runId={runId} />}
      <MyOrders key={runId} />
    </main>
  );
}
```

## Run it

Start the hub, the worker and `npm run dev`, and open `http://localhost:5173`. Add two mugs. The
cart shows them before the hub answers, then `(saving)` goes away. Try **Continue** with no
address: the button is disabled. Give an address, pay by card and place the order. The checkout
shows `Placing`, and a few seconds later `Placed`, with the order's id, when the worker has run
`PlaceOrder` and the hub has applied its end.

What the hub answered, as the hook saw it:

```text
SetLines      confirmed  revision 2  Cart     {"lines":[{"sku":"MUG","quantity":2}]}
SetAddress    refused    GUARD_FAILED  Put something in the cart and give an address to send it to.
SetAddress    confirmed  revision 3  Address
ChoosePayment confirmed  revision 4  Payment
PlaceOrder    confirmed  revision 5  Placing
(pushed)                 revision 6  Placed
Track         confirmed  revision 7  Shipping
```

The refused `SetAddress` was sent with an empty address, which the page's own button never
allows: the hub applies the guard whatever the page does, and the page shows the reason.

Reload the page and the checkout is where it was. Sign in as someone else, change the `signIn` in
`main.tsx`, and they get a checkout of their own.

## Test it

The twin can be tested without a hub. A test gives the hook's controller a fake server that
answers with a snapshot and never answers an event, so what it shows is the twin's prediction. Add
Vitest:

`web/package.json`:

```json file=web/package.json
{
  "name": "shopfront-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "test": "vitest run"
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
    "vite": "^8.3.3",
    "vitest": "^4.1.12"
  }
}
```

`web/src/checkout.test.ts`:

```ts file=web/src/checkout.test.ts
import { describe, expect, it } from "vitest";
import { ServerMachineController, type MachineServer, type ServerSnapshot } from "@traxsharp/state-machine/client";
import { checkout } from "./trax/checkout/checkout.machine.g";

// A server that answers with `snapshot`, records the events it is sent, and never answers one, so
// what the page shows is the twin's prediction.
function host(snapshot: ServerSnapshot) {
  const events: Parameters<MachineServer["event"]>[0][] = [];
  const server: MachineServer = {
    start: async () => ({ kind: "started", id: "c-1", snapshot }),
    get: async () => ({ kind: "ok", snapshot }),
    event: async (request) => {
      events.push(request);
      return new Promise(() => {});
    },
    subscribe: () => () => {},
  };
  return { server, events };
}

async function open(context: Record<string, unknown>) {
  const { server, events } = host({ rowId: "row-1", revision: 1, state: "Cart", context });
  const controller = new ServerMachineController({ machine: checkout, server });
  await controller.start();
  return { controller, events };
}

describe("the checkout twin", () => {
  it("will not take an address for an empty cart", async () => {
    const { controller } = await open({ lines: [] });

    expect(controller.can("SetAddress", { address: "1 Main St" })).toBe(false);
    controller.dispose();
  });

  it("takes an address once the cart has something in it", async () => {
    const { controller } = await open({ lines: [{ sku: "MUG", quantity: 2 }] });

    expect(controller.can("SetAddress", { address: "1 Main St" })).toBe(true);
    expect(controller.can("SetAddress", { address: "" })).toBe(false);
    controller.dispose();
  });

  it("shows new lines at once, and sends the server what it predicted", async () => {
    const { controller, events } = await open({ lines: [] });

    void controller.send("SetLines", { lines: [{ sku: "MUG", quantity: 1 }] });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(controller.get().snapshot?.context).toEqual({ lines: [{ sku: "MUG", quantity: 1 }] });
    expect(events).toHaveLength(1);
    expect(events[0].clientResult).toBeDefined();
    controller.dispose();
  });
});
```

```bash
npm install
npm test
```

```text
 ✓ src/checkout.test.ts (3 tests)

 Test Files  1 passed (1)
      Tests  3 passed (3)
```

The hub's side is tested as it was: the C# tests replay the corpus against the machine, and the
twin is generated from the same definition.

## What we have

The customer checks out in the browser. Each click shows its result at once, the hub decides and
stores every move, and the moves the hub makes on its own arrive as they happen.

Everything the storefront sends is a document the hub registered. The next chapter makes that the
only thing it accepts.

## SDK Reference

> [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) | [The TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package)
