import { describe, expect, it } from "vitest";
import {
  applyReduction,
  evaluateRule,
  validateSchema,
  type ContextSchema,
  type Reduction,
  type Rule,
} from "./interpreter";

// These mirror the C# RuleEvaluator / ReductionEvaluator / SchemaValidator tests case-for-case, so the two
// runtimes provably agree on the declarative semantics the IR carries.

const ev = (
  rule: Rule,
  context: Record<string, unknown> = {},
  input?: unknown,
) => evaluateRule(rule, context, input);

describe("evaluateRule: present / absent", () => {
  it("present is true for a non-null field, false when missing or null", () => {
    const ctx = { a: "x", n: null };
    expect(ev({ rule: "present", source: "context", field: "a" }, ctx)).toBe(
      true,
    );
    expect(ev({ rule: "present", source: "context", field: "n" }, ctx)).toBe(
      false,
    );
    expect(
      ev({ rule: "present", source: "context", field: "missing" }, ctx),
    ).toBe(false);
  });

  it("present reads the trigger input", () => {
    expect(
      ev(
        { rule: "present", source: "input", field: "coin" },
        {},
        { coin: "quarter" },
      ),
    ).toBe(true);
    expect(
      ev({ rule: "present", source: "input", field: "coin" }, {}, undefined),
    ).toBe(false);
  });

  it("absent is the negation of present", () => {
    const ctx = { a: "x", n: null };
    expect(ev({ rule: "absent", source: "context", field: "a" }, ctx)).toBe(
      false,
    );
    expect(ev({ rule: "absent", source: "context", field: "n" }, ctx)).toBe(
      true,
    );
    expect(
      ev({ rule: "absent", source: "context", field: "missing" }, ctx),
    ).toBe(true);
  });
});

describe("evaluateRule: length (string length)", () => {
  it("compares string length and is false for non-strings", () => {
    const ctx = { body: "123456", n: 6 };
    const len = (op: "gt" | "gte" | "lt", value: number) =>
      ev({ rule: "length", source: "context", field: "body", op, value }, ctx);
    expect(len("gte", 6)).toBe(true);
    expect(len("gt", 6)).toBe(false);
    expect(len("lt", 6)).toBe(false);
    expect(
      ev({ rule: "length", source: "context", field: "n", op: "gte", value: 0 }, ctx),
    ).toBe(false);
    expect(
      ev(
        { rule: "length", source: "context", field: "missing", op: "gte", value: 0 },
        ctx,
      ),
    ).toBe(false);
  });
});

describe("evaluateRule: boolEquals", () => {
  it("matches a boolean value only", () => {
    const ctx = { guided: true, off: false, s: "true" };
    const be = (field: string, value: boolean) =>
      ev({ rule: "boolEquals", source: "context", field, value }, ctx);
    expect(be("guided", true)).toBe(true);
    expect(be("guided", false)).toBe(false);
    expect(be("off", false)).toBe(true);
    expect(be("s", true)).toBe(false);
    expect(be("missing", false)).toBe(false);
  });
});

describe("evaluateRule: arrayOf", () => {
  it("requires every element to match the type", () => {
    const ctx = {
      nums: [1, 2, 3],
      strs: ["a", "b"],
      mixed: [1, "b"],
      empty: [],
      scalar: 5,
    };
    const of = (field: string, type: "number" | "string") =>
      ev({ rule: "arrayOf", source: "context", field, type }, ctx);
    expect(of("nums", "number")).toBe(true);
    expect(of("strs", "string")).toBe(true);
    expect(of("empty", "number")).toBe(true);
    expect(of("mixed", "number")).toBe(false);
    expect(of("nums", "string")).toBe(false);
    expect(of("scalar", "number")).toBe(false);
  });
});

