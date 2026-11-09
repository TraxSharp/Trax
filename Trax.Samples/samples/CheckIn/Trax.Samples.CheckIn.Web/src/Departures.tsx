import { readDeparturesView } from "./trax/index.g";
import { usePoll } from "./usePoll";

/** The departures view: the flight's status, read from the system's flight, which every passenger and gate agent reads. */
export function Departures() {
  const { value, error } = usePoll(readDeparturesView);
  const label = value?.flight?.status;
  const open = label === "Check-in open";
  const closed = label === "Gate closed";
  return (
    <section aria-label="Departures" className={`sign${open ? " open" : ""}${closed ? " closing" : ""}`}>
      <span className="sign-light" aria-hidden />
      <div className="sign-text">
        <div className="sign-label">Departures · TX101</div>
        <div className="sign-value" data-departures>
          {label ?? "…"}
        </div>
      </div>
      <p className="sign-note">
        {error ??
          (open
            ? "A gate agent opened their desk: every booked passenger can check in."
            : closed
              ? "The gate is closed: anyone not checked in missed the flight. It departs in a few seconds."
              : label === "Departed"
                ? "Departed. The next flight is scheduled ten seconds after."
                : "Scheduled. Check-in opens when a gate agent opens their desk. Nobody moves the flight by hand.")}
      </p>
    </section>
  );
}
