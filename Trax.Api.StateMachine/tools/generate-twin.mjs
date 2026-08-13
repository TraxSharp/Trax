import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { parseFlags, requireFlags } from "./lib/args.mjs";
import { bundleModule } from "./lib/engine-loader.mjs";

const USAGE =
  "Usage: node tools/generate-twin.mjs --ir <machine.ir.json> --engine-src <engine/src> --out-dir <dir>\n" +
  "                                    [--import-style relative|specifier] [--specifier <module>]";

/**
 * Generate a machine's typed twin from its IR using the engine's own generators (bundled from `engineSrc`, so
 * the output matches the engine exactly). Writes `<id>.contexts.g.ts` and `<id>.machine.g.ts` into `outDir`
 * (both must sit together: the factory imports the contexts by relative path). Returns the two paths written.
 */
export async function generateTwin({
  irPath,
  engineSrc,
  outDir,
  importStyle = "relative",
  specifier,
}) {
  if (importStyle !== "relative" && importStyle !== "specifier")
    throw new Error(
      `--import-style must be 'relative' or 'specifier', got '${importStyle}'.`,
    );

  const ir = JSON.parse(readFileSync(irPath, "utf8"));
  const { generateContextTypes, generateMachineFactory } = await bundleModule(
    join(engineSrc, "rules", "generateTypes.ts"),
  );

  const contexts = generateContextTypes(ir);
  const factory = generateMachineFactory(ir, { importStyle, specifier });

  mkdirSync(outDir, { recursive: true });
  const contextsPath = join(outDir, `${ir.id}.contexts.g.ts`);
  const machinePath = join(outDir, `${ir.id}.machine.g.ts`);
  writeFileSync(contextsPath, contexts);
  writeFileSync(machinePath, factory);
  return { contextsPath, machinePath };
}

async function main(argv) {
  const flags = parseFlags(argv);
  requireFlags(flags, ["ir", "engine-src", "out-dir"], USAGE);
  const { contextsPath, machinePath } = await generateTwin({
    irPath: flags.ir,
    engineSrc: flags["engine-src"],
    outDir: flags["out-dir"],
    importStyle: flags["import-style"] ?? "relative",
    specifier:
      typeof flags.specifier === "string" ? flags.specifier : undefined,
  });
  console.log(contextsPath);
  console.log(machinePath);
}

// Run only when invoked directly (`node tools/generate-twin.mjs ...`), not when imported by a test.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href)
  main(process.argv.slice(2)).catch((err) => {
    console.error(err.message);
    process.exit(1);
  });
