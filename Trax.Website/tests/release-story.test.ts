// Checks the moving picture at the front of the deploy section (src/lib/release-story.ts) against the recorded
// Hub + Workers Kubernetes runs it is read from, and that the simulated AWS picture can never pass for a recorded one.
// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import data from "../src/data/deploy-recordings.json" with { type: "json" };
import { healthAt, type DeployRecordings } from "../src/lib/deploy-replay.ts";
import { DOTS_PER_SECOND, SIMULATED_CAPTION, STORY_MS, STORY_SOURCE, captionAt, counterAt, sourceLabel, stepAt, storyOf } from "../src/lib/release-story.ts";

const recordings = data as unknown as DeployRecordings;
const source = (key: "release-while-busy" | "broken-release") => {
  const s = recordings.shapes[STORY_SOURCE.shape].targets[STORY_SOURCE.target]?.scenarios[key];
  assert.ok(s, `no ${key} recorded for the story`);
  return s;
};

test("a normal release: the job finishes, then v2 starts, gets ready, and v1 goes, in that order", () => {
  const story = storyOf(recordings, "normal", "kubernetes");
  assert.ok(story && !story.simulated && story.job && story.waiting);
  assert.ok(story.job.to <= story.v2.from, "v2 started before the job finished");
  assert.ok(story.waiting.from < story.job.to, "the release did not wait on the job");
  assert.ok(story.v2.readyAt != null && story.v2.readyAt < (story.v1.goneAt ?? Infinity), "v1 went before v2 was ready");
  assert.ok(story.v1.goneAt != null && story.v1.goneAt <= STORY_MS);
  assert.ok(story.dots.every((d) => d.ok));
  assert.ok(story.dots.some((d) => d.to === "v2") && story.dots.some((d) => d.to === "v1"));
});

test("the counter at the end of the picture is the recording's own count, with nothing failed", () => {
  for (const [kind, key] of [["normal", "release-while-busy"], ["broken", "broken-release"]] as const) {
    const story = storyOf(recordings, kind, "kubernetes");
    assert.ok(story);
    const s = source(key);
    const end = counterAt(story, STORY_MS);
    const h = healthAt(s, s.duration);
    assert.deepEqual(end, { answered: h.answered, failed: h.failed }, kind);
    assert.equal(end?.failed, 0, `${kind}: a request failed, so the picture's "failed 0" is wrong`);
    // And it only ever counts up.
    let last = 0;
    for (let at = 0; at <= STORY_MS; at += 250) {
      const c = counterAt(story, at);
      assert.ok(c && c.answered >= last);
      last = c.answered;
    }
  }
});

test("a broken release: v2 never gets ready, requests stay on v1, and v2 is taken away", () => {
  const story = storyOf(recordings, "broken", "kubernetes");
  assert.ok(story);
  assert.equal(story.v2.readyAt, null);
  assert.ok(story.v2.crashes.length > 0 && story.v2.goneAt != null);
  assert.ok(story.dots.every((d) => d.to === "v1" && d.ok));
  assert.equal(story.v1.goneAt, null, "v1 stopped in the broken release");
  assert.match(captionAt(story, STORY_MS), /back on its own/);
  assert.ok(source("broken-release").cli.some((l) => /rolled back to the version it ran before this deploy/.test(l.text)));
});

test("the closing captions claim nobody waited only when the recording failed no request", () => {
  const claims = /Nobody had to wait|never stopped answering/;
  for (const kind of ["normal", "broken"] as const) {
    const clean = storyOf(recordings, kind, "kubernetes");
    assert.ok(clean);
    assert.match(clean.captions.map((c) => c.text).join(" "), claims, `${kind}: the clean recording lost its claim`);
    const sim = storyOf(recordings, kind, "aws");
    assert.ok(sim);
    assert.doesNotMatch(sim.captions.map((c) => c.text).join(" "), claims, `${kind}: the simulated picture claims what nothing measured`);

    const dropped = structuredClone(recordings);
    const s = dropped.shapes[STORY_SOURCE.shape].targets[STORY_SOURCE.target]?.scenarios[kind === "normal" ? "release-while-busy" : "broken-release"];
    assert.ok(s);
    s.probes[Math.floor(s.probes.length / 2)][1] = 0;
    s.probes[Math.floor(s.probes.length / 3)][1] = 0;
    const story = storyOf(dropped, kind, "kubernetes");
    assert.ok(story);
    const text = story.captions.map((c) => c.text).join(" ");
    assert.doesNotMatch(text, claims, `${kind}: ${text}`);
    assert.match(text, /2 requests failed/, `${kind}: ${text}`);
  }
});

test("AWS is simulated: marked in the data, never counted, never in the recorded shapes", () => {
  const sim = recordings.simulated?.aws;
  assert.ok(sim && sim.simulated === true && sim.reason.length > 0 && sim.basis.length >= 3);
  for (const b of sim.basis) assert.ok(readFileSync(new URL(`../../${b.source.split(" ")[0]}`, import.meta.url), "utf8").length > 0, b.source);
  for (const shape of Object.values(recordings.shapes)) assert.ok(!("aws" in shape.targets), "a simulated target sits among the recorded ones");
  for (const kind of ["normal", "broken"] as const) {
    const story = storyOf(recordings, kind, "aws");
    assert.ok(story && story.simulated);
    assert.equal(story.scenario, null);
    assert.equal(story.dots.length, 0, "the simulated picture draws recorded requests");
    for (let at = 0; at <= STORY_MS; at += 1000) assert.equal(counterAt(story, at), null, "the simulated picture counts requests");
  }
});

