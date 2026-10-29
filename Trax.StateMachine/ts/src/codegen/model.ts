// The language-neutral half of code generation: what a typed view of a machine holds, read once from the IR. Each
// language's emitter (typescript.ts, csharp.ts, and the ones after them) writes this model in its own syntax, so a
// rule about which IR field becomes which type is decided here, once, for every language.

import type {
  ContextSchema,
  FieldSchema,
  JsonFieldType,
  RuleSource,
  ValueSchema,
} from "../rules/interpreter";
import { checkIr, type IrDocument } from "../rules/irMachine";

/** A value's type, as far as the IR describes it. */
export type TypeRef =
  | { kind: "string" }
  | { kind: "number" }
  | { kind: "integer" }
  | { kind: "boolean" }
  /** A string the schema's own `oneOf` constraint limits to these values. */
  | { kind: "literals"; values: readonly string[] }
  /** A record whose fields the IR lists. */
  | { kind: "record"; fields: readonly FieldModel[] }
  /**
   * A list. `from` says which part of the IR described it: a list of records (`record`), a list typed by an `arrayOf`
   * constraint (`arrayOf`), or a list with a schema for its values (`values`). An emitter may ignore it; TypeScript
   * reads it to write exactly what it always wrote.
   */
  | { kind: "list"; of: ValueRef; from: "record" | "arrayOf" | "values" }
  /** A string-keyed map with a schema for its values. */
  | { kind: "map"; of: ValueRef }
  /** A list the IR says nothing more about. */
  | { kind: "unknownList" }
  /** An object the IR says nothing more about. */
  | { kind: "unknownObject" };

/** A value inside a list or a map, which may be null. */
export interface ValueRef {
  type: TypeRef;
  nullable: boolean;
}

/** One field of a record: absent or null when `nullable`, present otherwise. Fields are sorted by name. */
export interface FieldModel {
  name: string;
  type: TypeRef;
  nullable: boolean;
}

/** A record type the view declares, by the suffix that follows the machine's name (`CartContext`, `CoinInput`). */
export interface RecordModel {
  suffix: string;
  fields: readonly FieldModel[];
}

/** Everything a typed view of one machine declares. */
export interface MachineModel {
  /** The IR's id, kebab-case (`ship-to-store`). */
  id: string;
  /** The id in PascalCase (`ShipToStore`), every type's prefix. */
  pascal: string;
  /** The id in camelCase (`shipToStore`). */
  camel: string;
  states: readonly string[];
  triggers: readonly string[];
  /** The outcome triggers of states that invoke a train (`Fetching.done`), sorted. */
  outcomes: readonly string[];
  /** Each state's context, in the IR's state order. */
  contexts: readonly { state: string; record: RecordModel }[];
  /**
   * Each trigger's input, in the IR's trigger order: null when the trigger takes none (it declares no record, or its
   * record has no fields, or its edge runs the effect, whose input is the receipt the server's effect runner hands it).
   */
  inputs: readonly { trigger: string; record: RecordModel | null }[];
  /** Each outcome's input, the invoked train's output: null for one that carries none. */
  outcomeInputs: readonly { outcome: string; record: RecordModel | null }[];
  /** The triggers whose edge runs the machine's effect, sorted. */
  effectTriggers: readonly string[];
  /** The states with a timer, sorted. */
  timedStates: readonly string[];
}