describe("evaluateRule: ofType / nonEmpty / oneOf", () => {
  it("ofType matches the JSON kind and rejects others", () => {
    const ctx = { s: "x", n: 5, b: true, arr: [1], obj: {} };
    expect(
      ev(
        { rule: "ofType", source: "context", field: "s", type: "string" },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        { rule: "ofType", source: "context", field: "n", type: "number" },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        { rule: "ofType", source: "context", field: "b", type: "boolean" },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        { rule: "ofType", source: "context", field: "arr", type: "array" },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        { rule: "ofType", source: "context", field: "obj", type: "object" },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        { rule: "ofType", source: "context", field: "s", type: "number" },
        ctx,
      ),
    ).toBe(false);
  });

  it("nonEmpty is true for a non-empty string or array only", () => {
    const ctx = { s: "x", blank: "", arr: [1], empty: [], num: 3 };
    expect(ev({ rule: "nonEmpty", source: "context", field: "s" }, ctx)).toBe(
      true,
    );
    expect(ev({ rule: "nonEmpty", source: "context", field: "arr" }, ctx)).toBe(
      true,
    );
    expect(
      ev({ rule: "nonEmpty", source: "context", field: "blank" }, ctx),
    ).toBe(false);
    expect(
      ev({ rule: "nonEmpty", source: "context", field: "empty" }, ctx),
    ).toBe(false);
    expect(ev({ rule: "nonEmpty", source: "context", field: "num" }, ctx)).toBe(
      false,
    );
  });

  it("oneOf is true only for a string in the set", () => {
    const rule: Rule = {
      rule: "oneOf",
      source: "input",
      field: "coin",
      values: ["quarter", "dollar"],
    };
    expect(ev(rule, {}, { coin: "quarter" })).toBe(true);
    expect(ev(rule, {}, { coin: "penny" })).toBe(false);
    expect(ev(rule, {}, { coin: 25 })).toBe(false);
    expect(ev(rule, {}, undefined)).toBe(false);
  });
});

describe("evaluateRule: compare / count", () => {
  const ops: Array<["gt" | "gte" | "lt" | "lte" | "eq", number, boolean]> = [
    ["gt", 0, true],
    ["gt", 10, false],
    ["gte", 5, true],
    ["lt", 10, true],
    ["lte", 5, true],
    ["eq", 5, true],
    ["eq", 6, false],
  ];
  it.each(ops)("compare %s %d on total=5 -> %s", (op, value, expected) => {
    expect(
      ev(
        { rule: "compare", source: "context", field: "total", op, value },
        { total: 5 },
      ),
    ).toBe(expected);
  });

  it("compare is false for a missing or non-numeric field", () => {
    expect(
      ev(
        {
          rule: "compare",
          source: "context",
          field: "total",
          op: "gt",
          value: 0,
        },
        { total: "x" },
      ),
    ).toBe(false);
    expect(
      ev(
        {
          rule: "compare",
          source: "context",
          field: "missing",
          op: "gt",
          value: 0,
        },
        {},
      ),
    ).toBe(false);
  });

  it("count compares array length and is false for non-arrays", () => {
    const ctx = { items: ["a", "b"], scalar: 2 };
    expect(
      ev(
        {
          rule: "count",
          source: "context",
          field: "items",
          op: "gt",
          value: 0,
        },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        {
          rule: "count",
          source: "context",
          field: "items",
          op: "gte",
          value: 2,
        },
        ctx,
      ),
    ).toBe(true);
    expect(
      ev(
        {
          rule: "count",
          source: "context",
          field: "items",
          op: "gt",
          value: 5,
        },
        ctx,
      ),
    ).toBe(false);
    expect(
      ev(
        {
          rule: "count",
          source: "context",
          field: "scalar",
          op: "gt",
          value: 0,
        },
        ctx,
      ),
    ).toBe(false);
  });
});

