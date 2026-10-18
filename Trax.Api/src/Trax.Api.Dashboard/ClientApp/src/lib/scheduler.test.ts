import { describe, expect, test } from "vitest";
import { schedulerPaused } from "./scheduler";

describe("schedulerPaused", () => {
  test("says nothing while both halves run, or before the config loads", () => {
    expect(schedulerPaused({ manifestManagerEnabled: true, jobDispatcherEnabled: true })).toBeNull();
    expect(schedulerPaused(null)).toBeNull();
  });

  test("names which half is paused", () => {
    expect(schedulerPaused({ manifestManagerEnabled: false, jobDispatcherEnabled: false })).toBe("Scheduler paused");
    expect(schedulerPaused({ manifestManagerEnabled: false, jobDispatcherEnabled: true })).toBe("Schedules paused");
    expect(schedulerPaused({ manifestManagerEnabled: true, jobDispatcherEnabled: false })).toBe("Dispatch paused");
  });
});
