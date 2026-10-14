// Stands in for engine-loader.mjs in the generators bundled into the trax tool (tools/bundle-cli.mjs). The
// engine is compiled into the bundle, so there is no source to bundle and no esbuild to bundle it with. To
// generate from another engine's source, trax runs that engine's own tools instead (--engine-src).
import * as engine from "../../src/index.ts";

export async function loadEngine(engineSrc) {
  if (engineSrc !== undefined)
    throw new Error(
      "These generators carry their own engine and take no --engine-src. Run the tools/ beside that engine's src/ instead.",
    );
  return engine;
}
