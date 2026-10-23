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
    expect(view).toContain(`[JsonPropertyName("paid\\u${code}With")]`);
    expect(view).toContain(`public const string Ir = "{`);
    expect(view).toContain(`a\\u${code}b`);
  });

  it("keeps the IR a raw literal when it holds no line break", () => {
    const irText = fs.readFileSync(irFile("turnstile"), "utf8").trimEnd();
    const view = generateCSharpView(JSON.parse(irText) as IrDocument, { irText, irHash: "0".repeat(64) });

    expect(view).toContain(`public const string Ir = """"${irText}"""";`);
  });
});
