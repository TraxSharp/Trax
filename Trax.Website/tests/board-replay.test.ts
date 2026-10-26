// Plays every recorded Board scenario through the landing page's player (src/lib/board-replay.ts) and checks what it
// ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/board-recordings.json" with { type: "json" };
import { eventsOf, movesOf, viewAt, type BoardRecordings, type Scenario } from "../src/lib/board-replay.ts";

const recordings = data as unknown as BoardRecordings;
const scenario = (key: string): Scenario => {
  const s = recordings.scenarios.find((x) => x.key === key);
  assert.ok(s, `no ${key} scenario`);
  return s;
};
const end = (key: string) => viewAt(recordings, scenario(key), movesOf(scenario(key)));
const problems = (key: string) =>
  eventsOf(scenario(key))
    .map((e) => e.claim?.problem ?? e.answer?.problem)
    .filter((p): p is { code: string; message: string } => !!p)
    .map((p) => p.code);

test("the recording has the five scenarios, each in time and move order", () => {
  assert.deepEqual(recordings.scenarios.map((s) => s.key), ["every-lane", "skip-ahead", "fix-regresses", "qa-catches-edge", "cannot-reproduce"]);
  for (const s of recordings.scenarios) {
    assert.ok(s.title && s.caption, `${s.key} needs a title and a caption`);
    const ts = s.events.map((e) => e.t);
    assert.deepEqual(ts, [...ts].sort((a, b) => a - b), `${s.key} is not sorted by t`);
    const moves = s.events.map((e) => e.move);
    assert.deepEqual(moves, [...moves].sort((a, b) => a - b), `${s.key} is not sorted by move`);
  }
});

test("every move carries the script's caption, and the page shows it", () => {
  for (const s of recordings.scenarios) {
    for (let m = 1; m <= movesOf(s); m++) {
      const written = eventsOf(s).find((e) => e.move === m && e.caption)?.caption;
      assert.ok(written, `${s.key} move ${m} has no caption in the recording`);
      assert.equal(viewAt(recordings, s, m).caption, written);
    }
  }
});

test("a lane worked without trouble is one move", () => {
  const s = scenario("every-lane");
  assert.equal(movesOf(s), 5);
  assert.deepEqual(problems("every-lane"), []);
  const view = end("every-lane");
  assert.equal(view.tickets[0].state, "Done");
  assert.equal(view.tickets[0].lane, "done");
  assert.equal(view.tickets[0].holder, null);
});

test("every line an event lights is a line of the source it names", () => {
  for (const s of recordings.scenarios) {
    for (const e of s.events) {
      if (!e.line) continue;
      const [source] = e.line.split(":");
      const n = recordings.lines[e.line];
      assert.ok(recordings.sources[source], `${e.line} names no source`);
      assert.ok(typeof n === "number" && recordings.sources[source].code.split("\n")[n - 1]?.trim(), `${e.line} lights no line`);
    }
  }
});

test("every move shows one acting client, and every ticket sits in a lane", () => {
  const lanes = new Set(recordings.lanes.map((l) => l.key));
  for (const s of recordings.scenarios) {
    for (let m = 0; m <= movesOf(s); m++) {
      const view = viewAt(recordings, s, m);
      assert.ok(view.pods.filter((p) => p.current).length <= 1, `${s.key} move ${m} lights more than one pod`);
      for (const t of view.tickets) assert.ok(lanes.has(t.lane), `${s.key} move ${m}: ${t.id} is in no lane`);
      if (m > 0) assert.ok(view.caption, `${s.key} move ${m} has no caption`);
    }
  }
});

test("skipping ahead is refused twice, and the ticket still ends Done", () => {
  assert.deepEqual(problems("skip-ahead"), ["NOTHING_TO_CLAIM", "FORBIDDEN"]);
  const s = scenario("skip-ahead");
  const forbidden = eventsOf(s).find((e) => e.answer?.problem?.code === "FORBIDDEN");
  assert.ok(forbidden, "no FORBIDDEN");
  const view = viewAt(recordings, s, forbidden.move);
  assert.equal(view.tickets[0].lane, "review");
  assert.equal(view.pods.find((p) => p.current)?.name, "fix-bot");
  assert.equal(end("skip-ahead").tickets[0].state, "Done");
});

test("a fix that regresses is rejected by VerifyFixTrain, and the second patch gets through", () => {
  const s = scenario("fix-regresses");
  const checks = eventsOf(s).filter((e) => e.check?.train === "VerifyFixTrain" && e.check.status !== "running");
  assert.deepEqual(checks.map((e) => e.check?.status), ["failed", "done"]);
  assert.equal(end("fix-regresses").tickets[0].state, "Done");
});

test("QA's sweep sends a too-loose fix back to Fix", () => {
  const s = scenario("qa-catches-edge");
  const failed = eventsOf(s).find((e) => e.line === "ticket:event:QaFailed");
  assert.ok(failed, "no QaFailed");
  const view = viewAt(recordings, s, failed.move);
  assert.equal(view.tickets[0].lane, "fix");
  assert.match(view.caption, /back to Fix/);
  assert.equal(end("qa-catches-edge").tickets[0].state, "Done");
});

test("a ticket that cannot be reproduced closes only after two cases", () => {
  const s = scenario("cannot-reproduce");
  assert.deepEqual(problems("cannot-reproduce"), ["GUARD_FAILED"]);
  const failed = eventsOf(s).find((e) => e.check?.train === "ReproduceTrain" && e.check.status === "failed");
  assert.ok(failed, "no failed ReproduceTrain");
  const after = viewAt(recordings, s, failed.move);
  assert.equal(after.tickets[0].lane, "reproduce");
  assert.equal(after.pods.find((p) => p.name === "repro-bot")?.assignment, "Rejected");
  const view = end("cannot-reproduce");
  assert.equal(view.tickets[0].state, "Closed");
  assert.equal(view.tickets[0].lane, "done");
});

// BoardReplay keeps two lines for the caption at full width; a longer one would push the board down as it plays.
test("every caption fits the two lines the page keeps for it", () => {
  for (const s of recordings.scenarios) {
    const captions = [s.caption];
    for (let m = 1; m <= movesOf(s); m++) captions.push(viewAt(recordings, s, m).caption);
    for (const c of captions) assert.ok(c.length <= 180, `${s.key}: ${c.length} characters: ${c}`);
  }
});
