// Plays every recorded Recovery refund run through the landing page's player (src/lib/recovery-replay.ts) and checks
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
  trackOf,
  modelCallsOf,
  withCheckpointPauses,
  CHECKPOINT_PAUSE_MS,
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
  for (const order of ["A-1001", "A-1002", "A-1003"])
    for (const key of [
      keyOf(order, "none"),
      keyOf(order, "emailing"),
      ...(["none", "askAfresh", "changeData"] as const).map((f) => keyOf(order, "paying", f)),
    ])
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

test("a crash while paying pauses in the backoff, and the retry replays both answers", () => {
  for (const order of ["A-1001", "A-1002", "A-1003"]) {
    const key = keyOf(order, "paying");
    const r = recording(key);
    const fork = r.events.find((e) => e.type === "fork" && e.at === "backoff")!;
    const inBackoff = advance(begin(r), fork.t);
    assert.equal(phaseOf(inBackoff), "breaks", key);
    assert.equal(inBackoff.attempts[0].trainState, "FAILED", key);

    const done = advance(begin(r), Infinity);
    assert.equal(done.attempts.length, 2, key);
    assert.equal(done.attempts[1].trainState, "COMPLETED", key);
    assert.deepEqual(badges(done, 1), ["replayed: model not asked", "replayed: model not asked"], key);
    assert.ok(done.lines.some((l) => l.tone === "replay"), key);
  }
});

test("a crash while emailing retries from the checkpoint: only the email runs again, and nothing is asked", () => {
  for (const order of ["A-1001", "A-1002", "A-1003"]) {
    const key = keyOf(order, "emailing");
    const done = advance(begin(recording(key)), Infinity);
    assert.equal(done.attempts.length, 2, key);
    assert.equal(done.attempts[0].failureJunction ?? "NotifyCustomer", "NotifyCustomer", key);
    assert.deepEqual(
      Object.values(done.attempts[1].steps).map((s) => s.name),
      ["NotifyCustomer"],
      key,
    );
    // There is nothing left to ask afresh or change in its backoff.
    const fork = recording(key).events.find((e) => e.type === "fork" && e.at === "backoff")!;
    assert.equal(takeFork(advance(begin(recording(key)), fork.t), recordings, "askAfresh"), null, key);
  }
});

test("the timeline draws the checkpoint, saved on the crashed attempt and restored on the retry", () => {
  const r = recording(keyOf("A-1001", "emailing"));
  const done = advance(begin(r), Infinity);
  const lanes = trackOf(r, done, Infinity);
  assert.equal(lanes.length, 2);
  const names = lanes[0].slots.map((s) => s.label);
  assert.deepEqual(names.slice(-2), ["Checkpoint", "NotifyCustomer"]);
  assert.equal(lanes[0].slots.at(-2)?.tone, "saved");
  assert.equal(lanes[0].slots.at(-1)?.tone, "failed");
  assert.ok(lanes[1].slots.slice(0, -1).every((s) => s.tone === "restored"), String(lanes[1].slots.map((s) => s.tone)));
  assert.equal(lanes[1].slots.at(-1)?.tone, "done");

  // Before anything plays, every box is drawn and empty.
  const empty = trackOf(r, null, 0);
  assert.ok(empty.every((lane) => lane.slots.every((s) => s.tone === "idle" && s.fill === 0)));

  // A crash while paying never reaches the checkpoint on its first attempt; the retry saves it.
  const paid = recording(keyOf("A-1001", "paying"));
  const paidLanes = trackOf(paid, advance(begin(paid), Infinity), Infinity);
  const at = paidLanes[0].slots.findIndex((s) => s.label === "Checkpoint");
  assert.equal(paidLanes[0].slots[at].tone, "idle");
  assert.equal(paidLanes[1].slots[at].tone, "saved");
});

test("changing the data during the backoff refuses both replays and asks the model afresh", () => {
  const refund = advance(begin(recording(keyOf("A-1001", "paying", "changeData"))), Infinity);
  assert.deepEqual(badges(refund, 1), ["asked afresh: state changed", "asked afresh: state changed"]);
  // An earlier refund now shows, so the model is unsure and the case goes to a person.
  const gate = Object.values(refund.attempts[1].steps).find((s) => s.kind === "ROUTE" && s.questionKey === "ApproveRefund");
  assert.equal(gate?.answer, "Unsure");
});

test("asking afresh after the run asks the model every question again, on purpose", () => {
  const p = playOut(begin(recording(keyOf("A-1001", "paying"))));
  const afresh = badges(p, p.attempts.length - 1);
  assert.equal(afresh.length, 2);
  assert.ok(afresh.every((b) => b === "asked afresh: on purpose"), String(afresh));
});

