import { describe, expect, it } from "vitest";
import { LatestReads, mergeSteps } from "./runReads";
import type { Step } from "./types";

const step = (position: number, state: Step["state"], name = `Step${position}`): Step => ({
  position,
  kind: "JUNCTION",
  name,
  state,
  startedAt: "2026-10-08T00:00:00Z",
  endedAt: state === "IN_PROGRESS" ? null : "2026-10-08T00:00:01Z",
  durationMs: state === "IN_PROGRESS" ? null : 1000,
  failureClass: null,
  failureException: state === "FAILED" ? "TimeoutException" : null,
  questionKey: null,
  answer: null,
  confidence: null,
  replayed: false,
  answerWithheld: false,
  nameWithheld: false,
  trackPosition: null,
  attempt: null,
  nodeId: null,
});

describe("mergeSteps", () => {
  it("adds a step the page has not seen, and moves a running one on", () => {
    const running = mergeSteps({}, [step(0, "IN_PROGRESS")]);
    expect(running[0].state).toBe("IN_PROGRESS");

    const done = mergeSteps(running, [step(0, "COMPLETED"), step(1, "IN_PROGRESS")]);
    expect(done[0].state).toBe("COMPLETED");
    expect(done[1].state).toBe("IN_PROGRESS");
  });

  it("never moves an ended step back to running when a stale answer arrives after the newer one", () => {
    // The junction event said the step failed; the poll that started before it answers afterwards.
    const failed = mergeSteps({}, [step(0, "FAILED")]);

    const afterStalePoll = mergeSteps(failed, [step(0, "IN_PROGRESS")]);

    expect(afterStalePoll[0].state).toBe("FAILED");
    expect(afterStalePoll[0].failureException).toBe("TimeoutException");
  });

  it("leaves the steps it was given unchanged", () => {
    const known = { 0: step(0, "IN_PROGRESS") };

    mergeSteps(known, [step(0, "COMPLETED")]);

    expect(known[0].state).toBe("IN_PROGRESS");
  });
});

describe("LatestReads", () => {
  it("drops the answer to a read a later read of the same attempt overtook", () => {
    const reads = new LatestReads();
    const slowPoll = reads.begin(7);
    const finalRead = reads.begin(7);

    expect(reads.isLatest(7, finalRead)).toBe(true);
    expect(reads.isLatest(7, slowPoll)).toBe(false);
  });

  it("counts each attempt's reads on their own", () => {
    const reads = new LatestReads();
    const first = reads.begin(1);
    reads.begin(2);

    expect(reads.isLatest(1, first)).toBe(true);
  });
});
