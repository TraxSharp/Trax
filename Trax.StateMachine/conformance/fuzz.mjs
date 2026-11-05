import { spawn } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { canonical } from "./kit.mjs";
import { readLines } from "./run.mjs";

// Differential fuzzing over the testee protocol (PROTOCOL.md). From a seed it generates requests against the kit's
// machines, and with --irs against small random machines too, sends each to every testee and fails on the first
// answer that differs, or on an error answer to any op but load. No engine is the reference: the engines are held to
// each other. The cases are a function of
// the seed and of the answers the engines agreed on, so a seed replays the same run for as long as they agree.

const USAGE =
  "Usage: node conformance/fuzz.mjs [--seed <n>] [--cases <n>] [--irs] [--stdio] --testee <command> [args...] [--testee <command> [args...]]...\n" +
  "With --stdio the process on this one's stdin and stdout is a testee too, as with run.mjs. Needs two testees.";

const conformanceDir = fileURLToPath(new URL(".", import.meta.url));
const readJson = (path) => JSON.parse(readFileSync(path, "utf8"));

// ---------------------------------------------------------------------------------------------------------------
// Randomness: mulberry32, seeded, so a run depends on nothing but its seed and the answers.

export function random(seed) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  const int = (n) => Math.floor(next() * n);
  return {
    next,
    int,
    chance: (p) => next() < p,
    pick: (items) => items[int(items.length)],
    // A finite double from 64 random bits.
    double() {
      const view = new DataView(new ArrayBuffer(8));
      for (;;) {
        view.setUint32(0, int(2 ** 32));
        view.setUint32(4, int(2 ** 32));
        const n = view.getFloat64(0);
        if (Number.isFinite(n)) return n;
      }
    },
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Values. A number past what JSON.stringify writes (-0, NaN, 1e400) cannot reach a testee as a value; it goes in a
// stored text, as number text.

const NUMBERS = [
  0, 1, -1, 2, 4.5, -4.5, 0.1, 0.30000000000000004, 2.5, 100, 2147483647, 2147483648, -2147483648, -2147483649,
  4294967295, 4294967296, 9007199254740991, 9007199254740992, 9007199254740994, -9007199254740992, 1e19, -1e19, 1e20,
  1e21, 1e-6, 1e-7, 5e-324, -5e-324, 2.2250738585072014e-308, 1.7976931348623157e308, -1.7976931348623157e308,
  3.4028235e38, 3.4028236e38, 7.922816251426433e28, 65504, 65505, 18446744073709549568, 9223372036854774784,
  1125899906842624.2, 1234567890123456.8, 123456789012345680000, 999999999999999900000, 0.5, 1.5, 12.5,
];

const NUMBER_TEXTS = [
  "-0", "-0.0", "0.0", "4.0", "1e2", "1E+2", "1e-2", "10e-1", "9007199254740993", "9007199254740995",
  "-9007199254740993", "18446744073709551615", "9223372036854775808", "1.00000000000000011102230246251565404236316680908203125",
  "1.00000000000000011102230246251565404236316680908203126", "1125899906842624.25", "1125899906842624.75",
  "0.1000000000000000055511151231257827021181", "123456789012345678901234567890", "3e-324", "2e-324", "1e-400",
  "0e999999", "1.7976931348623158e308", `0.${"3".repeat(40)}`,
];

const BAD_NUMBER_TEXTS = [
  "1e400", "-1e400", "1.7976931348623159e308", "1e99999999999999999999", "NaN", "Infinity", "-Infinity", "+1", "01",
  "-01", ".5", "1.", "1e", "1e+", "-", "0x10", `1${"0".repeat(400)}`,
];

const CHARS = [
  "a", "b", "Z", "0", " ", "é", "ß", "😀", " ", " ", "\0", "\u0001", "\u001f", "\t", "\n", "\r", '"', "\\",
  "/", "\u007f", "\u0085", " ", "﻿", "￿", "퟿", "", "٥", "２",
];

const KEYS = ["__proto__", "constructor", "prototype", "toString", "hasOwnProperty", "valueOf", "", "10", "2", "a", "b", "x.y"];

// Strings the strict-input cases try a format with, and every string an IR names: the values a format or an enum is
// decided on.
function stringsOf(irs) {
  const strict = readJson(join(conformanceDir, "strict-input", "cases.json"));
  const out = new Set(["", "quarter", "dollar", "penny", "ada@example.com", "Ada", "PT5M", "s3://bucket/a"]);
  for (const c of strict.cases) if (typeof c.value === "string") out.add(c.value);
  for (const v of Object.values(strict.validInput)) if (typeof v === "string") out.add(v);
  const walk = (node) => {
    if (typeof node === "string") out.add(node);
    else if (Array.isArray(node)) node.forEach(walk);
    else if (node && typeof node === "object") Object.values(node).forEach(walk);
  };
  irs.forEach(walk);
  return [...out];
}

// Number text no double holds exactly, near the edges of the range or not: what a reader rounds.
function numberText(rng) {
  const digits = (n) => Array.from({ length: n }, () => rng.int(10)).join("");
  const whole = rng.chance(0.3) ? "0" : String(1 + rng.int(9)) + digits(rng.int(20));
  const fraction = rng.chance(0.6) ? `.${digits(1 + rng.int(25))}` : "";
  const exponent = rng.chance(0.5) ? `e${rng.pick(["", "+", "-"])}${rng.chance(0.5) ? rng.int(30) : rng.int(400)}` : "";
  return `${rng.chance(0.2) ? "-" : ""}${whole}${fraction}${exponent}`;
}

/** A value of number text, written as is: canonical writes the rest of a value with it. */
export class NumberText {
  constructor(text) {
    this.text = text;
  }
}

/** JSON text of `value`, members in their order, a NumberText as its text. */
export function textOf(value) {
  if (value instanceof NumberText) return value.text;
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(textOf).join(",")}]`;
  return `{${Object.keys(value)
    .map((k) => `${JSON.stringify(k)}:${textOf(value[k])}`)
    .join(",")}}`;
}

// An own data member, a key such as __proto__ included. A key of one character JSON writes with an escape ("\t",
// "\\") gets a second: node 24's JSON.parse can read such a key as a lone backslash once it has read one, and the
// TypeScript testee reads its requests with JSON.parse, so the run would report node, not an engine. Stored text keeps
// them, in the hostile values and the mutations below, where the engine itself is the reader.
const put = (object, key, value) =>
  Object.defineProperty(object, key.length === 1 && JSON.stringify(key).length > 3 ? `${key}_` : key, {
    value,
    enumerable: true,
    writable: true,
    configurable: true,
  });

function generator(rng, strings) {
  const number = () => {
    const roll = rng.int(10);
    if (roll < 4) return rng.pick(NUMBERS);
    if (roll < 6) return rng.int(21) - 10;
    if (roll < 8) return rng.double();
    return Number((rng.next() * 10 ** rng.int(25)).toPrecision(1 + rng.int(17)));
  };
  const string = () => {
    if (rng.chance(0.5)) return rng.pick(strings);
    let s = "";
    for (let i = rng.int(6); i > 0; i--) s += rng.pick(CHARS);
    return s;
  };
  const key = () => (rng.chance(0.3) ? rng.pick(KEYS) : string());
  // Any JSON value, at most `depth` containers deep.
  const any = (depth = 3) => {
    const roll = rng.int(depth > 0 ? 8 : 5);
    if (roll === 0) return null;
    if (roll === 1) return rng.chance(0.5);
    if (roll === 2 || roll === 3) return number();
    if (roll === 4) return string();
    if (roll === 5 || roll === 6) {
      const o = {};
      for (let i = rng.int(4); i > 0; i--) put(o, key(), any(depth - 1));
      return o;
    }
    return Array.from({ length: rng.int(4) }, () => any(depth - 1));
  };
  // A value for a field's schema: mostly one that fits, sometimes anything.
  const forShape = (shape, depth = 3) => {
    if (shape.nullable && rng.chance(0.1)) return null;
    if (rng.chance(0.15) || depth <= 0) return any(Math.max(depth, 0));
    switch (shape.type) {
      case "string":
        if (shape.enum && rng.chance(0.7)) return rng.pick(shape.enum);
        return string();
      case "number":
      case "integer": {
        if (rng.chance(0.3) && shape.minimum !== undefined) return rng.pick([shape.minimum, shape.maximum ?? shape.minimum]);
        const n = number();
        return shape.type === "integer" && rng.chance(0.7) ? Math.trunc(n) : n;
      }
      case "boolean":
        return rng.chance(0.5);
      case "array":
        return Array.from({ length: rng.int(4) }, () =>
          shape.values ? forShape(shape.values, depth - 1) : shape.fields ? record(shape.fields, depth - 1) : any(depth - 1),
        );
      case "object":
        if (shape.fields) return record(shape.fields, depth - 1);
        {
          const o = {};
          for (let i = rng.int(4); i > 0; i--)
            put(o, shape.keys ? String(forShape(shape.keys, 0)) : key(), shape.values ? forShape(shape.values, depth - 1) : any(depth - 1));
          return o;
        }
      default:
        return any(depth);
    }
  };
  // An object for a list of fields: each present, mostly, with the odd extra member.
  const record = (fields, depth = 3) => {
    const o = {};
    for (const f of fields ?? []) if (!rng.chance(0.08)) put(o, f.name, forShape(f, depth));
    if (rng.chance(0.08)) put(o, key(), any(1));
    return o;
  };
  return { number, string, key, any, forShape, record };
}

// ---------------------------------------------------------------------------------------------------------------
// Random machines that ir.schema.json accepts.

const STATE_NAMES = ["A", "B", "C", "D", "Done", "é", "toString", "constructor", "__proto__", "hasOwnProperty"];
const TRIGGER_NAMES = ["Go", "Back", "Set", "Stop", "toString", "constructor", "__proto__", "valueOf"];
const FIELD_NAMES = ["n", "s", "b", "list", "map", "rec", "total", "count", "toString", "valueOf", "length"];
const FORMATS = ["uuid", "date-time", "date-time-offset", "date", "time", "duration", "uri", "char"];
const BOUNDS = [
  [-2147483648, 2147483647], [0, 255], [-9223372036854774784, 9223372036854774784], [0, 18446744073709549568],
  [-7.922816251426433e28, 7.922816251426433e28], [-3.4028235e38, 3.4028235e38], [-65504, 65504], [4.5, 9007199254740993],
  [0, 0], [-0.5, 0.5],
];

export function randomIr(rng, id, gen) {
  const distinct = (pool, min, max) => {
    const n = min + rng.int(max - min + 1);
    const out = [];
    while (out.length < n) {
      const name = rng.chance(0.85) ? pool[rng.int(Math.min(pool.length, 5))] : rng.pick(pool);
      if (!out.includes(name)) out.push(name);
    }
    return out;
  };
  const shape = (depth, asKey = false) => {
    const type = asKey ? rng.pick(["string", "integer"]) : rng.pick(["string", "number", "integer", "boolean", "array", "object"]);
    const s = { type, nullable: asKey ? false : rng.chance(0.3) };
    if (type === "number" || type === "integer") {
      if (rng.chance(0.7)) [s.minimum, s.maximum] = rng.pick(BOUNDS);
    } else if (type === "string") {
      if (rng.chance(0.25)) s.enum = Array.from({ length: 1 + rng.int(3) }, () => gen.string());
      else if (rng.chance(0.3)) s.format = rng.pick(FORMATS);
    } else if (type === "array" && depth > 0) {
      if (rng.chance(0.8)) s.values = shape(depth - 1);
    } else if (type === "object" && depth > 0) {
      if (rng.chance(0.5)) s.fields = fields(depth - 1);
      else {
        s.values = shape(depth - 1);
        if (rng.chance(0.3)) s.keys = shape(0, true);
      }
    }
    return s;
  };
  const fields = (depth = 2) =>
    distinct(FIELD_NAMES, 0, 3).map((name) => ({ name, ...shape(depth), constraints: rng.chance(0.2) ? [rule(name, "context", 0)] : [] }));

  const states = distinct(STATE_NAMES, 1, 4);
  const triggers = distinct(TRIGGER_NAMES, 1, 4);
  const context = {};
  for (const s of states) put(context, s, { fields: fields() });
  const inputs = {};
  for (const t of triggers) if (rng.chance(0.7)) put(inputs, t, { fields: fields() });

  const pathsOf = (list) => (list ?? []).map((f) => f.name);
  function rule(field, source, depth) {
    const roll = rng.int(depth > 0 ? 14 : 12);
    const base = { source, field };
    switch (roll) {
      case 0: return { rule: "present", ...base };
      case 1: return { rule: "absent", ...base };
      case 2: return { rule: "nonEmpty", ...base };
      case 3: return { rule: "ofType", ...base, type: rng.pick(["string", "number", "integer", "boolean", "array", "object"]) };
      case 4: return { rule: "arrayOf", ...base, type: rng.pick(["string", "number", "integer", "boolean", "array", "object"]) };
      case 5: return { rule: "oneOf", ...base, values: Array.from({ length: rng.int(3) }, () => gen.string()) };
      case 6:
      case 7: return { rule: "compare", ...base, op: rng.pick(["gt", "gte", "lt", "lte", "eq"]), value: gen.number() };
      case 8: return { rule: "count", ...base, op: rng.pick(["gt", "gte", "lt", "lte", "eq"]), value: rng.int(4) };
      case 9: return { rule: "length", ...base, op: rng.pick(["gt", "gte", "lt", "lte", "eq"]), value: rng.int(4) };
      case 10: return { rule: "boolEquals", ...base, value: rng.chance(0.5) };
      case 11: return { rule: "custom", name: rng.pick(["throws", "approved"]) };
      default:
        return { rule: rng.pick(["all", "any"]), rules: Array.from({ length: rng.int(3) }, () => rule(field, source, depth - 1)) };
    }
  }
  const guardOver = (trigger, from) => {
    const inputPaths = pathsOf(inputs[trigger]?.fields);
    const contextPaths = pathsOf(context[from]?.fields);
    const source = inputPaths.length > 0 && (contextPaths.length === 0 || rng.chance(0.6)) ? "input" : "context";
    const paths = source === "input" ? inputPaths : contextPaths;
    return rule(paths.length > 0 ? rng.pick(paths) : rng.pick(FIELD_NAMES), source, 1);
  };
  const reduction = (trigger, to) => {
    const roll = rng.int(10);
    if (roll === 0) return { reduce: rng.pick(["keep", "clear", "reset"]) };
    if (roll === 1) return { reduce: "custom", name: "stamp" };
    const targets = pathsOf(context[to]?.fields);
    const sources = pathsOf(inputs[trigger]?.fields);
    const steps = [];
    for (const field of targets)
      if (rng.chance(0.6))
        steps.push({
          field,
          value: sources.length > 0 && rng.chance(0.7) ? { input: rng.pick(sources) } : { const: gen.any(1) },
        });
    return { reduce: "set", steps };
  };

  const transitions = [];
  for (let i = 1 + rng.int(6); i > 0; i--) {
    const from = rng.pick(states);
    const to = rng.pick(states);
    const trigger = rng.pick(triggers);
    const t = { from, trigger, to };
    if (rng.chance(0.5)) t.guard = guardOver(trigger, from);
    if (rng.chance(0.6)) t.reduce = reduction(trigger, to);
    if (rng.chance(0.05)) t.effect = { keyPrefix: "k" };
    transitions.push(t);
  }
  const ir = {
    id,
    version: 1 + rng.int(2),
    initialState: states[0],
    initialContext: rng.chance(0.7) ? gen.record(context[states[0]].fields, 2) : {},
    states,
    triggers,
    committedStates: rng.chance(0.2) ? [rng.pick(states)] : [],
    context,
    inputs,
    transitions,
  };
  if (rng.chance(0.2)) {
    const s = rng.pick(states);
    const paths = pathsOf(context[s].fields);
    ir.invariants = { [s]: rule(paths.length > 0 ? rng.pick(paths) : "n", "context", 0) };
  }
  if (rng.chance(0.2)) {
    const state = rng.pick(states);
    ir.timers = [{ state, trigger: `${state}.after`, target: rng.pick(states), rules: [], after: "PT5M" }];
  }
  if (rng.chance(0.2)) {
    const state = rng.pick(states);
    const outcome = rng.pick(["done", "failed", "cancelled"]);
    const edge = { to: rng.pick(states) };
    if (rng.chance(0.5)) edge.reduce = { reduce: "keep" };
    ir.outcomes = { [`${state}.${outcome}`]: { state, outcome, edges: [edge], ...(outcome === "done" ? { output: { fields: fields(1) } } : {}) } };
  }
  return ir;
}

// ---------------------------------------------------------------------------------------------------------------
// Cases.

// The machines every testee loads first: each the kit holds as an IR.
export function kitMachines() {
  const machinesDir = join(conformanceDir, "machines");
  const out = readdirSync(machinesDir, { withFileTypes: true })
    .filter((d) => d.isDirectory() && existsSync(join(machinesDir, d.name, `${d.name}.ir.json`)))
    .map((d) => d.name)
    .sort()
    .map((name) => ({ name, ir: readJson(join(machinesDir, name, `${name}.ir.json`)) }));
  out.push({ name: "strict", ir: readJson(join(conformanceDir, "strict-input", "strict.ir.json")) });
  for (const name of ["wizard", "notebook"]) out.push({ name, ir: readJson(join(conformanceDir, "plan", `${name}.ir.json`)) });
  out.push({ name: "hostile", ir: readJson(join(conformanceDir, "values", "hostile.ir.json")) });
  return out;
}

function opFor(ir, trigger) {
  if (ir.outcomes && Object.hasOwn(ir.outcomes, trigger)) return "outcome";
  if ((ir.timers ?? []).some((t) => t.trigger === trigger)) return "timer";
  return "advance";
}

const triggersOf = (ir) => [...ir.triggers, ...Object.keys(ir.outcomes ?? {}), ...(ir.timers ?? []).map((t) => t.trigger)];

const inputSchema = (ir, trigger) =>
  Object.hasOwn(ir.inputs, trigger) ? ir.inputs[trigger] : ir.outcomes && Object.hasOwn(ir.outcomes, trigger) ? ir.outcomes[trigger].output : undefined;

// Textual changes to a stored snapshot: the places a reader may differ.
function mutateText(rng, text, gen) {
  const numberAt = /-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g;
  switch (rng.int(12)) {
    case 0:
    case 1: {
      const spots = [...text.matchAll(numberAt)];
      if (spots.length === 0) return text;
      const m = rng.pick(spots);
      const roll = rng.int(10);
      const replacement = roll < 4 ? rng.pick(NUMBER_TEXTS) : roll < 8 ? numberText(rng) : rng.pick(BAD_NUMBER_TEXTS);
      return text.slice(0, m.index) + replacement + text.slice(m.index + m[0].length);
    }
    case 2: {
      // A member written twice, maybe once escaped.
      const at = text.indexOf('"context":{');
      if (at < 0) return text;
      const key = rng.pick(["n", "source", "title", "items", "__proto__", "paidWith"]);
      const escaped = rng.chance(0.5) ? `\\u00${key.charCodeAt(0).toString(16)}${key.slice(1)}` : key;
      const insert = `"${escaped}":${textOf(gen.any(1))},"${key}":${textOf(gen.any(1))}`;
      const body = at + '"context":{'.length;
      return text.slice(0, body) + insert + (text[body] === "}" ? "" : ",") + text.slice(body);
    }
    case 3: {
      // A context member of escaped, hostile string.
      const at = text.indexOf('"context":{');
      if (at < 0) return text;
      const units = Array.from({ length: 1 + rng.int(3) }, () =>
        rng.pick([0xd800, 0xdbff, 0xdc00, 0xdfff, 0x0000, 0x2028, 0x0041, 0xd83d, 0xde00, 0xfeff]),
      );
      const escaped = units.map((u) => `\\u${u.toString(16).padStart(4, "0")}`).join("");
      const body = at + '"context":{'.length;
      const member = rng.chance(0.5) ? `"${escaped}":1` : `"${rng.pick(["s", "title", "source", "paidWith"])}":"${escaped}"`;
      return text.slice(0, body) + member + (text[body] === "}" ? "" : ",") + text.slice(body);
    }
    case 4:
      return rng.pick(["﻿", " ", " ", " ", "\t\r\n", ""]) + text + rng.pick(["", " ", "x", "{}", "\n", " "]);
    case 5: {
      const points = Array.from(text);
      return points.slice(0, rng.int(points.length + 1)).join("");
    }
    case 6: {
      // Nested around the depth the stored reader holds to.
      const at = text.indexOf('"context":{');
      if (at < 0) return text;
      const depth = 60 + rng.int(8);
      const deep = "[".repeat(depth) + "]".repeat(depth);
      const body = at + '"context":{'.length;
      return text.slice(0, body) + `"deep":${deep}` + (text[body] === "}" ? "" : ",") + text.slice(body);
    }
    case 7:
      return text.replace(/"version":-?\d+/, `"version":${rng.pick(["1.0", "1e0", "-0", "2147483648", "1e400", '"1"', "null", "0", "2", "10e-1", "1.5"])}`);
    case 8:
      return text.replace(/"state":"[^"]*"/, `"state":${JSON.stringify(gen.string())}`);
    case 9:
      return text.replace(/}$/, `,${JSON.stringify(gen.key())}:${textOf(gen.any(1))}}`);
    case 10: {
      const points = Array.from(text);
      const i = rng.int(points.length);
      return [...points.slice(0, i), rng.pick(["\\", '"', ",", ":", "}", "]", "/*", "\0", "\u0001"]), ...points.slice(i)].join("");
    }
    default:
      return text.replace(/"context":\{.*\}\}$/s, `"context":${textOf(gen.any(2))}}`);
  }
}

/** The case generator: `next()` gives a request, `agreed(request, answer)` feeds back an answer every engine gave. */
export function cases({ seed, irs }) {
  const rng = random(seed);
  const machines = kitMachines();
  const gen = generator(rng, stringsOf(machines.map((m) => m.ir)));
  const byName = new Map(machines.map((m) => [m.name, m]));
  // Snapshots each machine reached, the start among them, to step from.
  const reached = new Map();
  const remember = (name, snapshot) => {
    const pool = reached.get(name) ?? [];
    if (pool.length >= 40) pool.splice(rng.int(pool.length), 1);
    pool.push(snapshot);
    reached.set(name, pool);
  };
  const start = ({ name, ir }) => remember(name, { machine: ir.id, version: ir.version, state: ir.initialState, context: structuredClone(ir.initialContext) });
  machines.forEach(start);
  let generated = 0;

  const snapshotFor = ({ name, ir }) => {
    if (rng.chance(0.7)) return structuredClone(rng.pick(reached.get(name)));
    const state = rng.chance(0.9) ? rng.pick(ir.states) : gen.string();
    const snapshot = {
      machine: rng.chance(0.95) ? ir.id : gen.string(),
      version: rng.chance(0.95) ? ir.version : rng.pick([0, 2, -1, 2147483647]),
      state,
      context: Object.hasOwn(ir.context, state) && rng.chance(0.8) ? gen.record(ir.context[state].fields) : gen.record([]),
    };
    return snapshot;
  };

  const step = (machine) => {
    const { name, ir } = machine;
    const snapshot = snapshotFor(machine);
    // Mostly a trigger something fires on from the snapshot's state, so a sequence goes somewhere.
    const fires = [
      ...ir.transitions.filter((t) => t.from === snapshot.state).map((t) => t.trigger),
      ...Object.entries(ir.outcomes ?? {}).filter(([, o]) => o.state === snapshot.state).map(([t]) => t),
      ...(ir.timers ?? []).filter((t) => t.state === snapshot.state).map((t) => t.trigger),
    ];
    const trigger =
      fires.length > 0 && rng.chance(0.75) ? rng.pick(fires) : rng.chance(0.95) ? rng.pick(triggersOf(ir)) : gen.string();
    const op = rng.chance(0.9) ? opFor(ir, trigger) : rng.pick(["advance", "outcome", "timer"]);
    const request = { op, machine: name, snapshot, trigger };
    if (op !== "timer") {
      const schema = inputSchema(ir, trigger);
      const roll = rng.int(10);
      if (roll < 7 && schema) request.input = gen.record(schema.fields);
      else if (roll < 8) request.input = null;
      else if (roll < 9) request.input = gen.any(2);
    }
    return request;
  };

  const plan = (machine) => {
    const { name, ir } = machine;
    // Mostly triggers a transition fires on, which the planner takes; now and then one it refuses.
    const fired = [...new Set(ir.transitions.filter((t) => !t.effect).map((t) => t.trigger))];
    const any = triggersOf(ir);
    const triggers = fired.length > 0 ? Array.from({ length: 8 }, (_, i) => (i < 7 ? rng.pick(fired) : rng.pick(any))) : any;
    const request = {
      op: "plan",
      machine: name,
      snapshot: snapshotFor(machine),
      target: rng.chance(0.9) ? rng.pick(ir.states) : gen.string(),
      fields: null,
      navigation: Array.from({ length: rng.int(5) }, () => (rng.chance(0.97) ? rng.pick(triggers) : gen.string())),
    };
    if (rng.chance(0.7)) {
      const trigger = rng.pick(triggers);
      const schema = inputSchema(ir, trigger);
      request.fields = { trigger, ...(schema || rng.chance(0.3) ? { input: schema ? gen.record(schema.fields) : gen.any(1) } : {}) };
    }
    if (rng.chance(0.3)) request.maxDepth = rng.int(7);
    return request;
  };

  const kitNames = machines.map((m) => m.name);
  const fuzzed = [];
  const pickMachine = () => (fuzzed.length > 0 && rng.chance(0.4) ? rng.pick(fuzzed) : byName.get(rng.pick(kitNames)));

  return {
    machines,
    next() {
      generated += 1;
      const roll = rng.int(100);
      if (irs && (roll < 8 || (fuzzed.length === 0 && generated === 1))) {
        const id = `fuzz${generated}`;
        const ir = randomIr(rng, id, gen);
        return { op: "load", machine: id, ir };
      }
      if (roll < 55) return step(pickMachine());
      if (roll < 70) {
        const machine = pickMachine();
        const base = structuredClone(rng.pick(reached.get(machine.name)));
        let text = textOf(base);
        for (let i = rng.int(3); i > 0; i--) text = mutateText(rng, text, gen);
        return { op: "rehydrate", machine: machine.name, text, ...(rng.chance(0.2) ? { fromServer: true } : {}) };
      }
      if (roll < 78) {
        const machine = pickMachine();
        return { op: "serialize", machine: machine.name, snapshot: { ...snapshotFor(machine), context: gen.record([]) } };
      }
      if (roll < 90) return plan(pickMachine());
      // canonical: valid JSON text with number text in places, or a string as UTF-16 code units.
      if (rng.chance(0.25)) {
        const units = Array.from({ length: rng.int(5) }, () => rng.pick([0xd800, 0xdbff, 0xdc00, 0xdfff, 0x61, 0x2028, 0xd83d, 0xde00, 0, 0xffff]));
        return rng.chance(0.5) ? { op: "canonical", stringUnits: units } : { op: "canonical", keyUnits: units };
      }
      const withText = (node, depth) => {
        // Only number text a double holds: past it, what canonical answers is the testee's reader's, not the engine's.
        if (typeof node === "number" && rng.chance(0.4)) {
          const text = rng.chance(0.5) ? rng.pick(NUMBER_TEXTS) : numberText(rng);
          return Number.isFinite(Number(text)) ? new NumberText(text) : node;
        }
        if (Array.isArray(node)) return node.map((v) => withText(v, depth + 1));
        if (node && typeof node === "object") {
          const o = {};
          for (const k of Object.keys(node)) put(o, k, withText(node[k], depth + 1));
          return o;
        }
        return node;
      };
      const context = {};
      for (let i = 1 + rng.int(3); i > 0; i--) put(context, gen.key(), withText(gen.any(2), 1));
      return { op: "canonical", context: textOf(context) };
    },
    agreed(request, answer) {
      if (request.op === "load" && answer.loaded === true) {
        const machine = { name: request.machine, ir: request.ir };
        fuzzed.push(machine);
        byName.set(machine.name, machine);
        start(machine);
      }
      const wire = answer.wire ?? null;
      if (wire !== null && request.machine !== undefined && (answer.outcome === "transitioned" || answer.result === "ok"))
        remember(request.machine, JSON.parse(wire));
    },
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Comparing and shrinking.

/** An answer as compared: without its id, and an `error` as the fact of one, since its message is the engine's own. */
export function normal(answer) {
  const { id: _, ...rest } = answer;
  if (typeof rest.error === "string") rest.error = true;
  return canonical(rest);
}

// Smaller versions of a value, simplest first.
function* smaller(value) {
  if (value instanceof NumberText) {
    if (value.text !== "0") yield new NumberText("0");
    return;
  }
  if (Array.isArray(value)) {
    for (let i = 0; i < value.length; i++) yield [...value.slice(0, i), ...value.slice(i + 1)];
    for (let i = 0; i < value.length; i++) for (const v of smaller(value[i])) yield value.map((x, j) => (j === i ? v : x));
    return;
  }
  if (value !== null && typeof value === "object") {
    const keys = Object.keys(value);
    for (const k of keys) {
      const o = {};
      for (const other of keys) if (other !== k) put(o, other, value[other]);
      yield o;
    }
    for (const k of keys)
      for (const v of smaller(value[k])) {
        const o = {};
        for (const other of keys) put(o, other, other === k ? v : value[other]);
        yield o;
      }
    return;
  }
  if (typeof value === "string" && value.length > 0) {
    yield "";
    for (let i = 0; i < value.length; i++) yield value.slice(0, i) + value.slice(i + 1);
  }
  if (typeof value === "number" && value !== 0) yield 0;
}

// Smaller texts: chunks cut out, halves first, never through a surrogate pair, which no request line could carry.
function* shorterTexts(text) {
  const points = Array.from(text);
  for (let size = Math.max(1, points.length >> 1); size >= 1; size >>= 1)
    for (let at = 0; at + size <= points.length; at += size) yield [...points.slice(0, at), ...points.slice(at + size)].join("");
}

// Each smaller request: only what the testee reads as data shrinks, never the members the protocol requires.
function* smallerRequests(request) {
  const each = function* (key, values) {
    for (const v of values) yield { ...request, [key]: v };
  };
  if (typeof request.text === "string") yield* each("text", shorterTexts(request.text));
  if (typeof request.context === "string") yield* each("context", shorterTexts(request.context));
  if (request.input !== undefined && request.input !== null) yield* each("input", smaller(request.input));
  if (request.snapshot) for (const c of smaller(request.snapshot.context)) yield { ...request, snapshot: { ...request.snapshot, context: c } };
  if (Array.isArray(request.navigation)) yield* each("navigation", smaller(request.navigation));
  if (request.fields?.input !== undefined) for (const i of smaller(request.fields.input)) yield { ...request, fields: { ...request.fields, input: i } };
  if (request.stringUnits) yield* each("stringUnits", smaller(request.stringUnits));
  if (request.keyUnits) yield* each("keyUnits", smaller(request.keyUnits));
  if (request.contextTree !== undefined)
    for (const tree of smaller(request.contextTree)) {
      if (tree === null || typeof tree !== "object" || Array.isArray(tree)) continue;
      yield { ...request, contextTree: tree };
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Running.

function lineExchange(input, output) {
  const lines = readLines(input);
  return async (request) => {
    output.write(`${JSON.stringify(request)}\n`);
    const { value, done } = await lines.next();
    if (done) throw new Error(`closed before answering request ${request.id} (${request.op})`);
    return JSON.parse(value);
  };
}

/**
 * Runs `count` cases from `seed` against `testees` (each `{ name, exchange }`). Returns null when every testee gave
 * every answer the others gave and no error but to a load, or else the first difference or error, shrunk:
 * `{ index, original, request, ir, answers }`.
 */
export async function fuzz({ seed, count, irs, testees, log = () => {} }) {
  let id = 0;
  const ask = async (request) => {
    id += 1;
    const answers = [];
    for (const t of testees) {
      try {
        answers.push(await t.exchange({ id, ...request }));
      } catch (err) {
        answers.push({ id, error: `the testee failed: ${err.message}` });
      }
    }
    return answers;
  };
  // Every op but load is total (PROTOCOL.md): it answers a refusal as a value, never an error, and the generator builds
  // no case that asks for one. An error there is a failure even when every testee answers one, since two engines that
  // both throw agree on nothing. Only a load may be refused with an error, and there the testees must agree.
  const differ = (request, answers) =>
    new Set(answers.map(normal)).size > 1 || (request.op !== "load" && answers.some((a) => typeof a.error === "string"));
  // A canonical case shrinks as a tree, so its number text survives; it is sent as text.
  const wireRequest = (request) => {
    if (request.contextTree === undefined) return request;
    const { contextTree, ...rest } = request;
    return { ...rest, context: textOf(contextTree) };
  };

  const generator = cases({ seed, irs });
  for (const { name, ir } of generator.machines) {
    const answers = await ask({ op: "load", machine: name, ir });
    const refused = testees.filter((_, i) => answers[i].loaded !== true).map((t) => t.name);
    if (refused.length > 0) throw new Error(`${refused.join(", ")} did not load the kit machine ${name}: ${canonical(answers)}`);
  }
  const loads = new Map();
  for (let index = 0; index < count; index++) {
    const request = generator.next();
    if (request.op === "load") loads.set(request.machine, request.ir);
    const answers = await ask(request);
    if (!differ(request, answers)) {
      generator.agreed(request, answers[0]);
      continue;
    }
    log(`difference at case ${index}; shrinking`);
    let smallest = request.op === "canonical" && request.context !== undefined ? { op: "canonical", contextTree: JSON.parse(request.context) } : request;
    // JSON.parse loses number text; keep the text when the tree no longer differs.
    if (smallest.contextTree !== undefined && !differ(request, await ask(wireRequest(smallest)))) smallest = request;
    let smallestAnswers = answers;
    let budget = 3000;
    for (let shrunk = true; shrunk && budget > 0; ) {
      shrunk = false;
      for (const candidate of smallerRequests(smallest)) {
        if (--budget <= 0) break;
        const got = await ask(wireRequest(candidate));
        if (differ(candidate, got)) {
          smallest = candidate;
          smallestAnswers = got;
          shrunk = true;
          break;
        }
      }
    }
    const machine = smallest.machine;
    return {
      index,
      original: request,
      request: wireRequest(smallest),
      ir: machine !== undefined && loads.has(machine) ? loads.get(machine) : undefined,
      answers: testees.map((t, i) => [t.name, smallestAnswers[i]]),
    };
  }
  return null;
}

/** The difference as text: the seed, the request shrunk, and what each testee answered. */
export function describe(seed, difference) {
  const alike = new Set(difference.answers.map(([, answer]) => normal(answer))).size === 1;
  const lines = [`${alike ? "ERROR" : "DIFFERENCE"} at case ${difference.index} of seed ${seed}.`];
  if (difference.ir) lines.push(`machine ${difference.request.machine}, loaded from: ${JSON.stringify(difference.ir)}`);
  const { id: _, ...request } = difference.request;
  lines.push(`request: ${JSON.stringify(request)}`);
  if (canonical(difference.original) !== canonical(difference.request))
    lines.push(`first seen as: ${JSON.stringify(difference.original)}`);
  for (const [name, answer] of difference.answers) {
    const { id: __, ...rest } = answer;
    lines.push(`  ${name}: ${JSON.stringify(rest)}`);
  }
  return lines.join("\n");
}

async function main(argv) {
  let seed = 1;
  let count = 500;
  let irs = false;
  let stdio = false;
  const commands = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--seed") seed = Number(argv[++i]);
    else if (argv[i] === "--cases") count = Number(argv[++i]);
    else if (argv[i] === "--irs") irs = true;
    else if (argv[i] === "--stdio") stdio = true;
    else if (argv[i] === "--testee") {
      const command = [];
      while (i + 1 < argv.length && argv[i + 1] !== "--testee") command.push(argv[++i]);
      commands.push(command);
    } else throw new Error(`${USAGE}\nUnknown argument ${argv[i]}.`);
  }
  if (!Number.isInteger(seed) || !Number.isInteger(count) || count < 0) throw new Error(`${USAGE}\n--seed and --cases are whole numbers.`);
  if (commands.some((c) => c.length === 0) || commands.length + (stdio ? 1 : 0) < 2) throw new Error(USAGE);

  const children = [];
  const testees = commands.map((command) => {
    const child = spawn(command[0], command.slice(1), { stdio: ["pipe", "pipe", "inherit"] });
    children.push(child);
    return { name: command.join(" "), exchange: lineExchange(child.stdout, child.stdin) };
  });
  if (stdio) testees.unshift({ name: "stdio", exchange: lineExchange(process.stdin, process.stdout) });

  const started = Date.now();
  const difference = await fuzz({ seed, count, irs, testees, log: (line) => console.error(line) });
  for (const t of testees) await t.exchange({ id: 0, op: "end" }).catch(() => {});
  for (const child of children) child.stdin.end();
  if (difference) {
    console.error(describe(seed, difference));
    return 1;
  }
  console.error(`${count} cases from seed ${seed}${irs ? " with random machines" : ""}: every testee answered alike (${Date.now() - started} ms).`);
  return 0;
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (err) => {
      console.error(err.message);
      process.exit(2);
    },
  );
