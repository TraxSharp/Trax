import { useState } from "react";
import { FeatureSource } from "./FeatureSource";
import { statePhrase } from "./features";
import { readOrdersView, readShopSignView, useRememberedOrder } from "./trax/index.g";
import { usePoll } from "./usePoll";

const TONE: Record<string, string> = {
  Draft: "",
  Paying: " tone-busy",
  Placed: " tone-ok",
  Baking: " tone-oven",
  Ready: " tone-ok",
  Declined: " tone-fail",
  Unpaid: " tone-fail",
};

const STATUS_TONE: Record<string, string> = {
  Paying: " tone-busy",
  "Waiting for the oven": " tone-ok",
  "In the oven": " tone-oven",
  "Ready to collect": " tone-ok",
  "Payment declined": " tone-fail",
  "Payment not taken": " tone-fail",
};

function Node({ name, current, small }: { name: string; current?: string; small?: boolean }) {
  const failed = name === "Declined" || name === "Unpaid";
  return (
    <span
      className={`node${small ? " node-small" : ""}${current === name ? " node-current" : ""}${current === name && failed ? " node-fail" : ""}`}
    >
      {name}
    </span>
  );
}

function Edge({ label, avail, server }: { label: string; avail?: boolean; server?: boolean }) {
  return (
    <span className="edge">
      <span className={`edge-label${avail ? " avail" : ""}${server ? " server" : ""}`}>{label} →</span>
      <span className="edge-line" />
    </span>
  );
}

/**
 * A customer's page: their order, which they fill and place, and the orders view, every order of theirs as the view
 * shows it. Placing takes payment; a paid order waits for a baker to fire the oven, and comes out ready to collect.
 */
export function Customer() {
  const order = useRememberedOrder();
  const [loaves, setLoaves] = useState(3);
  const orders = usePoll(readOrdersView);
  const sign = usePoll(readShopSignView);
  const shopOpen = sign.value?.shop?.open?.startsWith("We're open") ?? false;
  const state = order.state ?? undefined;
  const canOrder = order.can("Order", { loaves });
  const canPlace = order.available.includes("Place");
  const rows = orders.value?.order ?? [];
  const phrase = statePhrase("order", state);

  return (
    <div className="stage-body">
      <div className="column">
        <section aria-label="Your order" className="panel">
          <div className="machine-head">
            <div>
              <h2>
                <code>order</code>
              </h2>
              <p className="example">Yours alone. Say how many loaves, place it, and the payment train decides.</p>
            </div>
            <span className={`badge${TONE[state ?? ""] ?? ""}`} data-order-state>
              {state ?? "…"}
            </span>
          </div>

          <div className="diagram" aria-hidden>
            <div className="diagram-row">
              <Node name="Draft" current={state} />
              <Edge label="Place" avail={canPlace} />
              <Node name="Paying" current={state} />
              <Edge label="paid" server />
              <Node name="Placed" current={state} />
              <Edge label="oven" server />
              <Node name="Baking" current={state} />
              <Edge label="10 s" server />
              <Node name="Ready" current={state} />
            </div>
            <div className="diagram-side">
              <span className="side-label">Paying ↘</span>
              <Node name="Declined" current={state} small />
              <Node name="Unpaid" current={state} small />
            </div>
          </div>

          {state === "Placed" && (
            <p className="waiting">
              {shopOpen
                ? "Paid. It goes in the next time a baker fires the oven."
                : "Paid. The shop is closed: it goes in once a baker clocks in and fires the oven."}
            </p>
          )}
          {state === "Baking" && <p className="waiting">In the oven. It comes out ready to collect in 10 seconds.</p>}

          <div className="controls">
            {state === "Draft" && (
              <>
            <div className="stepper-label">
              <span className="stepper">
                <button type="button" aria-label="One fewer" onClick={() => setLoaves((n) => Math.max(0, n - 1))}>
                  −
                </button>
                <input
                  type="number"
                  min={0}
                  value={loaves}
                  onChange={(e) => setLoaves(Number(e.target.value))}
                  aria-label="Loaves"
                />
                <button type="button" aria-label="One more" onClick={() => setLoaves((n) => n + 1)}>
                  +
                </button>
              </span>
              loaves. More than a dozen and the payment is declined.
            </div>
            <div className="triggers">
              <button
                type="button"
                aria-label="Order"
                className={`trigger${canOrder ? " avail" : ""}`}
                disabled={!canOrder}
                onClick={() => order.send("Order", { loaves })}
              >
                <span className="trigger-name">Order</span>
                <span className="trigger-caption">set {loaves} loaves on the draft</span>
              </button>
              <button
                type="button"
                aria-label="Place"
                className={`trigger${canPlace ? " avail" : ""}`}
                disabled={!canPlace}
                onClick={() => order.send("Place")}
              >
                <span className="trigger-name">Place</span>
                <span className="trigger-caption">pay, and wait for the oven</span>
              </button>
            </div>
              </>
            )}
            {(state === "Ready" || state === "Declined" || state === "Unpaid") && (
              <button type="button" className="primary" onClick={() => order.forget()}>
                New order
              </button>
            )}
            {order.problem && (
              <p className="problem" role="alert">
                {order.problem.message}
              </p>
            )}
            {order.context && (
              <div className="context">
                <span className="context-item">
                  <span className="context-key">loaves</span>
                  <span className="context-value">{String(order.context.loaves ?? "–")}</span>
                </span>
                <span className="context-item">
                  <span className="context-key">approved</span>
                  <span className="context-value">{String(order.context.approved ?? "–")}</span>
                </span>
              </div>
            )}
          </div>
        </section>

        <section aria-label="Your orders" className="panel">
          <div className="machine-head">
            <h2>Your orders</h2>
          </div>
          {rows.length === 0 ? (
            <p className="orders-empty">None yet.</p>
          ) : (
            <ul className="orders">
              {rows.map((o) => (
                <li key={o.id} data-order={o.id}>
                  <span className="loaf" aria-hidden>
                    🍞
                  </span>
                  <span className="loaves">{o.loaves} loaves</span>
                  <span className={`badge${STATUS_TONE[o.status ?? ""] ?? ""}`}>{o.status}</span>
                </li>
              ))}
            </ul>
          )}
          {orders.error && (
            <p className="problem" role="status">
              {orders.error}
            </p>
          )}
          <p className="view-note">
            The <code>orders</code> view: your orders only, with the labels the glossary gives each state.
          </p>
        </section>
      </div>
      <FeatureSource initial="ordering.feature" phrases={phrase ? [phrase] : []} />
    </div>
  );
}
