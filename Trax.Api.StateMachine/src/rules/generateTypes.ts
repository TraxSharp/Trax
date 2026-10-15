// Emits per-machine TypeScript types from the IR: state/trigger unions, a context type per state, a typed
// input per trigger, and a Spec that binds them. This is the type-safety layer over the IR-driven runtime
// (machineFromIr): consumers get typed context per state and typed input per trigger, all derived from the
// one C# source, with no hand-written twin.
//
// Object shapes are emitted as `type` aliases, not `interface`, so they carry the implicit index signature
// that `MachineSpec` (Record<string, ...>) requires.

import type {
  ContextSchema,
  FieldSchema,
  JsonFieldType,
  RuleSource,
  ValueSchema,
} from "./interpreter";
import { irFormatVersion } from "./irFormat";
import type { IrDocument } from "./irMachine";

export function generateContextTypes(ir: IrDocument): string {
  checkNames(ir);
  const machine = pascal(ir.id);
  const lines: string[] = [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateContextTypes. Do not edit by hand.`,
    "",
    `export type ${machine}State = ${union(ir.states)};`,
    `export type ${machine}Trigger = ${union(ir.triggers)};`,
  ];
  // The outcome triggers of states that invoke a train (`Fetching.done`), a kind of their own. Emitted only
  // when the machine has some, so a machine that invokes nothing generates exactly what it did before.
  const outcomes = Object.keys(ir.outcomes ?? {}).sort();
  if (outcomes.length > 0)
    lines.push(`export type ${machine}Outcome = ${union(outcomes)};`);
  lines.push("");

  // A context type per state, and the state -> context map.
  for (const state of ir.states)
    lines.push(
      ...emitType(
        `${machine}${state}Context`,
        own(ir.context, state) ?? { fields: [] },
        "context",
      ),
      "",
    );
  lines.push(`export type ${machine}Contexts = {`);
  for (const state of ir.states)
    lines.push(`  ${state}: ${machine}${state}Context;`);
  lines.push("};", "");

  // An input type per trigger that declares one, and the trigger -> input map (undefined when none). An
  // effect-bound trigger's input is the receipt the server's effect runner hands it, so a client sends it with none.
  const effectBound = new Set(ir.transitions.filter((t) => t.effect).map((t) => t.trigger));
  const inputOf = (trigger: string) => (effectBound.has(trigger) ? undefined : own(ir.inputs, trigger));
  for (const trigger of ir.triggers) {
    const schema = inputOf(trigger);
    if (schema && schema.fields.length > 0)
      lines.push(
        ...emitType(`${machine}${trigger}Input`, schema, "input"),
        "",
      );
  }
  // A success outcome's input is the invoked train's output, exported under the outcome.
  const outputOf = (outcome: string) => own(ir.outcomes, outcome)?.output;
  for (const outcome of outcomes) {
    const schema = outputOf(outcome);
    if (schema && schema.fields.length > 0)
      lines.push(
        ...emitType(outcomeTypeName(machine, outcome), schema, "input"),
        "",
      );
  }
  lines.push(`export type ${machine}Inputs = {`);
  for (const trigger of ir.triggers) {
    const schema = inputOf(trigger);
    const type =
      schema && schema.fields.length > 0
        ? `${machine}${trigger}Input`
        : "undefined";
    lines.push(`  ${trigger}: ${type};`);
  }
  for (const outcome of outcomes) {
    const schema = outputOf(outcome);
    const type =
      schema && schema.fields.length > 0
        ? outcomeTypeName(machine, outcome)
        : "undefined";
    lines.push(`  ${JSON.stringify(outcome)}: ${type};`);
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
  checkNames(ir);
  const machine = pascal(ir.id);
  const importStyle = options.importStyle ?? "relative";
  const specifier = options.specifier ?? "@traxsharp/state-machine";
  const irHash = options.irHash ?? "";
  // The differential block is test-only fuzzing data (samples/seeds/contexts); the runtime machine never
  // reads it. Strip it so the generated module stays lean and doesn't ship test inputs to the browser.
  const { differential: _differential, ...runtimeIr } = ir;
  const specifierStyle = importStyle === "specifier";
  const engineImports = specifierStyle
      ? // JSON.stringify, so a specifier holding a quote stays one string literal rather than ending it.
        [`import { typedMachineFromIr, type IrDocument } from ${JSON.stringify(specifier)};`]
      : [
          `import { typedMachineFromIr } from "../../typed";`,
          `import type { IrDocument } from "../../rules/irMachine";`,
        ];
  // JSON.stringify keeps the specifier one line, so it cannot end the comment it is written into.
  const importNote = specifierStyle
    ? [
        `// Imports the engine from ${JSON.stringify(specifier)} (trax machine generate --import-style specifier, the`,
        "// default without --engine-src). Name another module with --specifier <module>, or import an engine",
        "// checkout by relative path with --import-style relative --engine-src <engine/src>.",
      ]
    : [];
  // The IR format rides in the IR rather than in a call, so an engine older than the check ignores it.
  const twinIr = specifierStyle ? { irFormat: irFormatVersion, ...runtimeIr } : runtimeIr;
  return [
    `// AUTO-GENERATED from ${ir.id}.ir.json by generateMachineFactory. Do not edit by hand.`,
    ...importNote,
    "",
    ...engineImports,
    `import type { ${machine}Spec } from "./${ir.id}.contexts.g";`,
    "",
    `const ir = ${JSON.stringify(twinIr, null, 2)} as IrDocument;`,
    "",
    `/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */`,
    `export const irHash = ${JSON.stringify(irHash)};`,
    "",
    `/** The ${ir.id} machine, built from the IR and typed by ${machine}Spec. No hand-written twin. */`,
    `export const ${camel(ir.id)} = typedMachineFromIr<${machine}Spec>(ir, irHash);`,
    "",
  ].join("\n");
}

// `source` is where the schema's own constraints say its fields live: a context's read the context, an input's
// (or a train output's) read the input.
function emitType(
  name: string,
  schema: ContextSchema,
  source: RuleSource,
): string[] {
  if (schema.fields.length === 0)
    return [`export type ${name} = Record<string, never>;`];

  const lines = [`export type ${name} = {`];
  for (const field of sorted(schema.fields))
    lines.push(`  ${member(field, source)};`);
  lines.push("};");
  return lines;
}

const sorted = (fields: readonly FieldSchema[]): FieldSchema[] =>
  [...fields].sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));

