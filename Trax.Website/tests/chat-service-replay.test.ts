// Plays the recorded ChatService traffic through the landing page's player (src/lib/chat-service-replay.ts) and
// checks what each user's chat ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/chat-service-recordings.json" with { type: "json" };
import {
  addCharlie,
  advance,
  begin,
  busy,
  choices,
  choose,
  describe,
  focusOf,
  lineOf,
  refuse,
  viewOf,
  type ChatPlayback,
  type ChatRecording,
} from "../src/lib/chat-service-replay.ts";

const recording = data as ChatRecording;
const settle = (p: ChatPlayback) => advance(p, Infinity);
const start = () => settle(begin(recording, 0));

/** Every path from the first turn to a line with no answers. */
function paths(parent = ""): string[][] {
  const next = recording.lines.filter((l) => l.parent === parent);
  if (next.length === 0) return [[]];
  return next.flatMap((l) => paths(l.key).map((rest) => [l.key, ...rest]));
}

test("the prologue opens the room for both users and tells Alice that Bob was added", () => {
  const p = start();
  assert.ok(!busy(p));
  for (const user of ["Alice", "Bob"]) {
    const view = viewOf(p, user);
    assert.ok(view.room && view.subscribed, user);
  }
  assert.deepEqual(viewOf(p, "Alice").lines.map((l) => l.content), ["Alice added Bob"]);
  assert.deepEqual(viewOf(p, "Bob").lines.map((l) => l.content), ["No messages yet"], "Bob loaded an empty history, then subscribed");
});

test("nothing can be sent while the prologue is still playing", () => {
  const p = begin(recording, 0);
  assert.ok(busy(p));
  assert.deepEqual(choices(recording, p), []);
  assert.equal(choose(recording, p, "a", 0), null);
});

test("every path through the conversation reaches both users, in the order it was picked", () => {
  const all = paths();
  assert.equal(all.length, 8);
  for (const path of all) {
    let p = start();
    for (const key of path) {
      const next = choose(recording, p, key, 0);
      assert.ok(next, `${path.join(" > ")}: ${key} was not offered`);
      p = settle(next);
    }
    const expected = path.map((k) => recording.lines.find((l) => l.key === k)!.content);
    for (const user of ["Alice", "Bob"]) {
      const said = viewOf(p, user).lines.filter((l) => l.kind === "message").map((l) => l.content);
      assert.deepEqual(said, expected, `${user}: ${path.join(" > ")}`);
    }
    assert.deepEqual(choices(recording, p), [], "the conversation ends");
  }
});

test("each line is sent by its speaker and marked as theirs only in their own chat", () => {
  let p = start();
  p = settle(choose(recording, p, "a", 0)!);
  p = settle(choose(recording, p, "a1", 0)!);
  const alice = viewOf(p, "Alice").lines.filter((l) => l.kind === "message");
  const bob = viewOf(p, "Bob").lines.filter((l) => l.kind === "message");
  assert.deepEqual(alice.map((l) => [l.sender, l.mine]), [["Alice", true], ["Bob", false]]);
  assert.deepEqual(bob.map((l) => [l.sender, l.mine]), [["Alice", false], ["Bob", true]]);
});

test("a line not on offer is refused", () => {
  const p = start();
  assert.equal(choose(recording, p, "a1", 0), null, "an answer before its question");
  assert.equal(choose(recording, p, "nope", 0), null);
});

test("Charlie's history read and subscription are refused, and a socket with no key is closed with 4403", () => {
  const p = settle(refuse(recording, start(), 0)!);
  assert.equal(refuse(recording, p, 0), null, "once");
  const notes = p.played.filter((e) => e.segment === "refusal").map((e) => describe(e, p.played));
  assert.ok(notes.some((d) => d.tone === "refused" && d.note?.includes("not a participant")));
  assert.ok(notes.some((d) => d.title === "error" && d.note?.includes("Not authorized.")));
  assert.ok(notes.some((d) => d.title === "WebSocket closed (4403)"));
  assert.deepEqual(
    viewOf(p, "Charlie").lines.map((l) => l.content),
    [
      `Trax refused the history: You are not a participant in room ${(recording.prologue.entries.find((e) => e.op === "CreateChatRoom" && e.direction === "in")!.detail as { data: { dispatch: { createChatRoom: { output: { chatRoomId: string } } } } }).data.dispatch.createChatRoom.output.chatRoomId}.`,
      "Trax refused the subscription: Not authorized.",
    ],
    "Charlie sees only the refusals",
  );
  assert.equal(viewOf(p, "Charlie").subscribed, false);
});

test("Charlie added at any point loads exactly the history picked so far, and hears every line after it", () => {
  for (const path of paths())
    for (let at = 0; at <= path.length; at++) {
      let p = start();
      for (const key of path.slice(0, at)) p = settle(choose(recording, p, key, 0)!);
      p = settle(addCharlie(recording, p, 0)!);
      assert.equal(addCharlie(recording, p, 0), null, "once");
      assert.equal(refuse(recording, p, 0), null, "he is in the room");
      for (const key of path.slice(at)) {
        const next = choose(recording, p, key, 0);
        assert.ok(next, `${path.join(" > ")} with Charlie after ${at}: ${key}`);
        p = settle(next);
      }
      const expected = path.map((k) => recording.lines.find((l) => l.key === k)!.content);
      for (const user of ["Alice", "Bob", "Charlie"]) {
        const said = viewOf(p, user).lines.filter((l) => l.kind === "message").map((l) => l.content);
        assert.deepEqual(said, expected, `${user}: ${path.join(" > ")} with Charlie after ${at}`);
      }
      const charlie = viewOf(p, "Charlie");
      assert.ok(charlie.room && charlie.subscribed);
      assert.ok(viewOf(p, "Alice").lines.some((l) => l.content === "Alice added Charlie"));
      assert.ok(viewOf(p, "Bob").lines.some((l) => l.content === "Alice added Charlie"));
    }
});

test("Charlie can be refused first and added after", () => {
  let p = settle(refuse(recording, start(), 0)!);
  p = settle(addCharlie(recording, p, 0)!);
  const charlie = viewOf(p, "Charlie");
  assert.ok(charlie.room && charlie.subscribed);
  assert.equal(charlie.lines.at(-1)?.content, "No messages yet");
});

test("the API key never appears in the recording", () => {
  const text = JSON.stringify(data);
  assert.ok(!text.includes("key-do-not-use-in-production"));
});

test("every event names the train that caused it, and every focus is found in its file", () => {
  let p = start();
  p = settle(choose(recording, p, "b", 0)!);
  p = settle(refuse(recording, p, 0)!);
  for (const entry of p.played) {
    const d = describe(entry, p.played);
    if (d.tone === "event") assert.ok(!d.note?.includes("the chat train"), d.note);
    const focus = focusOf(entry);
    if (!focus) continue;
    const source = recording.sources.find((s) => s.path.endsWith(`/${focus.file}`));
    assert.ok(source, focus.file);
    assert.ok(lineOf(source.code, focus.needle) >= 0, `${focus.file}: ${focus.needle}`);
  }
});

test("entries appear in order and no faster than the pace", () => {
  const p = begin(recording, 1000);
  const dues = p.queue.map((q) => q.due);
  for (let i = 1; i < dues.length; i++) assert.ok(dues[i] - dues[i - 1] >= 280, `entry ${i}`);
  assert.equal(advance(p, 999).played.length, 0);
  assert.equal(advance(p, 1000).played.length, 1);
});
