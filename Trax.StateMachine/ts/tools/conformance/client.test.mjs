import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { startHost } from "../../../conformance/client/host.mjs";
import { checkEntries, readExpectedFailures } from "../../../conformance/expected-failures.mjs";
import { allowedLacks, runJudged, runTranscripts, transcripts } from "../../../conformance/client/run.mjs";
import { createClientTestee, loadClient } from "./client-testee-ts.mjs";

// The client conformance kit over the TypeScript client: every transcript passes through a ServerMachineController
// over createMachineServer and graphql-ws, in process and as a spawned testee speaking conformance/client/PROTOCOL.md,
// and a testee that answers one send wrong fails with that transcript named.

const here = (file) => fileURLToPath(new URL(file, import.meta.url));
const runner = here("../../../conformance/client/run.mjs");
const list = transcripts();
// What the TypeScript client is known to fail today (conformance/client/expected-failures/ts.json).
const tsExpected = readExpectedFailures(here("../../../conformance/client/expected-failures"), "ts", "message").entries;
const listedAsFailing = (name) =>
  tsExpected.some((e) => (e.case !== undefined ? e.case === name : name.startsWith(e.prefix)));
let client;
let host;

beforeAll(async () => {
  client = await loadClient();
  host = await startHost();
}, 60_000);

afterAll(async () => {
  await host?.close();
});

// The testee's handler, as the runner's `ask`: a request without an id, its answer without one.
const askOf = (handle, rewrite = (_, a) => a) => {
  let id = 0;
  return async (request) => {
    const { id: _, ...answer } = await handle({ id: ++id, ...request });
    return rewrite(request, answer);
  };
};

describe("the client conformance kit", () => {
  it("covers each way a client ends", () => {
    const steps = list.flatMap((t) => t.steps);
    for (const code of ["GUARD_FAILED", "TRANSPORT_ERROR", "SERVER_OUTDATED", "CLIENT_OUTDATED", "CLIENT_DIVERGENCE", "GONE"])
      expect(JSON.stringify(steps), code).toContain(`"${code}"`);
    for (const kind of ["drop", "status", "errors", "push", "pushErrors", "complete", "dropSocket"])
      expect(steps.some((s) => s[kind] !== undefined), kind).toBe(true);
    expect(list.some((t) => t.refuseConnections > 0)).toBe(true);
    for (const op of ["can", "available", "discard"]) expect(steps.some((s) => s.client === op), op).toBe(true);
  });

  it("passes through the TypeScript client, but for what its expected failures list", async () => {
    const ask = askOf(createClientTestee(client));
    const result = await runJudged(
      (onResult) => runTranscripts(list, host, ask, onResult),
      tsExpected,
      list.map((t) => t.name),
    );
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([]);
  }, 180_000);

  it("fails a testee that answers one send with the wrong code, naming its transcript", async () => {
    let flipped = false;
    const ask = askOf(createClientTestee(client), (request, answer) => {
      if (!flipped && request.op === "send" && answer.code === "GUARD_FAILED") {
        flipped = true;
        return { ...answer, code: "INVALID_INPUT" };
      }
      return answer;
    });
    // The transcripts a host refuses something in are enough: the whole kit already ran above.
    const refusing = list.filter((t) => JSON.stringify(t.steps).includes('"GUARD_FAILED"'));
    const failures = await runTranscripts(refusing, host, ask);
    expect(failures.map((f) => f.name)).toEqual(["first-of-two-refused"]);
    expect(failures[0].error).toContain('"code":"INVALID_INPUT"');
  }, 60_000);

  it("lets a client lack only what PROTOCOL.md's allowlist grants it", async () => {
    // A few transcripts are enough: the check is on every open.
    const some = list.filter((t) => t.name.startsWith("send-"));
    const rewritten = (rewrite) =>
      runTranscripts(some, host, askOf(createClientTestee(client), (r, a) => (r.op === "open" ? rewrite(a) : a)));

    const extra = await rewritten((a) => ({ ...a, lacks: [...a.lacks, "streams"] }));
    expect(extra.map((f) => f.name)).toEqual(some.map((t) => t.name));
    expect(extra[0].error).toContain("lacks streams, which PROTOCOL.md does not allow it");

    // upgrade-status is granted to the TypeScript client, not to whichever client says it.
    const another = await rewritten((a) => ({ ...a, client: "rust" }));
    expect(another[0].error).toContain("the rust client says it lacks upgrade-status");

    const unnamed = await rewritten(({ client: _, ...a }) => a);
    expect(unnamed[0].error).toContain("names no client");
  }, 60_000);

  it("reads the allowlist from PROTOCOL.md, and refuses an entry without a reason", () => {
    expect(allowedLacks().map((a) => `${a.client}:${a.capability}`)).toEqual([
      "ts:upgrade-status",
      "ts:socket-message-limit",
      "ts:socket-close-code",
    ]);
    const block = (entries) => `# x\n\n\`\`\`json lacks\n${JSON.stringify(entries)}\n\`\`\`\n`;
    expect(() => allowedLacks(block([{ capability: "x", client: "ts", reason: " " }]))).toThrow(/no reason/);
    expect(() => allowedLacks("# no block")).toThrow(/no `json lacks` block/);
  });

  it("drives a spawned testee over the protocol", () => {
    // A few transcripts are enough to hold the pipes: the whole kit already ran above, in process.
    const only = "s";
    const run = spawnSync(
      process.execPath,
      [runner, "--only", only, "--name", "ts", "--testee", process.execPath, here("client-testee-ts.mjs")],
      { encoding: "utf8", timeout: 120_000 },
    );
    const lacked = new Set(allowedLacks().filter((a) => a.client === "ts").map((a) => a.capability));
    const some = list.filter((t) => t.name.startsWith(only));
    const runnable = some.filter((t) => !(t.needs ?? []).some((n) => lacked.has(n)));
    const excused = runnable.filter((t) => listedAsFailing(t.name)).length;
    const ran = runnable.length - excused;
    expect(runnable.length).toBeLessThan(some.length);
    // Skipped only where the allowlist grants the TypeScript client a lack, and excused only where its list names it.
    expect(run.stderr).toContain(
      `${ran} of ${ran} transcripts passed, ${some.length - runnable.length} skipped, ${excused} expected failure${excused === 1 ? "" : "s"}.`,
    );
    expect(run.status).toBe(0);
  }, 120_000);

  it("fails a testee run under one client's name whose open names another", async () => {
    const some = list.filter((t) => t.name.startsWith("send-p"));
    const failures = await runTranscripts(some, host, askOf(createClientTestee(client)), undefined, "rust");
    expect(failures.map((f) => f.name)).toEqual(some.map((t) => t.name));
    expect(failures[0].error).toContain("the testee runs as rust, and its answer names the ts client");
  }, 60_000);
});

