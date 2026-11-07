import { useRememberedShift } from "./trax/index.g";

/** A baker's page: their shift. Clocking in opens the shop, which puts the placed orders in the oven. */
export function Baker() {
  const shift = useRememberedShift();
  return (
    <section aria-label="Your shift" className="card">
      <h2>Your shift</h2>
      <p>
        It is <strong data-shift-state>{shift.state ?? "…"}</strong>.
      </p>
      <button type="button" disabled={!shift.available.includes("ClockIn")} onClick={() => shift.send("ClockIn")}>
        Clock in
      </button>
      {shift.problem && (
        <p className="problem" role="alert">
          {shift.problem.message}
        </p>
      )}
    </section>
  );
}
