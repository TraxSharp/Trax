// Checks the recorded deploys the home page replays (src/data/deploy-recordings.json, made by Trax.Samples'
// scripts/recordings/deploy.sh) against every claim the deploy section's copy makes, through the same player
// (src/lib/deploy-replay.ts). Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import data from "../src/data/deploy-recordings.json" with { type: "json" };
import {
  SCENARIO_KEYS,
  healthAt,
  lanesOf,
  mostRunningAt,
  runAt,
  stateAt,
  summaryOf,
  type DeployRecordings,
  type DeployScenario,
  type ScenarioKey,
  type TargetKey,
} from "../src/lib/deploy-replay.ts";

const recordings = data as unknown as DeployRecordings;
// Every shape trax deploy can deploy, which the page offers a replay of. Ephemeral dispatch (Lambda) is not one.
const SHAPES = ["all-in-one", "api-scheduler", "hub-workers", "scheduler"];
const TARGETS: TargetKey[] = ["kubernetes", "podman"];

const scenario = (shape: string, target: TargetKey, key: ScenarioKey): DeployScenario => {
  const s = recordings.shapes[shape]?.targets[target]?.scenarios[key];
  assert.ok(s, `no ${key} recorded for ${shape} on ${target}`);
  return s;
};
const each = (fn: (shape: string, target: TargetKey, s: DeployScenario, key: ScenarioKey) => void) => {
  for (const shape of SHAPES) for (const target of TARGETS) for (const key of SCENARIO_KEYS) fn(shape, target, scenario(shape, target, key), key);
};
const probed = (s: DeployScenario) => s.processes.filter((p) => p.service === s.probe);
const appPods = (s: DeployScenario) => s.processes.filter((p) => p.kind === "app");

test("every deployable shape is recorded on both targets, every scenario, and nothing else is", () => {
  assert.deepEqual(Object.keys(recordings.shapes).sort(), [...SHAPES].sort());
  for (const shape of SHAPES) assert.deepEqual(Object.keys(recordings.shapes[shape].targets).sort(), [...TARGETS].sort());
  each(() => undefined);
});

test("every recording is in time order and ends after the last thing in it", () => {
  each((shape, target, s, key) => {
    const where = `${shape}/${target}/${key}`;
    for (const list of [s.cli.map((l) => l.t), s.probes.map(([t]) => t), s.checks.map((c) => c.t), s.run?.states.map((r) => r.t) ?? []]) {
      assert.deepEqual(list, [...list].sort((a, b) => a - b), `${where} is out of order`);
      assert.ok(list.every((t) => t >= 0 && t <= s.duration), `${where} has a time outside the recording`);
    }
    for (const p of s.processes) assert.deepEqual(p.events.map((e) => e.t), p.events.map((e) => e.t).sort((a, b) => a - b), `${where} ${p.name}`);
    assert.ok(s.cli.some((l) => l.from === "trax"), `${where}: trax printed nothing`);
    assert.ok(s.file.length > 0 && s.commit.length === 40, where);
    // The page shows the file each run deployed; one that differs from the shape's own says how.
    assert.equal(s.file !== recordings.shapes[shape].file.code, s.change != null, `${where}: the file changed without saying so, or the other way`);
  });
});

test("a first deploy: the migration finishes before the app is ready, and the app ends up answering", () => {
  for (const shape of SHAPES)
    for (const target of TARGETS) {
      const s = scenario(shape, target, "first-deploy");
      const h = healthAt(s, s.duration, true);
      assert.ok(h.firstAnswer != null, `${shape}/${target} never answered`);
      assert.equal(s.probes[s.probes.length - 1][1], 1, `${shape}/${target} does not end answering`);
      assert.equal(h.failed, 0, `${shape}/${target} failed a probe after it first answered`);
      // On Kubernetes the migration is a Job of its own; under podman it runs in the migrator's unit before its pod.
      const ran = Math.min(...probed(s).map((p) => p.events.find((e) => e.state === "running" || e.state === "ready")?.t ?? Infinity));
      if (target === "kubernetes") {
        const migrated = s.processes.find((p) => p.service === "migrate")?.events.find((e) => e.state === "done");
        assert.ok(migrated && migrated.t <= ran, `${shape}/${target}: the app ran before the migration finished`);
      }
      assert.ok(s.jobs.some((j) => j.job === "migrate" && /Migration finished/.test(j.text) && j.t <= (h.firstAnswer ?? Infinity)), `${shape}/${target}: no migration before the first answer`);
    }
});

