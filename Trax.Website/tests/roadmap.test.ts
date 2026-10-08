// Checks src/data/roadmap.json, which drives /roadmap. Run with `npm test`, which
// syncs Trax.Docs into .docs-cache first, so the /docs links are checked against
// the pages the site would publish.

import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const SRC_DIR = path.join(process.cwd(), "src");
const DOCS_DIR = path.join(process.cwd(), ".docs-cache");
const raw = fs.readFileSync(path.join(SRC_DIR, "data", "roadmap.json"), "utf-8");
const roadmap = JSON.parse(raw) as {
  today: { label: string; href: string }[];
  milestones: { key: string; status: string; title: string; outcome: string; points: string[] }[];
};
const keys = new Set(roadmap.milestones.map((m) => m.key));

test("every milestone has a known status and a unique key, in status order", () => {
  const order = ["building", "next", "later"];
  assert.equal(keys.size, roadmap.milestones.length, "duplicate milestone key");
  const ranks = roadmap.milestones.map((m) => order.indexOf(m.status));
  assert.ok(!ranks.includes(-1), "unknown status");
  assert.deepEqual(ranks, [...ranks].sort((a, b) => a - b), "milestones out of status order");
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
  const page = fs.readFileSync(path.join(SRC_DIR, "app/roadmap/page.tsx"), "utf-8");
  for (const m of page.matchAll(/by: "([^"]+)"/g)) refs.push(m[1]);
  assert.ok(refs.length > 0, "found no roadmap references to check");
  assert.deepEqual(refs.filter((k) => !keys.has(k)), []);
});

test("every 'available today' link leads to a published docs page", () => {
  const missing = roadmap.today
    .map((t) => t.href.replace(/^\/docs\/?/, ""))
    .filter(
      (slug) =>
        !fs.existsSync(path.join(DOCS_DIR, `${slug}.md`)) &&
        !fs.existsSync(path.join(DOCS_DIR, slug, "index.md"))
    );
  assert.deepEqual(missing, []);
});
