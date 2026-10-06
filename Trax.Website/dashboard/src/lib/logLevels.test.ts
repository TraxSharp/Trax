import { describe, expect, test } from "vitest";
import { changedLogLevels, notAppliedMessage, toLogLevelEnum } from "./logLevels";

const LEVELS = [
  { category: "Default", level: "Information", configuredLevel: "Information", overridden: false },
  { category: "Trax", level: "Warning", configuredLevel: "Warning", overridden: false },
];

describe("log levels", () => {
  test("only the edited categories whose level differs are sent, as the LogLevel enum", () => {
    expect(changedLogLevels(LEVELS, {})).toEqual([]);
    expect(changedLogLevels(LEVELS, { Default: "Information", Trax: "Debug" })).toEqual([
      { category: "Trax", level: "DEBUG" },
    ]);
  });

  test("names a level as the enum spells it", () => {
    expect(toLogLevelEnum("Critical")).toBe("CRITICAL");
  });

  test("explains a level that is not in force", () => {
    expect(notAppliedMessage(["Trax", "Default"])).toMatch(/not the one in force: Trax, Default\.$/);
  });
});