test("a release while busy holds the roll until the run ends, and the run is left as it was ended", () => {
  for (const shape of SHAPES)
    for (const target of TARGETS) {
      const s = scenario(shape, target, "release-while-busy");
      const where = `${shape}/${target}`;
      assert.ok(s.run, `${where} recorded no busy run`);
      assert.ok(s.run.states.some((r) => r.state === "in_progress" && r.drain), `${where}: the drain was never on while the run was busy`);
      assert.equal(runAt(s, s.duration)?.state, "completed", `${where}: the run did not end as it was ended`);
      assert.ok(s.jobs.some((j) => j.job === "drain" && /still busy/.test(j.text)), `${where}: the drain never said the run was busy`);
      // No copy of the new version starts before the run ended. What ran when the recording began is the old one.
      const pods = appPods(s);
      const old = pods.filter((p) => p.events[0].t < 1000).map((p) => p.tag);
      const fresh = pods.filter((p) => !old.includes(p.tag));
      assert.ok(fresh.length > 0, `${where}: nothing new started`);
      const firstNew = Math.min(...fresh.map((p) => p.events[0].t));
      assert.ok(firstNew > s.run.ended, `${where}: a new copy started at ${firstNew}, before the run ended at ${s.run.ended}`);
    }
});

test("on Kubernetes a release while busy fails no probe and runs the old and new copy at once", () => {
  for (const shape of SHAPES) {
    const s = scenario(shape, "kubernetes", "release-while-busy");
    const h = healthAt(s, s.duration);
    assert.equal(h.failed, 0, `${shape}: ${h.failed} probes failed`);
    assert.ok(h.answered > 100, `${shape}: only ${h.answered} probes`);
    assert.ok(mostRunningAt(s, s.duration) >= 2, `${shape}: never two copies running at once`);
    const lane = lanesOf(s).find((l) => l.key === s.probe);
    assert.ok(lane && lane.rows.length >= 2, `${shape}: the lanes do not show the overlap`);
    assert.ok(lane.rows.flat().some((b) => b.version === "old") && lane.rows.flat().some((b) => b.version === "new"), shape);
  }
});

test("on your machine a release while busy restarts the app, leaves Postgres running, and the probes show the gap", () => {
  for (const shape of SHAPES) {
    const s = scenario(shape, "podman", "release-while-busy");
    const lives = probed(s);
    assert.ok(lives.length >= 2, `${shape}: the app was not restarted`);
    // Podman restarts rather than overlaps: the new copy starts after the old one died.
    const died = lives[0].events.find((e) => e.state === "gone" || e.state === "failed");
    assert.ok(died && died.t <= lives[1].events[0].t, `${shape}: the old and new copy overlapped under podman`);
    const db = s.processes.filter((p) => p.service === "database");
    assert.equal(db.length, 1, `${shape}: Postgres was restarted`);
    assert.equal(db[0].events.length, 1, `${shape}: Postgres stopped during the release`);
    const h = healthAt(s, s.duration);
    assert.ok(h.failed > 0 && h.longestGap > 0, `${shape}: no gap recorded, so the page's copy about one is wrong`);
    assert.equal(s.probes[s.probes.length - 1][1], 1, `${shape}: the app did not come back`);
  }
});

