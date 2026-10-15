import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
// The entrypoints are plain .mjs (they run under `node` from the C# CLI), so the test is .mjs too: it imports
// and exercises the real code path, and tsc leaves it alone (it only typechecks the .ts engine sources).
import { parseFlags, requireFlags } from "./lib/args.mjs";
import { bundleModule, loadEngine } from "./lib/engine-loader.mjs";
import { bundleCli } from "./bundle-cli.mjs";
import { generateCorpus } from "./generate-corpus.mjs";
import { generateTwin } from "./generate-twin.mjs";

// These pin the node codegen entrypoints the `trax machine generate` CLI shells out to. The whole point is
// byte-parity: the entrypoints must reproduce the committed turnstile artifacts exactly, so the CLI's output
// equals what the engine's own tests produce.
const repoRoot = fileURLToPath(new URL("..", import.meta.url));
const engineSrc = join(repoRoot, "src");
const turnstileIr = join(
  repoRoot,
  "machines",
  "turnstile",
  "turnstile.ir.json",
);
const committedContexts = join(
  repoRoot,
  "src",
  "machines",
  "turnstile",
  "turnstile.contexts.g.ts",
);
const committedMachine = join(
  repoRoot,
  "src",
  "machines",
  "turnstile",
  "turnstile.machine.g.ts",
);
const committedCorpus = join(
  repoRoot,
  "machines",
  "turnstile",
  "differential.json",
);

const read = (path) => readFileSync(path, "utf8");

// A machine whose state invokes a train: its IR carries outcome triggers, which the twin and the corpus
// generators must carry through to the committed artifacts unchanged.
const ingestIr = join(repoRoot, "machines", "ingest", "ingest.ir.json");
const ingestTwin = join(repoRoot, "src", "machines", "ingest");

describe("generate-twin entrypoint", () => {
  let out;
  beforeEach(() => {
    out = mkdtempSync(join(tmpdir(), "trax-twin-"));
  });
  afterEach(() => {
    rmSync(out, { recursive: true, force: true });
  });

  it("reproduces the committed turnstile twin byte-for-byte (relative imports)", async () => {
    const { contextsPath, machinePath } = await generateTwin({
      irPath: turnstileIr,
      engineSrc,
      outDir: out,
      importStyle: "relative",
    });
    expect(read(contextsPath)).toBe(read(committedContexts));
    expect(read(machinePath)).toBe(read(committedMachine));
  });

  it("reproduces the committed ingest twin, outcome triggers included", async () => {
    const { contextsPath, machinePath } = await generateTwin({
      irPath: ingestIr,
      engineSrc,
      outDir: out,
    });
    expect(read(contextsPath)).toBe(read(join(ingestTwin, "ingest.contexts.g.ts")));
    expect(read(machinePath)).toBe(read(join(ingestTwin, "ingest.machine.g.ts")));
  });

  it("derives the filenames from the IR id", async () => {
    const { contextsPath, machinePath } = await generateTwin({
      irPath: turnstileIr,
      engineSrc,
      outDir: out,
    });
    expect(contextsPath).toBe(join(out, "turnstile.contexts.g.ts"));
    expect(machinePath).toBe(join(out, "turnstile.machine.g.ts"));
  });

  it("specifier style emits the collapsed @traxsharp/state-machine import", async () => {
    const { machinePath, contextsPath } = await generateTwin({
      irPath: turnstileIr,
      engineSrc,
      outDir: out,
      importStyle: "specifier",
    });
    const machine = read(machinePath);
    expect(machine).toContain(
      `import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";`,
    );
    expect(machine).not.toContain(`from "../../machine"`);
    // Context types are independent of import style, so they stay byte-identical to the committed file.
    expect(read(contextsPath)).toBe(read(committedContexts));
  });

  it("honors a custom specifier", async () => {
    const { machinePath } = await generateTwin({
      irPath: turnstileIr,
      engineSrc,
      outDir: out,
      importStyle: "specifier",
      specifier: "@acme/machines",
    });
    expect(read(machinePath)).toContain(`from "@acme/machines";`);
  });

  it("refuses an IR whose id would write outside the out dir", async () => {
    // The engine refuses an id that is not kebab-case before the files are named, so an engine that writes anything
    // stands in for it here: the twin generator's own check is what is left.
    const engine = join(out, "engine");
    mkdirSync(engine);
    writeFileSync(
      join(engine, "index.ts"),
      'export const generateContextTypes = () => "";\nexport const generateMachineFactory = () => "";\n',
    );
    const ir = JSON.parse(read(turnstileIr));
    const escaping = join(out, "escaping.ir.json");
    writeFileSync(escaping, JSON.stringify({ ...ir, id: "../escaped" }));
    const inside = join(out, "inside");

    await expect(
      generateTwin({ irPath: escaping, engineSrc: engine, outDir: inside }),
    ).rejects.toThrow(/would write "\.\.\/escaped\.contexts\.g\.ts" outside/);
    expect(existsSync(join(out, "escaped.contexts.g.ts"))).toBe(false);
    expect(existsSync(join(out, "escaped.machine.g.ts"))).toBe(false);
  });

  it("rejects an unknown import style", async () => {
    await expect(
      generateTwin({
        irPath: turnstileIr,
        engineSrc,
        outDir: out,
        importStyle: "package",
      }),
    ).rejects.toThrow(/import-style/);
  });
});

