import { FeatureSource } from "./FeatureSource";
import { statePhrase } from "./features";
import { readShopSignView, useRememberedShift } from "./trax/index.g";
import { usePoll } from "./usePoll";

const STATE_OF_SIGN: Record<string, string> = {
  "We're open": "Open",
  "We're open, the oven is on": "Firing",
  Closed: "Closed",
};

/**
 * A baker's page: their shift. Clocking in opens the shop and clocking out closes it; firing the oven puts every
 * waiting order in. Each of those is a policy the server runs; the baker only moves their own shift.
 */
export function Baker() {
  const shift = useRememberedShift();
  const sign = usePoll(readShopSignView);
  const state = shift.state ?? undefined;
  const label = sign.value?.shop?.open;
  const shop = label ? STATE_OF_SIGN[label] : undefined;
  const can = (trigger: "ClockIn" | "FireOven" | "ClockOut") => shift.available.includes(trigger);
  const phrases = [statePhrase("shift", state), statePhrase("shop", shop)].filter((p): p is string => p !== null);
  const node = (name: string, text = name) => (
    <span className={`node${state === name ? " node-current" : ""}`}>{text}</span>
  );

  return (
    <div className="stage-body">
      <div className="column">
        <section aria-label="Your shift" className="panel">
          <div className="machine-head">
            <div>
              <h2>
                <code>shift</code>
              </h2>
              <p className="example">Yours alone. Clock in, fire the oven when orders are waiting, clock out.</p>
            </div>
            <span className={`badge${state === "On" ? " tone-ok" : state === "Oven" ? " tone-oven" : ""}`} data-shift-state>
              {state ?? "…"}
            </span>
          </div>

          <div className="diagram" aria-hidden>
            <div className="diagram-row">
              {node("Off")}
              <span className="edge">
                <span className={`edge-label${can("ClockIn") ? " avail" : ""}`}>ClockIn →</span>
                <span className="edge-line" />
              </span>
              {node("On")}
              <span className="edge">
                <span className={`edge-label${can("FireOven") ? " avail" : ""}`}>FireOven →</span>
                <span className="edge-line" />
                <span className="edge-label server">← 2 s</span>
              </span>
              {node("Oven")}
            </div>
            <div className="diagram-side">
              <span className="side-label">On ↘ ClockOut</span>
              <span className={`node node-small${state === "Over" ? " node-current" : ""}`}>Over</span>
            </div>
          </div>

          <div className="triggers">
            <button
              type="button"
              aria-label="Clock in"
              className={`trigger${can("ClockIn") ? " avail" : ""}`}
              disabled={!can("ClockIn")}
              onClick={() => shift.send("ClockIn")}
            >
              <span className="trigger-name">ClockIn</span>
              <span className="trigger-caption">start your shift, open the shop</span>
            </button>
            <button
              type="button"
              aria-label="Fire the oven"
              className={`trigger${can("FireOven") ? " avail" : ""}`}
              disabled={!can("FireOven")}
              onClick={() => shift.send("FireOven")}
            >
              <span className="trigger-name">FireOven</span>
              <span className="trigger-caption">every waiting order goes in</span>
            </button>
            <button
              type="button"
              aria-label="Clock out"
              className={`trigger danger${can("ClockOut") ? " avail" : ""}`}
              disabled={!can("ClockOut")}
              onClick={() => shift.send("ClockOut")}
            >
              <span className="trigger-name">ClockOut</span>
              <span className="trigger-caption">end your shift, close the shop</span>
            </button>
          </div>
          {state === "Over" && (
            <button type="button" className="primary" onClick={() => shift.forget()}>
              New shift
            </button>
          )}
          {shift.problem && (
            <p className="problem" role="alert">
              {shift.problem.message}
            </p>
          )}
        </section>

        <section aria-label="What the server does" className="panel">
          <div className="machine-head">
            <h2>What the server does for you</h2>
          </div>
          <ol className="chain">
            <li className={shop && shop !== "Closed" ? "done" : ""}>
              <span className="chain-dot">{shop && shop !== "Closed" ? "✓" : "1"}</span>
              <span className="chain-text">
                Your shift enters <code>On</code>: the shop opens
                <small>
                  A policy fires <code>Open</code> on <code>shop/main</code>. No person may: the shop is the
                  system&apos;s.
                </small>
              </span>
            </li>
            <li className={shop === "Firing" ? "done" : ""}>
              <span className="chain-dot">{shop === "Firing" ? "✓" : "2"}</span>
              <span className="chain-text">
                Your shift enters <code>Oven</code>: the shop bakes a batch
                <small>
                  A policy fires <code>Batch</code>; the shop entering <code>Firing</code> fires <code>Bake</code> on
                  every <code>order</code> in <code>Placed</code>, whoever placed it.
                </small>
              </span>
            </li>
            <li>
              <span className="chain-dot">3</span>
              <span className="chain-text">
                Orders come out after 10 seconds
                <small>A timer on each order&apos;s Baking state: it is ready to collect.</small>
              </span>
            </li>
            <li className={state === "Over" && shop === "Closed" ? "done" : ""}>
              <span className="chain-dot">{state === "Over" && shop === "Closed" ? "✓" : "4"}</span>
              <span className="chain-text">
                Your shift enters <code>Over</code>: the shop closes
                <small>
                  A policy fires <code>Close</code>. Orders placed after that wait for the next baker.
                </small>
              </span>
            </li>
          </ol>
        </section>
      </div>
      <FeatureSource initial="shop.feature" phrases={phrases} />
    </div>
  );
}
