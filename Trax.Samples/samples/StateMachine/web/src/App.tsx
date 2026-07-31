import { useMemo, useState } from "react";
import { createTransport, type Snapshot, type TraxTransport } from "./traxTransport";
import { useMachine } from "./useMachine";

const ENDPOINT = "http://localhost:5220/trax/graphql";

// Stable ids: both users drive the same instance ids, and the server scopes each draft to the caller, so
// switching user shows a different draft for the same id.
const TURNSTILE_ID = "a0000000-0000-0000-0000-000000000001";
const CHECKOUT_ID = "b0000000-0000-0000-0000-000000000002";

const USERS = [
  { label: "Alice", key: "alice-key" },
  { label: "Bob", key: "bob-key" },
];

// Module-level so the references are stable (the hook resumes on identity change).
const initialTurnstile = (): Snapshot => ({
  machine: "turnstile",
  version: 1,
  state: "Locked",
  context: {},
});
const initialCheckout = (): Snapshot => ({
  machine: "checkout",
  version: 1,
  state: "Cart",
  context: { items: [], receipt: null },
});

export function App() {
  const [apiKey, setApiKey] = useState(USERS[0].key);
  const transport = useMemo(() => createTransport(ENDPOINT, () => apiKey), [apiKey]);

  return (
    <div className="page">
      <header>
        <h1>Trax State Machine</h1>
        <p className="sub">
          One machine-agnostic transport, driving two machines over the four generic{" "}
          <code>stateMachine</code> mutations. Drafts are scoped per user.
        </p>
        <div className="who">
          <span>Signed in as</span>
          {USERS.map((u) => (
            <button
              key={u.key}
              className={u.key === apiKey ? "chip active" : "chip"}
              onClick={() => setApiKey(u.key)}
            >
              {u.label}
            </button>
          ))}
          <span className="endpoint">{ENDPOINT}</span>
        </div>
      </header>

      <main>
        <Turnstile transport={transport} />
        <Checkout transport={transport} />
      </main>
    </div>
  );
}

function Turnstile({ transport }: { transport: TraxTransport }) {
  const m = useMachine(transport, "turnstile", TURNSTILE_ID, initialTurnstile);
  const unlocked = m.state === "Unlocked";

  return (
    <section className="card">
      <div className="card-head">
        <h2>turnstile</h2>
        <StateBadge state={m.state} tone={unlocked ? "good" : "muted"} />
      </div>
      <p className="hint">Authoritative advance: the server re-drives the stored draft from a trigger.</p>

      <div className="turnstile-visual">{unlocked ? "🔓 open" : "🔒 locked"}</div>

      <div className="actions">
        <button disabled={m.busy} onClick={() => m.advance("Coin", { coin: "quarter" })}>
          Insert quarter
        </button>
        <button disabled={m.busy} onClick={() => m.advance("Coin", { coin: "dollar" })}>
          Insert dollar
        </button>
        <button disabled={m.busy} onClick={() => m.advance("Push")}>
          Push
        </button>
      </div>

      <Panel machine={m} />
    </section>
  );
}

function Checkout({ transport }: { transport: TraxTransport }) {
  const m = useMachine(transport, "checkout", CHECKOUT_ID, initialCheckout);
  const [item, setItem] = useState("");
  const items = (m.context.items as string[] | undefined) ?? [];
  const receipt = m.context.receipt as string | null;

  function addItem() {
    if (!item.trim()) return;
    void m.save({
      machine: "checkout",
      version: 1,
      state: "Cart",
      context: { items: [...items, item.trim()], receipt: null },
    });
    setItem("");
  }

  return (
    <section className="card">
      <div className="card-head">
        <h2>checkout</h2>
        <StateBadge state={m.state} tone={m.state === "Paid" ? "good" : "muted"} />
      </div>

      <Stepper current={m.state} steps={["Cart", "Review", "Paid"]} />

      <ul className="items">
        {items.length === 0 ? (
          <li className="empty">no items yet</li>
        ) : (
          items.map((it, i) => <li key={i}>{it}</li>)
        )}
      </ul>

      {m.state === "Cart" && (
        <div className="actions">
          <input
            value={item}
            placeholder="add an item"
            onChange={(e) => setItem(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && addItem()}
          />
          <button disabled={m.busy} onClick={addItem}>
            Add
          </button>
          <button disabled={m.busy || items.length === 0} onClick={() => m.advance("Next")}>
            Review →
          </button>
        </div>
      )}

      {m.state === "Review" && (
        <div className="actions">
          <button disabled={m.busy} onClick={() => m.advance("Back")}>
            ← Back
          </button>
          <button className="primary" disabled={m.busy} onClick={() => m.send(`pay-${Date.now()}`)}>
            Pay (charge)
          </button>
        </div>
      )}

      {m.state === "Paid" && (
        <div className="paid">
          <div className="receipt">
            charged, receipt <code>{receipt}</code>
          </div>
          <div className="actions">
            <button disabled={m.busy} onClick={() => m.send(`pay-${Date.now()}`)}>
              Charge again
            </button>
            <button disabled={m.busy} onClick={() => m.advance("Reset")}>
              Start over
            </button>
          </div>
          <p className="hint">
            &ldquo;Charge again&rdquo; returns the same receipt and does not re-charge. That is the
            exactly-once effect.
          </p>
        </div>
      )}

      <Panel machine={m} />
    </section>
  );
}

function Stepper({ current, steps }: { current: string | null; steps: string[] }) {
  const idx = current ? steps.indexOf(current) : -1;
  return (
    <div className="stepper">
      {steps.map((s, i) => (
        <span key={s} className={i === idx ? "step active" : i < idx ? "step done" : "step"}>
          {s}
        </span>
      ))}
    </div>
  );
}

function StateBadge({ state, tone }: { state: string | null; tone: "good" | "muted" }) {
  return <span className={`badge ${tone}`}>{state ?? "…"}</span>;
}

function Panel({ machine }: { machine: ReturnType<typeof useMachine> }) {
  return (
    <div className="panel">
      {machine.problem && (
        <div className="problem">
          <strong>{machine.problem.code}</strong> {machine.problem.message}
        </div>
      )}
      <pre>{machine.snapshot ? JSON.stringify(machine.snapshot, null, 2) : "loading…"}</pre>
    </div>
  );
}
