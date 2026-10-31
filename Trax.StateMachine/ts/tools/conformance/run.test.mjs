import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { beforeAll, describe, expect, it } from "vitest";
import { differentialCases, kitCases } from "../../../conformance/kit.mjs";
import { checkEntries } from "../../../conformance/expected-failures.mjs";
import { report, runCases, verdict } from "../../../conformance/run.mjs";
import { createTestee, loadEngine } from "./testee-ts.mjs";

// The conformance runner over the TypeScript engine: the kit passes through the engine's public exports, both
// in process and as a spawned testee speaking conformance/PROTOCOL.md, and a testee that answers one case wrong
// fails with that case named.

const here = (file) => fileURLToPath(new URL(file, import.meta.url));
const runner = here("../../../conformance/run.mjs");
const cases = kitCases();
let engine;

beforeAll(async () => {
  engine = await loadEngine();
}, 60_000);

describe("the conformance kit", () => {
  it("draws on every source", () => {
    for (const source of ["/differential/", "/advance/", "/rehydrate/", "/wire-handoff/", "strict-input/", "plan/", "unreadable-ir/", "values/stored/", "values/text/", "values/input/", "canonical-wire/"])
      expect(cases.some((c) => c.name.includes(source)), source).toBe(true);
    expect(cases.filter((c) => c.expect.loaded).map((c) => c.request.machine)).toEqual([
      "checkout",
      "deep",
      "gauge",
      "ingest",
      "reminder",
      "turnstile",
      "vault",
      "wait",
      "strict",
      "wizard",
      "notebook",
      "odometer",
      "hostile",
    ]);
    for (const op of ["advance", "outcome", "timer", "plan"])
      expect(cases.some((c) => c.request.op === op), op).toBe(true);
  });

  it("names every case once, and a differential case by what it sends, not where it sits", () => {
    const names = cases.map((c) => c.name);
    expect(names.filter((n, i) => names.indexOf(n) !== i)).toEqual([]);
    // An expected failure names a case: inserting a case before it in the corpus must not rename it.
    const corpus = JSON.parse(readFileSync(here("../../../conformance/machines/gauge/differential.json"), "utf8")).cases;
    const ir = JSON.parse(readFileSync(here("../../../conformance/machines/gauge/gauge.ir.json"), "utf8"));
    const byName = (list) => Object.fromEntries(differentialCases("gauge", ir, list).map((c) => [c.name, c.request]));
    expect(byName([...corpus].reverse())).toEqual(byName(corpus));
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
      [runner, "--name", "ts", "--testee", process.execPath, here("testee-ts.mjs")],
      { encoding: "utf8", timeout: 120_000 },
    );
    // The TypeScript engine has no expected failures: every case of the kit ran and passed.
    expect(run.stderr).toContain(`${cases.length} of ${cases.length} cases passed, 0 expected failures.`);
    expect(run.status).toBe(0);
  }, 120_000);

  it("refuses a selection of no case rather than pass it", () => {
    const run = spawnSync(
      process.execPath,
      [runner, "--only", "no-case-is-named-this", "--testee", process.execPath, here("testee-ts.mjs")],
      { encoding: "utf8", timeout: 120_000 },
    );
    expect(run.stderr).toContain("No case of the kit");
    expect(run.status).toBe(2);
  }, 120_000);

  it("refuses a testee name that is not one", () => {
    const run = spawnSync(process.execPath, [runner, "--name", "../ts", "--testee", process.execPath, here("testee-ts.mjs")], {
      encoding: "utf8",
      timeout: 120_000,
    });
    expect(run.stderr).toContain("is not a testee name");
    expect(run.status).toBe(2);
  }, 120_000);
});

