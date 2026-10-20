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