test("a fork taken in the backoff keeps what is on screen and continues in the forked recording", () => {
  const plain = recording(keyOf("A-1001", "paying"));
  const fork = plain.events.find((e) => e.type === "fork" && e.at === "backoff")!;
  const before = advance(begin(plain), fork.t + 100);
  assert.equal(takeFork(begin(plain), recordings, "changeData"), null, "forked before the backoff");

  const taken = takeFork(before, recordings, "changeData");
  assert.ok(taken);
  assert.equal(taken.playback.recording.key, keyOf("A-1001", "paying", "changeData"));
  assert.deepEqual(taken.playback.attempts, before.attempts);
  assert.equal(takeFork(taken.playback, recordings, "askAfresh"), null, "forked twice");

  const after = advance(taken.playback, Infinity);
  assert.deepEqual(after.attempts.map((a) => a.id), [1041, 1042]);
  assert.ok(after.lines.some((l) => l.text.startsWith("# DATA CHANGED")));
});

test("the trigger asks afresh at once instead of waiting out the backoff", () => {
  const plain = recording(keyOf("A-1002", "paying"));
  const fork = plain.events.find((e) => e.type === "fork" && e.at === "backoff")!;
  const taken = takeFork(advance(begin(plain), fork.t), recordings, "askAfresh");
  assert.ok(taken);
  const after = advance(taken.playback, Infinity);
  assert.equal(after.attempts[1].startedBy, "askAfresh");
  assert.deepEqual(badges(after, 1), ["asked afresh: on purpose", "asked afresh: on purpose"]);
});

test("the code panel finds every step a recording shows in its train's source", () => {
  for (const r of recordings) {
    const code = sources[r.scenario].code;
    for (const e of r.events)
      if (e.type === "step") assert.ok(lineOf(code, e.step) >= 0, `${r.key}: ${e.step.kind} ${e.step.name} ${e.step.answer ?? ""}`);
  }
});

test("the player holds at the checkpoint so it can be seen, and the box fills while it does", () => {
  const raw = recording(keyOf("A-1001", "emailing"));
  const paused = withCheckpointPauses(raw);
  const marks = paused.events.filter((e) => e.type === "checkpoint");
  // Attempt 1 and the requeue store it; the retry started from it, so it stores nothing.
  assert.equal(marks.length, 2);
  assert.equal(paused.events.at(-1)!.t, raw.events.at(-1)!.t + 2 * CHECKPOINT_PAUSE_MS);

  const at = marks[0].t;
  const mid = advance(begin(paused), at + CHECKPOINT_PAUSE_MS / 2);
  const notify = Object.values(mid.attempts[0].steps).find((s) => s.name === "NotifyCustomer");
  assert.equal(notify, undefined, "the email waits for the checkpoint");
  const box = trackOf(paused, mid, at + CHECKPOINT_PAUSE_MS / 2)[0].slots.find((s) => s.label === "Checkpoint")!;
  assert.equal(box.note, "saving");
  assert.ok(box.fill > 0.4 && box.fill < 0.6, String(box.fill));
  assert.ok(mid.lines.some((l) => l.text.includes("CHECKPOINT")));

  // A crash while paying stores it only on the retry, and every fork still lines up.
  const paying = withCheckpointPauses(recording(keyOf("A-1001", "paying")));
  assert.equal(paying.events.filter((e) => e.type === "checkpoint").length, 2);
  const forks = (["none", "changeData", "askAfresh"] as const).map((f) => withCheckpointPauses(recording(keyOf("A-1001", "paying", f))));
  const backoff = (r: Recording) => r.events.find((e) => e.type === "fork" && e.at === "backoff")!.t;
  assert.ok(forks.every((r, i) => backoff(r) === backoff(recording(keyOf("A-1001", "paying", (["none", "changeData", "askAfresh"] as const)[i])))));
});

test("the model panel shows each question as asked and each answer as given, and the one it waits on", () => {
  const r = withCheckpointPauses(recording(keyOf("A-1001", "paying")));
  const done = advance(begin(r), Infinity);
  const calls = modelCallsOf(done);
  assert.deepEqual(calls.map((c) => `${c.attempt} ${c.questionKey}`), [
    "attempt 1 Problem",
    "attempt 1 ApproveRefund",
    "attempt 2 Problem",
    "attempt 2 ApproveRefund",
  ]);
  assert.equal(calls[0].question?.instructions, "What went wrong with this order?");
  assert.deepEqual(calls[0].question?.options?.map((o) => o.name), ["Damaged", "Lost", "Unwanted"]);
  assert.match(calls[0].answer ?? "", /^Damaged, \d+% sure$/);
  assert.match(calls[1].answer ?? "", /^93% yes: the Yes track$/);
  assert.equal(calls[1].model, "demo-rules-1.0");
  assert.ok(calls.slice(2).every((c) => c.badge?.tone === "replay"));

  // Between the case loading and the first answer, the panel waits on the first question.
  const loaded = r.events.find((e) => e.type === "step" && e.step.name === "LoadRefundCase" && e.step.state === "COMPLETED")!;
  const waiting = modelCallsOf(advance(begin(r), loaded.t));
  assert.equal(waiting.at(-1)?.state, "waiting");
  assert.equal(waiting.at(-1)?.questionKey, "Problem");
});
