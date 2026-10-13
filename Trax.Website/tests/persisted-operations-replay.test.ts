// Plays the recorded PersistedOperations stories through the landing page's player
// (src/lib/persisted-operations-replay.ts) and checks what it shows. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/persisted-operations-recordings.json" with { type: "json" };
import { durationOf, scheduleOf, viewAt, type Recording, type Story } from "../src/lib/persisted-operations-replay.ts";

const recording = data as unknown as Recording;
const story = (key: string): Story => {
  const found = recording.stories.find((s) => s.key === key);
  assert.ok(found, `no story ${key}`);
  return found;
};
const end = (key: string) => viewAt(story(key), durationOf(story(key)));

test("there are the three stories, each starting from the query the app shipped with", () => {
  assert.deepEqual(recording.stories.map((s) => s.key), ["calls", "fix", "refused"]);
  const shipped = recording.manifest.find((op) => op.id === "greet_v1")?.document;
  assert.ok(shipped?.includes("style: CASUAL"));
  for (const s of recording.stories) assert.equal(s.storedBefore, shipped, s.key);
});

test("the shipped app calls greet_v1 by id and is answered; a query of someone else's is refused", () => {
  const view = end("calls");
  assert.deepEqual(view.cards.map((c) => [c.actor, c.outcome]), [
    ["app", "answered"],
    ["stranger", "refused"],
  ]);
  assert.equal(view.phone.greeting, "Hey Alice!");
  assert.match(view.cards[0].sent, /^greet_v1 /);
  assert.ok(!view.cards[0].sent.includes("query"), "the app sends an id, never a query");
});

test("the operator's fix changes the answer, and the app's call does not change", () => {
  const view = end("fix");
  assert.deepEqual(view.cards.map((c) => [c.actor, c.outcome]), [
    ["app", "answered"],
    ["operator", "accepted"],
    ["app", "answered"],
  ]);
  assert.equal(view.cards[0].answer, "Hey Alice!");
  assert.equal(view.cards[2].answer, "Good day, Alice.");
  assert.equal(view.cards[0].sent, view.cards[2].sent);
  assert.ok(view.stored?.includes("style: FORMAL"));
  assert.equal(view.phone.greeting, "Good day, Alice.");
});

test("an edit that renames the field the app reads is refused, and the old answer stands", () => {
  const view = end("refused");
  assert.deepEqual(view.cards.map((c) => [c.actor, c.outcome]), [
    ["app", "answered"],
    ["operator", "refused"],
    ["app", "answered"],
  ]);
  assert.ok(view.proposed?.document.includes("message: greeting"));
  assert.equal(view.proposed?.outcome, "refused");
  assert.equal(view.stored, story("refused").storedBefore, "the store kept what it had");
  assert.equal(view.cards[2].answer, view.cards[0].answer);
});

test("nothing shows before the first request, and a request shows as in flight before its answer", () => {
  const s = story("fix");
  const empty = viewAt(s, -1);
  assert.equal(empty.cards.length, 0);
  assert.equal(empty.phone.greeting, null);

  const [first] = scheduleOf(s);
  const inFlight = viewAt(s, first.sendAt);
  assert.equal(inFlight.cards[0].outcome, "waiting");
  assert.equal(inFlight.phone.calling, true);
  assert.equal(viewAt(s, first.answerAt).cards[0].outcome, "answered");
});
