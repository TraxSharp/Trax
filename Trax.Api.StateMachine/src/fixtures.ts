import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Locates the shared `machines/` directory (the language-neutral source of truth both the C# and
 * TypeScript engines drive). Walks up from this file until it finds it, so it works from `src/` under
 * vitest and from a built `dist/` alike — no hard-coded path.
 */
function machinesRoot(): string {
  let dir = path.dirname(fileURLToPath(import.meta.url));
  for (let i = 0; i < 10; i++) {
    // The repo root is the directory that holds both package.json and the shared machines/ tree —
    // this disambiguates it from the src/machines/ code directory of the same shape.
    if (
      fs.existsSync(path.join(dir, "package.json")) &&
      fs.existsSync(path.join(dir, "machines"))
    ) {
      return path.join(dir, "machines");
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error("Could not locate the shared machines/ directory.");
}

export const advanceDir = (machine: string): string =>
  path.join(machinesRoot(), machine, "fixtures", "advance");
export const rehydrateDir = (machine: string): string =>
  path.join(machinesRoot(), machine, "fixtures", "rehydrate");
export const wireHandoffFile = (machine: string): string =>
  path.join(machinesRoot(), machine, "wire-handoff.json");
export const structureFile = (machine: string): string =>
  path.join(machinesRoot(), machine, "structure.json");
export const differentialFile = (machine: string): string =>
  path.join(machinesRoot(), machine, "differential.json");
export const migrationFile = (machine: string): string =>
  path.join(machinesRoot(), machine, "migration.json");
export const irFile = (machine: string): string =>
  path.join(machinesRoot(), machine, `${machine}.ir.json`);

export interface LoadedFixture {
  file: string;
  fixture: Record<string, unknown>;
}

/** Loads every `*.json` in a fixtures directory, ordered deterministically. */
export function loadFixtures(dir: string): LoadedFixture[] {
  return fs
    .readdirSync(dir)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((file) => ({
      file,
      fixture: JSON.parse(
        fs.readFileSync(path.join(dir, file), "utf8"),
      ) as Record<string, unknown>,
    }));
}

/** The stored JSON a rehydrate fixture stands for: a verbatim `raw` string, or a stringified `json`. */
export function rehydrateInput(fixture: Record<string, unknown>): string {
  if (typeof fixture.raw === "string") return fixture.raw;
  return JSON.stringify(fixture.json);
}
