import fs from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { irFile } from "../fixtures";
import { SnapshotMachine } from "../machine";
import type {
  TurnstileCoinInput,
  TurnstileSpec,
  TurnstileUnlockedContext,
} from "../machines/turnstile/turnstile.contexts.g";
import { turnstile } from "../machines/turnstile/turnstile.machine.g";
import { generateContextTypes, generateMachineFactory } from "./generateTypes";
import { machineFromIr, type IrDocument } from "./irMachine";

// The typed generator emits state/trigger unions and a context interface per state from the IR schema. The
// committed `.g.ts` is typechecked by tsc (so the emitted types are valid), and this drift check proves the
// generator is deterministic and the committed file is up to date.

const committedFile = fileURLToPath(
  new URL("../machines/turnstile/turnstile.contexts.g.ts", import.meta.url),
);

describe("generateContextTypes", () => {
  const ir = JSON.parse(
    fs.readFileSync(irFile("turnstile"), "utf8"),
  ) as IrDocument;

  it("regenerates the committed turnstile context types (drift check)", () => {
    expect(generateContextTypes(ir)).toBe(
      fs.readFileSync(committedFile, "utf8"),
    );
  });

  it("emits unions and a context interface derived from the schema", () => {
    const out = generateContextTypes(ir);
    expect(out).toContain(
      'export type TurnstileState = "Locked" | "Unlocked";',
    );
    expect(out).toContain('export type TurnstileTrigger = "Coin" | "Push";');
    expect(out).toContain(
      "export type TurnstileLockedContext = Record<string, never>;",
    );
    expect(out).toContain("export type TurnstileUnlockedContext = {");
    expect(out).toContain("  paidWith: string;");
  });

  it("the generated type matches the schema and the runtime produces it", () => {
    // Compile-time proof: the generated type is exactly `{ paidWith: string }`. If the generator emitted a
    // wrong shape (missing paidWith, or a non-string), this construction would fail to compile.
    const shape: TurnstileUnlockedContext = { paidWith: "quarter" };
    expect(shape.paidWith).toBe("quarter");

    // Runtime proof: the IR-driven machine produces exactly that context on Coin.
    const machine = new SnapshotMachine(machineFromIr(ir));
    const result = machine.advance(
      { machine: "turnstile", version: 1, state: "Locked", context: {} },
      "Coin",
      { coin: "quarter" },
    );
    expect(result.outcome).toBe("transitioned");
    if (result.outcome === "transitioned")
      expect(result.snapshot.context).toEqual({ paidWith: "quarter" });
  });

  it("the generated spec types trigger inputs (Coin has an input, Push has none)", () => {
    // Compile-time: Coin's input is `{ coin: string }`, and the Spec binds triggers -> inputs.
    const coin: TurnstileCoinInput = { coin: "quarter" };
    const triggers: TurnstileSpec["triggers"] = { Coin: coin, Push: undefined };
    expect(triggers.Coin.coin).toBe("quarter");
    expect(triggers.Push).toBeUndefined();
  });

  it("regenerates the committed machine factory (drift check)", () => {
    const committedFactory = fileURLToPath(
      new URL("../machines/turnstile/turnstile.machine.g.ts", import.meta.url),
    );
    expect(generateMachineFactory(ir)).toBe(
      fs.readFileSync(committedFactory, "utf8"),
    );
  });

  it("strips the differential block from the generated runtime machine (test-only data)", () => {
    // The IR carries a differential block (test fuzzing inputs), but the runtime machine embeds structure
    // only — it must not ship samples/seeds/contexts to the browser.
    expect(ir.differential).toBeDefined();
    expect(generateMachineFactory(ir)).not.toContain('"differential"');
  });

  it("the generated typed machine drives the turnstile with typed context and input", () => {
    const initial = turnstile.initial();
    expect(initial.state).toBe("Locked");

    // Coin requires a typed `{ coin }` input (Push would take none); the result's context is discriminated
    // by state, so on Unlocked `context.paidWith` is typed as string.
    const result = turnstile.advance(initial, "Coin", { coin: "quarter" });
    expect(result.outcome).toBe("transitioned");
    if (
      result.outcome === "transitioned" &&
      result.snapshot.state === "Unlocked"
    )
      expect(result.snapshot.context.paidWith).toBe("quarter");
  });
});

// The import-style option is what lets a vendoring consumer (nwyc's apps/web) collapse the three relative
// engine imports into one `@trax/state-machine` import without the hard-coded string `.replace()` its codegen
// test used to do. These pin both styles byte-exact so `trax machine generate --import-style` and any consumer
// stay in lockstep with the generator.
describe("generateMachineFactory import styles", () => {
  const ir = JSON.parse(
    fs.readFileSync(irFile("turnstile"), "utf8"),
  ) as IrDocument;

  const RELATIVE_IMPORTS = [
    `import { typedMachineFromIr } from "../../typed";`,
    `import type { IrDocument } from "../../rules/irMachine";`,
  ].join("\n");

  const SPECIFIER_IMPORT = `import { typedMachineFromIr, type IrDocument } from "@trax/state-machine";`;

  it("defaults to relative imports (unchanged from the no-options call)", () => {
    // The default MUST equal the argless call so the committed in-repo twin and its drift check never move.
    expect(generateMachineFactory(ir, { importStyle: "relative" })).toBe(
      generateMachineFactory(ir),
    );
    expect(generateMachineFactory(ir)).toContain(RELATIVE_IMPORTS);
    expect(generateMachineFactory(ir)).not.toContain("@trax/state-machine");
  });

  it("specifier style emits exactly the collapsed @trax/state-machine import", () => {
    // Byte-exact against nwyc's PACKAGE_IMPORT: symbol order and spacing must match, since this replaces the
    // three `.replace()` calls its codegen test did by hand.
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    expect(out).toContain(SPECIFIER_IMPORT);
    expect(out).not.toContain(`from "../../machine"`);
    expect(out).not.toContain(`from "../../rules/irMachine"`);
    expect(out).not.toContain(`from "../../typed"`);
  });

  it("specifier style differs from relative ONLY in the engine import lines", () => {
    // Everything after the imports (the embedded IR and the export) must be identical, so the two
    // styles are the same module with a different import head, nothing else.
    const relative = generateMachineFactory(ir, { importStyle: "relative" });
    const specifier = generateMachineFactory(ir, { importStyle: "specifier" });
    expect(specifier.replace(SPECIFIER_IMPORT, RELATIVE_IMPORTS)).toBe(
      relative,
    );
  });

  it("honors a custom specifier", () => {
    const out = generateMachineFactory(ir, {
      importStyle: "specifier",
      specifier: "@acme/machines",
    });
    expect(out).toContain(
      `import { typedMachineFromIr, type IrDocument } from "@acme/machines";`,
    );
  });

  it("still strips the differential block regardless of import style", () => {
    expect(
      generateMachineFactory(ir, { importStyle: "specifier" }),
    ).not.toContain('"differential"');
  });
});
