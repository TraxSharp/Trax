// Emits per-machine TypeScript types from the IR: state/trigger unions, a context type per state, a typed
// input per trigger, and a Spec that binds them. This is the type-safety layer over the IR-driven runtime
// (machineFromIr): consumers get typed context per state and typed input per trigger, all derived from the
// one C# source, with no hand-written twin.
//
// Object shapes are emitted as `type` aliases, not `interface`, so they carry the implicit index signature
// that `MachineSpec` (Record<string, ...>) requires.

import type { ContextSchema, FieldSchema, JsonFieldType } from "./interpreter";
import type { IrDocument } from "./irMachine";

export function generateContextTypes(ir: IrDocument): string {
  const machine = pascal(ir.id);
  const lines: string[] = [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateContextTypes. Do not edit by hand.`,
    "",
    `export type ${machine}State = ${union(ir.states)};`,
    `export type ${machine}Trigger = ${union(ir.triggers)};`,
    "",
  ];

  // A context type per state, and the state -> context map.
  for (const state of ir.states)
    lines.push(
      ...emitType(
        `${machine}${state}Context`,
        ir.context[state] ?? { fields: [] },
      ),
      "",
    );
  lines.push(`export type ${machine}Contexts = {`);
  for (const state of ir.states)
    lines.push(`  ${state}: ${machine}${state}Context;`);
  lines.push("};", "");

  // An input type per trigger that declares one, and the trigger -> input map (undefined when none).
  for (const trigger of ir.triggers) {
    const schema = ir.inputs[trigger];
    if (schema && schema.fields.length > 0)
      lines.push(...emitType(`${machine}${trigger}Input`, schema), "");
  }
  lines.push(`export type ${machine}Inputs = {`);
  for (const trigger of ir.triggers) {
    const schema = ir.inputs[trigger];
    const type =
      schema && schema.fields.length > 0
        ? `${machine}${trigger}Input`
        : "undefined";
    lines.push(`  ${trigger}: ${type};`);
  }
  lines.push("};", "");

  // The Spec: states -> contexts, triggers -> inputs. Satisfies MachineSpec, ready for TypedMachine.
  lines.push(
    `export type ${machine}Spec = {`,
    `  states: ${machine}Contexts;`,
    `  triggers: ${machine}Inputs;`,
    "};",
    "",
  );

  return lines.join("\n");
}

/**
 * How the generated factory imports the engine symbols (SnapshotMachine, machineFromIr, TypedMachine,
 * IrDocument):
 *
 * - `"relative"` (default): three imports relative to the engine's own `src`
 *   (`../../machine`, `../../rules/irMachine`, `../../typed`). Right for a machine living inside this repo.
 * - `"specifier"`: one collapsed import from a module specifier (`@trax/state-machine` by default). Right for
 *   a consumer that vendors the engine behind a path alias, e.g. nwyc's `apps/web`. This replaces the
 *   hard-coded string-`.replace()` rewrite consumers used to do by hand.
 */
export interface MachineFactoryOptions {
  importStyle?: "relative" | "specifier";
  /** The module specifier used when `importStyle` is `"specifier"`. Defaults to `@trax/state-machine`. */
  specifier?: string;
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
  const machine = pascal(ir.id);
  const importStyle = options.importStyle ?? "relative";
  const specifier = options.specifier ?? "@trax/state-machine";
  // The differential block is test-only fuzzing data (samples/seeds/contexts); the runtime machine never
  // reads it. Strip it so the generated module stays lean and doesn't ship test inputs to the browser.
  const { differential: _differential, ...runtimeIr } = ir;
  const engineImports =
    importStyle === "specifier"
      ? [`import { typedMachineFromIr, type IrDocument } from "${specifier}";`]
      : [
          `import { typedMachineFromIr } from "../../typed";`,
          `import type { IrDocument } from "../../rules/irMachine";`,
        ];
  return [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateMachineFactory. Do not edit by hand.`,
    "",
    ...engineImports,
    `import type { ${machine}Spec } from "./${ir.id}.contexts.g";`,
    "",
    `const ir = ${JSON.stringify(runtimeIr, null, 2)} as IrDocument;`,
    "",
    `/** The ${ir.id} machine, built from the IR and typed by ${machine}Spec. No hand-written twin. */`,
    `export const ${camel(ir.id)} = typedMachineFromIr<${machine}Spec>(ir);`,
    "",
  ].join("\n");
}

function emitType(name: string, schema: ContextSchema): string[] {
  if (schema.fields.length === 0)
    return [`export type ${name} = Record<string, never>;`];

  const lines = [`export type ${name} = {`];
  for (const field of [...schema.fields].sort((a, b) =>
    a.name < b.name ? -1 : a.name > b.name ? 1 : 0,
  ))
    lines.push(`  ${member(field)};`);
  lines.push("};");
  return lines;
}

// A nullable field is optional and may be null (matches the validator: absent-or-null is allowed).
function member(field: FieldSchema): string {
  const key = /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(field.name)
    ? field.name
    : JSON.stringify(field.name);
  const optional = field.nullable ? "?" : "";
  const nullable = field.nullable ? " | null" : "";
  return `${key}${optional}: ${tsType(field.type)}${nullable}`;
}

function tsType(type: JsonFieldType): string {
  switch (type) {
    case "string":
      return "string";
    case "number":
      return "number";
    case "boolean":
      return "boolean";
    case "array":
      return "unknown[]";
    case "object":
      return "Record<string, unknown>";
  }
}

const union = (values: readonly string[]): string =>
  values.map((v) => `"${v}"`).join(" | ");

// A machine id is kebab/snake (write-to-congress); its type prefix is PascalCase (WriteToCongress) and its
// exported const is camelCase (writeToCongress). Splitting on - and _ is what makes a multi-word id emit valid
// identifiers; a single-word id (turnstile) is unchanged.
const pascal = (id: string): string =>
  id
    .split(/[-_]+/)
    .filter((s) => s.length > 0)
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("");

const camel = (id: string): string => {
  const p = pascal(id);
  return p.length === 0 ? p : p.charAt(0).toLowerCase() + p.slice(1);
};
