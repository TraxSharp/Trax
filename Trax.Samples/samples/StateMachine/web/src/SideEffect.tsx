import { useEffect, useRef, useState } from "react";
import type { Charge, TraxTransport } from "./traxTransport";
import type { LastAction } from "./useMachine";

// The binding as Machines.cs declares it, trimmed to the lines that matter here.
const BINDING = [
  { code: "m.In(CheckoutState.Review)" },
  { code: "    .On(CheckoutTrigger.Pay)" },
  { code: '    .RunsOnce<ICharge>("checkout:charge")', mark: true },
  { code: "    .Reduce(/* write the receipt into the context */)" },
  { code: "    .To(CheckoutState.Paid);" },
];

/**
 * The side effect, shown from both ends: the machine binds the charge to the Pay edge, so the page only
 * asks for the move, and the payment provider's own list of charges shows the server took it once.
 */
export function SideEffect({ transport, last, draftId }: { transport: TraxTransport; last: LastAction | null; draftId: string }) {
  const [charges, setCharges] = useState<Charge[] | null>(null);
  const known = useRef<Set<string> | null>(null);
  const [fresh, setFresh] = useState<string | null>(null);

  // Read the provider's list when the panel opens and after every send; a new receipt is marked.
  useEffect(() => {
    if (last && last.kind !== "send") return;
    let cancelled = false;
    transport
      .listCharges()
      .then((list) => {
        if (cancelled) return;
        if (known.current) {
          const added = list.find((c) => !known.current!.has(c.receipt));
          if (added) setFresh(added.receipt);
        }
        known.current = new Set(list.map((c) => c.receipt));
        setCharges(list);
      })
      .catch(() => !cancelled && setCharges([]));
    return () => {
      cancelled = true;
    };
  }, [transport, last]);

  const repeated = last?.kind === "send" && last.replayed;

  return (
    <section className="card effect">
      <div>
        <h3>Side effect: the charge</h3>
        <p className="effect-lead">
          The page never calls a payment API. The machine binds the charge to the <strong>Pay</strong> edge, and the
          server runs it itself when a draft takes <code>Review → Paid</code>, exactly once.
        </p>
      </div>

      <pre className="binding">
        {BINDING.map((line) => (
          <div key={line.code} className={line.mark ? "binding-mark" : ""}>
            {line.code}
          </div>
        ))}
      </pre>

      <div className="sent">
        <span className="group-label">All the page sends to pay</span>
        <code className="sent-body">
          sendSnapshot {"{"} machine: "checkout", id: "{draftId.slice(0, 8)}…", requestId {"}"}
        </code>
        <span className="sent-note">No amount, no card, no charge call: the server takes the total from its own copy.</span>
      </div>

      <div className="provider">
        <div className="provider-head">
          <span className="group-label">Payment provider (simulated)</span>
          <span className="provider-count">
            {charges === null ? "…" : `${charges.length} charge${charges.length === 1 ? "" : "s"}`}
          </span>
        </div>
        {repeated && <div className="provider-note">Pay again: no new charge. The server returned the first one's receipt.</div>}
        {charges && charges.length === 0 && <div className="provider-empty">No charges yet. Take the draft to Review and press Pay.</div>}
        <ul className="charges">
          {charges?.map((c) => (
            <li key={c.receipt} className={c.receipt === fresh ? "charge-fresh" : ""}>
              <span className="charge-amount">${(c.amountCents / 100).toFixed(2)}</span>
              <span className="charge-items">
                {c.items} item{c.items === 1 ? "" : "s"}
              </span>
              <code className="charge-receipt">{c.receipt.slice(0, 18)}…</code>
              <span className="charge-time">{new Date(c.chargedAt).toLocaleTimeString()}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
