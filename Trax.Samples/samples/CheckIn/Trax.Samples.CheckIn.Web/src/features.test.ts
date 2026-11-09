import { describe, expect, it } from "vitest";
import { statePhrase } from "./features";

// The phrase is how a step's Then ends, read from the glossary, so the feature-source panel lights the scenarios that
// end where the machine is now.

describe("statePhrase", () => {
  it("is a user machine's noun and the state's phrase", () => {
    expect(statePhrase("booking", "CheckedIn")).toBe("booking is checked in");
    expect(statePhrase("booking", "Missed")).toBe("booking missed the flight");
  });

  it("is a system machine's name and the state's phrase", () => {
    expect(statePhrase("flight", "Open")).toBe("the flight is open for check-in");
  });

  it("is null for a state, a machine or a state name the glossary does not have", () => {
    expect(statePhrase("booking", undefined)).toBeNull();
    expect(statePhrase("booking", "Lost")).toBeNull();
    expect(statePhrase("ticket", "Booked")).toBeNull();
  });
});
