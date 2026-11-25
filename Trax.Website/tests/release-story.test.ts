// Checks the moving picture at the front of the deploy section (src/lib/release-story.ts) against the recorded
// Hub + Workers Kubernetes runs it is read from, and that the simulated AWS picture can never pass for a recorded one.
// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import data from "../src/data/deploy-recordings.json" with { type: "json" };
import { healthAt, type DeployRecordings } from "../src/lib/deploy-replay.ts";
import { SIMULATED_CAPTION, STORY_MS, STORY_SOURCE, captionAt, counterAt, storyOf } from "../src/lib/release-story.ts";

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
  assert.ok(story.dots.length > 50 && story.dots.every((d) => d.ok));
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
