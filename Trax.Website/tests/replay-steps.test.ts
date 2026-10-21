// The stops Back and Next move between on the order and persisted-operations replays (src/lib/replay-steps.ts), and
// that every recorded scenario gives them somewhere to go. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import orderData from "../src/data/order-recordings.json" with { type: "json" };
import poData from "../src/data/persisted-operations-recordings.json" with { type: "json" };
import { eventsOf, type OrderRecordings } from "../src/lib/order-replay.ts";
import { scheduleOf, type Recording } from "../src/lib/persisted-operations-replay.ts";
import { nextStop, previousStop, stepIndex, stopsOf } from "../src/lib/replay-steps.ts";

test("events close together are one stop, after the last of them", () => {
  assert.deepEqual(stopsOf([135, 222, 299, 926, 990, 1812]), [300, 991, 1813]);
});

test("Next and Back walk the stops and stay inside them", () => {
  const stops = [300, 991, 1813];
  assert.equal(nextStop(stops, -1), 300);
  assert.equal(nextStop(stops, 300), 991);
  assert.equal(nextStop(stops, 1813), 1813);
  assert.equal(previousStop(stops, 1813), 991);
  assert.equal(previousStop(stops, 300), 0);
  assert.equal(stepIndex(stops, -1), 0);
  assert.equal(stepIndex(stops, 991), 2);
});

test("every food order scenario has several steps", () => {
  for (const s of (orderData as unknown as OrderRecordings).scenarios) {
    const stops = stopsOf(eventsOf(s).map((e) => e.t));
    assert.ok(stops.length >= 3, `${s.key} has ${stops.length} stops`);
  }
});

test("every persisted-operations story has a stop per call", () => {
  const recording = poData as unknown as Recording;
  for (const story of recording.stories) {
    const stops = stopsOf(scheduleOf(story).map((x) => x.answerAt));
    assert.ok(stops.length >= 1, `${story.key} has no stops`);
  }
});
