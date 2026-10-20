// The TypeScript mirror of the C# declarative rule engine (Trax.Effect.StateMachine.Rules). Guards,
// reducers, and context validators arrive from the IR as DATA; these interpreters evaluate that data, so the
// generated TS machine runs the same rules the C# source authored. Semantics match RuleEvaluator /
// ReductionEvaluator / SchemaValidator exactly (proven by the shared tests and, ultimately, the differential).

import { isOneOf, matchesFormat } from "./formats";

export type RuleSource = "context" | "input";
// `integer` is a number with no fractional part, decided by its value (2020 and 2020.0 are one JavaScript number).
export type JsonFieldType =
  "string" | "number" | "integer" | "boolean" | "array" | "object";
export type CompareOp = "gt" | "gte" | "lt" | "lte" | "eq";

export type Rule =
  | { rule: "present"; source: RuleSource; field: string }
  | { rule: "absent"; source: RuleSource; field: string }
  | { rule: "ofType"; source: RuleSource; field: string; type: JsonFieldType }
  | { rule: "nonEmpty"; source: RuleSource; field: string }
  | { rule: "oneOf"; source: RuleSource; field: string; values: string[] }
  | {
      rule: "compare";
      source: RuleSource;
      field: string;
      op: CompareOp;
      value: number;
    }
  | {
      rule: "count";
      source: RuleSource;
      field: string;
      op: CompareOp;
      value: number;
    }
  | {
      rule: "length";
      source: RuleSource;
      field: string;
      op: CompareOp;
      value: number;
    }
  | { rule: "boolEquals"; source: RuleSource; field: string; value: boolean }
  | { rule: "arrayOf"; source: RuleSource; field: string; type: JsonFieldType }
  | { rule: "all"; rules: Rule[] }
  | { rule: "any"; rules: Rule[] }
  | { rule: "custom"; name: string };

export type ValueSource = { input: string } | { const: unknown };
export type SetStep = { field: string; value: ValueSource };

export type Reduction =
  | { reduce: "keep" }
  | { reduce: "clear" }
  | { reduce: "reset" }
  | { reduce: "set"; steps: SetStep[] }
  | { reduce: "custom"; name: string };

/** What a field shares with a dictionary value or a list element: its type, nullability, range and shape. */
export interface ValueSchema {
  type: JsonFieldType;
  nullable: boolean;
  /**
   * The smallest value a number accepts: its CLR type's minimum (an integral type's, a decimal's, a float's or a
   * Half's), or past 2^53 the nearest double inside it. Absent when the value is not range-checked.
   */
  minimum?: number;
  /** The largest value a number accepts, as `minimum`. */
  maximum?: number;
  /**
   * The names a `string` from a C# enum may hold, as the server's JSON writes them, compared ignoring ASCII case
   * unless the name is one of `enumExact`. Absent for any other value.
   */
  enum?: string[];
  /**
   * The names of `enum` that match only as written: a member renamed with `[JsonStringEnumMemberName]`, which
   * System.Text.Json reads case-sensitively. Absent when the enum renames none.
   */
  enumExact?: string[];
  /**
   * The shape a `string` has from its C# type: `uuid`, `date-time`, `date-time-offset`, `date`, `time`, `duration`,
   * `uri` or `char` (see `formats.ts`). Absent for any other value.
   */
  format?: string;
  /**
   * The shape of a nested record: the fields of an `object` field, or of every element of an `array` field.
   * Absent when the field has no declared shape (a scalar, a list of scalars, a map, or a context field).
   */
  fields?: FieldSchema[];
  /**
   * The schema every value of a map (an `object` field from a C# dictionary), or every element of a list whose
   * elements are not records, is checked against. Absent for anything else.
   */
  values?: ValueSchema;
  /**
   * The schema every key of a map is checked against, from its C# key type: an `integer` key is an integer's digits
   * within `minimum` and `maximum`, compared exactly, and a `string` key carries an enum's names or a format. Absent for
   * a map keyed by string, and for anything else.
   */
  keys?: ValueSchema;
}

