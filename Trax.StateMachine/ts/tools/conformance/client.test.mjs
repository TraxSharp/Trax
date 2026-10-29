import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { startHost } from "../../../conformance/client/host.mjs";
import { allowedLacks, runTranscripts, transcripts } from "../../../conformance/client/run.mjs";
import { createClientTestee, loadClient } from "./client-testee-ts.mjs";

// The client conformance kit over the TypeScript client: every transcript passes through a ServerMachineController
// over createMachineServer and graphql-ws, in process and as a spawned testee speaking conformance/client/PROTOCOL.md,
// and a testee that answers one send wrong fails with that transcript named.

const here = (file) => fileURLToPath(new URL(file, import.meta.url));
const runner = here("../../../conformance/client/run.mjs");
const list = transcripts();
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
  });

  it("passes through the TypeScript client", async () => {
    const failures = await runTranscripts(list, host, askOf(createClientTestee(client)));
    expect(failures).toEqual([]);
  }, 60_000);

  it("fails a testee that answers one send with the wrong code, naming its transcript", async () => {
    let flipped = false;
    const ask = askOf(createClientTestee(client), (request, answer) => {
      if (!flipped && request.op === "send" && answer.code === "GUARD_FAILED") {
        flipped = true;
        return { ...answer, code: "INVALID_INPUT" };
      }
      return answer;
    });
    const failures = await runTranscripts(list, host, ask);
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
    expect(allowedLacks().map((a) => `${a.client}:${a.capability}`)).toEqual(["ts:upgrade-status"]);
    const block = (entries) => `# x\n\n\`\`\`json lacks\n${JSON.stringify(entries)}\n\`\`\`\n`;
    expect(() => allowedLacks(block([{ capability: "x", client: "ts", reason: " " }]))).toThrow(/no reason/);
    expect(() => allowedLacks("# no block")).toThrow(/no `json lacks` block/);
  });

  it("drives a spawned testee over the protocol", () => {
    const run = spawnSync(process.execPath, [runner, "--testee", process.execPath, here("client-testee-ts.mjs")], {
      encoding: "utf8",
      timeout: 120_000,
    });
    const ran = list.filter((t) => !(t.needs ?? []).includes("upgrade-status")).length;
    expect(run.stderr).toContain(`${ran} of ${ran} transcripts passed, ${list.length - ran} skipped.`);
    expect(run.status).toBe(0);
  }, 120_000);
});
