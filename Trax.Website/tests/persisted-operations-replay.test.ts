// Replays the recorded PersistedOperations requests through the landing page's player
// (src/lib/persisted-operations-replay.ts) and checks what it shows. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/persisted-operations-recordings.json" with { type: "json" };
import {
  ACTIONS,
  act,
  highlightFor,
  offered,
  outcomeOf,
  rangeOf,
  start,
  type Recording,
  type Session,
} from "../src/lib/persisted-operations-replay.ts";

const recording = data as unknown as Recording;
const run = (...keys: string[]) => keys.reduce<Session>((s, k) => act(recording, s, k), start(recording));
const last = (s: Session) => s.log[s.log.length - 1];

test("every action the page lists was recorded in at least one state, and every recorded one is listed", () => {
  const listed = new Set(ACTIONS.map((a) => a.key));
  for (const state of Object.values(recording.states))
    for (const key of Object.keys(state.actions)) assert.ok(listed.has(key), `${key} is recorded but not listed`);
  for (const key of listed)
    assert.ok(Object.values(recording.states).some((s) => key in s.actions), `${key} was never recorded`);
});

test("an inline document is refused in every state, and a dev_ one runs", () => {
  for (const keys of [[], ["upload"], ["upload", "hotfix"]]) {
    const refused = outcomeOf(last(run(...keys, "inline")).exchanges[0]);
    assert.equal(refused.code, "PERSISTED_OPERATION_REQUIRED");
    assert.ok(outcomeOf(last(run(...keys, "devInline")).exchanges[0]).ok);
  }
});

test("a call by id fails before the manifest is uploaded and runs the train after", () => {
  const before = run("callGreet:Alice");
  assert.equal(outcomeOf(last(before).exchanges[0]).ok, false);
  assert.equal(highlightFor(last(before)).needle, "opts.UseDatabase(connectionString)");

  const after = run("upload", "callGreet:Alice");
  assert.equal(after.state, "uploaded");
  const greeting = JSON.stringify(last(after).exchanges[0].response);
  assert.match(greeting, /"greeting":"Hello, Alice\.","greetedAt"/);
  assert.equal(highlightFor(last(after)).needle, "Chain<ComposeGreetingJunction>()");
});

test("the hot-fix keeps the fingerprint and changes what the same id returns, until the manifest restores it", () => {
  const uploaded = run("upload");
  const hotfixed = act(recording, uploaded, "hotfix");
  assert.equal(hotfixed.state, "hotfixed");
  assert.equal(hotfixed.store.greet_v1?.shapeFingerprint, uploaded.store.greet_v1?.shapeFingerprint);
  assert.notEqual(hotfixed.store.greet_v1?.document, uploaded.store.greet_v1?.document);
  assert.match(JSON.stringify(last(act(recording, hotfixed, "callGreet:Eve")).exchanges[0].response), /\{"greetedAt":/);

  const restored = act(recording, hotfixed, "upload");
  assert.equal(restored.state, "uploaded");
  assert.equal(restored.store.greet_v1?.document, recording.manifest.find((m) => m.id === "greet_v1")?.document);
});

test("a shape-changing edit and an upload without a key are refused and leave the store as it was", () => {
  for (const keys of [["upload"], ["upload", "hotfix"]]) {
    const before = run(...keys);
    const edited = act(recording, before, "shapeEdit");
    assert.equal(outcomeOf(last(edited).exchanges[0]).code, "SHAPE_DIFF_VIOLATION");
    assert.deepEqual(edited.store, before.store);

    const anonymous = act(recording, before, "uploadAnonymous");
    assert.equal(outcomeOf(last(anonymous).exchanges[0]).code, "TRAX_AUTHORIZATION");
    assert.equal(last(anonymous).exchanges[0].request.headers["X-Api-Key"], undefined);
    assert.deepEqual(anonymous.store, before.store);
  }
});

test("the hot-fix and the shape edit are not offered before anything is uploaded", () => {
  const empty = start(recording);
  assert.equal(offered(recording, empty, "hotfix"), false);
  assert.equal(offered(recording, empty, "shapeEdit"), false);
  assert.throws(() => act(recording, empty, "hotfix"));
});

test("the code panel finds every line it highlights inside the range it shows", () => {
  for (const [state, { actions }] of Object.entries(recording.states))
    for (const [key, recorded] of Object.entries(actions)) {
      const { file, needle } = highlightFor({ key, from: state as Session["state"], exchanges: recorded.exchanges });
      const source = recording.sources.find((s) => s.path === file);
      assert.ok(source, `${file} is not in the recording`);
      const lines = source.code.split("\n");
      const [first, end] = rangeOf(file, source.code);
      const at = lines.findIndex((l) => l.includes(needle));
      assert.ok(at >= first && at < end, `${key} in ${state}: "${needle}" not shown`);
    }
});
