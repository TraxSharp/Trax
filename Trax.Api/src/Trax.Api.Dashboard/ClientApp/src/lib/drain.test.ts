import { describe, expect, test } from "vitest";
import { DEFAULT_DRAIN_MINUTES, drainMark, drainRecheckDelay, drainSetBy, parseDrainMinutes } from "./drain";

describe("drainMark", () => {
  test("says Drained only while a drain is in force", () => {
    expect(drainMark({ drained: true })).toBe("Drained");
    expect(drainMark({ drained: false })).toBeNull();
    expect(drainMark(null)).toBeNull();
    expect(drainMark(undefined)).toBeNull();
  });
});

describe("drainRecheckDelay", () => {
  test("reads again a second past the until, at once when it has passed", () => {
    const now = Date.parse("2026-11-25T12:00:00Z");
    expect(drainRecheckDelay("2026-11-25T12:10:00Z", now)).toBe(601_000);
    expect(drainRecheckDelay("2026-11-25T11:00:00Z", now)).toBe(1000);
  });

  test("caps the wait at what a browser timer holds, and guesses a minute for an unreadable until", () => {
    expect(drainRecheckDelay("2099-01-01T00:00:00Z", 0)).toBe(2_147_483_647);
    expect(drainRecheckDelay("soon", 0)).toBe(60_000);
  });
});

describe("parseDrainMinutes", () => {
  test("takes a whole number from 1 to 1440", () => {
    expect(parseDrainMinutes(String(DEFAULT_DRAIN_MINUTES))).toBe(20);
    expect(parseDrainMinutes(" 1 ")).toBe(1);
    expect(parseDrainMinutes("1440")).toBe(1440);
  });

  test("refuses anything else, saying why", () => {
    expect(parseDrainMinutes("0")).toBe("A drain lasts from 1 to 1440 minutes.");
    expect(parseDrainMinutes("1441")).toBe("A drain lasts from 1 to 1440 minutes.");
    for (const bad of ["", "abc", "1.5", "-3", "10m"])
      expect(parseDrainMinutes(bad)).toBe("Give the drain's length as a whole number of minutes.");
  });
});

describe("drainSetBy", () => {
  test("names a deploy, an operator, or nobody known", () => {
    expect(drainSetBy("deploy")).toBe("a deploy");
    expect(drainSetBy("apikey:ops")).toBe("apikey:ops");
    expect(drainSetBy(null)).toBe("someone");
  });
});
