import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { irFile } from "../fixtures";
import { irFormatVersion, oldestIrFormatRead, requireReadableIrFormat } from "./irFormat";
import { machineFromIr, type IrDocument } from "./irMachine";

// A twin runs on whichever engine the consumer installed. What decides whether it can is the IR format it was
// generated for, not the package's version, which every release bumps whether the IR changed or not.
describe("requireReadableIrFormat", () => {
  it("accepts every format this engine reads", () => {
    for (let format = oldestIrFormatRead; format <= irFormatVersion; format++)
      expect(() => requireReadableIrFormat(format)).not.toThrow();
  });

  it("does not check an IR that names no format", () => {
    expect(() => requireReadableIrFormat(undefined)).not.toThrow();
  });

  it("refuses a newer format, saying what to do", () => {
    expect(() => requireReadableIrFormat(irFormatVersion + 1)).toThrow(
      new RegExp(
        `generated for IR format ${irFormatVersion + 1}, but the installed @traxsharp/state-machine reads only up ` +
          `to format ${irFormatVersion}\\. Upgrade @traxsharp/state-machine .* or regenerate the twin`,
      ),
    );
  });

  it("refuses a format older than it reads", () => {
    expect(() => requireReadableIrFormat(oldestIrFormatRead - 1)).toThrow(
      /no longer reads .* Regenerate the twin/,
    );
  });

  it.each([["1"], [1.5], [null]])("refuses %j, which is not a format number", (format) => {
    expect(() => requireReadableIrFormat(format)).toThrow(/not a format number/);
  });
});

describe("machineFromIr", () => {
  const ir = JSON.parse(fs.readFileSync(irFile("turnstile"), "utf8")) as IrDocument;

  it("builds an IR of a format it reads, or of none", () => {
    expect(() => machineFromIr(ir)).not.toThrow();
    expect(() => machineFromIr({ ...ir, irFormat: irFormatVersion })).not.toThrow();
  });

  it("refuses an IR of a format it cannot read before building anything", () => {
    expect(() => machineFromIr({ ...ir, irFormat: irFormatVersion + 1 })).toThrow(/IR format/);
  });
});