test("on Kubernetes a broken release is rolled back and ends on the image it started on", () => {
  for (const shape of SHAPES) {
    const s = scenario(shape, "kubernetes", "broken-release");
    assert.notEqual(s.exitCode, 0, `${shape}: the deploy succeeded`);
    assert.ok(s.cli.some((l) => /rolled back to the version it ran before this deploy/.test(l.text)), `${shape}: nothing was rolled back`);
    assert.ok(s.before && s.after === s.before, `${shape}: ends on ${s.after}, not ${s.before}`);
    const ready = probed(s).filter((p) => stateAt(p, s.duration) === "ready");
    assert.ok(ready.length >= 1 && ready.every((p) => p.tag === s.before), `${shape}: what serves at the end is not the old image`);
    assert.ok(probed(s).some((p) => p.tag !== s.before && p.events.some((e) => e.state === "crashing" || e.state === "starting")), `${shape}: the broken copy never shows`);
    assert.equal(healthAt(s, s.duration).failed, 0, `${shape}: a probe failed while the broken copy was refused`);
  }
});

test("on your machine a broken release has no rollback: the app stays down", () => {
  for (const shape of SHAPES) {
    const s = scenario(shape, "podman", "broken-release");
    const tail = s.probes.slice(-25);
    assert.ok(tail.length > 0 && tail.every(([, ok]) => ok === 0), `${shape}: the app answered again, so the page's copy is wrong`);
  }
});

test("who can reach what: every check came out as it should, and the outside is refused", () => {
  each((shape, target, s, key) => {
    if (key !== "who-can-reach-what") return;
    const where = `${shape}/${target}`;
    assert.ok(s.checks.length >= 2, `${where} recorded too few checks`);
    // Every check on a cluster, and what is published under podman. The podman runtime has no network policies (the
    // CLI reference says so), so whether a container on another podman network reaches the app's Postgres is shown
    // as recorded, and the page's summary names it when it does (checked below).
    for (const c of s.checks)
      if (target === "kubernetes" || c.group !== "network") assert.equal(c.result, c.want, `${where}: ${c.who} to ${c.what} was ${c.result}`);
    assert.ok(s.checks.some((c) => c.want === "refused"), where);
    // A run deployed without its egress (the sample under podman, deploy/README.md says why) has no egress check,
    // and the summary says it was not checked.
    if (s.egress.length === 0) {
      assert.ok(target === "podman" && !/"egress"/.test(s.file), `${where}: a held deploy recorded no egress check`);
      assert.match(summaryOf(s, key, target), /egress was not checked/, where);
    } else {
      assert.ok(s.egress.length >= 4, `${where}: the egress check recorded too little`);
      assert.ok(s.egress.every((e) => e.ok), `${where}: ${JSON.stringify(s.egress.filter((e) => !e.ok))}`);
      assert.ok(s.egress.some((e) => e.got !== "open"), where);
      assert.match(s.file, /"egressImage"/, `${where}: the file shown is not the one held to its egress`);
    }
    const wrong = s.checks.filter((c) => c.result !== c.want);
    assert.ok(wrong.every((c) => summaryOf(s, key, target).includes(`${c.who} to ${c.what}, ${c.result}`)), `${where}: the summary hides a check`);
  });
});

test("who can reach what: a failed egress check is named as not as it should be", () => {
  const s = structuredClone(scenario("hub-workers", "kubernetes", "who-can-reach-what"));
  const row = s.egress.find((e) => e.target === "169.254.169.254:80");
  assert.ok(row);
  row.ok = false;
  row.got = "open";
  const text = summaryOf(s, "who-can-reach-what", "kubernetes");
  assert.match(text, /Not as it should: .*169\.254\.169\.254:80, open/, text);
  assert.doesNotMatch(text, /Nothing outside the app reached it/, text);
});

test("the section's copy keeps to the home page's rules", () => {
  for (const file of ["src/components/landing/ShipIt.tsx", "src/components/landing/DeployReplay.tsx"]) {
    const text = readFileSync(new URL(`../${file}`, import.meta.url), "utf8");
    // What a reader sees: JSX text and string literals, not comments.
    const prose = [...text.matchAll(/"([^"\n]{12,})"|>([^<>{}\n]{12,})</g)].map((m) => m[1] ?? m[2]).join("\n");
    assert.ok(!prose.includes("—"), `${file} has an em dash`);
    assert.ok(!/\b(trains?|junctions?|TypeScript)\b/i.test(prose), `${file}: ${prose.match(/.*\b(trains?|junctions?|TypeScript)\b.*/i)?.[0]}`);
  }
});
