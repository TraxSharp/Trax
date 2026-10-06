import { describe, expect, test } from "vitest";
import { lookupQuery } from "./lookup";
import { recordings } from "./harness";

// What a person types into a filter cannot be recorded in advance, so the demo filters the recorded
// list the way the API does. These pin that the answer is cut from recorded rows only.

type Page = { items: Record<string, unknown>[]; totalCount: number; nextCursor: number | null };

const executionsPage = (vars: Record<string, unknown>) =>
  lookupQuery(recordings(), "Executions", { take: 25, order: "NEWEST", hideAdminTrains: true, ...vars });

describe("typed filters", () => {
  test("a train name the recorder walked is answered by its recording", () => {
    const first = (executionsPage({}).data!.operations as { executions: Page }).executions.items[0];
    const result = executionsPage({ trainName: first.name });
    expect(result.served).toBe("recorded");
  });

  test("a time range is cut from the recorded list, exactly as the API compares it", () => {
    const all = (executionsPage({}).data!.operations as { executions: Page }).executions.items;
    const middle = String(all[Math.floor(all.length / 2)].startTime);
    const result = executionsPage({ startedAfter: middle });
    expect(result.served).toBe("derived");
    const rows = (result.data!.operations as { executions: Page }).executions.items;
    expect(rows.length).toBeGreaterThan(0);
    expect(rows.every((r) => Date.parse(String(r.startTime)) >= Date.parse(middle))).toBe(true);
  });

  test("a name nobody has matches nothing, rather than something invented", () => {
    const result = executionsPage({ trainName: "No.Such.Train" });
    expect(result.served).toBe("derived");
    const page = (result.data!.operations as { executions: Page }).executions;
    expect(page.items).toEqual([]);
    expect(page.totalCount).toBe(0);
  });

  test("a part of a manifest's name is matched anywhere in it", () => {
    const result = lookupQuery(recordings(), "Manifests", { take: 25, hideAdminTrains: true, nameContains: "Digest" });
    expect(result.served).toBe("derived");
    const names = (result.data!.operations as { manifests: Page }).manifests.items.map((m) => String(m.name));
    expect(names.length).toBeGreaterThan(0);
    expect(names.every((n) => n.includes("Digest"))).toBe(true);
  });

  test("a query nothing recorded is a miss, never auto-mocked", () => {
    expect(lookupQuery(recordings(), "ExecutionDetail", { id: 123_456 }).served).toBe("missing");
    expect(lookupQuery(recordings(), "NoSuchOperation", {}).served).toBe("missing");
  });
});