// A testee's expected failures (conformance/expected-failures/<name>.json) excuse what it fails today and nothing else:
// the list can only shrink.
describe("the expected failures", () => {
  // The TypeScript engine with the first GUARD_FAILED answered INVALID_INPUT: one case fails, with a code.
  const flipping = () => {
    const handle = createTestee(engine);
    let flipped = false;
    return async (r) => {
      const answer = handle(r);
      if (!flipped && answer.reason === "GUARD_FAILED") {
        flipped = true;
        return { ...answer, reason: "INVALID_INPUT" };
      }
      return answer;
    };
  };
  const honest = () => {
    const handle = createTestee(engine);
    return async (r) => handle(r);
  };
  const judged = async (exchange, entries, ran = cases) => verdict(checkEntries(entries, "code"), cases, ran, await runCases(ran, exchange));

  it("fail a run on a failing case they do not list", async () => {
    const result = await judged(flipping(), []);
    expect(result.failures).toHaveLength(1);
    expect(result.expected).toBe(0);
  });

  it("excuse a listed case that fails as listed, and the count says so", async () => {
    const failed = (await runCases(cases, flipping()))[0].name;
    const result = await judged(flipping(), [{ case: failed, reason: "not built yet", code: "INVALID_INPUT" }]);
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([]);
    expect(result.expected).toBe(1);
    expect(report(result.failures, cases.length, result)).toContain(
      `${cases.length - 1} of ${cases.length - 1} cases passed, 1 expected failure.`,
    );
  });

  it("fail a listed case that passes", async () => {
    const result = await judged(honest(), [{ case: cases[1].name, reason: "not built yet" }]);
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([expect.stringContaining(`${cases[1].name} is listed as failing but passed; remove`)]);
  });

  it("fail a listed case that fails with another code than the entry names", async () => {
    const failed = (await runCases(cases, flipping()))[0].name;
    const result = await judged(flipping(), [{ case: failed, reason: "not built yet", code: "GUARD_FAILED" }]);
    expect(result.failures).toHaveLength(1);
    expect(result.failures[0].note).toBe("listed as failing with code GUARD_FAILED, it fails with INVALID_INPUT");
    expect(report(result.failures, cases.length, result)).toContain("listed as failing with code GUARD_FAILED");
  });

  it("fail an entry that names no case of the kit, even one --only left out", async () => {
    const some = cases.filter((c) => c.name.startsWith("turnstile/"));
    const result = await judged(honest(), [{ prefix: "no-such-machine/", reason: "x" }, { case: "plan/load", reason: "y" }], some);
    expect(result.problems).toEqual([expect.stringContaining("the entry for the prefix no-such-machine/ names no case")]);
  });

  it("excuse every case under a listed prefix, a machine that does not load among them", async () => {
    const handle = createTestee(engine);
    const refusingVault = async (r) => (r.op === "load" && r.machine === "vault" ? { id: r.id, error: "no vault yet" } : handle(r));
    const vault = cases.filter((c) => c.name.startsWith("vault/")).length;
    const result = await judged(refusingVault, [{ prefix: "vault/", reason: "custom rules not built yet", code: "error" }]);
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([]);
    expect(result.expected).toBe(vault);
  });

  it("fail a case two entries list", async () => {
    const result = await judged(honest(), [
      { prefix: "turnstile/", reason: "x" },
      { case: "turnstile/load", reason: "y" },
    ]);
    expect(result.problems).toContainEqual(expect.stringContaining("turnstile/load is listed twice"));
  });

  it("refuse an entry with no reason, both a case and a prefix, or a key it does not take", () => {
    expect(() => checkEntries({}, "code")).toThrow(/not an array/);
    expect(() => checkEntries([{ case: "a" }], "code")).toThrow(/gives no reason/);
    expect(() => checkEntries([{ case: "a", reason: " " }], "code")).toThrow(/reason that is not a non-empty string/);
    expect(() => checkEntries([{ case: "a", prefix: "a", reason: "r" }], "code")).toThrow(/neither or both/);
    expect(() => checkEntries([{ reason: "r" }], "code")).toThrow(/neither or both/);
    expect(() => checkEntries([{ case: "a", reason: "r", message: "m" }], "code")).toThrow(/has message/);
  });
});