export interface FieldSchema extends ValueSchema {
  name: string;
  constraints: Rule[];
}
export interface ContextSchema {
  fields: FieldSchema[];
}

type Ctx = Record<string, unknown>;
export type CustomGuards = Record<
  string,
  (ctx: Ctx, input: unknown) => boolean
>;
export type CustomReducers = Record<string, (ctx: Ctx, input: unknown) => Ctx>;

/**
 * Evaluate a declarative guard/validator rule. Total: a missing/wrong-typed field is `false`, never throws.
 * `options.inputUnknown` reads a rule whose input nobody here has (an effect-bound trigger's receipt): every leaf on
 * the input, and every custom rule, counts as passing, so the result is false only when the context alone rules the
 * edge out. Rules combine with `all` and `any` only, so passing a leaf can never turn a rule false.
 */
export function evaluateRule(
  rule: Rule,
  context: Ctx,
  input: unknown,
  customGuards: CustomGuards = {},
  options?: { inputUnknown?: boolean },
): boolean {
  if (options?.inputUnknown && (rule.rule === "custom" || ("source" in rule && rule.source === "input")))
    return true;
  switch (rule.rule) {
    case "present":
      return !isNullish(read(rule.source, rule.field, context, input));
    case "absent":
      return isNullish(read(rule.source, rule.field, context, input));
    case "ofType":
      return matchesType(
        read(rule.source, rule.field, context, input),
        rule.type,
      );
    case "nonEmpty":
      return isNonEmpty(read(rule.source, rule.field, context, input));
    case "oneOf": {
      const v = read(rule.source, rule.field, context, input);
      return typeof v === "string" && rule.values.includes(v);
    }
    case "compare": {
      const v = read(rule.source, rule.field, context, input);
      return typeof v === "number" && compare(v, rule.op, rule.value);
    }
    case "count": {
      const v = read(rule.source, rule.field, context, input);
      return Array.isArray(v) && compare(v.length, rule.op, rule.value);
    }
    case "length": {
      const v = read(rule.source, rule.field, context, input);
      return typeof v === "string" && compare(v.length, rule.op, rule.value);
    }
    case "boolEquals": {
      const v = read(rule.source, rule.field, context, input);
      return typeof v === "boolean" && v === rule.value;
    }
    case "arrayOf": {
      const v = read(rule.source, rule.field, context, input);
      return Array.isArray(v) && v.every((e) => matchesType(e, rule.type));
    }
    case "all":
      return rule.rules.every((r) =>
        evaluateRule(r, context, input, customGuards, options),
      );
    case "any":
      return rule.rules.some((r) =>
        evaluateRule(r, context, input, customGuards, options),
      );
    case "custom":
      // Own handlers only: a name like `toString` is not bound by Object.prototype.
      return hasOwn(customGuards, rule.name)
        ? customGuards[rule.name](context, input)
        : false;
  }
}

/** Apply a declarative reducer to produce the destination context. Total and non-mutating. */
export function applyReduction(
  reduction: Reduction,
  context: Ctx,
  input: unknown,
  initial: Ctx,
  customReducers: CustomReducers = {},
): Ctx {
  switch (reduction.reduce) {
    case "keep":
      return { ...context };
    case "clear":
      return {};
    case "reset":
      return { ...initial };
    case "set": {
      const next: Ctx = { ...context };
      for (const step of reduction.steps)
        next[step.field] = resolveSource(step.value, input);
      return next;
    }
    case "custom":
      return hasOwn(customReducers, reduction.name)
        ? customReducers[reduction.name](context, input)
        : { ...context };
  }
}

/** Validate a context against a schema. Returns null when valid, else a message. */
export function validateSchema(
  schema: ContextSchema,
  context: Ctx,
): string | null {
  return validateObject(schema.fields, context, "");
}

/**
 * Validate a trigger's input (or an invoked train's output) against the schema it declares, as the C# engine
 * does: no input (`undefined` or `null`) reads as an empty object, so it passes exactly when every field is
 * nullable; anything other than a plain object is refused. A trigger that declares no input has an empty schema,
 * which accepts only no input or `{}`. Returns null when valid, else a message. Never throws.
 */
