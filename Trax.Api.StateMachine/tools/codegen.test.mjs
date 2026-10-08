import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
// The entrypoints are plain .mjs (they run under `node` from the C# CLI), so the test is .mjs too: it imports
// and exercises the real code path, and tsc leaves it alone (it only typechecks the .ts engine sources).
import { parseFlags, requireFlags } from "./lib/args.mjs";
import { bundleModule } from "./lib/engine-loader.mjs";
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

  it("specifier style emits the collapsed @trax/state-machine import", async () => {
    const { machinePath, contextsPath } = await generateTwin({
      irPath: turnstileIr,
      engineSrc,
      outDir: out,
      importStyle: "specifier",
    });
    const machine = read(machinePath);
    expect(machine).toContain(
      `import { typedMachineFromIr, type IrDocument } from "@trax/state-machine";`,
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
});
