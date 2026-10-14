import { Buffer } from "node:buffer";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// The engine's folder, two up from this file (tools/lib), whose node_modules esbuild resolves from.
const engineRoot = join(fileURLToPath(new URL("../..", import.meta.url)), ".");

/**
 * Bundle a TypeScript entry module (and everything it imports) into a single in-memory ESM with esbuild, then
 * import it. Running the EXACT engine `src` the consumer ships is what keeps the CLI's output byte-identical to
 * what the consumer's own tests produce — no reliance on a possibly-stale built `dist`. esbuild resolves the
 * engine's extensionless relative imports (`from "../../machine"`) and strips/transforms its TypeScript,
 * including the class parameter properties that Node's native type-stripping refuses.
 *
 * `importEsbuild` is injectable so the "esbuild missing" branch is testable; production callers omit it.
 */
export async function bundleModule(
  entryPath,
  importEsbuild = () => import("esbuild"),
) {
  let esbuild;
  try {
    esbuild = await importEsbuild();
  } catch (cause) {
    throw new Error(
      "Could not load esbuild, which is needed to bundle the engine source. It is one of the engine's " +
        `devDependencies and is not installed: run \`npm ci\` in ${engineRoot}, then generate again.`,
      { cause },
    );
  }
  const result = await esbuild.build({
    entryPoints: [entryPath],
    bundle: true,
    format: "esm",
    platform: "node",
    write: false,
    logLevel: "silent",
  });
  const code = result.outputFiles[0].text;
  return import(
    "data:text/javascript;base64," + Buffer.from(code).toString("base64")
  );
}

/**
 * The engine's public module, `src/index.ts`, bundled from `engineSrc`. The generators load the engine through
 * this one function, so the copy of them bundled into the trax tool can swap it for an engine compiled in
 * (`engine-bundled.mjs`, by `tools/bundle-cli.mjs`).
 */
export async function loadEngine(engineSrc, importEsbuild) {
  if (typeof engineSrc !== "string")
    throw new Error("Missing required --engine-src (the engine's src/ directory).");
  return bundleModule(join(engineSrc, "index.ts"), importEsbuild);
}
