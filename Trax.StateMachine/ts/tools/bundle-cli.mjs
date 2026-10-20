import { rmSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { invokedDirectly, parseFlags, requireFlags } from "./lib/args.mjs";

const USAGE = "Usage: node tools/bundle-cli.mjs --out <dir>";

const tools = fileURLToPath(new URL(".", import.meta.url));

// The bundles run under whatever node the developer has. Older than 22 is refused by name rather than left to
// fail on a syntax error.
const NODE_GUARD = `if (Number(process.versions.node.split(".")[0]) < 22) {
  console.error("trax machine generate needs Node.js 22 or later to write a twin or a corpus; this is " + process.version + ".");
  process.exit(1);
}`;

/**
 * Bundle the twin and corpus generators, with the engine compiled in, into `outDir` as plain JavaScript:
 * `generate-twin.mjs` and `generate-corpus.mjs`. Trax.Cli ships them in its package, so an installed `trax`
 * generates a twin and a corpus with node alone: no engine source, no npm install, no esbuild.
 */
export async function bundleCli(outDir) {
  let esbuild;
  try {
    esbuild = await import("esbuild");
  } catch (cause) {
    throw new Error(
      `Could not load esbuild to bundle the generators: run \`npm ci\` in ${resolve(tools, "..")} first.`,
      { cause },
    );
  }
  const outputs = [join(outDir, "generate-twin.mjs"), join(outDir, "generate-corpus.mjs")];
  for (const file of outputs) rmSync(file, { force: true });
  await esbuild.build({
    entryPoints: {
      "generate-twin": join(tools, "generate-twin.mjs"),
      "generate-corpus": join(tools, "generate-corpus.mjs"),
    },
    outdir: outDir,
    outExtension: { ".js": ".mjs" },
    bundle: true,
    format: "esm",
    platform: "node",
    target: "node22",
    banner: { js: NODE_GUARD },
    logLevel: "warning",
    plugins: [
      {
        // The generators load the engine through engine-loader.mjs, which bundles its source with esbuild at run
        // time. Here they get engine-bundled.mjs, which imports the engine directly so it is compiled in.
        name: "engine-compiled-in",
        setup(build) {
          build.onResolve({ filter: /engine-loader\.mjs$/ }, () => ({
            path: join(tools, "lib", "engine-bundled.mjs"),
          }));
        },
      },
    ],
  });
  return outputs;
}

async function main(argv) {
  const flags = parseFlags(argv);
  requireFlags(flags, ["out"], USAGE);
  for (const file of await bundleCli(resolve(flags.out))) console.log(file);
}

// Run only when invoked directly (`node tools/bundle-cli.mjs ...`), not when imported by a test.
if (invokedDirectly(import.meta.url))
  main(process.argv.slice(2)).catch((err) => {
    console.error(err.message);
    process.exit(1);
  });
