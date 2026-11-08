import { FeatureSource } from "./FeatureSource";
import { statePhrase } from "./features";
import { readShopSignView, useRememberedShift } from "./trax/index.g";
import { usePoll } from "./usePoll";

/** A baker's page: their shift. Clocking in opens the shop, which puts the placed orders in the oven. */
export function Baker() {
  const shift = useRememberedShift();
  const sign = usePoll(readShopSignView);
  const state = shift.state ?? undefined;
  const on = state === "On";
  const open = sign.value?.shop?.open === "We're open";
  const canClockIn = shift.available.includes("ClockIn");
  const phrases = [statePhrase("shift", state), statePhrase("shop", open ? "Open" : sign.value ? "Closed" : undefined)];

  return (
    <div className="stage-body">
      <div className="column">
        <section aria-label="Your shift" className="panel">
          <div className="machine-head">
            <div>
              <h2>
                <code>shift</code>
              </h2>
              <p className="example">Yours alone. Clock in, and the server takes it from there.</p>
            </div>
            <span className={`badge${on ? " tone-ok" : ""}`} data-shift-state>
              {state ?? "…"}
            </span>
          </div>

          <div className="diagram" aria-hidden>
            <div className="diagram-row">
              <span className={`node${state === "Off" ? " node-current" : ""}`}>Off</span>
              <span className="edge">
                <span className={`edge-label${canClockIn ? " avail" : ""}`}>ClockIn →</span>
                <span className="edge-line" />
              </span>
              <span className={`node${on ? " node-current" : ""}`}>On</span>
            </div>
          </div>

          <div className="triggers">
            <button
              type="button"
              aria-label="Clock in"
              className={`trigger${canClockIn ? " avail" : ""}`}
              disabled={!canClockIn}
              onClick={() => shift.send("ClockIn")}
            >
              <span className="trigger-name">ClockIn</span>
              <span className="trigger-caption">start your shift</span>
            </button>
          </div>
          {shift.problem && (
            <p className="problem" role="alert">
              {shift.problem.message}
            </p>
          )}
        </section>

        <section aria-label="What clocking in sets off" className="panel">
          <div className="machine-head">
            <h2>What clocking in sets off</h2>
          </div>
          <ol className="chain">
            <li className={on ? "done" : ""}>
              <span className="chain-dot">{on ? "✓" : "1"}</span>
              <span className="chain-text">
                Your shift enters <code>On</code>
                <small>The only move you make.</small>
              </span>
            </li>
            <li className={open ? "done" : ""}>
              <span className="chain-dot">{open ? "✓" : "2"}</span>
              <span className="chain-text">
                The shop opens
                <small>
                  A policy: <code>shift</code> enters <code>On</code>, the server fires <code>Open</code> on{" "}
                  <code>shop/main</code>. No person may.
                </small>
              </span>
            </li>
            <li className={open ? "done" : ""}>
              <span className="chain-dot">{open ? "✓" : "3"}</span>
              <span className="chain-text">
                Every placed order goes in the oven
                <small>
                  A second policy: <code>shop</code> enters <code>Open</code>, every <code>order</code> in{" "}
                  <code>Placed</code> gets <code>Bake</code>, in capped batches.
                </small>
              </span>
            </li>
            <li>
              <span className="chain-dot">4</span>
              <span className="chain-text">
                The shop closes after hours
                <small>A timer on the shop&apos;s Open state, written as &quot;When 5 seconds pass&quot;.</small>
              </span>
            </li>
          </ol>
        </section>
      </div>
      <FeatureSource initial="shop.feature" phrases={phrases.filter((p): p is string => p !== null)} />
    </div>
  );
}
