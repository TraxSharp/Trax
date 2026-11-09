import { useState } from "react";
import { FeatureSource } from "./FeatureSource";
import { statePhrase } from "./features";
import { readBookingsView, readDeparturesView, useRememberedBooking } from "./trax/index.g";
import { usePoll } from "./usePoll";

const TONE: Record<string, string> = {
  Booked: "",
  Open: " tone-ok",
  Issuing: " tone-busy",
  CheckedIn: " tone-ok",
  TurnedDown: " tone-fail",
  NotIssued: " tone-fail",
  Missed: " tone-fail",
};

const STATUS_TONE: Record<string, string> = {
  "Open for check-in": " tone-ok",
  "Getting a boarding pass": " tone-busy",
  "Checked in": " tone-ok",
  "Passport not accepted": " tone-fail",
  "No boarding pass yet": " tone-fail",
  "Missed the flight": " tone-fail",
};

const ENDED = new Set(["CheckedIn", "TurnedDown", "NotIssued", "Missed"]);

function Node({ name, current, small }: { name: string; current?: string; small?: boolean }) {
  const failed = name === "TurnedDown" || name === "NotIssued" || name === "Missed";
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
 * A passenger's page: their booking, which opens for check-in when a gate agent opens their desk, and the bookings
 * view, every booking of theirs as the view shows it. Checking in runs the boarding pass train; a booking still open
 * when the gate closes misses the flight.
 */
export function Passenger() {
  const booking = useRememberedBooking();
  const [passport, setPassport] = useState("X1234567");
  const [seat, setSeat] = useState("14C");
  const bookings = usePoll(readBookingsView);
  const departures = usePoll(readDeparturesView);
  const flight = departures.value?.flight?.status;
  const state = booking.state ?? undefined;
  const canGive = booking.can("GivePassport", { passport });
  // Offered whenever the booking has the move, so a check-in with no passport reaches the server and its refusal shows.
  const canCheckIn = booking.available.includes("CheckIn");
  const rows = bookings.value?.booking ?? [];
  const phrase = statePhrase("booking", state);

  return (
    <div className="stage-body">
      <div className="column">
        <section aria-label="Your booking" className="panel">
          <div className="machine-head">
            <div>
              <h2>
                <code>booking</code>
              </h2>
              <p className="example">Yours alone. Give a passport number, pick a seat, and the boarding pass train decides.</p>
            </div>
            <span className={`badge${TONE[state ?? ""] ?? ""}`} data-booking-state>
              {state ?? "…"}
            </span>
          </div>

          <div className="diagram" aria-hidden>
            <div className="diagram-row">
              <Node name="Booked" current={state} />
              <Edge label="desk opens" server />
              <Node name="Open" current={state} />
              <Edge label="CheckIn" avail={canCheckIn} />
              <Node name="Issuing" current={state} />
              <Edge label="issued" server />
              <Node name="CheckedIn" current={state} />
            </div>
            <div className="diagram-side">
              <span className="side-label">Open ↘ gate closes</span>
              <Node name="Missed" current={state} small />
              <span className="side-label">Issuing ↘</span>
              <Node name="TurnedDown" current={state} small />
              <Node name="NotIssued" current={state} small />
            </div>
          </div>

          {state === "Booked" && (
            <p className="waiting">
              {flight === "Gate closed" || flight === "Departed"
                ? "This flight has closed. Your booking waits for the next one, when a gate agent opens their desk."
                : "Booked. Check-in opens when a gate agent opens their desk."}
            </p>
          )}
          {state === "Issuing" && <p className="waiting">Checking your passport and issuing the boarding pass.</p>}

          <div className="controls">
            {state === "Open" && (
              <>
                <div className="triggers">
                  <label className="field">
                    Passport number
                    <input value={passport} onChange={(e) => setPassport(e.target.value)} aria-label="Passport number" />
                  </label>
                  <button
                    type="button"
                    aria-label="Give passport"
                    className={`trigger${canGive ? " avail" : ""}`}
                    disabled={!canGive}
                    onClick={() => booking.send("GivePassport", { passport })}
                  >
                    <span className="trigger-name">GivePassport</span>
                    <span className="trigger-caption">a number ending in 0 has expired</span>
                  </button>
                </div>
                <div className="triggers">
                  <label className="field">
                    Seat
                    <input value={seat} onChange={(e) => setSeat(e.target.value)} aria-label="Seat" />
                  </label>
                  <button
                    type="button"
                    aria-label="Check in"
                    className={`trigger${canCheckIn ? " avail" : ""}`}
                    disabled={!canCheckIn}
                    onClick={() => booking.send("CheckIn", { seat })}
                  >
                    <span className="trigger-name">CheckIn</span>
                    <span className="trigger-caption">in seat {seat}, and get a boarding pass</span>
                  </button>
                </div>
              </>
            )}
            {state && ENDED.has(state) && (
              <button type="button" className="primary" onClick={() => booking.forget()}>
                New booking
              </button>
            )}
            {booking.problem && (
              <p className="problem" role="alert">
                {booking.problem.message}
              </p>
            )}
            {booking.context && (
              <div className="context">
                <span className="context-item">
                  <span className="context-key">passport</span>
                  <span className="context-value">{String(booking.context.passport || "–")}</span>
                </span>
                <span className="context-item">
                  <span className="context-key">seat</span>
                  <span className="context-value">{String(booking.context.seat ?? "–")}</span>
                </span>
              </div>
            )}
          </div>
        </section>

        <section aria-label="Your bookings" className="panel">
          <div className="machine-head">
            <h2>Your bookings</h2>
          </div>
          {rows.length === 0 ? (
            <p className="list-empty">None yet.</p>
          ) : (
            <ul className="items">
              {rows.map((b) => (
                <li key={b.id} data-booking={b.id}>
                  <span className="item-icon" aria-hidden>
                    ✈
                  </span>
                  <span className="item-text">TX101{b.seat ? `, seat ${b.seat}` : ""}</span>
                  <span className={`badge${STATUS_TONE[b.status ?? ""] ?? ""}`}>{b.status}</span>
                </li>
              ))}
            </ul>
          )}
          {bookings.error && (
            <p className="problem" role="status">
              {bookings.error}
            </p>
          )}
          <p className="view-note">
            The <code>bookings</code> view: your bookings only, with the labels the lexicon gives each state.
          </p>
        </section>
      </div>
      <FeatureSource initial="checking-in.feature" phrases={phrase ? [phrase] : []} />
    </div>
  );
}
