import { useState } from "react";
import { readOrdersView, useRememberedOrder } from "./trax/index.g";
import { usePoll } from "./usePoll";

/**
 * A customer's page: their order, which they fill and place, and the orders view, every order of theirs as the view
 * shows it. Placing takes payment; when the shop opens, a placed order goes in the oven.
 */
export function Customer() {
  const order = useRememberedOrder();
  const [loaves, setLoaves] = useState(3);
  const orders = usePoll(readOrdersView);

  return (
    <>
      <section aria-label="Your order" className="card">
        <h2>Your order</h2>
        <p>
          It is <strong data-order-state>{order.state ?? "…"}</strong>
          {order.context?.loaves != null && <>, {order.context.loaves} loaves</>}.
        </p>
        <label>
          Loaves{" "}
          <input
            type="number"
            min={0}
            value={loaves}
            onChange={(e) => setLoaves(Number(e.target.value))}
            aria-label="Loaves"
          />
        </label>{" "}
        <button type="button" disabled={!order.can("Order", { loaves })} onClick={() => order.send("Order", { loaves })}>
          Order
        </button>{" "}
        <button type="button" disabled={!order.available.includes("Place")} onClick={() => order.send("Place")}>
          Place
        </button>
        {order.problem && (
          <p className="problem" role="alert">
            {order.problem.message}
          </p>
        )}
      </section>
      <section aria-label="Your orders" className="card">
        <h2>Your orders</h2>
        <ul>
          {(orders.value?.order ?? []).map((o) => (
            <li key={o.id} data-order={o.id}>
              {o.loaves} loaves: {o.status}
            </li>
          ))}
        </ul>
        {orders.error && <p className="problem">{orders.error}</p>}
      </section>
    </>
  );
}
