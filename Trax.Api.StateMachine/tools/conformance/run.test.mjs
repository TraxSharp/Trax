import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { beforeAll, describe, expect, it } from "vitest";
import { kitCases } from "./kit.mjs";
import { report, runCases } from "./run.mjs";
import { createTestee, loadEngine } from "./testee-ts.mjs";

// The conformance runner over the TypeScript engine: the kit passes through the engine's public exports, both
// in process and as a spawned testee speaking conformance/PROTOCOL.md, and a testee that answers one case wrong
// fails with that case named.

const here = (file) => fileURLToPath(new URL(file, import.meta.url));
const cases = kitCases();
let engine;

beforeAll(async () => {
  engine = await loadEngine();
}, 60_000);

describe("the conformance kit", () => {
  it("draws on every source", () => {
    for (const source of ["/differential/", "/advance/", "/rehydrate/", "/wire-handoff/", "strict-input/", "canonical-wire/"])
      expect(cases.some((c) => c.name.includes(source)), source).toBe(true);
    expect(cases.filter((c) => c.request.op === "load").map((c) => c.request.machine)).toEqual([
      "checkout",
      "gauge",
      "ingest",
      "reminder",
      "turnstile",
      "strict",
    ]);
    for (const op of ["advance", "outcome", "timer"])
      expect(cases.some((c) => c.request.op === op), op).toBe(true);
  });

  it("passes through the TypeScript engine", async () => {
    const handle = createTestee(engine);
    const failures = await runCases(cases, async (r) => handle(r));
    expect(failures, report(failures, cases.length)).toEqual([]);
  });

  it("fails a testee that gives one case the wrong code, naming it", async () => {
    const handle = createTestee(engine);
    let flipped;
    const failures = await runCases(cases, async (r) => {
      const answer = handle(r);
      if (!flipped && answer.reason === "GUARD_FAILED") {
        flipped = r.id;
        return { ...answer, reason: "INVALID_INPUT" };
      }
      return answer;
    });
    expect(failures).toHaveLength(1);
    expect(failures[0].expect).toEqual({ outcome: "rejected", reason: "GUARD_FAILED" });
    expect(report(failures, cases.length)).toContain(`FAIL ${failures[0].name}`);
  });

  it("drives a spawned testee over the protocol", () => {
    const run = spawnSync(
      process.execPath,
      [here("run.mjs"), "--testee", process.execPath, here("testee-ts.mjs")],
      { encoding: "utf8", timeout: 120_000 },
    );
    expect(run.stderr).toContain(`${cases.length} of ${cases.length} cases passed.`);
    expect(run.status).toBe(0);
  }, 120_000);
});
