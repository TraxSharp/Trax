import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// The conformance kit as requests: every case in machines/ and conformance/, each a request for the testee and the
// answer it must give (conformance/PROTOCOL.md). Engine-independent: nothing here imports the engine.

const root = fileURLToPath(new URL("../..", import.meta.url));
const machinesDir = join(root, "machines");
const conformanceDir = join(root, "conformance");

const readJson = (path) => JSON.parse(readFileSync(path, "utf8"));

/** RFC 8785: keys sorted by UTF-16 code unit, values as JSON.stringify writes them. */
export function canonical(value) {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;
  return `{${Object.keys(value)
    .sort()
    .map((k) => `${JSON.stringify(k)}:${canonical(value[k])}`)
    .join(",")}}`;
}

/** The canonical wire of a snapshot object: the envelope in fixed order, the context canonical. */
export function wireOf(snapshot) {
  return (
    `{"machine":${JSON.stringify(snapshot.machine)},"version":${JSON.stringify(snapshot.version)},` +
    `"state":${JSON.stringify(snapshot.state)},"context":${canonical(snapshot.context)}}`
  );
}

// The entry point a trigger goes through, by its kind in the IR.
function opFor(ir, trigger) {
  if (ir.outcomes && Object.hasOwn(ir.outcomes, trigger)) return "outcome";
  if ((ir.timers ?? []).some((t) => t.trigger === trigger)) return "timer";
  return "advance";
}

function step(name, machine, op, given, when, expect) {
  const request = { op, machine, snapshot: given, trigger: when.trigger };
  if (op !== "timer" && when.input !== undefined) request.input = when.input;
  return {
    name,
    request,
    expect:
      expect.outcome === "transitioned"
        ? { outcome: "transitioned", wire: expect.wire ?? wireOf(expect.snapshot) }
        : { outcome: "rejected", reason: expect.reason },
  };
}

function jsonFiles(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((f) => [f.replace(/\.json$/, ""), readJson(join(dir, f))]);
}

function machineCases(machine) {
  const dir = join(machinesDir, machine);
  const ir = readJson(join(dir, `${machine}.ir.json`));
  const cases = [{ name: `${machine}/load`, request: { op: "load", machine, ir }, expect: { loaded: true } }];

  const differential = join(dir, "differential.json");
  if (existsSync(differential))
    readJson(differential).cases.forEach((c, i) => {
      const op = opFor(ir, c.when.trigger);
      cases.push(
        step(`${machine}/differential/${i} ${c.given.state} + ${c.when.trigger}`, machine, op, c.given, c.when, c.expect),
      );
    });

  for (const [file, f] of jsonFiles(join(dir, "fixtures", "advance")))
    cases.push(step(`${machine}/advance/${file}`, machine, opFor(ir, f.when.trigger), f.given, f.when, f.expect));

  for (const [file, f] of jsonFiles(join(dir, "fixtures", "rehydrate")))
    cases.push({
      name: `${machine}/rehydrate/${file}`,
      request: { op: "rehydrate", machine, text: f.raw ?? JSON.stringify(f.json) },
      expect:
        f.expect.result === "ok"
          ? { result: "ok", wire: wireOf(f.expect.snapshot) }
          : { result: "error", code: f.expect.code },
    });

  const handoff = join(dir, "wire-handoff.json");
  if (existsSync(handoff))
    for (const s of readJson(handoff).samples)
      cases.push({
        name: `${machine}/wire-handoff/${s.name}`,
        request: { op: "serialize", machine, snapshot: s.build },
        expect: { wire: s.snapshot },
      });

  return cases;
}

function strictInputCases() {
  const dir = join(conformanceDir, "strict-input");
  const golden = readJson(join(dir, "cases.json"));
  const machine = golden.machine;
  const given = JSON.parse(golden.start);
  return [
    { name: "strict-input/load", request: { op: "load", machine, ir: readJson(join(dir, "strict.ir.json")) }, expect: { loaded: true } },
    ...golden.cases.map((c) => {
      const input = structuredClone(golden.validInput);
      if (c.field !== undefined) input[c.field] = c.value;
      return {
        name: `strict-input/${c.name}`,
        request: { op: "advance", machine, snapshot: given, trigger: golden.trigger, input },
        expect: c.expect,
      };
    }),
  ];
}

function canonicalWireCases() {
  const doc = readJson(join(conformanceDir, "canonical-wire.json"));
  return doc.cases.map(({ name, wire, ...context }) => ({
    name: `canonical-wire/${name}`,
    request: { op: "canonical", ...context },
    expect: { wire: `{"machine":"m","version":1,"state":"S","context":${wire}}` },
  }));
}

/** Every case of the kit, in a fixed order: each machine's, then the strict inputs, then the wire vectors. */
export function kitCases() {
  const machines = readdirSync(machinesDir, { withFileTypes: true })
    .filter((d) => d.isDirectory() && existsSync(join(machinesDir, d.name, `${d.name}.ir.json`)))
    .map((d) => d.name)
    .sort();
  return [...machines.flatMap(machineCases), ...strictInputCases(), ...canonicalWireCases()];
}
