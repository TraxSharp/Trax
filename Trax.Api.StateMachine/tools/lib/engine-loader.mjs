import { Buffer } from "node:buffer";

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
      "Could not load esbuild, which is needed to bundle the engine source. It ships transitively with the " +
        "engine's tsup/vitest devDependencies; run `npm install` in the engine repo, or invoke the CLI from a " +
        "directory where `esbuild` resolves.",
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