// A nullable field is optional and may be null (matches the validator: absent-or-null is allowed).
function member(field: FieldSchema, source: RuleSource): string {
  const key = /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(field.name)
    ? field.name
    : JSON.stringify(field.name);
  const optional = field.nullable ? "?" : "";
  const nullable = field.nullable ? " | null" : "";
  return `${key}${optional}: ${tsType(field, source)}${nullable}`;
}

// A nested record's shape, inline: the fields of an object field, or of each element of an array field.
function shapeType(fields: readonly FieldSchema[], source: RuleSource): string {
  if (fields.length === 0) return "Record<string, never>";
  return `{ ${sorted(fields)
    .map((f) => `${member(f, source)};`)
    .join(" ")} }`;
}

/**
 * The TypeScript type for one context field. `field.type` alone is lossy: the IR also carries the
 * field's constraints, and two of them ARE type information — `arrayOf` gives an array its element
 * type, `oneOf` gives a string its literal union. Reading them here is what stops a consumer from
 * hand-maintaining a "precise" copy of a context the IR already described exactly.
 *
 * Only a field's own, top-level constraint, read from where the schema's record lives, narrows it. A rule nested inside
 * `all`/`any` may be conditional, and one aimed at another field or at trigger input says nothing
 * about this field, so neither is safe to read as a type.
 */
function tsType(field: FieldSchema, source: RuleSource): string {
  if (field.fields && field.type === "object") return shapeType(field.fields, source);
  if (field.fields && field.type === "array")
    return `${shapeType(field.fields, source)}[]`;
  if (field.values) return valuesType(field.values, field.type, source);
  for (const rule of field.constraints) {
    if (rule.rule === "arrayOf" && field.type === "array" && ownRule(rule, field, source))
      return `${baseType(rule.type)}[]`;
    if (rule.rule === "oneOf" && field.type === "string" && ownRule(rule, field, source))
      return union(rule.values);
  }
  return baseType(field.type);
}

// A map's or a list's type from the schema of its values: `Record<string, number>`, `string[]`.
function valuesType(
  values: ValueSchema,
  type: JsonFieldType,
  source: RuleSource,
): string {
  const value = valueType(values, source);
  if (type !== "array") return `Record<string, ${value}>`;
  return value.includes(" | ") ? `(${value})[]` : `${value}[]`;
}

function valueType(schema: ValueSchema, source: RuleSource): string {
  const base =
    schema.fields && schema.type === "object" ? shapeType(schema.fields, source)
    : schema.fields && schema.type === "array" ? `${shapeType(schema.fields, source)}[]`
    : schema.values ? valuesType(schema.values, schema.type, source)
    : baseType(schema.type);
  return schema.nullable ? `${base} | null` : base;
}

