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
