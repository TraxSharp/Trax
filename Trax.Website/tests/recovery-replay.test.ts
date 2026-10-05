// Plays every recorded Recovery run through the landing page's player (src/lib/recovery-replay.ts) and checks
// what it ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/recovery-recordings.json" with { type: "json" };
import {
  advance,
  badgeOf,
  begin,
  keyOf,
  lineOf,
  phaseOf,
  requeue,
  takeFork,
  type Playback,
  type Recording,
} from "../src/lib/recovery-replay.ts";

const recordings = data.recordings as Recording[];
const sources = data.sources as Record<string, { file: string; code: string }>;
const recording = (key: string) => {
  const found = recordings.find((r) => r.key === key);
  assert.ok(found, `no recording ${key}`);
  return found;
};

/** Plays to the end of the run and, unless told not to, through the requeue after it. */
function playOut(playback: Playback, withRequeue = true): Playback {
  playback = advance(playback, Infinity);
  if (withRequeue) {
    const asked = requeue(playback);
    assert.ok(asked, `${playback.recording.key} has no requeue to play`);
    playback = advance(asked.playback, Infinity);
  }
  return playback;
}

const badges = (p: Playback, attemptIndex: number) => {
  const attempt = p.attempts[attemptIndex];
  return Object.values(attempt.steps)
    .map((s) => badgeOf(p, attempt, s)?.text)
    .filter(Boolean);
};

test("there is a recording for every choice the player offers", () => {
  for (const subject of ["papers", "wiki"])
    for (const key of [keyOf("RESEARCH", subject, false), ...(["none", "askAfresh", "changeData"] as const).map((f) => keyOf("RESEARCH", subject, true, f))])
      recording(key);
  for (const order of ["A-1001", "A-1002", "A-1003"])
    for (const key of [keyOf("REFUND", order, false), ...(["none", "askAfresh", "changeData"] as const).map((f) => keyOf("REFUND", order, true, f))])
      recording(key);
});

test("every recording stops when the run is over, then plays its requeue to the end", () => {
  for (const r of recordings) {
    const done = advance(begin(r), Infinity);
    assert.equal(phaseOf(done), "done", r.key);
    assert.ok(done.waiting, `${r.key} did not wait after the run`);
    const after = playOut(begin(r));
    assert.ok(after.finished, `${r.key} did not finish`);
    assert.equal(after.attempts.at(-1)?.origin, "requeue", r.key);
    assert.equal(after.attempts.at(-1)?.trainState, "COMPLETED", r.key);
    assert.equal(phaseOf(after), "done", r.key);
  }
});

test("a crash pauses in the backoff, and the retry replays every answer", () => {
  for (const key of ["research-papers-crash-none", "research-wiki-crash-none", "refund-A-1001-crash-none"]) {
    const r = recording(key);
    const fork = r.events.find((e) => e.type === "fork" && e.at === "backoff")!;
    const inBackoff = advance(begin(r), fork.t);
    assert.equal(phaseOf(inBackoff), "backoff", key);
    assert.equal(inBackoff.attempts[0].trainState, "FAILED", key);

    const done = advance(begin(r), Infinity);
    assert.equal(done.attempts.length, 2, key);
    assert.equal(done.attempts[1].trainState, "COMPLETED", key);
    const replayed = badges(done, 1);
    assert.ok(replayed.length > 0, key);
    assert.ok(replayed.every((b) => b === "replayed, model not called"), `${key}: ${replayed}`);
  }
});

test("changing the data during the backoff refuses the replay and asks the model afresh", () => {
  const research = advance(begin(recording("research-papers-crash-changeData")), Infinity);
  assert.deepEqual(badges(research, 1), ["asked again: the case changed", "asked again: the case changed"]);
  // The research is now for executives, so the model sends it down another track.
  const route = Object.values(research.attempts[1].steps).find((s) => s.kind === "ROUTE" && s.questionKey === "Source");
  assert.equal(route?.answer, "Web");

  const refund = advance(begin(recording("refund-A-1001-crash-changeData")), Infinity);
  const gate = Object.values(refund.attempts[1].steps).find((s) => s.kind === "ROUTE");
  assert.equal(gate?.answer, "Unsure");
});

test("asking afresh after the run asks the model every question again, on purpose", () => {
  const p = playOut(begin(recording("research-papers-crash-none")));
  const afresh = badges(p, p.attempts.length - 1);
  assert.ok(afresh.length > 0);
  assert.ok(afresh.every((b) => b === "asked again, on purpose"), String(afresh));
});

test("a fork taken in the backoff keeps what is on screen and continues in the forked recording", () => {
  const plain = recording("research-papers-crash-none");
  const fork = plain.events.find((e) => e.type === "fork" && e.at === "backoff")!;
  const before = advance(begin(plain), fork.t + 100);
  assert.equal(takeFork(begin(plain), recordings, "changeData"), null, "forked before the backoff");

  const taken = takeFork(before, recordings, "changeData");
  assert.ok(taken);
  assert.equal(taken.playback.recording.key, "research-papers-crash-changeData");
  assert.deepEqual(taken.playback.attempts, before.attempts);
  assert.equal(takeFork(taken.playback, recordings, "askAfresh"), null, "forked twice");

  const after = advance(taken.playback, Infinity);
  assert.deepEqual(after.attempts.map((a) => a.id), [1041, 1042]);
  assert.deepEqual(badges(after, 1), ["asked again: the case changed", "asked again: the case changed"]);
  assert.ok(after.lines.some((l) => l.text.startsWith("Case changed during the backoff")));
});

test("the trigger asks afresh at once instead of waiting out the backoff", () => {
  const plain = recording("refund-A-1002-crash-none");
  const fork = plain.events.find((e) => e.type === "fork" && e.at === "backoff")!;
  const taken = takeFork(advance(begin(plain), fork.t), recordings, "askAfresh");
  assert.ok(taken);
  const after = advance(taken.playback, Infinity);
  assert.equal(after.attempts[1].startedBy, "askAfresh");
  assert.deepEqual(badges(after, 1), ["asked again, on purpose"]);
});

test("the code panel finds every step a recording shows in its train's source", () => {
  for (const r of recordings) {
    const code = sources[r.scenario].code;
    for (const e of r.events)
      if (e.type === "step") assert.ok(lineOf(code, e.step) >= 0, `${r.key}: ${e.step.kind} ${e.step.name} ${e.step.answer ?? ""}`);
  }
});