export function validateInput(
  schema: ContextSchema,
  input: unknown,
): string | null {
  try {
    if (input === undefined || input === null)
      return validateObject(schema.fields, {}, "");
    if (!isRecord(input)) return "The input must be a JSON object.";
    return validateObject(schema.fields, input, "");
  } catch {
    return "The input could not be read.";
  }
}

function validateObject(
  fields: readonly FieldSchema[],
  obj: Ctx,
  path: string,
): string | null {
  const known = new Set<string>();
  for (const field of fields) {
    known.add(field.name);
    const name = path + field.name;
    // Own properties only: a key on the prototype (`constructor`, `toString`) is never a field that was sent.
    const value = hasOwn(obj, field.name) ? obj[field.name] : undefined;
    if (isNullish(value)) {
      if (!field.nullable) return `'${name}' is required.`;
      continue;
    }
    if (!matchesType(value, field.type))
      return `'${name}' must be a ${field.type}.`;
    if (!inRange(value, field)) return `'${name}' is out of range.`;
    const unshaped = shapeless(value, field);
    if (unshaped) return `'${name}' ${unshaped}.`;
    // A constraint names a field of the object being validated, whichever side (context or input) it says that
    // object lives on, so both read this object.
    for (const constraint of field.constraints)
      if (!evaluateRule(constraint, obj, obj))
        return `'${name}' failed a constraint.`;
    if (field.fields) {
      const nested = validateShape(field.fields, value, name);
      if (nested) return nested;
    }
    if (field.values) {
      const problem = validateValues(field.values, value, name);
      if (problem) return problem;
    }
    if (field.keys) {
      const problem = validateKeys(field.keys, value, name);
      if (problem) return problem;
    }
  }
  for (const key of Object.keys(obj))
    if (!known.has(key)) return `unexpected field '${path}${key}'.`;
  return null;
}

// Every value of a map, or every element of a list, against the schema its CLR type gives it.
function validateValues(
  values: ValueSchema,
  value: unknown,
  name: string,
): string | null {
  if (Array.isArray(value)) {
    for (let i = 0; i < value.length; i++) {
      const problem = validateValue(values, value[i], `${name}[${i}]`);
      if (problem) return problem;
    }
    return null;
  }
  if (isRecord(value))
    for (const key of Object.keys(value)) {
      const problem = validateValue(values, value[key], `${name}.${key}`);
      if (problem) return problem;
    }
  return null;
}

function validateValue(
  schema: ValueSchema,
  value: unknown,
  name: string,
): string | null {
  if (isNullish(value)) return schema.nullable ? null : `'${name}' is required.`;
  if (!matchesType(value, schema.type))
    return `'${name}' must be a ${schema.type}.`;
  if (!inRange(value, schema)) return `'${name}' is out of range.`;
  const unshaped = shapeless(value, schema);
  if (unshaped) return `'${name}' ${unshaped}.`;
  if (schema.fields) {
    const nested = validateShape(schema.fields, value, name);
    if (nested) return nested;
  }
  if (schema.values) {
    const problem = validateValues(schema.values, value, name);
    if (problem) return problem;
  }
  if (schema.keys) return validateKeys(schema.keys, value, name);
  return null;
}

// Every key of a map, against the schema its C# key type gives it.
function validateKeys(keys: ValueSchema, value: unknown, name: string): string | null {
  if (!isRecord(value)) return null;
  for (const key of Object.keys(value)) {
    const problem =
      keys.type === "integer" ? (isIntegerKey(key, keys) ? null : "must be an integer in range") : shapeless(key, keys);
    if (problem) return `'${name}' has a key '${key}' that ${problem}.`;
  }
  return null;
}

// An integer key is its digits as System.Text.Json writes them (no sign but a leading minus, no leading zero), within
// the key type's range compared exactly: a key is text, never a double.
const integerKey = /^(?:0|-?[1-9][0-9]*)$/;
function isIntegerKey(key: string, keys: ValueSchema): boolean {
  if (key.length > 40 || !integerKey.test(key)) return false;
  const value = BigInt(key);
  return (
    (keys.minimum === undefined || value >= BigInt(keys.minimum)) &&
    (keys.maximum === undefined || value <= BigInt(keys.maximum))
  );
}

