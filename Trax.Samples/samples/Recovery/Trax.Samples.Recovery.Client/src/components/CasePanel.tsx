import { useState } from "react";
import { AUDIENCE, ORDERS, TOPICS } from "../cases";
import type { Phase, Scenario } from "../types";

interface Props {
  scenario: Scenario;
  onScenario(s: Scenario): void;
  phase: Phase;
  changed: boolean;
  onRun(scenario: Scenario, crashOnce: boolean, orderId: string, topic: string): void;
  onReset(): void;
}

/** The case a run is about: which train, what its model call sees, and whether to crash it once. */
export function CasePanel({ scenario, onScenario, phase, changed, onRun, onReset }: Props) {
  const [crashOnce, setCrashOnce] = useState(true);
  const [orderId, setOrderId] = useState(ORDERS[0].orderId);
  const [topic, setTopic] = useState(TOPICS[0]);
  const busy = phase === "starting" || phase === "running" || phase === "retrying" || phase === "backoff";
  const order = ORDERS.find((o) => o.orderId === orderId) ?? ORDERS[0];

  return (
    <section className="case">
      <div className="scenario-picker" role="tablist" aria-label="Train">
        {(["REFUND", "RESEARCH"] as Scenario[]).map((s) => (
          <button key={s} role="tab" aria-selected={scenario === s} className={scenario === s ? "active" : ""} onClick={() => onScenario(s)} disabled={busy}>
            {s === "REFUND" ? "Refund approval" : "Research brief"}
          </button>
        ))}
      </div>

      {scenario === "REFUND" ? (
        <div className="orders" role="radiogroup" aria-label="Order">
          {ORDERS.map((o) => (
            <button key={o.orderId} role="radio" aria-checked={o.orderId === orderId} className={`order ${o.orderId === orderId ? "active" : ""}`} onClick={() => setOrderId(o.orderId)} disabled={busy}>
              <span className="order-id">{o.orderId}</span>
              <span className="order-amount">{o.amount}</span>
              <span className="order-reason">{o.reason}</span>
            </button>
          ))}
        </div>
      ) : (
        <label className="field">
          Topic
          <input value={topic} onChange={(e) => setTopic(e.target.value)} disabled={busy} maxLength={200} list="topics" />
          <datalist id="topics">
            {TOPICS.map((t) => (
              <option key={t} value={t} />
            ))}
          </datalist>
        </label>
      )}

      <div className="sees">
        <div className="sees-title">
          What the model is asked about
          <span className="sees-note">hashed with each answer</span>
        </div>
        {scenario === "REFUND" ? (
          <dl>
            <dt>Order</dt>
            <dd>{order.orderId}</dd>
            <dt>Amount</dt>
            <dd>{order.amount}</dd>
            <dt>Reason</dt>
            <dd>{order.reason}</dd>
            <dt>Earlier refunds</dt>
            <dd className={changed ? "was-changed" : ""}>
              {changed ? (
                <>
                  <s>{order.priorRefunds}</s> {order.priorRefunds + 1}
                </>
              ) : (
                order.priorRefunds
              )}
            </dd>
            <dt>Customer email</dt>
            <dd className="sensitive">[TraxSensitive], hashed with a key</dd>
          </dl>
        ) : (
          <dl>
            <dt>Topic</dt>
            <dd>{topic}</dd>
            <dt>Audience</dt>
            <dd className={changed ? "was-changed" : ""}>
              {changed ? (
                <>
                  <s>{AUDIENCE.before}</s> {AUDIENCE.after}
                </>
              ) : (
                AUDIENCE.before
              )}
            </dd>
          </dl>
        )}
      </div>

      <label className="check">
        <input type="checkbox" checked={crashOnce} onChange={(e) => setCrashOnce(e.target.checked)} disabled={busy} />
        <span>
          Crash the first attempt
          <small>{scenario === "REFUND" ? "in the step the approval routes to" : "while it writes the report"}</small>
        </span>
      </label>

      <div className="run-row">
        <button className="primary" onClick={() => onRun(scenario, crashOnce, orderId, topic)} disabled={busy}>
          Run
        </button>
        <button onClick={onReset} disabled={busy || phase === "idle"}>
          Clear
        </button>
      </div>
    </section>
  );
}
