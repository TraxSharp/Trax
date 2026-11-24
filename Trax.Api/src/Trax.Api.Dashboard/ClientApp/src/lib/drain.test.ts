import { describe, expect, test } from "vitest";
import { DEFAULT_DRAIN_MINUTES, drainMark, drainSetBy, parseDrainMinutes } from "./drain";

describe("drainMark", () => {
  test("says Drained only while a drain is in force", () => {
    expect(drainMark({ drained: true })).toBe("Drained");
    expect(drainMark({ drained: false })).toBeNull();
    expect(drainMark(null)).toBeNull();
    expect(drainMark(undefined)).toBeNull();
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
