import { describe, expect, test } from "vitest";
import { buildDuration, parseDuration } from "../lib/duration";

describe("duration <-> ISO 8601", () => {
  test("parses single-unit ISO into value + coarsest unit", () => {
    expect(parseDuration("PT5S")).toEqual({ value: "5", unit: "s" });
    expect(parseDuration("PT5M")).toEqual({ value: "5", unit: "m" });
    expect(parseDuration("PT1H")).toEqual({ value: "1", unit: "h" });
    expect(parseDuration("P30D")).toEqual({ value: "30", unit: "d" });
  });

  test("normalises compound values to the finest evenly-dividing unit", () => {
    expect(parseDuration("PT1H30M")).toEqual({ value: "90", unit: "m" }); // 5400s
    expect(parseDuration("PT90S")).toEqual({ value: "90", unit: "s" }); // not divisible by 60
  });

  test("blank / unparseable yields an empty value", () => {
    expect(parseDuration("")).toEqual({ value: "", unit: "m" });
    expect(parseDuration("garbage")).toEqual({ value: "", unit: "m" });
  });

  test("builds ISO from value + unit", () => {
    expect(buildDuration("30", "m")).toBe("PT30M");
    expect(buildDuration("5", "s")).toBe("PT5S");
    expect(buildDuration("2", "h")).toBe("PT2H");
    expect(buildDuration("7", "d")).toBe("P7D");
  });

  test("empty / non-positive builds an empty string (blank = off)", () => {
    expect(buildDuration("", "m")).toBe("");
    expect(buildDuration("0", "m")).toBe("");
  });

  test("single-unit values round-trip exactly", () => {
    for (const iso of ["PT5S", "PT30M", "PT2H", "P30D"]) {
      const { value, unit } = parseDuration(iso);
      expect(buildDuration(value, unit)).toBe(iso);
    }
  });
});
