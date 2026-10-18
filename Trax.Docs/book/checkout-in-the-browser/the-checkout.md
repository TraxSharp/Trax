---
title: "16.2 The checkout"
description: "Chapter 16 of the Trax book, section 2: The checkout."
type: tutorial
area: general
parent: "16. Checkout in the browser"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/checkout-in-the-browser/the-twin-and-the-hook
---

# 16.2 The checkout

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

## SDK Reference

> [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) | [The TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package)

---

Next: [16.3 Run it](/docs/book/checkout-in-the-browser/run-it)
