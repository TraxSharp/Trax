// Checks the "What you write, what Trax writes" tab's data (src/data/deploy-generated.json, from Trax.Samples'
// scripts/recordings/deploy-generated.sh running the real CLI) and the "Who can talk to whom" map's edges against the
// recorded checks in deploy-recordings.json. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import generated from "../src/data/deploy-generated.json" with { type: "json" };
import recorded from "../src/data/deploy-recordings.json" with { type: "json" };
import { kindsInOrder, needsOf, refusalOf, type DeployGenerated } from "../src/lib/deploy-generated.ts";
import { reachEdges, reachScenario } from "../src/lib/reach-map.ts";
import type { DeployRecordings } from "../src/lib/deploy-replay.ts";

const g = generated as unknown as DeployGenerated;
const rec = recorded as unknown as DeployRecordings;

test("the file shown is the sample's own, with its real line count", () => {
  const file = readFileSync(new URL(`../../${g.file.path}`, import.meta.url), "utf8");
  assert.equal(g.file.code, file, "the sample's trax.deploy.json changed: run deploy-generated.sh again");
  assert.equal(g.file.lines, file.replace(/\n$/, "").split("\n").length);
  assert.deepEqual(g.hosts.map((h) => h.service), Object.keys(JSON.parse(file).services));
});

test("what each host needs comes from its own report", () => {
  const hub = g.hosts.find((h) => h.service === "hub");
  const worker = g.hosts.find((h) => h.service === "worker");
  assert.ok(hub && worker);
  assert.deepEqual(hub.export.roles, ["api", "scheduler"]);
  assert.deepEqual(worker.export.roles, ["worker"]);
  for (const h of [hub, worker]) {
    assert.equal(h.export.database?.provider, "postgres");
    assert.ok(h.export.transports.includes("rabbitmq"));
  }
  assert.equal(hub.export.endpoints.health, "/trax/health");
  assert.ok(needsOf(hub).includes("health at /trax/health") && needsOf(worker).includes("RabbitMQ"));
});

test("the counts are the files' own: objects, kinds, lines and hardening add up", () => {
  const k = g.targets.kubernetes;
  assert.equal(k.exit, 0);
  assert.equal(k.objects, k.files.filter((f) => f.kind).length);
  assert.equal(k.lines, k.files.reduce((n, f) => n + f.lines, 0));
  assert.equal(Object.values(k.kinds).reduce((a, b) => a + b, 0), k.objects);
  assert.equal(kindsInOrder(g).reduce((a, b) => a + b.count, 0), k.objects);
  for (const kind of ["Deployment", "Job", "Service", "NetworkPolicy", "StatefulSet"]) assert.ok(k.kinds[kind] > 0, kind);
  assert.equal(k.kinds.Job, 3, "the migrate, drain and resume Jobs");
  // What the page says of every container holds: each drops every capability and cannot gain privileges, and the
  // only writable roots are Postgres's and RabbitMQ's.
  assert.equal(k.hardening.dropAll, k.hardening.containers);
  assert.equal(k.hardening.noPrivilegeEscalation, k.hardening.containers);
  assert.equal(k.hardening.readOnlyRoot, k.hardening.containers - k.writableRoot.length);
  assert.deepEqual(k.writableRoot.map((w) => w.service).sort(), ["database", "rabbitmq"]);
  assert.ok(k.hardening.secretReferences > 0);
  // The log names every object written.
  assert.ok(k.files.every((f) => !f.kind || k.log.some((l) => l.trim().startsWith(f.kind ?? ""))));

  const p = g.targets.podman;
  assert.equal(p.exit, 0);
  assert.equal(p.lines, p.files.reduce((n, f) => n + f.lines, 0));
  assert.equal(p.units, p.files.filter((f) => f.name.endsWith(".kube")).length);
  assert.ok(g.file.lines < k.lines && g.file.lines < p.lines);
});

test("what trax refused, it refused before writing anything, and the page quotes it", () => {
  assert.notEqual(g.targets.compose.exit, 0);
  assert.notEqual(g.targets.aws.exit, 0);
  assert.notEqual(g.targets.aws.bare.exit, 0);
  assert.match(refusalOf(g.targets.aws.bare.log), /connects to RabbitMQ, which an AWS stack does not provide yet/);
  assert.match(refusalOf(g.targets.aws.log), /names egress/);
  assert.match(refusalOf(g.targets.compose.log), /count 2/);
  const page = readFileSync(new URL("../src/components/landing/WhatYouWrite.tsx", import.meta.url), "utf8");
  assert.match(page, /refusalOf\(aws\.bare\.log\)/);
  // No count is typed into the page: every number it shows is read from the data.
  const prose = [...page.matchAll(/>([^<>{}\n]+)</g)]
    .map((m) => m[1])
    .filter((t) => !/&&|=>|===|\?\?/.test(t))
    .join(" ");
  assert.ok(!/\d/.test(prose.replace(/--trax-export-deploy/g, "")), `a number is typed into the page: ${prose.match(/[^.]*\d[^.]*/)?.[0]}`);
});

test("the map's edges are the recorded checks: green where they opened, red where they were refused", () => {
  const s = reachScenario(rec);
  assert.ok(s);
  const edges = reachEdges(s);
  const edge = (key: string) => {
    const e = edges.find((x) => x.key === key);
    assert.ok(e, key);
    return e;
  };
  for (const e of edges) assert.notEqual(e.result, "mixed", `${e.key}: ${e.sources.join("; ")}`);
  for (const e of edges) assert.ok(e.sources.length > 0, e.key);
  assert.equal(edge("app-database").result, "open");
  assert.equal(edge("app-rabbitmq").result, "open");
  assert.equal(edge("other-database").result, "refused");
  assert.equal(edge("other-rabbitmq").result, "refused");
  assert.equal(edge("app-internet").result, "refused");
  assert.equal(edge("app-metadata").result, "refused");
  // Each edge's colour is what every check behind it recorded.
  for (const e of edges) {
    const opened = e.sources.map((x) => /: open$/.test(x));
    assert.ok(opened.every((o) => o === (e.result === "open")), e.key);
  }
});

test("the new tabs' copy keeps to the home page's rules", () => {
  for (const file of ["src/components/landing/WhatYouWrite.tsx", "src/components/landing/ReachMap.tsx", "src/components/landing/ShipItTabs.tsx", "src/lib/reach-map.ts", "src/lib/deploy-generated.ts"]) {
    const text = readFileSync(new URL(`../${file}`, import.meta.url), "utf8");
    const prose = [...text.matchAll(/"([^"\n]{12,})"|>([^<>{}\n]{12,})</g)].map((m) => m[1] ?? m[2]).join("\n");
    assert.ok(!prose.includes("—"), `${file} has an em dash`);
    assert.ok(!/\b(trains?|junctions?|TypeScript)\b/i.test(prose), `${file}: ${prose.match(/.*\b(trains?|junctions?|TypeScript)\b.*/i)?.[0]}`);
  }
});
