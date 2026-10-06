import { describe, expect, test } from "vitest";
import { reportBatchTrigger } from "./batchTrigger";
import type { BatchTriggerResponse } from "../types";

const base: BatchTriggerResponse = {
  success: true,
  matched: 2,
  queued: 2,
  alreadyQueued: 0,
  tooLateToAskAfresh: 0,
  skipped: 0,
  message: "2 queued across 2 of 2 manifest(s).",
  notes: [],
};

describe("reportBatchTrigger", () => {
  test("a batch that triggered every id is a success and clears the selection", () => {
    expect(reportBatchTrigger(base)).toEqual({
      refused: false,
      severity: "success",
      message: base.message,
      notes: [],
      clearSelection: true,
    });
  });

  test("a note makes it a warning and is reported", () => {
    const r = reportBatchTrigger({
      ...base,
      queued: 1,
      skipped: 1,
      notes: [{ id: 99, message: "Manifest 99 not found." }],
    });
    expect(r.severity).toBe("warning");
    expect(r.notes).toEqual(["Manifest 99 not found."]);
    expect(r.clearSelection).toBe(true);
  });

  test("an entry that was already queued, or claimed too late to ask afresh, counts as triggered", () => {
    expect(reportBatchTrigger({ ...base, queued: 0, alreadyQueued: 1 }).severity).toBe("success");
    expect(reportBatchTrigger({ ...base, queued: 0, tooLateToAskAfresh: 1 }).severity).toBe("success");
  });

  test("triggering nothing is a warning", () => {
    expect(reportBatchTrigger({ ...base, matched: 0, queued: 0, message: "0 queued." }).severity).toBe("warning");
  });

  test("a refusal is an error that keeps the selection", () => {
    const r = reportBatchTrigger({ ...base, success: false, queued: 0, message: "No ids were given." });
    expect(r).toMatchObject({ refused: true, severity: "error", message: "No ids were given.", clearSelection: false });
  });
});
