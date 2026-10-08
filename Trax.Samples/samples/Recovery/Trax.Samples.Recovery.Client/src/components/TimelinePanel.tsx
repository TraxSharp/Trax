import { branchOf, shownAnswer, type Attempt, type Step } from "../types";
import { RunGraphView } from "./RunGraphView";

interface Props {
  attempts: Attempt[];
  maxRetries: number;
  labelOf(attempt: Attempt): string;
  /** Milliseconds since Run was pressed, for a timestamp the host sent. */
  sinceStart(iso: string | null | undefined): number | undefined;
  /** Milliseconds since Run was pressed, now. */
  elapsed: number;
}

/** Why a question's answer was or was not replayed, from its junction event and the attempt's decision journal. */
function badgeOf(attempts: Attempt[], attempt: Attempt, step: Step): { text: string; tone: string } | null {
  if (step.kind === "JUNCTION" || step.kind === "ROUTE") return null;
  if (step.replayed) return { text: "replayed: model not asked", tone: "replay" };
  const entry = attempt.journal?.decisions.find((d) => d.questionKey === step.questionKey);
  if (entry?.replayRefused) return { text: "asked afresh: state changed", tone: "afresh" };
  if (attempt.journal?.replayAbandoned) return { text: "asked afresh: replay abandoned", tone: "afresh" };
  if (attempts[0]?.id !== attempt.id)
    return attempt.journal && attempt.journal.replayDecisionsOf == null
      ? { text: "asked afresh: on purpose", tone: "afresh" }
      : { text: "asked afresh", tone: "afresh" };
  return { text: "model asked", tone: "model" };
}

function barTone(step: Step): string {
  if (step.kind !== "JUNCTION") return step.replayed ? "question replayed" : "question";
  if (step.state === "FAILED") return "failed";
  if (step.state === "IN_PROGRESS") return "running";
  return "ran";
}

export function TimelinePanel({ attempts, maxRetries, labelOf, sinceStart, elapsed }: Props) {
  if (attempts.length === 0)
    return (
      <div className="panel timeline">
        <div className="panel-title">Timeline</div>
        <p className="empty">Each attempt&apos;s junctions, questions and tracks appear here as they happen.</p>
      </div>
    );

  const running = attempts.some((a) => a.trainState === "IN_PROGRESS" || a.trainState === "PENDING");
  const t0 = Math.min(...attempts.map((a) => sinceStart(a.startTime) ?? 0));
  const ends = attempts.flatMap((a) => [
    sinceStart(a.endTime) ?? 0,
    ...Object.values(a.steps).map((s) => sinceStart(s.endedAt) ?? sinceStart(s.startedAt) ?? 0),
  ]);
  const t1 = Math.max(running ? elapsed : 0, ...ends, t0 + 1000);

  return (
    <div className="panel timeline">
      <div className="panel-title">Timeline</div>
      {attempts.map((attempt) => (
        <Lane key={attempt.id} {...{ attempts, attempt, maxRetries, labelOf, sinceStart, elapsed, t0, t1 }} />
      ))}
    </div>
  );
}

function Lane({
  attempts,
  attempt,
  maxRetries,
  labelOf,
  sinceStart,
  elapsed,
  t0,
  t1,
}: Omit<Props, "attempts"> & { attempts: Attempt[]; attempt: Attempt; t0: number; t1: number }) {
  const steps = Object.values(attempt.steps).sort((a, b) => a.position - b.position);
  const manifestIndex = attempts.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id);
  const [status, statusTone] =
    attempt.trainState === "COMPLETED"
      ? [manifestIndex > 0 ? "Recovered" : "Completed", "completed"]
      : attempt.trainState === "FAILED"
        ? [
            attempt.origin === "manifest" && manifestIndex < maxRetries ? `Failed, retrying ${manifestIndex + 1}/${maxRetries}` : "Failed",
            "failed",
          ]
        : ["Running", "running"];

  // A question's bar runs from the previous step's end to its answer: the time the model took.
  const bars: { step: Step; from: number; to: number }[] = [];
  let previousEnd = sinceStart(attempt.startTime) ?? 0;
  for (const step of steps) {
    const started = sinceStart(step.startedAt) ?? previousEnd;
    const ended = sinceStart(step.endedAt) ?? (step.state === "IN_PROGRESS" ? elapsed : started);
    const from = step.kind === "JUNCTION" ? started : Math.min(previousEnd, started);
    const to = step.kind === "JUNCTION" ? ended : started;
    previousEnd = Math.max(previousEnd, to);
    bars.push({ step, from, to });
  }

  // The lane is a row: each bar and each gap between bars grows with the time it covers, so the lanes
  // keep one time axis, but a bar never shrinks below its name. A lane too long for the panel scrolls.
  const segments: ({ kind: "gap"; ms: number } | { kind: "bar"; ms: number; step: Step })[] = [];
  let cursor = t0;
  for (const { step, from, to } of bars) {
    if (from > cursor) segments.push({ kind: "gap", ms: from - cursor });
    segments.push({ kind: "bar", ms: Math.max(to - from, 0), step });
    cursor = Math.max(cursor, to);
  }
  if (t1 > cursor) segments.push({ kind: "gap", ms: t1 - cursor });

  const nameOf = (step: Step) => {
    const name = step.nameWithheld ? "(withheld)" : step.kind === "JUNCTION" ? step.name : `${step.questionKey}?`;
    const branch = branchOf(step.nodeId);
    return branch ? `${branch}: ${name}` : name;
  };

  return (
    <div className="lane">
      <div className="lane-head">
        <span>
          <strong>{labelOf(attempt)}</strong> execution {attempt.id}
        </span>
        {steps.map((step) => {
          const badge = badgeOf(attempts, attempt, step);
          return badge ? (
            <span key={step.position} className={`badge ${badge.tone}`}>
              {step.questionKey} = {step.answerWithheld ? "(withheld)" : shownAnswer(step.answer)}: {badge.text}
            </span>
          ) : null;
        })}
        {attempt.failureJunction && <span className="badge failed">crashed in {attempt.failureJunction}</span>}
        <span className={`status ${statusTone}`}>{status}</span>
      </div>
      <div className="lane-track">
        {segments.map((segment, i) => {
          if (segment.kind === "gap") return <div key={`gap-${i}`} className="gap" style={{ flex: `${segment.ms} 1 0px` }} />;
          const step = segment.step;
          const title = [
            step.kind === "ROUTE" ? `→ ${step.answer}` : nameOf(step),
            step.answer != null ? `answer ${shownAnswer(step.answer)}` : "",
            badgeOf(attempts, attempt, step)?.text ?? "",
          ]
            .filter(Boolean)
            .join("\n");
          // A route is a marker between two steps and takes no width of its own.
          if (step.kind === "ROUTE") return <div key={step.position} title={title} className="route-mark" />;
          return (
            <div key={step.position} title={title} className={`bar ${barTone(step)}`} style={{ flex: `${Math.max(segment.ms, 1)} 1 0px` }}>
              {nameOf(step)}
            </div>
          );
        })}
      </div>
      {attempt.graph?.hasGraph && <RunGraphView graph={attempt.graph} />}
    </div>
  );
}
