import { readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { bundleModule } from "./lib/engine-loader.mjs";

// Exit code when the engine's dependencies are not installed (esbuild cannot be loaded), so a caller can skip
// rather than fail.
export const MISSING_DEPENDENCIES = 3;

/**
 * Replay event sequences through the TypeScript twin and report the answer after every step, for a caller (the
 * C# engine's sequence-parity test) to compare with its own. The request, read from stdin as JSON:
 *
 *   { "engineSrc": "<engine>/src",
 *     "machines": { "<name>": { "ir": { ...IR document... } } },
 *     "sequences": [ { "machine": "<name>", "start": { ...snapshot... },
 *                      "steps": [ { "op": "advance" | "outcome" | "timer", "trigger": "...", "input": ... } ] } ] }
 *
 * `advance` goes through `advance`, `outcome` through `applyOutcome` and `timer` through `applyTimer`. The answer,
 * written to stdout, has one entry per sequence and one per step: `{ "outcome", "reason", "wire" }`, where
 * `wire` is the canonical snapshot after the step (unchanged by a rejection).
 */
export async function replaySequences(request) {
  const { SnapshotMachine, machineFromIr } = await bundleModule(
    join(request.engineSrc, "index.ts"),
  );

  const cores = {};
  for (const [name, spec] of Object.entries(request.machines))
    cores[name] = new SnapshotMachine(machineFromIr(spec.ir));

  return request.sequences.map((sequence) => {
    const core = cores[sequence.machine];
    let current = sequence.start;
    return sequence.steps.map((step) => {
      const result =
        step.op === "outcome"
          ? core.applyOutcome(current, step.trigger, step.input ?? undefined)
          : step.op === "timer"
            ? core.applyTimer(current, step.trigger)
            : core.advance(current, step.trigger, step.input ?? undefined);
      if (result.outcome === "transitioned") current = result.snapshot;
      return {
        outcome: result.outcome,
        reason: result.outcome === "rejected" ? result.reason : null,
        wire: core.serialize(current),
      };
    });
  });
}

async function main() {
  const request = JSON.parse(readFileSync(0, "utf8"));
  let answers;
  try {
    answers = await replaySequences(request);
  } catch (err) {
    if (/Could not load esbuild/.test(err.message)) {
      console.error(err.message);
      process.exit(MISSING_DEPENDENCIES);
    }
    throw err;
  }
  process.stdout.write(JSON.stringify(answers));
}

// Run only when invoked directly (`node tools/replay-sequences.mjs < request.json`), not when imported.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href)
  main().catch((err) => {
    console.error(err.message);
    process.exit(1);
  });