/** Reads the model a typed view is written from. The IR's names are checked first, as code will be written from them. */
export function machineModel(ir: IrDocument): MachineModel {
  checkNames(ir);
  const effectBound = new Set(ir.transitions.filter((t) => t.effect).map((t) => t.trigger));
  const inputOf = (trigger: string) => (effectBound.has(trigger) ? undefined : own(ir.inputs, trigger));
  const outcomes = Object.keys(ir.outcomes ?? {}).sort();
  const record = (suffix: string, schema: ContextSchema | undefined, source: RuleSource): RecordModel | null =>
    schema && schema.fields.length > 0 ? { suffix, fields: fieldsOf(schema.fields, source) } : null;

  return {
    id: ir.id,
    pascal: pascal(ir.id),
    camel: camel(ir.id),
    states: ir.states,
    triggers: ir.triggers,
    outcomes,
    contexts: ir.states.map((state) => ({
      state,
      record: { suffix: `${state}Context`, fields: fieldsOf((own(ir.context, state) ?? { fields: [] }).fields, "context") },
    })),
    inputs: ir.triggers.map((trigger) => ({ trigger, record: record(`${trigger}Input`, inputOf(trigger), "input") })),
    outcomeInputs: outcomes.map((outcome) => ({
      outcome,
      record: record(outcomeSuffix(outcome), own(ir.outcomes, outcome)?.output, "input"),
    })),
    effectTriggers: [...effectBound].sort(),
    timedStates: (ir.timers ?? []).map((t) => t.state).sort(),
  };
}

// `Fetching.done` -> `FetchingDoneOutput`: the record of the train output the outcome carries.
const outcomeSuffix = (outcome: string): string =>
  `${outcome
    .split(".")
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("")}Output`;

// `source` is where the schema's own constraints say its fields live: a context's read the context, an input's (or a
// train output's) read the input.
const fieldsOf = (fields: readonly FieldSchema[], source: RuleSource): FieldModel[] =>
  [...fields]
    .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))
    .map((field) => ({ name: field.name, type: fieldType(field, source), nullable: field.nullable }));

/**
 * The type of one field. `field.type` alone is lossy: two of a field's constraints are type information too,
 * `arrayOf` (a list's element type) and `oneOf` (a string's values). Only a field's own, top-level constraint, read from
 * where its record lives, narrows it: a rule nested in `all` or `any` may be conditional, and one aimed at another field
 * or at the input says nothing about this one.
 */
function fieldType(field: FieldSchema, source: RuleSource): TypeRef {
  if (field.fields && field.type === "object") return { kind: "record", fields: fieldsOf(field.fields, source) };
  if (field.fields && field.type === "array")
    return {
      kind: "list",
      of: { type: { kind: "record", fields: fieldsOf(field.fields, source) }, nullable: false },
      from: "record",
    };
  if (field.values) return valuesType(field.values, field.type, source);
  for (const rule of field.constraints) {
    if (rule.rule === "arrayOf" && field.type === "array" && rule.source === source && rule.field === field.name)
      return { kind: "list", of: { type: baseType(rule.type), nullable: false }, from: "arrayOf" };
    if (rule.rule === "oneOf" && field.type === "string" && rule.source === source && rule.field === field.name)
      return { kind: "literals", values: rule.values };
  }
  return baseType(field.type);
}

// A map's or a list's type from the schema of its values.
function valuesType(values: ValueSchema, type: JsonFieldType, source: RuleSource): TypeRef {
  const of = valueRef(values, source);
  return type === "array" ? { kind: "list", of, from: "values" } : { kind: "map", of };
}

function valueRef(schema: ValueSchema, source: RuleSource): ValueRef {
  const type: TypeRef =
    schema.fields && schema.type === "object" ? { kind: "record", fields: fieldsOf(schema.fields, source) }
    : schema.fields && schema.type === "array"
      ? { kind: "list", of: { type: { kind: "record", fields: fieldsOf(schema.fields, source) }, nullable: false }, from: "record" }
    : schema.values ? valuesType(schema.values, schema.type, source)
    : baseType(schema.type);
  return { type, nullable: schema.nullable };
}

function baseType(type: JsonFieldType): TypeRef {
  switch (type) {
    case "string":
    case "number":
    case "integer":
    case "boolean":
      return { kind: type };
    case "array":
      return { kind: "unknownList" };
    case "object":
      return { kind: "unknownObject" };
  }
}

const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;

// The characters that reorder how a line of source reads (the bidirectional embeddings, overrides, isolates and
// marks). The IR's strings are written into generated code, where one of these would show a reviewer something other
// than what the compiler reads.
const BIDI = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/g;

/**
 * `text` with every bidirectional control written as a `\uXXXX` escape. Only for generated text whose IR strings sit
 * in string literals that read the escape as the character, which both TypeScript's and C#'s regular literals do.
 */