describe("generate-corpus entrypoint", () => {
  let out;
  beforeEach(() => {
    out = mkdtempSync(join(tmpdir(), "trax-corpus-"));
  });
  afterEach(() => {
    rmSync(out, { recursive: true, force: true });
  });

  it("reproduces the committed turnstile differential corpus byte-for-byte", async () => {
    const outPath = join(out, "differential.json");
    const result = await generateCorpus({
      irPath: turnstileIr,
      engineSrc,
      outPath,
    });
    expect(result.outPath).toBe(outPath);
    expect(read(outPath)).toBe(read(committedCorpus));
  });

  it("reproduces the committed ingest corpus, which fires its outcome triggers", async () => {
    const outPath = join(out, "differential.json");
    await generateCorpus({ irPath: ingestIr, engineSrc, outPath });
    expect(read(outPath)).toBe(
      read(join(repoRoot, "machines", "ingest", "differential.json")),
    );
    expect(read(outPath)).toContain(`"trigger": "Fetching.done"`);
  });
});

describe("parseFlags", () => {
  it("reads --flag value pairs", () => {
    expect(parseFlags(["--ir", "x.json", "--out-dir", "d"])).toEqual({
      ir: "x.json",
      "out-dir": "d",
    });
  });

  it("treats a flag with no following value as boolean true", () => {
    expect(parseFlags(["--check"])).toEqual({ check: true });
    expect(parseFlags(["--check", "--ir", "x"])).toEqual({
      check: true,
      ir: "x",
    });
  });

  it("ignores non-flag leading tokens and takes the last value on repeat", () => {
    expect(parseFlags(["positional", "--ir", "a", "--ir", "b"])).toEqual({
      ir: "b",
    });
  });
});

describe("requireFlags", () => {
  const usage = "usage line";

  it("passes when every required flag has a value", () => {
    expect(() =>
      requireFlags({ ir: "x", "engine-src": "s" }, ["ir", "engine-src"], usage),
    ).not.toThrow();
  });

  it("throws naming the missing flag (and includes the usage)", () => {
    expect(() => requireFlags({ ir: "x" }, ["ir", "out"], usage)).toThrow(
      /Missing required --out[\s\S]*usage line/,
    );
  });

  it("treats a boolean-only flag as missing when a value was required", () => {
    expect(() => requireFlags({ out: true }, ["out"], usage)).toThrow(
      /Missing required --out/,
    );
  });
});

describe("bundleModule", () => {
  it("bundles and imports the engine generators from src", async () => {
    const mod = await bundleModule(
      join(engineSrc, "rules", "generateTypes.ts"),
    );
    expect(typeof mod.generateContextTypes).toBe("function");
    expect(typeof mod.generateMachineFactory).toBe("function");
  });

  it("throws a clear error when esbuild cannot be loaded", async () => {
    await expect(
      bundleModule(join(engineSrc, "index.ts"), () => {
        throw new Error("not installed");
      }),
    ).rejects.toThrow(/Could not load esbuild/);
  });

  it("names the folder to run npm ci in when esbuild is missing", async () => {
    const failure = bundleModule(join(engineSrc, "index.ts"), () => {
      throw new Error("not installed");
    });
    await expect(failure).rejects.toThrow(`run \`npm ci\` in ${join(repoRoot, ".")}`);
  });
});

describe("loadEngine", () => {
  it("needs the engine's source without the bundled generators", async () => {
    await expect(loadEngine(undefined)).rejects.toThrow(/Missing required --engine-src/);
  });
});

// The generators Trax.Cli ships: bundled with the engine compiled in, run by node with no engine source and
// nothing installed beside them, they must still reproduce the committed artifacts byte for byte.
describe("bundleCli", () => {
  let out;
  let bundled;
  beforeEach(async () => {
    out = mkdtempSync(join(tmpdir(), "trax-bundle-"));
    bundled = mkdtempSync(join(tmpdir(), "trax-bundled-"));
    await bundleCli(bundled);
  });
  afterEach(() => {
    rmSync(out, { recursive: true, force: true });
    rmSync(bundled, { recursive: true, force: true });
  });

  const node = (script, ...args) =>
    execFileSync(process.execPath, [join(bundled, script), ...args], {
      cwd: out,
      encoding: "utf8",
    });

  it("writes a twin and a corpus identical to the committed turnstile's, with no --engine-src", () => {
    node("generate-twin.mjs", "--ir", turnstileIr, "--out-dir", out);
    node("generate-corpus.mjs", "--ir", turnstileIr, "--out", join(out, "differential.json"));

    expect(read(join(out, "turnstile.contexts.g.ts"))).toBe(read(committedContexts));
    expect(read(join(out, "turnstile.machine.g.ts"))).toBe(read(committedMachine));
    expect(read(join(out, "differential.json"))).toBe(read(committedCorpus));
  });

  it("writes a specifier twin that records its IR format and imports only what every engine exports", () => {
    node("generate-twin.mjs", "--ir", turnstileIr, "--out-dir", out, "--import-style", "specifier");

    const twin = read(join(out, "turnstile.machine.g.ts"));
    expect(twin).toContain('const ir = {\n  "irFormat": 1,');
    expect(twin).toContain('// Imports the engine from "@traxsharp/state-machine" (trax machine generate');
    expect(twin.split("\n").filter((line) => line.startsWith("import "))).toEqual([
      'import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";',
      'import type { TurnstileSpec } from "./turnstile.contexts.g";',
    ]);
  });

  it("refuses --engine-src, which only the engine's own tools take", () => {
    const result = spawnSync(
      process.execPath,
      [join(bundled, "generate-twin.mjs"), "--ir", turnstileIr, "--out-dir", out, "--engine-src", engineSrc],
      { encoding: "utf8" },
    );
    expect(result.status).toBe(1);
    expect(result.stderr).toMatch(/carry their own engine/);
  });
});
