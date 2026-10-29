// Emits per-machine TypeScript types from the IR: state/trigger unions, a context type per state, a typed
// input per trigger, and a Spec that binds them. This is the type-safety layer over the IR-driven runtime
// (machineFromIr): consumers get typed context per state and typed input per trigger, all derived from the
// one C# source, with no hand-written twin.
//
// Object shapes are emitted as `type` aliases, not `interface`, so they carry the implicit index signature
// that `MachineSpec` (Record<string, ...>) requires.

import { checkTwinNames, camel, escapeBidi, machineModel, pascal, type FieldModel, type TypeRef, type ValueRef } from "../codegen/model";
import { irFormatVersion } from "./irFormat";
import type { IrDocument } from "./irMachine";

export function generateContextTypes(ir: IrDocument): string {
  checkTwinNames(ir);
  const model = machineModel(ir);
  const machine = model.pascal;
  const lines: string[] = [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateContextTypes. Do not edit by hand.`,
    "",
    `export type ${machine}State = ${union(model.states)};`,
    `export type ${machine}Trigger = ${union(model.triggers)};`,
  ];
  // The outcome triggers of states that invoke a train (`Fetching.done`), a kind of their own. Emitted only
  // when the machine has some, so a machine that invokes nothing generates exactly what it did before.
  if (model.outcomes.length > 0)
    lines.push(`export type ${machine}Outcome = ${union(model.outcomes)};`);
  lines.push("");

  // A context type per state, and the state -> context map.
  for (const { record } of model.contexts)
    lines.push(...emitType(`${machine}${record.suffix}`, record.fields), "");
  lines.push(`export type ${machine}Contexts = {`);
  for (const state of model.states)
    lines.push(`  ${state}: ${machine}${state}Context;`);
  lines.push("};", "");

  // An input type per trigger that declares one, and the trigger -> input map (undefined when none). An
  // effect-bound trigger's input is the receipt the server's effect runner hands it, so a client sends it with none.
  for (const { record } of model.inputs)
    if (record) lines.push(...emitType(`${machine}${record.suffix}`, record.fields), "");
  // A success outcome's input is the invoked train's output, exported under the outcome.
  for (const { record } of model.outcomeInputs)
    if (record) lines.push(...emitType(`${machine}${record.suffix}`, record.fields), "");
  lines.push(`export type ${machine}Inputs = {`);
  for (const { trigger, record } of model.inputs)
    lines.push(`  ${trigger}: ${record ? `${machine}${record.suffix}` : "undefined"};`);
  for (const { outcome, record } of model.outcomeInputs)
    lines.push(`  ${JSON.stringify(outcome)}: ${record ? `${machine}${record.suffix}` : "undefined"};`);
  lines.push("};", "");

  // The Spec: states -> contexts, triggers -> inputs. Satisfies MachineSpec, ready for TypedMachine.
  lines.push(
    `export type ${machine}Spec = {`,
    `  states: ${machine}Contexts;`,
    `  triggers: ${machine}Inputs;`,
    "};",
    "",
  );

  // A field name or a value the IR holds is written into a string literal, where a bidirectional control is escaped.
  return escapeBidi(lines.join("\n"));
}

/**
 * How the generated factory imports the engine symbols (SnapshotMachine, machineFromIr, TypedMachine,
 * IrDocument):
 *
 * - `"relative"` (default): three imports relative to the engine's own `src`
 *   (`../../machine`, `../../rules/irMachine`, `../../typed`). Right for a machine living inside this repo.
 * - `"specifier"`: one collapsed import from a module specifier (`@traxsharp/state-machine` by default). Right for
 *   a consumer that installs the package, or vendors the engine behind a path alias. This replaces the
 *   hard-coded string-`.replace()` rewrite consumers used to do by hand.
 *
 * A specifier twin runs on whichever engine the consumer installed, so its header names the import it was
 * generated with and how to change it, and its IR carries `irFormat`, the IR format it was generated for, which
 * the installed engine checks when the twin is imported (irFormat.ts). It imports nothing but
 * `typedMachineFromIr` and `IrDocument`, which every release of the engine exports, so an older engine still
 * loads it. A relative twin imports the very engine that generated it, and carries neither.
 */
export interface MachineFactoryOptions {
  importStyle?: "relative" | "specifier";
  /** The module specifier used when `importStyle` is `"specifier"`. Defaults to `@traxsharp/state-machine`. */
  specifier?: string;
  /**
   * SHA-256 of the machine's canonical IR (the committed `ir.json`). Emitted as an `irHash` export and passed
   * to the factory so the twin carries the version-skew handshake token; matches C#'s `IMachine.SchemaHash`.
   * Callers that read the IR from disk should hash those exact bytes.
   */
  irHash?: string;
}

/**
 * Emits a runnable, typed machine factory: it embeds the IR (so the module is self-contained and
 * browser-safe, no fs), builds the machine via machineFromIr, and wraps it in TypedMachine<Spec> for typed
 * context-by-state and input-by-trigger. This is the generated replacement for a hand-written twin.
 */
export function generateMachineFactory(
  ir: IrDocument,
  options: MachineFactoryOptions = {},
): string {
  checkTwinNames(ir);
  const machine = pascal(ir.id);
  const importStyle = options.importStyle ?? "relative";
  const specifier = options.specifier ?? "@traxsharp/state-machine";
  const irHash = options.irHash ?? "";
  // The differential block is test-only fuzzing data (samples/seeds/contexts); the runtime machine never
  // reads it. Strip it so the generated module stays lean and doesn't ship test inputs to the browser.
  const { differential: _differential, ...runtimeIr } = ir;
  const specifierStyle = importStyle === "specifier";
  // JSON.stringify escapes CR and LF but leaves U+2028 and U+2029, which end a line comment as well.
  const literal = JSON.stringify(specifier).replace(/[\u2028\u2029]/g, (c) => `\\u${c.charCodeAt(0).toString(16)}`);
  const engineImports = specifierStyle
      ? // A JSON literal, so a specifier holding a quote stays one string literal rather than ending it.
        [`import { typedMachineFromIr, type IrDocument } from ${literal};`]
      : [
          `import { typedMachineFromIr } from "../../typed";`,
          `import type { IrDocument } from "../../rules/irMachine";`,
        ];
  // The literal keeps the specifier one line, so it cannot end the comment it is written into.
  const importNote = specifierStyle
    ? [
        `// Imports the engine from ${literal} (trax machine generate --import-style specifier, the`,
        "// default without --engine-src). Name another module with --specifier <module>, or import an engine",
        "// checkout by relative path with --import-style relative --engine-src <engine/src>.",
      ]
    : [];
  // The IR format rides in the IR rather than in a call, so an engine older than the check ignores it.
  const twinIr = specifierStyle ? { irFormat: irFormatVersion, ...runtimeIr } : runtimeIr;
  const { text: irText, shared } = shareOutputSchemas(twinIr);
  const source = [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateMachineFactory. Do not edit by hand.`,
    ...importNote,
    "",
    ...engineImports,
    `import type { ${machine}Spec } from "./${ir.id}.contexts.g";`,
    "",
    ...shared,
    `const ir = ${irText} as IrDocument;`,
    "",
    `/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */`,
    `export const irHash = ${JSON.stringify(irHash)};`,
    "",
    `/** The ${ir.id} machine, built from the IR and typed by ${machine}Spec. No hand-written twin. */`,
    `export const ${camel(ir.id)} = typedMachineFromIr<${machine}Spec>(ir, irHash);`,
    "",
  ].join("\n");
  // The IR's strings sit in string literals, where a bidirectional control is escaped; the import note's specifier is
  // in a comment, where the escape is shown as text, which is what a reviewer should see.
  return escapeBidi(source);
}

// An output schema two or more outcomes share (invoking states whose trains answer with one type) is written once,
// as a const each outcome names, rather than once per state: the IR's value is the same, and the page's bundle
// carries the schema once. Placeholders stand in for the consts while the IR is stringified.
function shareOutputSchemas(ir: object): { text: string; shared: string[] } {
  const outcomes = (ir as { outcomes?: Record<string, { output?: unknown }> }).outcomes ?? {};
  const byText = new Map<string, string[]>();
  for (const [key, outcome] of Object.entries(outcomes)) {
    if (outcome.output === undefined) continue;
    const text = JSON.stringify(outcome.output);
    byText.set(text, [...(byText.get(text) ?? []), key]);
  }

  const shared: string[] = [];
  const replaced: Record<string, unknown> = {};
  const placeholders: [string, string][] = [];
  for (const [text, keys] of byText) {
    if (keys.length < 2) continue;
    const name = `sharedOutput${shared.length}`;
    const placeholder = `\u0000${name}`;
    shared.push(`const ${name} = ${JSON.stringify(JSON.parse(text), null, 2)};`, "");
    placeholders.push([JSON.stringify(placeholder), name]);
    for (const key of keys) replaced[key] = { ...outcomes[key], output: placeholder };
  }
  if (shared.length === 0) return { text: JSON.stringify(ir, null, 2), shared };

  let text = JSON.stringify(
    { ...ir, outcomes: { ...outcomes, ...replaced } },
    null,
    2,
  );
  for (const [quoted, name] of placeholders) text = text.split(quoted).join(name);
  return { text, shared };
}

// Object shapes are `type` aliases rather than interfaces, so they carry the implicit index signature `MachineSpec`
// (Record<string, ...>) requires.
function emitType(name: string, fields: readonly FieldModel[]): string[] {
  if (fields.length === 0) return [`export type ${name} = Record<string, never>;`];
  const lines = [`export type ${name} = {`];
  for (const field of fields) lines.push(`  ${member(field)};`);
  lines.push("};");
  return lines;
}

// A nullable field is optional and may be null (matches the validator: absent-or-null is allowed).
function member(field: FieldModel): string {
  const key = /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(field.name) ? field.name : JSON.stringify(field.name);
  const optional = field.nullable ? "?" : "";
  const nullable = field.nullable ? " | null" : "";
  return `${key}${optional}: ${tsType(field.type)}${nullable}`;
}

// A nested record's shape, inline.
const shapeType = (fields: readonly FieldModel[]): string =>
  fields.length === 0 ? "Record<string, never>" : `{ ${fields.map((f) => `${member(f)};`).join(" ")} }`;

function tsType(type: TypeRef): string {
  switch (type.kind) {
    case "string":
      return "string";
    case "number":
    case "integer":
      return "number";
    case "boolean":
      return "boolean";
    case "literals":
      return union(type.values);
    case "record":
      return shapeType(type.fields);
    case "unknownList":
      return "unknown[]";
    case "unknownObject":
      return "Record<string, unknown>";
    case "map":
      return `Record<string, ${valueType(type.of)}>`;
    case "list": {
      const value = valueType(type.of);
      // A list read from a schema of its values parenthesizes an element that holds a union anywhere in it.
      return type.from === "values" && value.includes(" | ") ? `(${value})[]` : `${value}[]`;
    }
  }
}

const valueType = (value: ValueRef): string =>
  value.nullable ? `${tsType(value.type)} | null` : tsType(value.type);

// Each value as a string literal: its JSON, so nothing in it can end the literal.
const union = (values: readonly string[]): string =>
  values.map((v) => JSON.stringify(v)).join(" | ");
