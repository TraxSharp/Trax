import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { startHost } from "../../../conformance/client/host.mjs";
import { kitDocuments, runTranscripts, transcripts } from "../../../conformance/operations/run.mjs";
import { createOperationsTestee, loadOperationsClient } from "./operations-testee-ts.mjs";

// The operations kit over the TypeScript operations client trax client generated for the kit's fixture: every
// transcript passes in process and as a spawned testee speaking conformance/operations/PROTOCOL.md. Trax.Cli's
// OperationsConformanceTests checks the generated client is current and runs the C# one through the same kit.

const here = (file) => fileURLToPath(new URL(file, import.meta.url));
const runner = here("../../../conformance/operations/run.mjs");
const list = transcripts();
let client;
let host;

beforeAll(async () => {
  client = await loadOperationsClient();
  host = await startHost();
}, 60_000);

afterAll(async () => {
  await host?.close();
});

const askOf = (handle) => {
  let id = 0;
  return async (request) => {
    const { id: _, ...answer } = await handle({ id: ++id, ...request });
    return answer;
  };
};

describe("the operations kit", () => {
  it("sends each document by the id a host registers it under", () => {
    const ids = Object.fromEntries(kitDocuments().map((d) => [d.operation, d.id]));
    expect(client.runsDocument.id).toBe(ids.Runs);
    expect(client.retryDocument.id).toBe(ids.Retry);
    expect(client.changesDocument.id).toBe(ids.Changes);
  });

  it("covers each way an operation ends", () => {
    const steps = JSON.stringify(list.flatMap((t) => t.steps));
    for (const outcome of ['"outcome":"data"', '"outcome":"refused"', '"outcome":"failed"', '"ended":"complete"', '"ended":"refused"', '"ended":"closed"'])
      expect(steps, outcome).toContain(outcome);
    expect(list.filter((t) => t.opened?.opened === false).length).toBeGreaterThanOrEqual(3);
  });

  it("passes through the TypeScript operations client", async () => {
    const failures = await runTranscripts(list, host, askOf(createOperationsTestee(client)));
    expect(failures).toEqual([]);
  }, 60_000);

  it("drives a spawned testee over the protocol", () => {
    const run = spawnSync(process.execPath, [runner, "--testee", process.execPath, here("operations-testee-ts.mjs")], {
      encoding: "utf8",
      timeout: 120_000,
    });
    expect(run.stderr).toContain(`${list.length} of ${list.length} transcripts passed.`);
    expect(run.status).toBe(0);
  }, 120_000);
});