// Why a string is not a value of the schema's C# type: not one of its enum's names, or not of its format. Null when
// it is, or the schema has neither.
function shapeless(value: unknown, schema: ValueSchema): string | null {
  if (typeof value !== "string") return null;
  if (schema.enum && !isOneOf(value, schema.enum, schema.enumExact)) return `must be one of ${schema.enum.join(", ")}`;
  if (schema.format && !matchesFormat(schema.format, value)) return `must be a ${schema.format}`;
  return null;
}

// A number within the schema's range, compared as the double both engines read it as. A schema with no range
// accepts any value of its type.
function inRange(value: unknown, schema: ValueSchema): boolean {
  if (schema.minimum === undefined && schema.maximum === undefined) return true;
  if (typeof value !== "number" || !Number.isFinite(value)) return false;
  return (
    (schema.minimum === undefined || value >= schema.minimum) &&
    (schema.maximum === undefined || value <= schema.maximum)
  );
}

// A nested record's fields, or every element of a list of records.
function validateShape(
  shape: readonly FieldSchema[],
  value: unknown,
  name: string,
): string | null {
  if (Array.isArray(value)) {
    for (let i = 0; i < value.length; i++) {
      const element: unknown = value[i];
      if (!isRecord(element)) return `'${name}[${i}]' must be an object.`;
      const problem = validateObject(shape, element, `${name}[${i}].`);
      if (problem) return problem;
    }
    return null;
  }
  return isRecord(value) ? validateObject(shape, value, `${name}.`) : null;
}

function read(
  source: RuleSource,
  field: string,
  context: Ctx,
  input: unknown,
): unknown {
  const obj =
    source === "context" ? context : isRecord(input) ? input : undefined;
  return obj && hasOwn(obj, field) ? obj[field] : undefined;
}

function hasOwn(obj: object, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(obj, key);
}

function resolveSource(source: ValueSource, input: unknown): unknown {
  if ("input" in source)
    return isRecord(input) && hasOwn(input, source.input)
      ? input[source.input]
      : null;
  return source.const ?? null;
}

function isRecord(x: unknown): x is Record<string, unknown> {
  return typeof x === "object" && x !== null && !Array.isArray(x);
}

function isNullish(x: unknown): boolean {
  return x === undefined || x === null;
}

// The same notion of "a value of this JSON type" as the C# engine: a number is finite (JSON.parse reads 1e400
// as Infinity, which C# refuses), an integer is a number with no fractional part, and a string is well-formed
// UTF-16 (a lone surrogate has no UTF-8 encoding, so the server could not read it back).
function matchesType(x: unknown, type: JsonFieldType): boolean {
  switch (type) {
    case "string":
      return typeof x === "string" && isWellFormed(x);
    case "number":
      return typeof x === "number" && Number.isFinite(x);
    case "integer":
      return typeof x === "number" && Number.isInteger(x);
    case "boolean":
      return typeof x === "boolean";
    case "array":
      return Array.isArray(x);
    case "object":
      return isRecord(x);
  }
}

function isWellFormed(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const unit = value.charCodeAt(i);
    if (unit >= 0xd800 && unit <= 0xdbff) {
      const next = value.charCodeAt(i + 1);
      if (!(next >= 0xdc00 && next <= 0xdfff)) return false;
      i++;
    } else if (unit >= 0xdc00 && unit <= 0xdfff) return false;
  }
  return true;
}

function isNonEmpty(x: unknown): boolean {
  if (Array.isArray(x)) return x.length > 0;
  if (typeof x === "string") return x.length > 0;
  return false;
}

function compare(actual: number, op: CompareOp, target: number): boolean {
  switch (op) {
    case "gt":
      return actual > target;
    case "gte":
      return actual >= target;
    case "lt":
      return actual < target;
    case "lte":
      return actual <= target;
    case "eq":
      return actual === target;
  }
}