export const escapeBidi = (text: string): string =>
  text.replace(BIDI, (c) => `\\u${c.charCodeAt(0).toString(16).toUpperCase().padStart(4, "0")}`);

// The names every object has (`__proto__`, `constructor`, `toString`): a state or trigger named so is found on every
// map a lookup reads it from, and a `"__proto__"` key in the twin's IR literal sets its prototype instead.
const PROTOTYPE_MEMBERS = new Set(Object.getOwnPropertyNames(Object.prototype));

/** The value `map` holds under `key` itself, never one it inherits. */
export const own = <T>(map: Readonly<Record<string, T>> | undefined, key: string): T | undefined =>
  map !== undefined && Object.prototype.hasOwnProperty.call(map, key) ? map[key] : undefined;

/**
 * The names a machine's TypeScript twin module declares or imports beside the const named after the machine (its id
 * in camelCase): a machine whose id is one of them would redeclare it. `sharedOutput<n>` is a schema several of its
 * outcomes share. Trax.Cli's `trax client` refuses the same names for the modules it writes around the twin
 * (`MachineModuleNames` in ClientGenerator.cs).
 */
export const TWIN_MODULE_NAMES: readonly string[] = ["ir", "irHash", "typedMachineFromIr"];
const SHARED_OUTPUT = /^sharedOutput\d+$/;

/**
 * Refuses an IR whose names cannot be written as code: the id names files and every type, a state or trigger names a
 * type and a key, and an outcome is `<State>.<kind>`, each half a part of a type name. The IR is data a host
 * exported, so it is checked before any of it is written.
 */
export function checkNames(ir: IrDocument): void {
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
  // Each outcome names its output's type (`Fetching.done` -> `FetchingDoneOutput`), with the first letter of each half
  // raised: two outcomes that differ only there would declare one type twice.
  const outputTypes = new Map<string, string>();
  for (const outcome of Object.keys(ir.outcomes ?? {})) {
    const parts = outcome.split(".");
    if (parts.length !== 2 || !parts.every((p) => IDENTIFIER.test(p)))
      throw new Error(
        `The machine '${ir.id}' has the outcome ${JSON.stringify(outcome)}, which is not <State>.<kind>.`,
      );
    const type = outcomeSuffix(outcome);
    const other = outputTypes.get(type);
    if (other !== undefined)
      throw new Error(
        `The machine '${ir.id}' has the outcomes ${JSON.stringify(other)} and ${JSON.stringify(outcome)}, which would both name the type ${type}. Rename one of them.`,
      );
    outputTypes.set(type, outcome);
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
  // What no engine reads is refused as the engine refuses it: a field `constructor` would write a view that throws
  // when it is loaded.
  checkIr(ir);
}

/** Refuses a machine whose id the TypeScript twin cannot name its const after: {@link checkNames}, then this. */
export function checkTwinNames(ir: IrDocument): void {
  checkNames(ir);
  const name = camel(ir.id);
  if (TWIN_MODULE_NAMES.includes(name) || SHARED_OUTPUT.test(name))
    throw new Error(
      `The machine id '${ir.id}' is written as '${name}', a name its twin module already declares. Rename the machine.`,
    );
}

const hasProtoKey = (value: unknown): boolean =>
  Array.isArray(value)
    ? value.some(hasProtoKey)
    : typeof value === "object" && value !== null
      ? Object.prototype.hasOwnProperty.call(value, "__proto__") || Object.values(value).some(hasProtoKey)
      : false;

// A machine id is kebab/snake (ship-to-store); its type prefix is PascalCase (ShipToStore) and its exported const is
// camelCase (shipToStore). Splitting on - and _ is what makes a multi-word id emit valid identifiers.
export const pascal = (id: string): string =>
  id
    .split(/[-_]+/)
    .filter((s) => s.length > 0)
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("");

export const camel = (id: string): string => {
  const p = pascal(id);
  return p.length === 0 ? p : p.charAt(0).toLowerCase() + p.slice(1);
};
