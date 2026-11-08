import { describe, expect, it } from "vitest";
import { statePhrase } from "./features";

// The phrase is how a step's Then ends, read from the glossary, so the feature-source panel lights the scenarios that
// end where the machine is now.

describe("statePhrase", () => {
  it("is a user machine's noun and the state's phrase", () => {
    expect(statePhrase("order", "Placed")).toBe("order is placed");
    expect(statePhrase("order", "Ready")).toBe("order is ready to collect");
  });

  it("is a system machine's name and the state's phrase", () => {
    expect(statePhrase("shop", "Firing")).toBe("the shop is baking a batch");
  });

  it("is null for a state, a machine or a state name the glossary does not have", () => {
    expect(statePhrase("order", undefined)).toBeNull();
    expect(statePhrase("order", "Lost")).toBeNull();
    expect(statePhrase("loaf", "Placed")).toBeNull();
  });
});
