import { createHash } from "node:crypto";
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
  // The twin embeds SHA-256 of the IR content with the trailing newline stripped (matching the codegen
  // entrypoint and C#'s SchemaHash, which hashes ExportIr() — no trailing newline), so the drift check must
  // regenerate with that same hash.
  const irHash = createHash("sha256")
    .update(fs.readFileSync(irFile("turnstile"), "utf8").replace(/\n+$/, ""), "utf8")
    .digest("hex");

  it("regenerates the committed turnstile context types (drift check)", () => {
    expect(generateContextTypes(ir)).toBe(
      fs.readFileSync(committedFile, "utf8"),
    );
  });

  // Turnstile alone is not enough cover: its schema carries no arrayOf/oneOf, so a generator change
  // that alters constrained fields regenerates it byte-identically and the drift check stays green.
  // Checkout has a constrained field, so every committed twin in the repo is now pinned.
  it("regenerates the committed checkout context types (drift check)", () => {
    const checkoutIr = JSON.parse(
      fs.readFileSync(irFile("checkout"), "utf8"),
    ) as IrDocument;
    expect(generateContextTypes(checkoutIr)).toBe(
      fs.readFileSync(
        fileURLToPath(
          new URL("../machines/checkout/checkout.contexts.g.ts", import.meta.url),
        ),
        "utf8",
      ),
    );
  });

  // A field's own constraints ARE type information: reading them is what keeps a consumer from
  // hand-maintaining a precise copy of a context the IR already describes exactly.
  describe("constraint-derived field types", () => {
    const schemaFor = (field: Record<string, unknown>): IrDocument =>
      ({
        id: "sample",
        version: 1,
        initialState: "Only",
        states: ["Only"],
        triggers: ["Go"],
        committedStates: [],
        context: { Only: { fields: [field] } },
        inputs: {},
        transitions: [],
      }) as unknown as IrDocument;

    it("gives an arrayOf array its element type instead of unknown[]", () => {
      const out = generateContextTypes(
        schemaFor({
          name: "ids",
          type: "array",
          nullable: false,
          constraints: [
            { rule: "arrayOf", source: "context", field: "ids", type: "number" },
          ],
        }),
      );
      expect(out).toContain("ids: number[];");
    });

    it("gives a oneOf string its literal union instead of string", () => {
      const out = generateContextTypes(
        schemaFor({
          name: "size",
          type: "string",
          nullable: false,
          constraints: [
            { rule: "oneOf", source: "context", field: "size", values: ["s", "m"] },
          ],
        }),
      );
      expect(out).toContain('size: "s" | "m";');
    });

    it("leaves a field alone when the constraint targets another field or the trigger input", () => {
      const out = generateContextTypes(
        schemaFor({
          name: "ids",
          type: "array",
          nullable: false,
          constraints: [
            { rule: "arrayOf", source: "input", field: "ids", type: "number" },
            { rule: "arrayOf", source: "context", field: "other", type: "number" },
          ],
        }),
      );
      expect(out).toContain("ids: unknown[];");
    });

    it("keeps a narrowed nullable field optional and nullable", () => {
      const out = generateContextTypes(
        schemaFor({
          name: "size",
          type: "string",
          nullable: true,
          constraints: [
            { rule: "oneOf", source: "context", field: "size", values: ["s"] },
          ],
        }),
      );
      expect(out).toContain('size?: "s" | null;');
    });
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
    expect(generateMachineFactory(ir, { irHash })).toBe(
      fs.readFileSync(committedFactory, "utf8"),
    );
  });

  it("embeds the IR hash and exposes it as the machine's schemaHash (the handshake token)", () => {
    expect(irHash).toMatch(/^[0-9a-f]{64}$/);
    expect(generateMachineFactory(ir, { irHash })).toContain(
      `export const irHash = "${irHash}";`,
    );
    // The built twin surfaces it as schemaHash, so a client can send it to the server for the skew handshake.
    expect(turnstile.schemaHash).toBe(irHash);
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
