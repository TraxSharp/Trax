import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const invokedDirectly = process.argv[1] === fileURLToPath(import.meta.url);

// The exit code when the engine's dependencies are not installed, which the C# caller reads as "not installed"
// (the same code ir-schema.mjs uses).
const MISSING_DEPENDENCIES = 3;

let Ajv2020;
try {
  ({ default: Ajv2020 } = await import("ajv/dist/2020.js"));
} catch (err) {
  if (!invokedDirectly) throw err;
  console.error("ajv is not installed: run npm ci in Trax.StateMachine/ts.");
  process.exit(MISSING_DEPENDENCIES);
}

const conformance = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "conformance");
const read = (name) => JSON.parse(readFileSync(join(conformance, name), "utf8"));

/** The formats a machine derived from features is written in, by name, and the schema each is held to. */
export const formats = { glossary: "glossary.schema.json", host: "host.schema.json" };

/**
 * Compiles the schema of one format (`glossary` or `host`), with the IR schema it borrows shapes from, and returns a
 * function that checks one document's text, answering `instancePath message` lines (empty when it is valid).
 */
export function formatValidator(format) {
  const file = formats[format];
  if (!file) throw new Error(`'${format}' is not a format: ${Object.keys(formats).join(", ")}.`);
  const ajv = new Ajv2020({ allErrors: true, strict: true, strictRequired: false });
  ajv.addSchema(read("ir.schema.json"));
  const validate = ajv.compile(read(file));
  return (text) => {
    let document;
    try {
      document = JSON.parse(text);
    } catch (err) {
      return [`/ is not JSON: ${err.message}`];
    }
    return validate(document) ? [] : (validate.errors ?? []).map((e) => `${e.instancePath || "/"} ${e.message}`);
  };
}

// `node tools/conformance/format-schema.mjs < request.json` reads `{ "format": "glossary" | "host", "documents":
// [text, ...] }` and writes, for each document, the list of its errors, as one JSON array. The C# FormatCorpusTests
// holds the readers to the schemas this way.
if (invokedDirectly) {
  const request = JSON.parse(readFileSync(0, "utf8"));
  const check = formatValidator(request.format);
  process.stdout.write(JSON.stringify(request.documents.map(check)));
}
