import type { JunctionStep } from "../types";
import { enumLabel, formatMs } from "./format";

// The largest junctionRuns page the API returns, and so the most steps the timeline shows, as the
// Blazor dashboard does: a run with more says so rather than ending its timeline silently.
export const MAX_JUNCTION_STEPS = 500;

/**
 * Merge steps read from the API or carried by onJunctionEvent into the held list: a step replaces
 * the held one at its position (a step's row is updated in place when it ends), the result is
 * ordered by position and holds at most MAX_JUNCTION_STEPS. Returns the held list itself when
 * nothing changed, so a caller can skip re-rendering.
 */
export function mergeSteps(held: JunctionStep[], incoming: JunctionStep[]): JunctionStep[] {
  if (incoming.length === 0) return held;
  const byPosition = new Map(held.map((s) => [s.position, s]));
  let changed = false;
  for (const step of incoming) {
    const current = byPosition.get(step.position);
    if (current && sameStep(current, step)) continue;
    byPosition.set(step.position, step);
    changed = true;
  }
  if (!changed) return held;
  return [...byPosition.values()]
    .sort((a, b) => a.position - b.position)
    .slice(0, MAX_JUNCTION_STEPS);
}

function sameStep(a: JunctionStep, b: JunctionStep): boolean {
  return (
    a.state === b.state &&
    a.endedAt === b.endedAt &&
    a.durationMs === b.durationMs &&
    a.answer === b.answer &&
    a.failureException === b.failureException
  );
}

/**
 * Where an incremental read starts: just before the first step still in progress (its row is
 * re-read, since it is updated when it ends), else after the last held step. Null reads from the
 * beginning.
 */
export function incrementalCursor(held: JunctionStep[]): number | null {
  const firstInProgress = held.find((s) => s.state === "IN_PROGRESS");
  if (firstInProgress) return firstInProgress.position - 1;
  return held.length > 0 ? held[held.length - 1].position : null;
}

export interface TimelineRow {
  step: JunctionStep;
  // Fractions of the timeline's span, 0..1.
  left: number;
  width: number;
  // A question or route is drawn as a point; a junction as a bar.
  isPoint: boolean;
  duration: string;
}

const END_NOT_RECORDED = "end not recorded";

/**
 * Lay the steps out on one time axis, from the run's start (or the first step, if earlier) to its
 * end, or to `now` while it runs. An in-progress step's bar extends to now while the run runs, and
 * to the run's end once it has one; its duration reads "so far", or "end not recorded" when the
 * run ended without the step's end being written. Mirrors the Blazor JunctionTimeline.
 */
export function layoutTimeline(
  steps: JunctionStep[],
  runStart: string,
  runEnd: string | null,
  now: number,
): TimelineRow[] {
  if (steps.length === 0) return [];
  const runEndMs = runEnd ? Date.parse(runEnd) : null;
  let origin = Date.parse(runStart);
  const firstStart = Math.min(...steps.map((s) => Date.parse(s.startedAt)));
  if (!Number.isFinite(origin) || firstStart < origin) origin = firstStart;

  let end = runEndMs ?? now;
  for (const step of steps) end = Math.max(end, endOf(step, now, runEndMs));
  const span = Math.max(end - origin, 1);

  return steps.map((step) => {
    const started = Date.parse(step.startedAt);
    const stepEnd = endOf(step, now, runEndMs);
    const left = clamp((started - origin) / span, 0, 1);
    const width = clamp((stepEnd - started) / span, 0, 1 - left);
    const inProgress = step.state === "IN_PROGRESS";
    const duration =
      step.durationMs != null
        ? formatMs(step.durationMs)
        : inProgress && runEndMs != null
          ? END_NOT_RECORDED
          : formatMs(Math.max(stepEnd - started, 0)) + (inProgress ? " so far" : "");
    return {
      step,
      left,
      width: Math.max(width, 0.005),
      isPoint: step.kind !== "JUNCTION",
      duration,
    };
  });
}

// A step that has not ended is drawn up to now while its run runs, and up to the run's end once
// the run has one, since a step cannot outlast its run; a step that ended without an end time (a
// question, or a row whose end was dropped) is drawn as a point.
function endOf(step: JunctionStep, now: number, runEnd: number | null): number {
  const started = Date.parse(step.startedAt);
  if (step.endedAt) return Date.parse(step.endedAt);
  if (step.state !== "IN_PROGRESS") return started;
  const until = runEnd ?? now;
  return Math.max(until, started);
}

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(Math.max(n, lo), hi);
}

/** The attempt the run's steps record (the first one that has it), or null. */
export function attemptOf(steps: JunctionStep[]): number | null {
  return steps.find((s) => s.attempt != null)?.attempt ?? null;
}

const KIND_LABEL: Record<string, string> = {
  CHOICE: "Choice",
  SCORE: "Score",
  YES_NO: "Yes/No",
  ROUTE: "Route",
};

export function kindLabel(kind: string): string {
  return KIND_LABEL[kind] ?? kind;
}

/** "0.92" -> "92%". */
export function formatConfidence(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

/** A failed or cancelled step's one-line explanation: "Cancelled" or the class, then the exception. */
export function stepFailure(step: JunctionStep): string | null {
  if (step.state !== "FAILED" && step.state !== "CANCELLED") return null;
  const head = step.state === "CANCELLED" ? "Cancelled" : step.failureClass && enumLabel(step.failureClass);
  return [head, step.failureException].filter((s) => s).join(" · ");
}
