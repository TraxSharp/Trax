import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const invokedDirectly = process.argv[1] === fileURLToPath(import.meta.url);

// The exit code when the engine's dependencies are not installed, which the C# caller reads as "not installed"
// (the same code replay-sequences.mjs uses).
const MISSING_DEPENDENCIES = 3;

let Ajv2020;
try {
  ({ default: Ajv2020 } = await import("ajv/dist/2020.js"));
} catch (err) {
  if (!invokedDirectly) throw err;
  console.error("ajv is not installed: run npm ci in Trax.Api.StateMachine.");
  process.exit(MISSING_DEPENDENCIES);
}

const schemaPath = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "conformance", "ir.schema.json");

/**
 * Compiles conformance/ir.schema.json once and returns a function that checks one IR document, answering the
 * schema's errors as `instancePath message` lines (empty when the document is valid).
 */
export function irValidator() {
  const ajv = new Ajv2020({ allErrors: true, strict: true, strictRequired: false });
  const validate = ajv.compile(JSON.parse(readFileSync(schemaPath, "utf8")));
  return (ir) =>
    validate(ir) ? [] : (validate.errors ?? []).map((e) => `${e.instancePath || "/"} ${e.message}`);
}

// `node tools/conformance/ir-schema.mjs < irs.json` reads a JSON array of IR documents and writes, for each, the
// list of its errors, as one JSON array. The C# IrSchemaTests checks every IR its exporter writes this way.
if (invokedDirectly) {
  const check = irValidator();
  const irs = JSON.parse(readFileSync(0, "utf8"));
  process.stdout.write(JSON.stringify(irs.map(check)));
}
