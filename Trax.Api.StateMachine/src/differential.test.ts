import fs from "node:fs";
import { describe, expect, it } from "vitest";
import {
  enumerate,
  serializeCorpus,
  type DifferentialSpec,
} from "./differential";
import { differentialFile, irFile, machineSpecFile } from "./fixtures";
import type { SnapshotMachine } from "./machine";
import { checkoutCore } from "./machines/checkout/checkout";
import { turnstileCore } from "./machines/turnstile/turnstile";

// TypeScript is the oracle. It re-enumerates the corpus from the machine's IR (the single source authored in
// C#, whose differential block carries the samples/seeds/contexts) plus the engine on every run, and compares
// to the committed golden (jest-snapshot style): a diff means either the engine changed behavior or the IR's
// differential block changed. Regenerate deliberately with UPDATE_DIFFERENTIAL=1 — the git diff of the golden
// is the review of what changed. C# replays the same committed file to prove parity.
const UPDATE = process.env.UPDATE_DIFFERENTIAL === "1";

function assertCorpus<S extends string, T extends string>(
  name: string,
  machine: SnapshotMachine<S, T>,
): void {
  // Enumerate off the IR when the machine has one (the IR is structurally a DifferentialSpec — enumerate
  // reads only id/version/states/triggers + the differential block). Machines not yet on the IR path
  // (checkout) fall back to the legacy hand-written machine.json.
  const specFile = fs.existsSync(irFile(name))
    ? irFile(name)
    : machineSpecFile(name);
  const spec = JSON.parse(
    fs.readFileSync(specFile, "utf8"),
  ) as DifferentialSpec;
  const generated = serializeCorpus(enumerate(machine, spec));
  const file = differentialFile(name);

  if (UPDATE) {
    fs.writeFileSync(file, generated);
    return;
  }

  const committed = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
  expect(
    generated,
    `${name} differential corpus is stale or the engine diverged — regenerate with UPDATE_DIFFERENTIAL=1`,
  ).toBe(committed);
}

describe("differential corpus (TypeScript oracle)", () => {
  it("turnstile matches its committed corpus", () =>
    assertCorpus("turnstile", turnstileCore));
  it("checkout matches its committed corpus", () =>
    assertCorpus("checkout", checkoutCore));
});
