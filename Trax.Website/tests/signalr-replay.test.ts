// Plays the recorded SignalRBroadcaster session through the landing page's player (src/lib/signalr-replay.ts) and
// checks what it ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/signalr-recordings.json" with { type: "json" };
import {
  MASKED,
  apply,
  initial,
  lineOf,
  ping,
  probe,
  runsOf,
  signIn,
  signOut,
  type HubState,
  type Outcome,
  type Scheduled,
  type SignalRRecording,
} from "../src/lib/signalr-replay.ts";

const rec = data as SignalRRecording;
const OUTCOMES: Outcome[] = ["Succeed", "FailForClients", "FailUnexpectedly"];

let seq = 0;
/** Applies a press's messages in time order, as if pressed at `at`. */
function play(state: HubState, scheduled: Scheduled[], at = 0): HubState {
  for (const s of [...scheduled].sort((a, b) => a.at - b.at)) state = apply(state, s, at + s.at, seq++);
  return state;
}

const signedIn = () => play(initial(), signIn(rec));

test("the hub refuses a client that is not signed in", () => {
  assert.equal(rec.probe.status, 401);
  const state = play(initial(), probe(rec));
  assert.equal(state.signedIn, false);
  assert.equal(state.hub, "disconnected");
  assert.match(state.probe ?? "", /401 Unauthorized: no connection was opened/);
  assert.deepEqual(state.traffic.map((t) => t.title), ["POST negotiate", "401 Unauthorized"]);
});

test("signing in connects to the hub with the recorded connection", () => {
  const state = signedIn();
  assert.equal(state.signedIn, true);
  assert.equal(state.who, "demo-operator");
  assert.equal(state.hub, "connected");
  assert.equal(state.connectionId, rec.connect.connectionId);
});

test("each button replays a recorded run: Started, then how it ended, and the 202 says nothing else", () => {
  for (const outcome of OUTCOMES) {
    const scheduled = ping(rec, outcome, 0);
    const state = play(signedIn(), scheduled, 10_000);
    assert.equal(state.runs.length, 1, outcome);
    const card = state.runs[0];
    assert.deepEqual(card.steps.map((s) => s.eventType), ["Started", outcome === "Succeed" ? "Completed" : "Failed"], outcome);
    assert.equal(card.status, outcome === "Succeed" ? "Completed" : "Failed", outcome);
    assert.equal(card.steps[1].gapMs, runsOf(rec, outcome)[0].events[1].serverMs, outcome);
    assert.ok(state.traffic.some((t) => t.title === "202 Accepted"), outcome);
  }
});

test("a reason meant to be read reaches the page, any other is withheld", () => {
  const readable = play(signedIn(), ping(rec, "FailForClients", 0)).runs[0];
  assert.deepEqual(readable.reason, { text: "The ping target did not answer.", withheld: false });
  const internal = play(signedIn(), ping(rec, "FailUnexpectedly", 0)).runs[0];
  assert.deepEqual(internal.reason, { text: MASKED, withheld: true });
  // The internal message is in the junction's source, but no message the hub sent carries it.
  assert.ok(rec.sources.junction.code.includes("10.0.4.17"));
  assert.ok(!JSON.stringify(rec.runs).includes("10.0.4.17"));
});

test("every press is a run of its own until a button's recordings run out, then the oldest card is replaced", () => {
  let state = signedIn();
  const n = runsOf(rec, "Succeed").length;
  assert.ok(n >= 3);
  for (let i = 0; i < n; i++) state = play(state, ping(rec, "Succeed", i), 1000 * (i + 1));
  assert.equal(state.runs.length, n);
  assert.equal(new Set(state.runs.map((r) => r.externalId)).size, n);
  state = play(state, ping(rec, "Succeed", n), 1000 * (n + 1));
  assert.equal(state.runs.length, n);
  assert.equal(state.runs[0].externalId, runsOf(rec, "Succeed")[0].events[0].evt.externalId);
});

test("signing out goes back to the sign-in gate", () => {
  const state = play(play(signedIn(), ping(rec, "Succeed", 0)), signOut(rec));
  assert.deepEqual(state, initial());
});

test("the code panel finds every line the player points at", () => {
  const presses = [probe(rec), signIn(rec), signOut(rec), ...OUTCOMES.flatMap((o) => runsOf(rec, o).map((_, i) => ping(rec, o, i)))];
  for (const s of presses.flat()) {
    const focus = "focus" in s ? s.focus : undefined;
    if (focus) assert.ok(lineOf(rec.sources[focus.source].code, focus) >= 0, `${focus.source}: ${focus.needle}`);
  }
});
