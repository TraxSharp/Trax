// Checks src/data/roadmap.json, which drives /roadmap. Run with `npm test`.

import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const SRC_DIR = path.join(process.cwd(), "src");
const raw = fs.readFileSync(path.join(SRC_DIR, "data", "roadmap.json"), "utf-8");
const roadmap = JSON.parse(raw) as {
  milestones: { key: string; status: string; shipped?: string; title: string; points: string[] }[];
};
const keys = new Set(roadmap.milestones.map((m) => m.key));

test("every milestone has a known status and a unique key, in status order", () => {
  const order = ["done", "building", "next", "later"];
  assert.equal(keys.size, roadmap.milestones.length, "duplicate milestone key");
  const ranks = roadmap.milestones.map((m) => order.indexOf(m.status));
  assert.ok(!ranks.includes(-1), "unknown status");
  assert.deepEqual(ranks, [...ranks].sort((a, b) => a - b), "milestones out of status order");
});

test("a milestone carries a shipped date exactly when it has shipped", () => {
  // Shipped milestones are dated; what is ahead never is.
  const wrong = roadmap.milestones
    .filter((m) => (m.status === "done") !== Boolean(m.shipped))
    .map((m) => m.key);
  assert.deepEqual(wrong, []);
});

test("the roadmap names no internal work-item or milestone ids", () => {
  // The page shows outcomes only; ids belong to the private work index.
  const id = /\b(?:CORE|MED|EFF|SCH|API|DSH|CLI|SAM|DOC|WS|MS)-\d+\b/;
  assert.ok(!id.test(raw), `roadmap.json contains an id: ${raw.match(id)?.[0]}`);
});

test("every link into the roadmap names a milestone that exists", () => {
  const refs: string[] = [];
  const stops = fs.readFileSync(path.join(SRC_DIR, "components/landing/WhereTraxStops.tsx"), "utf-8");
  for (const m of stops.matchAll(/roadmap: \{ key: "([^"]+)"/g)) refs.push(m[1]);
  assert.deepEqual(refs.filter((k) => !keys.has(k)), []);
});
