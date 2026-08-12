// The TypeScript mirror of the C# declarative rule engine (Trax.Effect.StateMachine.Rules). Guards,
// reducers, and context validators arrive from the IR as DATA; these interpreters evaluate that data, so the
// generated TS machine runs the same rules the C# source authored. Semantics match RuleEvaluator /
// ReductionEvaluator / SchemaValidator exactly (proven by the shared tests and, ultimately, the differential).

export type RuleSource = "context" | "input";
export type JsonFieldType =
  "string" | "number" | "boolean" | "array" | "object";
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

export interface FieldSchema {
  name: string;
  type: JsonFieldType;
  nullable: boolean;
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

/** Evaluate a declarative guard/validator rule. Total: a missing/wrong-typed field is `false`, never throws. */
export function evaluateRule(
  rule: Rule,
  context: Ctx,
  input: unknown,
  customGuards: CustomGuards = {},
): boolean {
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
    case "all":
      return rule.rules.every((r) =>
        evaluateRule(r, context, input, customGuards),
      );
    case "any":
      return rule.rules.some((r) =>
        evaluateRule(r, context, input, customGuards),
      );
    case "custom":
      return customGuards[rule.name]?.(context, input) ?? false;
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
      return customReducers[reduction.name]?.(context, input) ?? { ...context };
  }
}

/** Validate a context against a schema. Returns null when valid, else a message. */
export function validateSchema(
  schema: ContextSchema,
  context: Ctx,
): string | null {
  const known = new Set<string>();
  for (const field of schema.fields) {
    known.add(field.name);
    const value = field.name in context ? context[field.name] : undefined;
    if (isNullish(value)) {
      if (!field.nullable) return `'${field.name}' is required.`;
      continue;
    }
    if (!matchesType(value, field.type))
      return `'${field.name}' must be a ${field.type}.`;
    for (const constraint of field.constraints)
      if (!evaluateRule(constraint, context, null))
        return `'${field.name}' failed a constraint.`;
  }
  for (const key of Object.keys(context))
    if (!known.has(key)) return `unexpected field '${key}'.`;
  return null;
}

function read(
  source: RuleSource,
  field: string,
  context: Ctx,
  input: unknown,
): unknown {
  const obj =
    source === "context" ? context : isRecord(input) ? input : undefined;
  return obj && field in obj ? obj[field] : undefined;
}

function resolveSource(source: ValueSource, input: unknown): unknown {
  if ("input" in source)
    return isRecord(input) && source.input in input
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

function matchesType(x: unknown, type: JsonFieldType): boolean {
  switch (type) {
    case "string":
      return typeof x === "string";
    case "number":
      return typeof x === "number";
    case "boolean":
      return typeof x === "boolean";
    case "array":
      return Array.isArray(x);
    case "object":
      return isRecord(x);
  }
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
