import { describe, expect, test } from "vitest";
import { offersDecisions } from "./decisions";

const asking = new Map([
  ["Billing.IScoreInvoice", true],
  ["Billing.ISendReminder", false],
]);

describe("offersDecisions", () => {
  test("a train that asks no decider gets no Ask afresh or replay controls", () => {
    expect(offersDecisions(asking, ["Billing.ISendReminder"])).toBe(false);
  });

  test("a train that asks one gets them", () => {
    expect(offersDecisions(asking, ["Billing.IScoreInvoice"])).toBe(true);
  });

  test("a selection gets them when any train in it asks", () => {
    expect(offersDecisions(asking, ["Billing.ISendReminder", "Billing.IScoreInvoice"])).toBe(true);
  });

  test("they stay while the console cannot tell: no answer yet, or a train this host does not register", () => {
    expect(offersDecisions(null, ["Billing.ISendReminder"])).toBe(true);
    expect(offersDecisions(asking, ["Elsewhere.IUnknown"])).toBe(true);
  });

  test("an empty selection offers nothing", () => {
    expect(offersDecisions(asking, [])).toBe(false);
  });
});
