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
import { irFormatVersion } from "./irFormat";
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

  it("an effect-bound trigger takes no input in send", () => {
    const checkoutIr = JSON.parse(fs.readFileSync(irFile("checkout"), "utf8")) as IrDocument;
    expect(checkoutIr.inputs.Pay?.fields.length).toBeGreaterThan(0);

    const types = generateContextTypes(checkoutIr);

    expect(types).toContain("  Pay: undefined;");
    expect(types).not.toContain("CheckoutPayInput");
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

    // What the C# exporter writes for a `List<string>?` property: the element schema in `values`,
    // with no constraint to read it from.
    it("gives a nullable list its element type", () => {
      const out = generateContextTypes(
        schemaFor({
          name: "fields",
          type: "array",
          nullable: true,
          values: { type: "string", nullable: false },
        }),
      );
      expect(out).toContain("fields?: string[] | null;");
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

// The import-style option is what lets a vendoring consumer collapse the three relative
// engine imports into one `@traxsharp/state-machine` import without the hard-coded string `.replace()` its codegen
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

  const SPECIFIER_IMPORT = `import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";`;

  it("defaults to relative imports (unchanged from the no-options call)", () => {
    // The default MUST equal the argless call so the committed in-repo twin and its drift check never move.
    expect(generateMachineFactory(ir, { importStyle: "relative" })).toBe(
      generateMachineFactory(ir),
    );
    expect(generateMachineFactory(ir)).toContain(RELATIVE_IMPORTS);
    expect(generateMachineFactory(ir)).not.toContain("@traxsharp/state-machine");
  });

  it("specifier style emits exactly the collapsed @traxsharp/state-machine import", () => {
    // Byte-exact against the vendoring consumer's PACKAGE_IMPORT: symbol order and spacing must match, since this replaces the
    // three `.replace()` calls its codegen test did by hand.
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    expect(out).toContain(SPECIFIER_IMPORT);
    expect(out).not.toContain(`from "../../machine"`);
    expect(out).not.toContain(`from "../../rules/irMachine"`);
    expect(out).not.toContain(`from "../../typed"`);
  });

  it("specifier style differs from relative only in its header note, imports and IR format", () => {
    // The two styles are the same module but for the import head, the note saying how it was imported, and the
    // IR format the installed engine checks; the embedded IR and the export are otherwise identical.
    const relative = generateMachineFactory(ir, { importStyle: "relative" });
    const specifier = generateMachineFactory(ir, { importStyle: "specifier" });
    const lines = specifier.split("\n");
    const note = lines.slice(1, 4).join("\n") + "\n";
    expect(
      specifier
        .replace(note, "")
        .replace(SPECIFIER_IMPORT, RELATIVE_IMPORTS)
        .replace(`\n  "irFormat": ${irFormatVersion},`, ""),
    ).toBe(relative);
  });

  it("says in the header which import a specifier twin was generated with and how to change it", () => {
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    expect(out.split("\n").slice(0, 5).join("\n")).toBe(
      [
        "// AUTO-GENERATED from turnstile.ir.json by generateMachineFactory. Do not edit by hand.",
        '// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the',
        "// default without --engine-src). Name another module with --specifier <module>, or import an engine",
        "// checkout by relative path with --import-style relative --engine-src <engine/src>.",
        "",
      ].join("\n"),
    );
    expect(
      generateMachineFactory(ir, { importStyle: "specifier", specifier: "@acme/machines" }),
    ).toContain('// Imports the engine from "@acme/machines" (trax machine generate');
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

  it("writes the specifier as one string literal, whatever it holds", () => {
    // trax refuses a specifier that is not a package name; the generator must still never let one end the
    // string and become code.
    const hostile = 'x"; globalThis.pwned = true; import "y';
    const out = generateMachineFactory(ir, {
      importStyle: "specifier",
      specifier: hostile,
    });
    expect(out).toContain(
      `import { typedMachineFromIr, type IrDocument } from ${JSON.stringify(hostile)};`,
    );
    expect(out).not.toContain(`from "x";`);
  });

  it("records the IR format in a specifier twin's IR, where an engine older than the check ignores it", () => {
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    expect(out).toContain(`const ir = {\n  "irFormat": ${irFormatVersion},\n  "committedStates"`);
  });

  it("imports nothing but what every released engine exports, whatever generated it", () => {
    // A consumer who upgrades the trax tool before the package must still load the twin: an engine from before the
    // IR format existed exports typedMachineFromIr and IrDocument, and nothing newer.
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    const imports = out.split("\n").filter((line) => line.startsWith("import "));
    expect(imports).toEqual([
      SPECIFIER_IMPORT,
      `import type { TurnstileSpec } from "./turnstile.contexts.g";`,
    ]);
  });

  it("records no IR format in a relative twin, which imports the engine that generated it", () => {
    expect(generateMachineFactory(ir)).not.toContain("irFormat");
  });

  it("builds a specifier twin's IR on the engine of today", () => {
    const out = generateMachineFactory(ir, { importStyle: "specifier" });
    const literal = out.slice(out.indexOf("const ir = ") + "const ir = ".length, out.indexOf(" as IrDocument;"));
    const twinIr = JSON.parse(literal) as IrDocument;
    expect(twinIr.irFormat).toBe(irFormatVersion);
    expect(() => machineFromIr(twinIr)).not.toThrow();
  });

  it("still strips the differential block regardless of import style", () => {
    expect(
      generateMachineFactory(ir, { importStyle: "specifier" }),
    ).not.toContain('"differential"');
  });
});

// The IR is data a host exported: its names are checked before any of them is written as code.
describe("generated names", () => {
  const ir = JSON.parse(fs.readFileSync(irFile("turnstile"), "utf8")) as IrDocument;

  it("refuses a state that is not an identifier", () => {
    const quoted = { ...ir, states: [...ir.states, 'Done"; alert(1); "'] };
    expect(() => generateContextTypes(quoted)).toThrow(/not an identifier/);
    expect(() => generateMachineFactory(quoted)).toThrow(/not an identifier/);
  });

  it("refuses a trigger that is not an identifier", () => {
    expect(() => generateContextTypes({ ...ir, triggers: ["Coin\nexport"] })).toThrow(/not an identifier/);
  });

  it("refuses a state or trigger named after an Object.prototype member", () => {
    for (const name of ["__proto__", "constructor", "toString", "hasOwnProperty"]) {
      const state = { ...ir, states: [...ir.states, name] };
      expect(() => generateContextTypes(state)).toThrow(/every JavaScript object has/);
      expect(() => generateMachineFactory(state)).toThrow(/every JavaScript object has/);
      expect(() => generateContextTypes({ ...ir, triggers: [...ir.triggers, name] })).toThrow(
        /every JavaScript object has/,
      );
    }
  });

  it("refuses a context, invariant or input keyed by something that is not a state or trigger", () => {
    // Parsed, as a manifest is: a "__proto__" key written in an object literal would set the prototype instead.
    const keyed = (map: string, key: string) =>
      JSON.parse(JSON.stringify(ir).replace(`"${map}":{`, `"${map}":{${JSON.stringify(key)}:{"fields":[]},`)) as IrDocument;
    for (const [map, key] of [["context", "__proto__"], ["context", "Nowhere"], ["inputs", "__proto__"], ["inputs", "Locked"]]) {
      const bad = keyed(map, key);
      expect(Object.keys(bad[map as "context"])).toContain(key);
      expect(() => generateContextTypes(bad)).toThrow(/which is not one of its/);
      expect(() => generateMachineFactory(bad)).toThrow(/which is not one of its/);
    }
    const invariants = JSON.parse(JSON.stringify({ ...ir, invariants: {} }).replace('"invariants":{}', '"invariants":{"__proto__":{}}')) as IrDocument;
    expect(() => generateMachineFactory(invariants)).toThrow(/invariants for "__proto__"/);
  });

  it("refuses a __proto__ key anywhere in the IR", () => {
    const bad = JSON.parse(JSON.stringify(ir).replace('"initialContext":{}', '"initialContext":{"__proto__":{"x":1}}')) as IrDocument;
    expect(Object.hasOwn(bad.initialContext as object, "__proto__")).toBe(true);
    expect(() => generateMachineFactory(bad)).toThrow(/prototype/);
  });

  it("refuses an outcome that is not <State>.<kind>", () => {
    const outcomes = { "Fetching.done;x": { from: "Locked" } } as unknown as IrDocument["outcomes"];
    expect(() => generateContextTypes({ ...ir, outcomes })).toThrow(/not <State>.<kind>/);
  });

  it("refuses an id that is not kebab-case", () => {
    expect(() => generateContextTypes({ ...ir, id: "../escaped" })).toThrow(/kebab-case/);
    expect(() => generateMachineFactory({ ...ir, id: "turnstile\n// x" })).toThrow(/kebab-case/);
  });

  it("writes each state of the union as a string literal", () => {
    expect(generateContextTypes(ir)).toContain('export type TurnstileState = "Locked" | "Unlocked";');
  });
});