// A client's expected failures (conformance/client/expected-failures/<name>.json) excuse what it fails today and nothing
// else; the judging is the engine kit's (run.test.mjs), here over transcripts and a failure's text.
describe("the client kit's expected failures", () => {
  const names = list.map((t) => t.name);
  // The TypeScript client with the first GUARD_FAILED a send settles as answered INVALID_INPUT.
  const flipping = () => {
    let flipped = false;
    return askOf(createClientTestee(client), (request, answer) => {
      if (!flipped && request.op === "send" && answer.code === "GUARD_FAILED") {
        flipped = true;
        return { ...answer, code: "INVALID_INPUT" };
      }
      return answer;
    });
  };
  const judged = (some, ask, entries) =>
    runJudged((onResult) => runTranscripts(some, host, ask, onResult), checkEntries(entries, "message"), names);
  const refused = () => list.filter((t) => t.name === "first-of-two-refused" || t.name === "send-predicts-and-confirms");

  it("fail a run on a failing transcript they do not list", async () => {
    const result = await judged(refused(), flipping(), []);
    expect(result.failures.map((f) => f.name)).toEqual(["first-of-two-refused"]);
    expect(result.expected).toBe(0);
  }, 60_000);

  it("excuse a listed transcript that fails with the message listed", async () => {
    const result = await judged(refused(), flipping(), [{ case: "first-of-two-refused", reason: "not built yet", message: "step " }]);
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([]);
    expect(result.expected).toBe(1);
  }, 60_000);

  it("fail a listed transcript that fails with another message", async () => {
    const result = await judged(refused(), flipping(), [{ case: "first-of-two-refused", reason: "not built yet", message: "open:" }]);
    expect(result.failures.map((f) => f.name)).toEqual(["first-of-two-refused"]);
    expect(result.failures[0].note).toMatch(/^listed as failing with message open:, it fails with step /);
  }, 60_000);

  it("fail a listed transcript that passes, or that the client skips", async () => {
    const needing = list.filter((t) => (t.needs ?? []).includes("upgrade-status"));
    expect(needing.length).toBeGreaterThan(0);
    const result = await judged([...refused(), ...needing], askOf(createClientTestee(client)), [
      { case: "first-of-two-refused", reason: "not built yet" },
      { case: needing[0].name, reason: "not built yet" },
    ]);
    expect(result.failures).toEqual([]);
    expect(result.problems).toEqual([
      expect.stringContaining("first-of-two-refused is listed as failing but passed; remove"),
      expect.stringContaining(`${needing[0].name} is listed as failing but was skipped`),
    ]);
  }, 60_000);

  it("fail an entry that names no transcript", async () => {
    const result = await judged(refused(), askOf(createClientTestee(client)), [{ prefix: "no-such-", reason: "x" }]);
    expect(result.problems).toEqual([expect.stringContaining("the entry for the prefix no-such- names no case")]);
  }, 60_000);
});
