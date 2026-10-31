import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { kitCases } from "../../../conformance/kit.mjs";
import { allowedLacks, transcripts as clientTranscripts } from "../../../conformance/client/run.mjs";
import { transcripts as operationsTranscripts } from "../../../conformance/operations/run.mjs";

// Each kit's PROTOCOL.md against its runner: the ops it documents are the ops the runner sends, the step kinds its
// table documents are the ones the runner handles, and what a transcript needs is what the allowlist grants, so a
// testee written from the document answers what the runner asks.

const read = (file) => readFileSync(fileURLToPath(new URL(`../../../conformance/${file}`, import.meta.url)), "utf8");
const sorted = (values) => [...new Set(values)].sort();

// The ops the document's "## The protocol" section introduces, each as a bold code span (**`load`**).
function documentedOps(protocol) {
  const start = protocol.indexOf("## The protocol");
  const end = protocol.indexOf("\n## ", start + 1);
  return sorted([...protocol.slice(start, end).matchAll(/\*\*`([a-z]+)`\*\*/g)].map((m) => m[1]));
}

// The ops a runner's source sends itself, as `op: "<op>"` literals.
const literalOps = (source) => [...source.matchAll(/\bop: "([a-z]+)"/g)].map((m) => m[1]);

// The step kinds a transcript table's first column documents: each `{ "<kind>"` in it.
function documentedSteps(protocol) {
  const table = protocol.slice(protocol.indexOf("| Step | What the runner does |"));
  return sorted(
    table
      .split("\n")
      .slice(2)
      .filter((line) => line.startsWith("| `{"))
      .flatMap((line) => [...line.split(" | ")[0].matchAll(/`\{ "(\w+)"/g)].map((m) => m[1])),
  );
}

// The step kinds a runner's dispatch tells apart: each `if (step.<kind> !== undefined ...) {` branch.
const handledSteps = (source) =>
  sorted([...source.matchAll(/(?:if|else if) \(step\.(\w+) !== undefined[^)]*\) \{/g)].map((m) => m[1]));

// Whether every step of every transcript is one of the kinds the runner handles.
const unhandled = (list, kinds) =>
  list.flatMap((t) => t.steps.filter((s) => !kinds.some((k) => s[k] !== undefined)).map((s) => `${t.name}: ${JSON.stringify(s)}`));

describe("the engine kit's PROTOCOL.md", () => {
  it("documents the ops the runner sends", () => {
    const sent = [...kitCases().map((c) => c.request.op), ...literalOps(read("run.mjs"))];
    expect(documentedOps(read("PROTOCOL.md"))).toEqual(sorted(sent));
  });
});

describe("the client kit's PROTOCOL.md", () => {
  const source = read("client/run.mjs");
  const protocol = read("client/PROTOCOL.md");
  const list = clientTranscripts();

  it("documents the ops the runner sends", () => {
    const steps = list.flatMap((t) => t.steps).filter((s) => s.client !== undefined).map((s) => s.client);
    expect(documentedOps(protocol)).toEqual(sorted([...literalOps(source), ...steps]));
  });

  it("documents the step kinds the runner handles, and every transcript uses only those", () => {
    const handled = handledSteps(source);
    expect(documentedSteps(protocol)).toEqual(handled);
    expect(unhandled(list, handled)).toEqual([]);
  });

  it("grants only lacks some transcript needs, and every need is one the allowlist names", () => {
    const capabilities = sorted(allowedLacks().map((a) => a.capability));
    const needed = sorted(list.flatMap((t) => t.needs ?? []));
    expect(needed).toEqual(capabilities);
  });
});

describe("the operations kit's PROTOCOL.md", () => {
  const source = read("operations/run.mjs");
  const protocol = read("operations/PROTOCOL.md");
  const list = operationsTranscripts();

  it("documents the ops the runner sends", () => {
    const steps = list.flatMap((t) => t.steps).filter((s) => s.client !== undefined).map((s) => s.client);
    expect(documentedOps(protocol)).toEqual(sorted([...literalOps(source), ...steps]));
  });

  it("documents the step kinds the runner handles, and every transcript uses only those", () => {
    const handled = handledSteps(source);
    expect(documentedSteps(protocol)).toEqual(handled);
    expect(unhandled(list, handled)).toEqual([]);
  });
});
