import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { COLUMNS, ORDERS, REFUND_CRASHES, SLICES, type RefundCrashKey } from "../cases";
import { lineOf, SOURCES } from "../sources";
import type { Attempt, Phase, Scenario, Step } from "../types";
import { useRecoveryRun } from "../useRecoveryRun";
import { columnsOf, lanesOf, modelCallsOf, pendingQuestion, questionOf } from "../workflowView";
import { CodePanel } from "./CodePanel";
import { Group } from "./Group";
import { ModelPanel } from "./ModelPanel";
import { WorkflowTimeline } from "./WorkflowTimeline";

// The wizard, its machine and the socket it follows its draft over load when the topic map is first chosen.
const TopicMapWizard = lazy(() => import("./TopicMapWizard").then((m) => ({ default: m.TopicMapWizard })));

// The host retries a failed run after four seconds and polls every second (see the README's demo-only
// settings), so the retry starts four to five seconds after the failure.
const RETRY_AFTER_MS = 4500;

const PHASES: { phases: Phase[]; label: string }[] = [
  { phases: ["starting", "running"], label: "Runs" },
  { phases: ["backoff", "dead"], label: "Breaks" },
  { phases: ["retrying", "requeue"], label: "Recovers" },
];

const LIT: Record<Step["state"], string> = {
  IN_PROGRESS: "var(--info)",
  COMPLETED: "var(--accent-bright)",
  FAILED: "var(--derail)",
  CANCELLED: "var(--derail)",
};