const ownRule = (
  rule: { source: RuleSource; field: string },
  field: FieldSchema,
  source: RuleSource,
): boolean => rule.source === source && rule.field === field.name;

function baseType(type: JsonFieldType): string {
  switch (type) {
    case "string":
      return "string";
    case "number":
    case "integer":
      return "number";
    case "boolean":
      return "boolean";
    case "array":
      return "unknown[]";
    case "object":
      return "Record<string, unknown>";
  }
}

// `Fetching.done` of machine `ingest` -> `IngestFetchingDoneOutput`: the type of the train output it carries.
const outcomeTypeName = (machine: string, outcome: string): string =>
  `${machine}${outcome
    .split(".")
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("")}Output`;

// Each value as a string literal: its JSON, so nothing in it can end the literal.
const union = (values: readonly string[]): string =>
  values.map((v) => JSON.stringify(v)).join(" | ");

const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;

// The names every object has (`__proto__`, `constructor`, `toString`): a state or trigger named so is found on every
// map a lookup reads it from, and a `"__proto__"` key in the twin's IR literal sets its prototype instead.
const PROTOTYPE_MEMBERS = new Set(Object.getOwnPropertyNames(Object.prototype));

// The value `map` holds under `key` itself, never one it inherits.
const own = <T>(map: Readonly<Record<string, T>> | undefined, key: string): T | undefined =>
  map !== undefined && Object.prototype.hasOwnProperty.call(map, key) ? map[key] : undefined;

/**
 * Refuses an IR whose names cannot be written as code: the id names files and every type, a state or trigger names a
 * type and a key, and an outcome is `<State>.<kind>`, each half a part of a type name. The IR is data a host
 * exported, so it is checked before any of it is written.
 */
function checkNames(ir: IrDocument): void {
  if (typeof ir.id !== "string" || !/^[a-z][a-z0-9]*([-_][a-z0-9]+)*$/.test(ir.id))
    throw new Error(
      `The machine id ${JSON.stringify(ir.id)} is not kebab-case (lowercase letters and digits in words joined by '-'), so it cannot name generated code.`,
    );
  const names: [string, unknown[]][] = [
    ["state", ir.states],
    ["trigger", ir.triggers],
  ];
  for (const [what, list] of names) {
    if (!Array.isArray(list))
      throw new Error(`The machine '${ir.id}' has no ${what}s.`);
    for (const name of list)
      if (typeof name !== "string" || !IDENTIFIER.test(name))
        throw new Error(
          `The machine '${ir.id}' has the ${what} ${JSON.stringify(name)}, which is not an identifier.`,
        );
      else if (PROTOTYPE_MEMBERS.has(name))
        throw new Error(
          `The machine '${ir.id}' has the ${what} ${JSON.stringify(name)}, a name every JavaScript object has.`,
        );
  }
  for (const outcome of Object.keys(ir.outcomes ?? {})) {
    const parts = outcome.split(".");
    if (parts.length !== 2 || !parts.every((p) => IDENTIFIER.test(p)))
      throw new Error(
        `The machine '${ir.id}' has the outcome ${JSON.stringify(outcome)}, which is not <State>.<kind>.`,
      );
  }
  // A state's context and invariant, and a trigger's input, are looked up by name: a key that is none of them is not
  // the host's.
  const declared = { state: new Set<unknown>(ir.states), trigger: new Set<unknown>(ir.triggers) };
  const maps: [string, Readonly<Record<string, unknown>> | undefined, keyof typeof declared][] = [
    ["context", ir.context, "state"],
    ["invariants", ir.invariants, "state"],
    ["inputs", ir.inputs, "trigger"],
  ];
  for (const [key, map, what] of maps)
    for (const name of Object.keys(map ?? {}))
      if (!declared[what].has(name))
        throw new Error(
          `The machine '${ir.id}' has ${key} for ${JSON.stringify(name)}, which is not one of its ${what}s.`,
        );
  // JSON.parse keeps a "__proto__" key as data, but the twin's `const ir = {...}` literal would make it a prototype.
  if (hasProtoKey(ir))
    throw new Error(
      `The machine '${ir.id}' has a key "__proto__", which the twin's object literal would read as its prototype.`,
    );
}

const hasProtoKey = (value: unknown): boolean =>
  Array.isArray(value)
    ? value.some(hasProtoKey)
    : typeof value === "object" && value !== null
      ? Object.prototype.hasOwnProperty.call(value, "__proto__") || Object.values(value).some(hasProtoKey)
      : false;

// A machine id is kebab/snake (ship-to-store); its type prefix is PascalCase (ShipToStore) and its
// exported const is camelCase (shipToStore). Splitting on - and _ is what makes a multi-word id emit valid
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
