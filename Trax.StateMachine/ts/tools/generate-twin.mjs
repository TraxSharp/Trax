import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { invokedDirectly, parseFlags, requireFlags } from "./lib/args.mjs";
import { loadEngine } from "./lib/engine-loader.mjs";

const USAGE =
  "Usage: node tools/generate-twin.mjs --ir <machine.ir.json> --engine-src <engine/src> --out-dir <dir>\n" +
  "                                    [--lang typescript|csharp]\n" +
  "                                    [--import-style relative|specifier] [--specifier <module>]  (typescript)\n" +
  "                                    [--namespace <C# namespace>]                               (csharp)\n" +
  "The copy bundled into the trax tool carries its engine and takes no --engine-src.";

/**
 * Generate a machine's typed twin from its IR using the engine's own generators (bundled from `engineSrc`, so
 * the output matches the engine exactly). Writes `<id>.contexts.g.ts` and `<id>.machine.g.ts` into `outDir`
 * (both must sit together: the factory imports the contexts by relative path). Returns the two paths written.
 */
export async function generateTwin({
  irPath,
  engineSrc,
  outDir,
  lang = "typescript",
  importStyle = "relative",
  specifier,
  namespace,
}) {
  if (lang !== "typescript" && lang !== "csharp")
    throw new Error(`--lang must be 'typescript' or 'csharp', got '${lang}'.`);
  if (importStyle !== "relative" && importStyle !== "specifier")
    throw new Error(
      `--import-style must be 'relative' or 'specifier', got '${importStyle}'.`,
    );

  const irRaw = readFileSync(irPath, "utf8");
  const ir = JSON.parse(irRaw);
  // SHA-256 of the IR content, trailing newline stripped so it matches C#'s IMachine.SchemaHash, which hashes
  // ExportIr() (no trailing newline; the committed file adds one, which the C# drift test trims). The twin
  // carries this for the runtime skew handshake.
  const irHash = createHash("sha256")
    .update(irRaw.replace(/\n+$/, ""), "utf8")
    .digest("hex");
  const { generateContextTypes, generateMachineFactory, generateCSharpView } =
    await loadEngine(engineSrc);

  if (lang === "csharp") {
    // One file: the view embeds the IR as the host exported it, which is the text its hash is of.
    const view = generateCSharpView(ir, { namespace, irText: irRaw.replace(/\n+$/, ""), irHash });
    const viewPath = resolve(outDir, `${pascalOf(ir.id)}.g.cs`);
    if (dirname(viewPath) !== resolve(outDir))
      throw new Error(`The IR's id ${JSON.stringify(ir.id)} would write outside ${outDir}.`);
    mkdirSync(outDir, { recursive: true });
    writeFileSync(viewPath, view);
    return { viewPath };
  }

  const contexts = generateContextTypes(ir);
  const factory = generateMachineFactory(ir, { importStyle, specifier, irHash });

  // The IR's id names the files: one that would put them anywhere but outDir is refused before anything is written.
  const into = (name) => {
    const path = resolve(outDir, name);
    if (typeof ir.id !== "string" || dirname(path) !== resolve(outDir))
      throw new Error(
        `The IR's id ${JSON.stringify(ir.id)} would write ${JSON.stringify(name)} outside ${outDir}.`,
      );
    return path;
  };
  const contextsPath = into(`${ir.id}.contexts.g.ts`);
  const machinePath = into(`${ir.id}.machine.g.ts`);
  mkdirSync(outDir, { recursive: true });
  writeFileSync(contextsPath, contexts);
  writeFileSync(machinePath, factory);
  return { contextsPath, machinePath };
}

// The view's file name: the id in PascalCase, as its types are named (`ship-to-store` is `ShipToStore.g.cs`).
const pascalOf = (id) =>
  String(id)
    .split(/[-_]+/)
    .filter((s) => s.length > 0)
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("");

async function main(argv) {
  const flags = parseFlags(argv);
  requireFlags(flags, ["ir", "out-dir"], USAGE);
  const written = await generateTwin({
    irPath: flags.ir,
    engineSrc: flags["engine-src"],
    outDir: flags["out-dir"],
    lang: flags.lang ?? "typescript",
    importStyle: flags["import-style"] ?? "relative",
    specifier:
      typeof flags.specifier === "string" ? flags.specifier : undefined,
    namespace:
      typeof flags.namespace === "string" ? flags.namespace : undefined,
  });
  for (const path of Object.values(written)) console.log(path);
}

// Run only when invoked directly (`node tools/generate-twin.mjs ...`), not when imported by a test.
if (invokedDirectly(import.meta.url))
  main(process.argv.slice(2)).catch((err) => {
    console.error(err.message);
    process.exit(1);
  });