describe("evaluateRule: all / any / custom", () => {
  it("all requires every subrule and is vacuously true when empty", () => {
    const ctx = { items: ["a"], total: 5 };
    const pass: Rule = {
      rule: "count",
      source: "context",
      field: "items",
      op: "gt",
      value: 0,
    };
    const fail: Rule = {
      rule: "compare",
      source: "context",
      field: "total",
      op: "gt",
      value: 100,
    };
    expect(
      ev(
        {
          rule: "all",
          rules: [pass, { rule: "present", source: "context", field: "total" }],
        },
        ctx,
      ),
    ).toBe(true);
    expect(ev({ rule: "all", rules: [pass, fail] }, ctx)).toBe(false);
    expect(ev({ rule: "all", rules: [] }, ctx)).toBe(true);
  });

  it("any requires one subrule and is false when empty", () => {
    const ctx = { total: 5 };
    const pass: Rule = {
      rule: "compare",
      source: "context",
      field: "total",
      op: "eq",
      value: 5,
    };
    const fail: Rule = {
      rule: "compare",
      source: "context",
      field: "total",
      op: "eq",
      value: 6,
    };
    expect(ev({ rule: "any", rules: [fail, pass] }, ctx)).toBe(true);
    expect(ev({ rule: "any", rules: [fail] }, ctx)).toBe(false);
    expect(ev({ rule: "any", rules: [] }, ctx)).toBe(false);
  });

  it("custom resolves through the handler map and is false when unregistered", () => {
    const handlers = { yes: () => true, no: () => false };
    expect(
      evaluateRule({ rule: "custom", name: "yes" }, {}, null, handlers),
    ).toBe(true);
    expect(
      evaluateRule({ rule: "custom", name: "no" }, {}, null, handlers),
    ).toBe(false);
    expect(
      evaluateRule(
        { rule: "custom", name: "unregistered" },
        {},
        null,
        handlers,
      ),
    ).toBe(false);
    expect(evaluateRule({ rule: "custom", name: "yes" }, {}, null)).toBe(false);
  });
});

describe("applyReduction", () => {
  const apply = (r: Reduction, ctx: Record<string, unknown>, input?: unknown) =>
    applyReduction(r, ctx, input, {});

  it("keep carries the context forward as an independent copy", () => {
    const ctx = { a: 1 };
    const result = apply({ reduce: "keep" }, ctx);
    expect(result).toEqual({ a: 1 });
    result.a = 99;
    expect(ctx.a).toBe(1);
  });

  it("clear produces an empty context and reset clones the initial", () => {
    expect(apply({ reduce: "clear" }, { a: 1 })).toEqual({});
    const initial = { items: [], total: 0 };
    expect(
      applyReduction({ reduce: "reset" }, { stale: 1 }, null, initial),
    ).toEqual(initial);
  });

  it("set clones and copies fields, missing input becomes null", () => {
    const result = apply(
      {
        reduce: "set",
        steps: [
          { field: "paidWith", value: { input: "coin" } },
          { field: "flag", value: { const: true } },
          { field: "missing", value: { input: "absent" } },
        ],
      },
      { keep: "me" },
      { coin: "quarter" },
    );
    expect(result).toEqual({
      keep: "me",
      paidWith: "quarter",
      flag: true,
      missing: null,
    });
  });

  it("custom uses the registered reducer and carries forward when unregistered", () => {
    const reducers = {
      double: (ctx: Record<string, unknown>) => ({ a: (ctx.a as number) * 2 }),
    };
    expect(
      applyReduction(
        { reduce: "custom", name: "double" },
        { a: 2 },
        null,
        {},
        reducers,
      ),
    ).toEqual({ a: 4 });
    expect(
      applyReduction(
        { reduce: "custom", name: "nope" },
        { a: 2 },
        null,
        {},
        reducers,
      ),
    ).toEqual({ a: 2 });
  });
});

describe("validateSchema", () => {
  const schema: ContextSchema = {
    fields: [
      {
        name: "name",
        type: "string",
        nullable: false,
        constraints: [{ rule: "nonEmpty", source: "context", field: "name" }],
      },
      { name: "note", type: "string", nullable: true, constraints: [] },
      { name: "count", type: "number", nullable: false, constraints: [] },
    ],
  };
  const ok = (c: Record<string, unknown>) => validateSchema(schema, c) === null;

  it("accepts a matching context and reports the failures", () => {
    expect(ok({ name: "a", count: 1 })).toBe(true);
    expect(ok({ name: "a", note: null, count: 1 })).toBe(true);
    expect(ok({ count: 1 })).toBe(false); // name required
    expect(ok({ name: "a" })).toBe(false); // count required
    expect(ok({ name: 5, count: 1 })).toBe(false); // wrong type
    expect(ok({ name: "", count: 1 })).toBe(false); // constraint
    expect(ok({ name: "a", count: 1, extra: true })).toBe(false); // unexpected field
  });

  it("an empty schema accepts only an empty context", () => {
    expect(validateSchema({ fields: [] }, {})).toBeNull();
    expect(validateSchema({ fields: [] }, { x: 1 })).not.toBeNull();
  });
});
