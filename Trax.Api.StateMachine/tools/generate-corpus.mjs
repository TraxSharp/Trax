import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";
import { invokedDirectly, parseFlags, requireFlags } from "./lib/args.mjs";
import { loadEngine } from "./lib/engine-loader.mjs";

const USAGE =
  "Usage: node tools/generate-corpus.mjs --ir <machine.ir.json> --engine-src <engine/src> --out <differential.json>\n" +
  "The copy bundled into the trax tool carries its engine and takes no --engine-src.";

/**
 * Generate a machine's differential corpus from its IR: build the IR-driven machine, enumerate its reachable
 * behavior (the IR is structurally the DifferentialSpec — enumerate reads id/version/states/triggers + the
 * differential block), and serialize the golden. TypeScript is the oracle until the Increment-D flip, so this
 * is the same code path the engine's own differential test runs. Writes `outPath` and returns it.
 */
export async function generateCorpus({ irPath, engineSrc, outPath }) {
  const ir = JSON.parse(readFileSync(irPath, "utf8"));
  const { SnapshotMachine, machineFromIr, enumerate, serializeCorpus } =
    await loadEngine(engineSrc);

  const core = new SnapshotMachine(machineFromIr(ir));
  const corpus = serializeCorpus(enumerate(core, ir));

  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, corpus);
  return { outPath };
}

async function main(argv) {
  const flags = parseFlags(argv);
  requireFlags(flags, ["ir", "out"], USAGE);
  const { outPath } = await generateCorpus({
    irPath: flags.ir,
    engineSrc: flags["engine-src"],
    outPath: flags.out,
  });
  console.log(outPath);
}

// Run only when invoked directly (`node tools/generate-corpus.mjs ...`), not when imported by a test.
if (invokedDirectly(import.meta.url))
  main(process.argv.slice(2)).catch((err) => {
    console.error(err.message);
    process.exit(1);
  });