test("the page labels AWS simulated whenever it shows it", () => {
  assert.equal(SIMULATED_CAPTION, "Simulated from what the deploy does on ECS; not yet recorded on AWS.");
  const page = readFileSync(new URL("../src/components/landing/ReleaseStory.tsx", import.meta.url), "utf8");
  assert.match(page, /story\.simulated && \(/, "no badge on the simulated picture");
  assert.match(page, /\{SIMULATED_CAPTION\}/, "the caption is not shown");
});

test("the picture's copy keeps to the home page's rules", () => {
  for (const file of ["src/components/landing/ReleaseStory.tsx", "src/lib/release-story.ts", "src/components/landing/ShipIt.tsx"]) {
    const text = readFileSync(new URL(`../${file}`, import.meta.url), "utf8");
    const prose = [...text.matchAll(/"([^"\n]{12,})"|>([^<>{}\n]{12,})</g)].map((m) => m[1] ?? m[2]).join("\n");
    assert.ok(!prose.includes("—"), `${file} has an em dash`);
    assert.ok(!/\b(trains?|junctions?|TypeScript)\b/i.test(prose), `${file}: ${prose.match(/.*\b(trains?|junctions?|TypeScript)\b.*/i)?.[0]}`);
  }
});

test("each dot is a recorded request, sampled sparsely enough to follow", () => {
  for (const [kind, key] of [["normal", "release-while-busy"], ["broken", "broken-release"]] as const) {
    const story = storyOf(recordings, kind, "kubernetes");
    assert.ok(story);
    const s = source(key);
    // Every perDot-th recorded request, in order, at the story time of its own recorded moment.
    const sampled = s.probes.filter((_, i) => i % story.perDot === 0);
    assert.equal(story.dots.length, sampled.length, kind);
    story.dots.forEach((d, i) => {
      assert.equal(d.ok, sampled[i][1] === 1);
      assert.ok(Math.abs(story.recordedAt(d.at) - sampled[i][0]) < 2, `${kind}: dot ${i} is not at its request's time`);
    });
    const perSecond = story.dots.length / (STORY_MS / 1000);
    assert.ok(perSecond <= DOTS_PER_SECOND * 1.5 && perSecond >= DOTS_PER_SECOND / 2, `${kind}: ${perSecond.toFixed(1)} dots a second`);
    // A handful on screen at once: a dot is drawn for three seconds.
    let most = 0;
    for (let at = 0; at <= STORY_MS; at += 100) most = Math.max(most, story.dots.filter((d) => Math.abs(d.at - at) <= 1500).length);
    assert.ok(most <= 8, `${kind}: ${most} dots on screen at once`);
  }
});

test("the deploy's steps light in the order the recording shows them", () => {
  const expect = {
    normal: ["build", "migrate", "drain", "start", "move", "stop", "resume"],
    broken: ["build", "migrate", "drain", "start", "never", "back", "resume"],
  };
  for (const target of ["kubernetes", "aws"] as const)
    for (const kind of ["normal", "broken"] as const) {
      const story = storyOf(recordings, kind, target);
      assert.ok(story);
      assert.deepEqual(story.steps.map((s) => s.key), expect[kind], `${kind} on ${target}`);
      const recorded = story.steps.map((s) => s.recorded);
      assert.deepEqual(recorded, [...recorded].sort((a, b) => a - b), `${kind} on ${target}: a step starts before the one before it`);
      assert.ok(story.steps.every((s, i) => i === 0 || s.at >= story.steps[i - 1].at));
      assert.equal(stepAt(story, STORY_MS), story.steps.length - 1);
      for (const s of story.steps) assert.ok(s.label.split(" ").length <= 3 && s.line.length < 70, s.label);
    }
  // The CLI's own lines are where the recorded steps start.
  const s = source("release-while-busy");
  const story = storyOf(recordings, "normal", "kubernetes");
  assert.ok(story);
  const t = (re: RegExp) => s.cli.find((l) => re.test(l.text))?.t;
  assert.equal(story.steps.find((x) => x.key === "migrate")?.recorded, t(/^Migrating the database/));
  assert.equal(story.steps.find((x) => x.key === "drain")?.recorded, t(/^Draining:/));
  assert.equal(story.steps.find((x) => x.key === "resume")?.recorded, t(/^Ending the drain/));
  const broken = storyOf(recordings, "broken", "kubernetes");
  assert.equal(broken?.steps.find((x) => x.key === "back")?.recorded, source("broken-release").cli.find((l) => /^Rolling the services back/.test(l.text))?.t);
});

test("the picture names where it ran: trax deploy on Kubernetes, recorded, or on AWS, simulated", () => {
  const k8s = storyOf(recordings, "normal", "kubernetes");
  const aws = storyOf(recordings, "normal", "aws");
  assert.ok(k8s && aws);
  assert.equal(sourceLabel(k8s), "trax deploy on Kubernetes (k3s), recorded");
  assert.equal(sourceLabel(aws), "trax deploy on AWS (ECS), simulated");
});
