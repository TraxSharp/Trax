import fs from "node:fs";
import { describe, expect, it } from "vitest";

import { irFile } from "../fixtures";
import type { IrDocument } from "../rules/irMachine";
import { generateCSharpView } from "./csharp";

// C# reads U+0085, U+2028 and U+2029 as line breaks, which a regular or a single-line raw string literal cannot hold,
// and JSON.stringify leaves them as they are.
const LINE_BREAKS = new RegExp(`[${String.fromCharCode(0x85, 0x2028, 0x2029)}]`);

describe("the C# view", () => {
  it.each(["0085", "2028", "2029"])("escapes U+%s wherever it writes a string", (code) => {
    const character = String.fromCharCode(parseInt(code, 16));
    const ir = JSON.parse(fs.readFileSync(irFile("turnstile"), "utf8")) as IrDocument;
    ir.context.Unlocked!.fields[0]!.name = `paid${character}With`;
    const irText = JSON.stringify({
      ...ir,
      transitions: ir.transitions.map((t) => ({ ...t, guardMessage: `a${character}b` })),
    });

    const view = generateCSharpView(ir, { irText, irHash: "0".repeat(64) });

    expect(view).not.toMatch(LINE_BREAKS);
    expect(view).toContain(`[global::System.Text.Json.Serialization.JsonPropertyName("paid\\u${code}With")]`);
    expect(view).toContain(`public const string Ir = "{`);
    expect(view).toContain(`a\\u${code}b`);
  });

  it("keeps the IR a raw literal when it holds no line break", () => {
    const irText = fs.readFileSync(irFile("turnstile"), "utf8").trimEnd();
    const view = generateCSharpView(JSON.parse(irText) as IrDocument, { irText, irHash: "0".repeat(64) });

    expect(view).toContain(`public const string Ir = """"${irText}"""";`);
  });
});

// Each IR below is one the TypeScript twin takes and C# cannot: written as it was, the view would not compile (or
// would throw when it reads its JSON). The view names the runtime's types in full, and refuses the rest naming both.
describe("the C# view of a machine whose names C# cannot hold", () => {
  const turnstile = () => JSON.parse(fs.readFileSync(irFile("turnstile"), "utf8")) as IrDocument;
  const view = (ir: IrDocument) => generateCSharpView(ir, { irText: JSON.stringify(ir), irHash: "0".repeat(64) });
  const field = (name: string, extra: object = {}) => ({ name, type: "string", nullable: false, constraints: [], ...extra });
  const withStates = (states: string[]): IrDocument => ({
    ...turnstile(),
    states,
    initialState: states[0]!,
    context: Object.fromEntries(states.map((s) => [s, { fields: [] }])),
    transitions: [],
  });
  const withTriggers = (triggers: string[]): IrDocument => ({ ...turnstile(), triggers, inputs: {}, transitions: [] });
  const withFields = (fields: object[]): IrDocument => {
    const ir = turnstile();
    return { ...ir, context: { ...ir.context, Unlocked: { fields } } } as IrDocument;
  };

  it("names the runtime's IrMachine in full, so a machine with id 'ir' declares its own beside it", () => {
    const written = view({ ...turnstile(), id: "ir" });

    expect(written).toContain("public static class IrMachine");
    expect(written).toContain("public static global::Trax.StateMachine.IrMachine Create() =>");
    expect(written).toContain("        global::Trax.StateMachine.ClientManifestMachine machine,");
    expect(written).not.toMatch(/^using /m);
  });

  it("refuses states whose contexts would be one property", () =>
    expect(() => view(withStates(["foo", "Foo"]))).toThrow(
      "the context of foo and the context of Foo both write the C# name 'FooContext'",
    ));

  it("refuses triggers whose methods would be one", () =>
    expect(() => view(withTriggers(["go_on", "goOn"]))).toThrow(
      "the trigger go_on and the trigger goOn both write the C# name 'SendGoOnAsync'",
    ));

  it("refuses a field whose record would be named as another field's", () =>
    expect(() =>
      view(
        withFields([
          field("bar", { type: "array", fields: [field("x")] }),
          field("barItem", { type: "object", fields: [field("y")] }),
        ]),
      ),
    ).toThrow(
      `the field "bar" of TurnstileUnlockedContext and the field "barItem" of TurnstileUnlockedContext both write the C# name 'TurnstileUnlockedContextBarItem'`,
    ));

  it.each(["toString", "equals", "getHashCode", "equalityContract", "getType"])(
    "refuses a field %s, which every record has",
    (name) => expect(() => view(withFields([field(name)]))).toThrow("which every C# record has"),
  );

  it.each(["$x", "__arglist", "__makeref"])("refuses a state %s, which C# does not take", (state) =>
    expect(() => view(withStates(["Locked", state]))).toThrow(
      `has the state ${JSON.stringify(state)}, which C# does not take as a member name`,
    ),
  );

  it("refuses context fields that differ only in case, which the view's JSON reads as one", () =>
    expect(() => view(withFields([field("Id"), field("id")]))).toThrow(
      'The fields "Id" and "id" of TurnstileUnlockedContext differ only in case',
    ));

  it("escapes a bidirectional control in the IR and in every string it writes", () => {
    const ir = turnstile();
    ir.context.Unlocked!.fields[0]!.name = "paid‮With";
    const irText = JSON.stringify({ ...ir, transitions: ir.transitions.map((t) => ({ ...t, guardMessage: "a‮b" })) });

    const written = generateCSharpView(ir, { irText, irHash: "0".repeat(64) });

    expect(written).not.toMatch(/[‪-‮⁦-⁩‎‏؜]/);
    expect(written).toContain('[global::System.Text.Json.Serialization.JsonPropertyName("paid\\u202EWith")]');
    expect(written).toContain("a\\u202Eb");
    expect(written).toContain('public const string Ir = "{');
  });
});
