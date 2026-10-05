import { useEffect, useMemo, useState } from "react";
import { SOURCES } from "../sources";
import type { Attempt, Scenario, Step } from "../types";
import { useRecoveryRun } from "../useRecoveryRun";
import { questionsOf, questionStep } from "../routes";
import { CasePanel } from "./CasePanel";
import { CodePanel } from "./CodePanel";
import { EventFeed } from "./EventFeed";
import { Ledger } from "./Ledger";
import { RouteMap } from "./RouteMap";

// The host retries a failed run after four seconds and polls every second (see the README's demo-only
// settings), so the retry starts four to five seconds after the failure.
const RETRY_AFTER_MS = 4500;

export function App() {
  const [scenario, setScenario] = useState<Scenario>("REFUND");
  const [tab, setTab] = useState<"source" | "events">("source");
  const recovery = useRecoveryRun();
  const showsRun = recovery.run?.scenario === scenario;
  const attempts = showsRun ? recovery.attempts : [];

  // The step the source highlights: the running junction, else the latest step of the latest attempt.
  const current: Step | null = useMemo(() => {
    const last = attempts[attempts.length - 1];
    if (!last) return null;
    const steps = Object.values(last.steps).sort((a, b) => a.position - b.position);
    return steps.find((s) => s.state === "IN_PROGRESS") ?? steps[steps.length - 1] ?? null;
  }, [attempts]);

  return (
    <div className="app">
      <header className="top">
        <div className="brand">
          <span className="mark">T</span>
          <span>Trax · Recovery sample</span>
        </div>
        <h1>A crashed run retries without paying for the model's answers twice</h1>
        <p className="subtitle">
          Trax records every answer with a hash of what it was about. The scheduler's retry reuses an answer only
          while that hash still matches; change the case and the model is asked again.
        </p>
        <a className="dashboard-link" href="http://localhost:5260/trax" target="_blank" rel="noreferrer">
          Dashboard ↗
        </a>
      </header>

      <aside className="sidebar">
        <CasePanel
          scenario={scenario}
          onScenario={setScenario}
          phase={recovery.phase}
          changed={showsRun && recovery.changed}
          onRun={recovery.start}
          onReset={recovery.reset}
        />
      </aside>

      <main className="main">
        <Moment recovery={recovery} scenario={scenario} />
        <Ledger scenario={scenario} attempts={attempts} labelOf={recovery.labelOf} />
        <RouteMap scenario={scenario} attempts={attempts} maxRetries={recovery.run?.maxRetries ?? 2} labelOf={recovery.labelOf} />
      </main>

      <section className="panel side">
        <div className="tabs" role="tablist">
          <button role="tab" aria-selected={tab === "source"} className={tab === "source" ? "active" : ""} onClick={() => setTab("source")}>
            Train source
          </button>
          <button role="tab" aria-selected={tab === "events"} className={tab === "events" ? "active" : ""} onClick={() => setTab("events")}>
            Junction events
          </button>
        </div>
        {tab === "source" ? (
          <CodePanel source={SOURCES[scenario]} current={showsRun ? current : null} />
        ) : (
          <EventFeed attempts={attempts} lines={showsRun ? recovery.lines : []} labelOf={recovery.labelOf} />
        )}
      </section>
    </div>
  );
}

type Recovery = ReturnType<typeof useRecoveryRun>;

/** The one sentence that says where the run is, and during the backoff, the choice the reader has. */
function Moment({ recovery, scenario }: { recovery: Recovery; scenario: Scenario }) {
  const { phase, attempts, run } = recovery;
  const failed = [...attempts].reverse().find((a) => a.trainState === "FAILED");
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    if (phase !== "backoff") return;
    const timer = setInterval(() => setNow(Date.now()), 100);
    return () => clearInterval(timer);
  }, [phase]);

  if (run && run.scenario !== scenario)
    return <div className="moment quiet">Showing the {scenario === "REFUND" ? "refund" : "research"} train. The last run was the other one.</div>;

  if (phase === "backoff" && failed) {
    const ended = failed.endTime ? new Date(failed.endTime).getTime() : now;
    const left = Math.max(0, ended + RETRY_AFTER_MS - now);
    const recorded = countAnswers(failed, scenario);
    return (
      <div className="moment choice">
        <div className="moment-text">
          <strong>
            {failed.failureJunction ?? "A junction"} crashed. The retry starts in {(left / 1000).toFixed(1)} s and
            will reuse {recorded} recorded answer{recorded === 1 ? "" : "s"}.
          </strong>
          <span>Before it does, you can change what the model would see, or ask it again on purpose. Or do nothing.</span>
        </div>
        <div className="moment-actions">
          <button onClick={recovery.changeData} disabled={recovery.changed}>
            {scenario === "REFUND" ? "Add an earlier refund to the order" : "Make the brief for executives"}
          </button>
          <button onClick={recovery.askAfresh}>Ask the model again</button>
        </div>
        <div className="countdown" style={{ width: `${(left / RETRY_AFTER_MS) * 100}%` }} />
      </div>
    );
  }

  if (phase === "done") {
    const last = attempts[attempts.length - 1];
    const replayed = attempts.flatMap((a) => questionsOf(scenario).map((q) => questionStep(a, q.key))).filter((s) => s?.replayed).length;
    return (
      <div className="moment done">
        <div className="moment-text">
          <strong>
            {attempts.filter((a) => a.origin === "manifest").length > 1
              ? replayed > 0
                ? `Recovered. The retry reused ${replayed} answer${replayed === 1 ? "" : "s"} and took the same tracks.`
                : "Recovered. The retry asked the model again, so its tracks follow the new answers."
              : "Completed on the first attempt."}
          </strong>
          <span>Re-run the last execution with every question put to the model, to compare.</span>
        </div>
        <div className="moment-actions">
          <button onClick={recovery.askAfresh} disabled={last?.origin === "requeue" && last.trainState !== "COMPLETED"}>
            Re-run, asking afresh
          </button>
        </div>
      </div>
    );
  }

  const text: Record<string, string> = {
    idle: "Pick a case and run it. With the crash on, the first attempt fails after the model has answered.",
    starting: "Scheduling a one-off manifest…",
    running: "The first attempt is running. Each answer is recorded before the train acts on it.",
    retrying: "The scheduler's retry is running the train again from the top.",
    dead: "Every retry failed, so the manifest is dead-lettered.",
  };
  return <div className={`moment ${phase === "dead" ? "dead" : "quiet"}`}>{text[phase] ?? ""}</div>;
}

function countAnswers(attempt: Attempt, scenario: Scenario) {
  return questionsOf(scenario).filter((q) => questionStep(attempt, q.key)).length;
}
