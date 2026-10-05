import { ROUTES, junctionStep, provenanceOf, questionStep, routeStep, type Stop } from "../routes";
import type { Attempt, Scenario, Step } from "../types";

interface Props {
  scenario: Scenario;
  attempts: Attempt[];
  maxRetries: number;
  labelOf(attempt: Attempt, index: number): string;
}

const stateClass = (step: Step | undefined) => (step ? step.state.toLowerCase() : "ahead");

/** One line per attempt: the train's whole route, with the tracks this attempt took lit. */
export function RouteMap({ scenario, attempts, maxRetries, labelOf }: Props) {
  const route = ROUTES[scenario];
  return (
    <section className="panel routes">
      <header className="panel-head">
        <h2>Route taken</h2>
        <div className="legend">
          <span className="key completed">ran</span>
          <span className="key in_progress">running</span>
          <span className="key failed">crashed</span>
          <span className="key replayed">answer replayed</span>
        </div>
      </header>
      <div className="routes-scroll">
        {attempts.length === 0 ? (
          <Line route={route} attempt={null} index={0} label="Route" status={null} />
        ) : (
          attempts.map((a, index) => (
            <Line key={a.id} route={route} attempt={a} index={index} label={labelOf(a, index)} status={statusOf(a, index, maxRetries, attempts)} />
          ))
        )}
      </div>
    </section>
  );
}

function statusOf(a: Attempt, index: number, maxRetries: number, all: Attempt[]): { text: string; tone: string } {
  if (a.trainState === "COMPLETED")
    return { text: index > 0 && a.origin === "manifest" ? "Recovered" : "Completed", tone: "completed" };
  if (a.trainState === "FAILED") {
    if (a.origin !== "manifest" || index >= maxRetries) return { text: "Crashed", tone: "failed" };
    const retried = all.slice(index + 1).some((x) => x.origin === "manifest");
    return { text: retried ? "Crashed, retried" : "Crashed, retry scheduled", tone: "failed" };
  }
  return { text: "Running", tone: "in_progress" };
}

function Line({ route, attempt, index, label, status }: {
  route: Stop[];
  attempt: Attempt | null;
  index: number;
  label: string;
  status: { text: string; tone: string } | null;
}) {
  return (
    <div className={`line-row ${attempt ? "" : "idle"}`}>
      <div className="line-head">
        <strong>{label}</strong>
        {status && <span className={`status ${status.tone}`}>{status.text}</span>}
      </div>
      <ol className="route-line">
        {route.map((stop) =>
          stop.kind === "junction" ? (
            <Station key={stop.name} name={stop.name} step={attempt ? junctionStep(attempt, stop.name) : undefined} />
          ) : (
            <Fork key={stop.key} stop={stop} attempt={attempt} index={index} />
          ),
        )}
      </ol>
    </div>
  );
}

function Station({ name, step }: { name: string; step: Step | undefined }) {
  return (
    <li className={`station ${stateClass(step)}`} title={step?.failureException ?? undefined}>
      <span className="dot" />
      <span className="station-name">{name}</span>
    </li>
  );
}

function Fork({ stop, attempt, index }: { stop: Extract<Stop, { kind: "question" }>; attempt: Attempt | null; index: number }) {
  const question = attempt ? questionStep(attempt, stop.key) : undefined;
  const route = attempt ? routeStep(attempt, stop.key) : undefined;
  const provenance = attempt && question ? provenanceOf(attempt, index, question) : null;
  return (
    <li className="fork">
      <span className={`switch ${question ? (provenance === "replayed" ? "replayed" : provenance === "model" ? "asked" : "afresh") : "ahead"}`} title={stop.label} />
      <div className="fork-body">
      <span className="fork-label">{stop.key}?</span>
      <ul className="tracks">
        {stop.tracks.map((t) => {
          const taken = route?.answer === t.answer;
          const step = attempt && taken ? junctionStep(attempt, t.junction) : undefined;
          return (
            <li key={t.answer} className={`track ${taken ? `taken ${stateClass(step)}` : route ? "passed" : "ahead"}`} title={step?.failureException ?? undefined}>
              <span className="track-answer">{t.answer}</span>
              <span className="track-junction">{t.junction}</span>
            </li>
          );
        })}
      </ul>
      </div>
    </li>
  );
}
