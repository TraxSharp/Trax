import { describe, expect, test } from "vitest";
import {
  MAX_JUNCTION_STEPS,
  attemptOf,
  formatConfidence,
  incrementalCursor,
  kindLabel,
  layoutTimeline,
  mergeSteps,
  stepFailure,
} from "./junctionSteps";
import type { JunctionStep } from "../types";

const T0 = Date.parse("2026-07-07T12:00:00.000Z");
const at = (ms: number) => new Date(T0 + ms).toISOString();

function step(position: number, fields: Partial<JunctionStep> = {}): JunctionStep {
  return {
    position,
    kind: "JUNCTION",
    name: `Step${position}`,
    state: "COMPLETED",
    startedAt: at((position - 1) * 1000),
    endedAt: at((position - 1) * 1000 + 500),
    durationMs: 500,
    failureClass: null,
    failureException: null,
    questionKey: null,
    answer: null,
    confidence: null,
    replayed: false,
    decider: null,
    answerWithheld: false,
    attempt: null,
    nameWithheld: false,
    trackPosition: null,
    ...fields,
  };
}

describe("mergeSteps", () => {
  test("upserts by position and keeps position order", () => {
    const held = [step(1), step(3)];
    const merged = mergeSteps(held, [step(2), step(3, { state: "FAILED" })]);
    expect(merged.map((s) => s.position)).toEqual([1, 2, 3]);
    expect(merged[2].state).toBe("FAILED");
  });

  test("returns the held list itself when nothing changed", () => {
    const held = [step(1), step(2)];
    expect(mergeSteps(held, [step(2)])).toBe(held);
    expect(mergeSteps(held, [])).toBe(held);
  });

  test("caps the list at the API's page", () => {
    const many = Array.from({ length: MAX_JUNCTION_STEPS + 5 }, (_, i) => step(i + 1));
    expect(mergeSteps([], many)).toHaveLength(MAX_JUNCTION_STEPS);
  });
});

describe("incrementalCursor", () => {
  test("re-reads from the first step still in progress", () => {
    expect(incrementalCursor([step(1), step(2, { state: "IN_PROGRESS" }), step(3)])).toBe(1);
  });
  test("reads after the last held step when none is in progress", () => {
    expect(incrementalCursor([step(1), step(2)])).toBe(2);
  });
  test("reads from the start when nothing is held", () => {
    expect(incrementalCursor([])).toBeNull();
  });
});

describe("layoutTimeline", () => {
  test("lays steps out from the run's start to its end", () => {
    const rows = layoutTimeline([step(1), step(2)], at(0), at(2000), T0 + 5000);
    expect(rows[0].left).toBe(0);
    expect(rows[1].left).toBeCloseTo(0.5);
    expect(rows[0].width).toBeCloseTo(0.25);
    expect(rows[0].duration).toBe("500 ms");
  });

  test("a running step's duration is so far, until now", () => {
    const running = step(1, { state: "IN_PROGRESS", endedAt: null, durationMs: null });
    const [row] = layoutTimeline([running], at(0), null, T0 + 3000);
    expect(row.duration).toBe("3.0 s so far");
    expect(row.width).toBeCloseTo(1);
  });

  test("a step still in progress when its run ended has no recorded end", () => {
    const stuck = step(1, { state: "IN_PROGRESS", endedAt: null, durationMs: null });
    const [row] = layoutTimeline([stuck], at(0), at(4000), T0 + 60_000);
    expect(row.duration).toBe("end not recorded");
  });

  test("a question is drawn as a point", () => {
    const question = step(1, { kind: "YES_NO", endedAt: null, durationMs: null });
    const [row] = layoutTimeline([question], at(0), at(1000), T0 + 1000);
    expect(row.isPoint).toBe(true);
  });

  test("a step that started before the run moves the origin back", () => {
    const early = step(1, { startedAt: at(-1000), endedAt: at(0), durationMs: 1000 });
    const [row] = layoutTimeline([early], at(0), at(1000), T0 + 1000);
    expect(row.left).toBe(0);
    expect(row.width).toBeCloseTo(0.5);
  });

  test("no steps lay out no rows", () => {
    expect(layoutTimeline([], at(0), null, T0)).toEqual([]);
  });
});

describe("step labels", () => {
  test("attempt comes from the first step that records one", () => {
    expect(attemptOf([step(1), step(2, { attempt: 3 })])).toBe(3);
    expect(attemptOf([step(1)])).toBeNull();
  });
  test("kinds, confidence and failures read as in the Blazor timeline", () => {
    expect(kindLabel("YES_NO")).toBe("Yes/No");
    expect(kindLabel("ROUTE")).toBe("Route");
    expect(formatConfidence(0.925)).toBe("93%");
    expect(stepFailure(step(1, { state: "FAILED", failureClass: "TRANSIENT", failureException: "Timeout" }))).toBe(
      "Transient · Timeout",
    );
    expect(stepFailure(step(1, { state: "CANCELLED" }))).toBe("Cancelled");
    expect(stepFailure(step(1))).toBeNull();
  });
});