/** The Workflows page: a refund that crashes and recovers, or a topic map built in parallel branches, run live. */
export function Workflows() {
  const [scenario, setScenario] = useState<Scenario>("REFUND");
  const [order, setOrder] = useState(ORDERS[0].key);
  const [slice, setSlice] = useState(SLICES[0].key);
  const [crashAt, setCrashAt] = useState<RefundCrashKey>(REFUND_CRASHES[0].key);
  const crash = crashAt !== "none";
  const recovery = useRecoveryRun();
  const { phase, attempts, run, forkTaken, resumable } = recovery;

  const locked = phase === "starting" || phase === "running" || phase === "backoff" || phase === "retrying" || phase === "requeue";

  // A clock for the countdown and the boxes that fill as they run.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!locked) return;
    const timer = setInterval(() => setNow(Date.now()), 80);
    return () => clearInterval(timer);
  }, [locked]);

  const runIt = () => {
    if (scenario === "REFUND")
      void recovery.start(scenario, crash, {
        orderId: order,
        ...(crash ? { crashWhile: crashAt === "emailing" ? "EMAILING" : "PAYING" } : {}),
      });
    else {
      const chosen = SLICES.find((s) => s.key === slice) ?? SLICES[0];
      void recovery.start(scenario, crash, { fromYear: chosen.fromYear, toYear: chosen.toYear });
    }
  };

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

  // The step the code lights: the running junction, else the latest step of the latest attempt.
  const current: Step | null = useMemo(() => {
    const last = attempts[attempts.length - 1];
    if (!last) return null;
    const steps = Object.values(last.steps).sort((a, b) => a.position - b.position);
    return steps.find((s) => s.state === "IN_PROGRESS") ?? steps[steps.length - 1] ?? null;
  }, [attempts]);

  const shown = run?.scenario ?? scenario;
  const source = SOURCES[shown];
  // While the model is being asked, the line that asks it; otherwise the step's own line.
  const asking = useMemo(() => {
    const last = attempts[attempts.length - 1];
    if (!last || last.trainState === "COMPLETED" || last.trainState === "FAILED") return null;
    const node = pendingQuestion(last.graph, Object.values(last.steps));
    return node ? source.code.split("\n").findIndex((l) => l.includes(`, ${questionOf(node)}>`)) : null;
  }, [attempts, source.code]);
  const lit =
    run?.scenario !== shown
      ? null
      : asking != null && asking >= 0
        ? { line: asking, color: LIT.IN_PROGRESS }
        : current && lineOf(source.code, current) >= 0
          ? { line: lineOf(source.code, current), color: LIT[current.state] }
          : null;

  // The boxes come from the train's declared chain as the first run graph read gives it; until one is read, from the
  // same chain written down (cases.ts).
  const columns = useMemo(() => {
    const graph = attempts.find((a) => a.graph?.hasGraph)?.graph;
    return graph ? columnsOf(graph.nodes) : COLUMNS[shown];
  }, [attempts, shown]);
  const lanes = lanesOf(attempts, columns, now);

  // A manifest's own retry replays the answers it can, so it shows no "waiting" card for them.
  const calls = useMemo(() => {
    const replays = (a: Attempt) =>
      a.origin === "manifest" && attempts.filter((x) => x.origin === "manifest").findIndex((x) => x.id === a.id) > 0 && forkTaken === "none";
    return modelCallsOf(attempts, replays);
  }, [attempts, forkTaken]);

  const reached = PHASES.findIndex((p) => p.phases.includes(phase));
  const crashed = attempts.some((a) => a.trainState === "FAILED");
  // A refund that crashed while emailing retries from its checkpoint, which comes after both questions: nothing is
  // left to ask afresh or to change during its backoff.
  const pastCheckpoint = run?.armedCrash === "REFUND_EMAIL";
  const forkable = phase === "backoff" && forkTaken === "none" && !pastCheckpoint;

  return (
    <div className="demo">
      <div className="controls">
        <Group label="Scenario">
          <div role="tablist" className="segmented">
            {(["REFUND", "TOPIC_MAP"] as const).map((s) => (
              <button
                key={s}
                role="tab"
                aria-selected={scenario === s}
                disabled={locked}
                onClick={() => choose(() => setScenario(s))}
                className={scenario === s ? "active" : ""}
              >
                {s === "REFUND" ? "Refund" : "Topic map"}
              </button>
            ))}
          </div>
          {scenario === "REFUND" ? (
            <>
              <Picker label="Order" value={order} options={ORDERS} disabled={locked} onChange={(v) => choose(() => setOrder(v))} />
              <Picker
                label="Crash"
                value={crashAt}
                options={REFUND_CRASHES}
                disabled={locked}
                onChange={(v) => choose(() => setCrashAt(v as RefundCrashKey))}
              />
            </>
          ) : (
            <>
              <Picker label="Papers" value={slice} options={SLICES} disabled={locked} onChange={(v) => choose(() => setSlice(v))} />
              <label className="check">
                <input
                  type="checkbox"
                  checked={crash}
                  disabled={locked}
                  onChange={(e) => choose(() => setCrashAt(e.target.checked ? "paying" : "none"))}
                />
                Crash once in the co-citation branch
              </label>
            </>
          )}
        </Group>

        {scenario === "TOPIC_MAP" && (
          <Group label="Build my map: a state machine">
            <Suspense fallback={<p className="hint">Loading your draft…</p>}>
              <TopicMapWizard />
            </Suspense>
          </Group>
        )}

        <Group label="Actions">
          <button className="primary" onClick={runIt} disabled={locked}>
            Run
          </button>
          <button className="action" onClick={recovery.changeData} disabled={!forkable || run?.scenario === "TOPIC_MAP"}>
            Change the data during the backoff
          </button>
          <div className="action-pair">
            <button className="action" onClick={recovery.askAfresh} disabled={!forkable && phase !== "done"}>
              Ask afresh
            </button>
            <button className="action" onClick={recovery.reset} disabled={!run || locked}>
              Reset
            </button>
          </div>
          {resumable && (
            <button
              className="action"
              onClick={recovery.resumeFromNotify}
              title={`resumeExecution(${resumable.id}, from: "NotifyCustomer#0"): every retry failed, so the run resumes from its checkpoint`}
            >
              Resume from NotifyCustomer
            </button>
          )}
          <p className="signal-line">
            {recovery.problem ? (
              <span className="run-problem">{recovery.problem}</span>
            ) : retryIn != null && forkTaken === "none" ? (
              `Retrying in ${retryIn.toFixed(1)} s`
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
      </div>

      <CodePanel
        tabs={[{ key: shown, source, part: "chain" }]}
        shown={shown}
        lit={lit}
      />
      <ModelPanel calls={calls} />
      <WorkflowTimeline lanes={lanes} />
    </div>
  );
}

function Picker({
  label,
  value,
  options,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  options: readonly { key: string; label: string }[];
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="field">
      {label}
      <select value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
        {options.map((o) => (
          <option key={o.key} value={o.key}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
}
