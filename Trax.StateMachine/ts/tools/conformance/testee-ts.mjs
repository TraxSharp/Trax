import { join } from "node:path";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";
import { bundleModule } from "../lib/engine-loader.mjs";

// The TypeScript engine as a conformance testee (conformance/PROTOCOL.md): it builds each machine from the IR it is
// loaded with and answers through the package's public exports only.

const MISSING_DEPENDENCIES = 3;
const engineSrc = fileURLToPath(new URL("../../src", import.meta.url));

// The snapshot `canonical` writes: no machine of the kit is named "m", so its wire comes from a machine of its own.
const vectorIr = {
  id: "m",
  version: 1,
  initialState: "S",
  initialContext: {},
  states: ["S"],
  triggers: [],
  committedStates: [],
  context: { S: { fields: [] } },
  inputs: {},
  transitions: [],
};

/** A handler from one request to its answer, over `engine`, the package's exports. */
export function createTestee(engine) {
  const { SnapshotMachine, machineFromIr } = engine;
  const machines = new Map();
  const vectors = new SnapshotMachine(machineFromIr(vectorIr));

  const step = (core, result) =>
    result.outcome === "transitioned"
      ? { outcome: "transitioned", wire: core.serialize(result.snapshot) }
      : { outcome: "rejected", reason: result.reason };

  const handlers = {
    load: (r) => {
      machines.set(r.machine, new SnapshotMachine(machineFromIr(r.ir)));
      return { loaded: true };
    },
    advance: (r, core) => step(core, core.advance(r.snapshot, r.trigger, r.input ?? undefined)),
    outcome: (r, core) => step(core, core.applyOutcome(r.snapshot, r.trigger, r.input ?? undefined)),
    timer: (r, core) => step(core, core.applyTimer(r.snapshot, r.trigger)),
    rehydrate: (r, core) => {
      const result = core.rehydrate(r.text);
      return result.result === "ok"
        ? { result: "ok", wire: core.serialize(result.snapshot) }
        : { result: "error", code: result.code };
    },
    serialize: (r, core) => ({ wire: core.serialize(r.snapshot) }),
    canonical: (r) => {
      const context = r.context !== undefined
        ? JSON.parse(r.context)
        : r.stringUnits
          ? { s: String.fromCharCode(...r.stringUnits) }
          : { [String.fromCharCode(...r.keyUnits)]: 1 };
      return { wire: vectors.serialize({ machine: "m", version: 1, state: "S", context }) };
    },
    end: () => ({}),
  };

  return (request) => {
    const handle = handlers[request.op];
    if (!handle) return { id: request.id, unsupported: true };
    const core = request.machine === undefined ? undefined : machines.get(request.machine);
    if (request.op !== "load" && request.machine !== undefined && !core)
      return { id: request.id, error: `no machine ${request.machine} is loaded` };
    try {
      return { id: request.id, ...handle(request, core) };
    } catch (err) {
      return { id: request.id, error: err.message };
    }
  };
}

/** Bundles the engine's source, as the CLI's entrypoints do, and returns its exports. */
export const loadEngine = () => bundleModule(join(engineSrc, "index.ts"));

async function main() {
  let engine;
  try {
    engine = await loadEngine();
  } catch (err) {
    console.error(err.message);
    process.exit(/Could not load esbuild/.test(err.message) ? MISSING_DEPENDENCIES : 1);
  }
  const handle = createTestee(engine);
  for await (const line of createInterface({ input: process.stdin })) {
    const request = JSON.parse(line);
    process.stdout.write(`${JSON.stringify(handle(request))}\n`);
    if (request.op === "end") break;
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) await main();
