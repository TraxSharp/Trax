import { useEffect, useMemo, useState } from "react";
import { ORDERS, SLICES, TOPICS } from "../cases";
import { SOURCES } from "../sources";
import type { Phase, Scenario, Step } from "../types";
import { useRecoveryRun } from "../useRecoveryRun";
import { CodePanel } from "./CodePanel";
import { ConsolePanel } from "./ConsolePanel";
import { TimelinePanel } from "./TimelinePanel";
import { TopicMapWizard } from "./TopicMapWizard";

// The host retries a failed run after four seconds and polls every second (see the README's demo-only
// settings), so the retry starts four to five seconds after the failure.
const RETRY_AFTER_MS = 4500;

const PHASES: { phases: Phase[]; label: string }[] = [
  { phases: ["starting", "running"], label: "Runs" },
  { phases: ["backoff", "dead"], label: "Breaks" },
  { phases: ["retrying", "requeue"], label: "Recovers" },
];

export function App() {
  const [scenario, setScenario] = useState<Scenario>("RESEARCH");
  const [topic, setTopic] = useState(TOPICS[0].key);
  const [order, setOrder] = useState(ORDERS[0].key);
  const [slice, setSlice] = useState(SLICES[0].key);
  const [crash, setCrash] = useState(true);
  const recovery = useRecoveryRun();
  const { phase, attempts, run, forkTaken } = recovery;

  const locked = phase === "starting" || phase === "running" || phase === "backoff" || phase === "retrying" || phase === "requeue";

  // A clock for the countdown and the timeline's running bars.
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    if (!locked) return;
    const timer = setInterval(() => setNow(Date.now()), 50);
    return () => clearInterval(timer);
  }, [locked]);

  const runIt = () => {
    if (scenario === "RESEARCH") {
      const subject = TOPICS.find((t) => t.key === topic)?.label ?? TOPICS[0].label;
      void recovery.start(scenario, crash, { topic: subject });
    } else if (scenario === "REFUND") void recovery.start(scenario, crash, { orderId: order });
    else {
      const chosen = SLICES.find((s) => s.key === slice) ?? SLICES[0];
      void recovery.start(scenario, crash, { fromYear: chosen.fromYear, toYear: chosen.toYear });
    }
  };

  // What the picker offers for each scenario, and what Crash once does in it.
  const picker = {
    RESEARCH: { label: "Topic", value: topic, set: setTopic, options: TOPICS, crashAt: "while it writes the report" },
    REFUND: { label: "Order", value: order, set: setOrder, options: ORDERS, crashAt: "in the step after the approval" },
    TOPIC_MAP: { label: "Papers", value: slice, set: setSlice, options: SLICES, crashAt: "in the co-citation branch" },
  }[scenario];

  // Changing what to run clears the last run.
  const choose = (change: () => void) => {
    change();
    recovery.reset();
  };

  const retryIn = useMemo(() => {
    if (phase !== "backoff") return null;
    const failed = [...attempts].reverse().find((a) => a.trainState === "FAILED");
    const ended = failed?.endTime ? Date.parse(failed.endTime) : now;
    return Math.max(0, (ended + RETRY_AFTER_MS - now) / 1000);
  }, [phase, attempts, now]);

  // The step the source highlights: the running junction, else the latest step of the latest attempt.
  const current: Step | null = useMemo(() => {
    const last = attempts[attempts.length - 1];
    if (!last) return null;
    const steps = Object.values(last.steps).sort((a, b) => a.position - b.position);
    return steps.find((s) => s.state === "IN_PROGRESS") ?? steps[steps.length - 1] ?? null;
  }, [attempts]);

  const shown = run?.scenario ?? scenario;
  const reached = PHASES.findIndex((p) => p.phases.includes(phase));
  const crashed = attempts.some((a) => a.trainState === "FAILED");
  const canAskAfresh = (phase === "backoff" && forkTaken === "none") || phase === "done";

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="intro">
          <span className="eyebrow">
            <span className="mark">T</span>
            Trax · Recovery
          </span>
          <h1>A crashed run retries without paying for the model twice</h1>
          <p className="subtitle">
            Trax records every answer with a hash of what it was about. The scheduler&apos;s retry reuses an answer only
            while that hash still matches; change the data during the backoff and the model is asked again.
          </p>
        </div>

        <Group label="Scenario">
          <div role="tablist" className="segmented">
            {(["RESEARCH", "REFUND", "TOPIC_MAP"] as const).map((s) => (
              <button
                key={s}
                role="tab"
                aria-selected={scenario === s}
                disabled={locked}
                onClick={() => choose(() => setScenario(s))}
                className={scenario === s ? "active" : ""}
              >
                {s === "RESEARCH" ? "Research" : s === "REFUND" ? "Refund" : "Topic map"}
              </button>
            ))}
          </div>
          <label className="field">
            {picker.label}
            <select value={picker.value} disabled={locked} onChange={(e) => choose(() => picker.set(e.target.value))}>
              {picker.options.map((o) => (
                <option key={o.key} value={o.key}>
                  {o.label}
                </option>
              ))}
            </select>
          </label>
          <label className="check">
            <input type="checkbox" checked={crash} disabled={locked} onChange={(e) => choose(() => setCrash(e.target.checked))} />
            Crash once ({picker.crashAt})
          </label>
        </Group>

        {scenario === "TOPIC_MAP" && (
          <Group label="Build my map: a state machine">
            <TopicMapWizard />
          </Group>
        )}

        <Group label="Actions">
          <button className="primary" onClick={runIt} disabled={locked}>
            Run
          </button>
          <button
            className="action"
            onClick={recovery.changeData}
            disabled={phase !== "backoff" || forkTaken !== "none" || run?.scenario === "TOPIC_MAP"}
          >
            Change the data during the backoff
          </button>
          <div className="action-pair">
            <button className="action" onClick={recovery.askAfresh} disabled={!canAskAfresh}>
              Ask afresh
            </button>
            <button className="action" onClick={recovery.reset} disabled={!run || locked}>
              Reset
            </button>
          </div>
          <p className="hint">
            {retryIn != null && forkTaken === "none" ? (
              <span className="signal">
                In the backoff: the retry starts in {retryIn.toFixed(1)} s. Change the data or ask afresh before it does.
              </span>
            ) : phase === "done" ? (
              "The run is over. Ask afresh to run it again with every question put to the model."
            ) : phase === "dead" ? (
              "Every retry failed, so the manifest is dead-lettered."
            ) : null}
          </p>
        </Group>

        <Group label="Progress">
          <ol className="progress">
            {PHASES.map((p, i) => {
              // A run that never crashed skips Breaks and Recovers.
              const done = phase === "done" ? i === 0 || crashed : i < reached;
              const tone = done ? "done" : i === reached ? (i === 1 ? "broken" : "current") : "";
              return (
                <li key={p.label} className={tone}>
                  {p.label}
                </li>
              );
            })}
          </ol>
        </Group>

        <a className="dashboard-link" href="http://localhost:5260/trax" target="_blank" rel="noreferrer">
          Open the Trax dashboard ↗
        </a>
      </aside>

      <CodePanel source={SOURCES[shown]} current={run?.scenario === shown ? current : null} />
      <ConsolePanel lines={recovery.lines} />
      <TimelinePanel
        attempts={attempts}
        maxRetries={run?.maxRetries ?? 2}
        labelOf={recovery.labelOf}
        sinceStart={recovery.sinceStart}
        elapsed={recovery.sinceStart(new Date(now).toISOString()) ?? 0}
      />
    </div>
  );
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="group">
      <span className="group-label">{label}</span>
      {children}
    </div>
  );
}
