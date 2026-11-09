import { FeatureSource } from "./FeatureSource";
import { statePhrase } from "./features";
import { readDeparturesView, useRememberedDesk } from "./trax/index.g";
import { usePoll } from "./usePoll";

const STATE_OF_LABEL: Record<string, string> = {
  Scheduled: "Scheduled",
  "Check-in open": "Open",
  "Gate closed": "Closed",
  Departed: "Departed",
};

/**
 * A gate agent's page: their desk. Opening it opens check-in for the flight, and every booked passenger with it;
 * closing the gate there closes the flight, and every passenger not checked in misses it. Each of those is a policy
 * the server runs; the gate agent only moves their own desk.
 */
export function Agent() {
  const desk = useRememberedDesk();
  const departures = usePoll(readDeparturesView);
  const state = desk.state ?? undefined;
  const label = departures.value?.flight?.status;
  const flight = label ? STATE_OF_LABEL[label] : undefined;
  const can = (trigger: "OpenDesk" | "CloseGate") => desk.available.includes(trigger);
  const phrases = [statePhrase("desk", state), statePhrase("flight", flight)].filter((p): p is string => p !== null);
  const node = (name: string, text = name) => (
    <span className={`node${state === name ? " node-current" : ""}`}>{text}</span>
  );

  return (
    <div className="stage-body">
      <div className="column">
        <section aria-label="Your desk" className="panel">
          <div className="machine-head">
            <div>
              <h2>
                <code>desk</code>
              </h2>
              <p className="example">Yours alone. Open it when check-in starts, close the gate when boarding ends.</p>
            </div>
            <span className={`badge${state === "Open" ? " tone-ok" : state === "Closing" ? " tone-gate" : ""}`} data-desk-state>
              {state ?? "…"}
            </span>
          </div>

          <div className="diagram" aria-hidden>
            <div className="diagram-row">
              {node("Waiting")}
              <span className="edge">
                <span className={`edge-label${can("OpenDesk") ? " avail" : ""}`}>OpenDesk →</span>
                <span className="edge-line" />
              </span>
              {node("Open")}
              <span className="edge">
                <span className={`edge-label${can("CloseGate") ? " avail" : ""}`}>CloseGate →</span>
                <span className="edge-line" />
              </span>
              {node("Closing")}
              <span className="edge">
                <span className="edge-label server">2 s →</span>
                <span className="edge-line" />
              </span>
              {node("Done")}
            </div>
          </div>

          <div className="triggers">
            <button
              type="button"
              aria-label="Open the desk"
              className={`trigger${can("OpenDesk") ? " avail" : ""}`}
              disabled={!can("OpenDesk")}
              onClick={() => desk.send("OpenDesk")}
            >
              <span className="trigger-name">OpenDesk</span>
              <span className="trigger-caption">open check-in for the flight</span>
            </button>
            <button
              type="button"
              aria-label="Close the gate"
              className={`trigger danger${can("CloseGate") ? " avail" : ""}`}
              disabled={!can("CloseGate")}
              onClick={() => desk.send("CloseGate")}
            >
              <span className="trigger-name">CloseGate</span>
              <span className="trigger-caption">anyone not checked in misses the flight</span>
            </button>
          </div>
          {state === "Done" && (
            <button type="button" className="primary" onClick={() => desk.forget()}>
              Next flight
            </button>
          )}
          {desk.problem && (
            <p className="problem" role="alert">
              {desk.problem.message}
            </p>
          )}
        </section>

        <section aria-label="What the server does" className="panel">
          <div className="machine-head">
            <h2>What the server does for you</h2>
          </div>
          <ol className="chain">
            <li className={flight === "Open" ? "done" : ""}>
              <span className="chain-dot">{flight === "Open" ? "✓" : "1"}</span>
              <span className="chain-text">
                Your desk enters <code>Open</code>: check-in opens
                <small>
                  A policy fires <code>OpenCheckIn</code> on <code>flight/tx101</code>; the flight entering{" "}
                  <code>Open</code> fires <code>OpenCheckIn</code> on every <code>booking</code> still{" "}
                  <code>Booked</code>, whoever holds it.
                </small>
              </span>
            </li>
            <li className={flight === "Closed" || flight === "Departed" ? "done" : ""}>
              <span className="chain-dot">{flight === "Closed" || flight === "Departed" ? "✓" : "2"}</span>
              <span className="chain-text">
                Your desk enters <code>Closing</code>: the gate closes
                <small>
                  A policy fires <code>CloseGate</code>; the flight entering <code>Closed</code> fires <code>Miss</code>{" "}
                  on every <code>booking</code> still <code>Open</code>.
                </small>
              </span>
            </li>
            <li className={flight === "Departed" ? "done" : ""}>
              <span className="chain-dot">{flight === "Departed" ? "✓" : "3"}</span>
              <span className="chain-text">
                The flight departs 4 seconds later
                <small>A timer on the flight&apos;s Closed state. Ten seconds after that, the next flight is scheduled.</small>
              </span>
            </li>
          </ol>
        </section>
      </div>
      <FeatureSource initial="gate.feature" phrases={phrases} />
    </div>
  );
}
