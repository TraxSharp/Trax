// Plays every recorded food order scenario through the landing page's player (src/lib/order-replay.ts) and checks
// what it ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/order-recordings.json" with { type: "json" };
import { durationOf, eventsOf, trackAt, viewAt, type OrderRecordings, type Scenario } from "../src/lib/order-replay.ts";

const recordings = data as unknown as OrderRecordings;
const scenario = (key: Scenario["key"]): Scenario => {
  const s = recordings.scenarios.find((x) => x.key === key);
  assert.ok(s, `no ${key} scenario`);
  return s;
};
const end = (key: Scenario["key"]) => viewAt(recordings, scenario(key), durationOf(scenario(key)));
const codeLine = (n: number) => recordings.source.code.split("\n")[n - 1] ?? "";

test("the recording has the four scenarios, each in time order", () => {
  assert.deepEqual(
    recordings.scenarios.map((s) => s.key).sort(),
    ["left-in-cart", "order", "restaurant-closed", "tablet-times-out"],
  );
  for (const s of recordings.scenarios) {
    assert.ok(s.title && s.caption, `${s.key} needs a title and a caption`);
    const ts = s.events.map((e) => e.t);
    assert.deepEqual(ts, [...ts].sort((a, b) => a - b), `${s.key} is not sorted by t`);
  }
});

test("every line the page can light is a line of OrderMachine.cs that says what it lights", () => {
  const expect: Record<string, string> = {
    "state:Cart": "OrderState.Cart",
    "state:Ordered": "OrderState.Ordered",
    "event:placeOrder": "PlaceOrder",
    "event:addItem": "AddItem",
    invoke: ".Invokes<",
    timer: ".After(",
  };
  for (const [key, text] of Object.entries(expect)) {
    const n = recordings.lines[key];
    assert.ok(typeof n === "number", `no line for ${key}`);
    assert.ok(codeLine(n).includes(text), `${key} lights line ${n}: ${codeLine(n)}`);
  }
});

test("a double click on Place order makes one order, without a word on the page", () => {
  const s = scenario("order");
  const places = eventsOf(s).filter((e) => e.kind === "request" && e.operation === "placeOrder");
  assert.equal(places.length, 2);
  const view = end("order");
  assert.equal(view.note, null);
  assert.equal(view.states.filter((x) => x.label === "Ordered").length, 1);
  assert.equal(view.junctions.filter((j) => j.label === "ChargeCard").length, 1);
});

test("an order is delivered: every junction completes once and the phone says Delivered", () => {
  const view = end("order");
  assert.deepEqual(view.states.map((x) => x.label), ["Cart", "Ordered", "Delivered"]);
  assert.deepEqual(view.junctions.map((j) => j.label), recordings.train.steps);
  assert.ok(view.junctions.every((j) => j.tone === "done"));
  assert.equal(view.phone.status, "Delivered");
  assert.equal(view.run?.status, "Completed");
  assert.equal(view.lit?.line, recordings.lines["state:Delivered"]);
  assert.ok(view.progress.every((p) => p.tone === "done"));
});

test("while the train runs, the lit line is the one that invokes it, and the phone follows the junction", () => {
  const s = scenario("order");
  const cook = eventsOf(s).find((e) => e.kind === "junction" && e.step === "Cook" && e.status === "running");
  assert.ok(cook);
  const view = viewAt(recordings, s, cook.t + 1);
  assert.equal(view.lit?.line, recordings.lines.invoke);
  assert.equal(view.lit?.tone, "running");
  assert.equal(view.phone.status, "Cooking");
  assert.equal(view.run?.status, "Running");
});

test("a tablet that times out: the step fails, is retried, and the pizza still arrives", () => {
  const s = scenario("tablet-times-out");
  const retry = eventsOf(s).find((e) => e.kind === "retry");
  assert.ok(retry);
  const during = viewAt(recordings, s, retry.t);
  assert.equal(during.lit?.tone, "failed");
  assert.equal(during.phone.status, "Waiting for the restaurant");
  const view = end("tablet-times-out");
  const sends = view.junctions.filter((j) => j.label === "SendToRestaurant");
  assert.deepEqual(sends.map((j) => j.tone), ["failed", "done"]);
  assert.equal(view.run?.status, "Recovered");
  assert.equal(view.phone.status, "Delivered");
  assert.ok(view.phone.lines.some((l) => /trying again/.test(l.text)));
});

test("a closed restaurant cancels the order before anything is charged", () => {
  const view = end("restaurant-closed");
  assert.deepEqual(view.states.map((x) => x.label), ["Cart", "Ordered", "Cancelled"]);
  assert.ok(!view.junctions.some((j) => j.label === "ChargeCard"));
  assert.equal(view.phone.status, "Cancelled");
  assert.match(view.phone.detail, /You were not charged/);
  assert.equal(view.run?.status, "Failed");
  assert.equal(view.lit?.line, recordings.lines["state:Cancelled"]);
});

test("a cart left alone is cleared by the timer, after a gap the page names", () => {
  const view = end("left-in-cart");
  assert.deepEqual(view.states.map((x) => x.label), ["Cart", "Cleared"]);
  assert.equal(view.junctions.length, 0);
  assert.equal(view.run, null);
  assert.equal(view.lit?.line, recordings.lines.timer);
  // The phone's clock keeps the half hour the recording leaves out.
  const cleared = view.phone.lines.find((l) => l.text === "Your cart was cleared");
  assert.equal(cleared?.clock, "8:12 PM");
  assert.equal(view.phone.status, "Cart cleared");
});

test("nothing has arrived before the first event, and fields the player does not know are ignored", () => {
  const s = scenario("order");
  const before = viewAt(recordings, s, -1);
  assert.equal(before.state, null);
  assert.equal(before.lit, null);
  const extra = { ...s, events: s.events.map((e) => ({ ...e, somethingNew: 1 })) } as Scenario;
  assert.deepEqual(viewAt(recordings, extra, durationOf(extra)).states, end("order").states);
  assert.equal(viewAt(recordings, undefined, 1000).state, null);
});

test("the timeline is drawn in full before anything plays, every box empty", () => {
  const track = trackAt(recordings, scenario("order"), -1);
  assert.deepEqual(track.machine.map((s) => s.key), ["Cart", "Ordered", "Delivered", "Cancelled"]);
  assert.deepEqual(track.train?.map((s) => s.key), recordings.train.steps);
  assert.ok([...track.machine, ...(track.train ?? [])].every((s) => s.fill === 0 && s.tone === "idle" && !s.dimmed));
  assert.equal(track.starts?.lit, false);
  assert.equal(track.outcome?.lit, false);
});

test("a box fills while its step runs, and Ordered waits for the train, filling as junctions complete", () => {
  const s = scenario("order");
  const cook = eventsOf(s).filter((e) => e.kind === "junction" && e.step === "Cook");
  const half = (cook[0].t + cook[1].t) / 2;
  const track = trackAt(recordings, s, half);
  const slot = track.train!.find((x) => x.key === "Cook")!;
  assert.equal(slot.tone, "running");
  assert.ok(Math.abs(slot.fill - 0.5) < 0.01);
  const ordered = track.machine.find((x) => x.key === "Ordered")!;
  assert.equal(ordered.tone, "waiting");
  assert.equal(ordered.fill, 2 / 5);
  assert.equal(track.starts?.lit, true);
  assert.equal(track.outcome?.lit, false);
  const end = trackAt(recordings, s, durationOf(s));
  assert.equal(end.outcome?.lit, true);
  assert.equal(end.machine.find((x) => x.key === "Cancelled")?.dimmed, true);
  assert.ok(end.train!.every((x) => x.tone === "done" && x.fill === 1));
});

test("a retried junction turns failed, then refills as attempt 2; the order keeps waiting through it", () => {
  const s = scenario("tablet-times-out");
  const retry = eventsOf(s).find((e) => e.kind === "retry")!;
  const during = trackAt(recordings, s, retry.t);
  assert.equal(during.train![0].tone, "failed");
  assert.equal(during.machine.find((x) => x.key === "Ordered")?.tone, "waiting");
  const end = trackAt(recordings, s, durationOf(s));
  assert.equal(end.train![0].attempt, 2);
  assert.equal(end.train![1].attempt, undefined);
});

test("a closed restaurant: the outcome reads failed, so Cancelled, and the unreached junctions dim", () => {
  const s = scenario("restaurant-closed");
  const end = trackAt(recordings, s, durationOf(s));
  assert.equal(end.outcome?.text, "failed, so Cancelled");
  assert.equal(end.outcome?.tone, "failed");
  assert.equal(end.machine.find((x) => x.key === "Delivered")?.dimmed, true);
  assert.ok(end.train!.slice(1).every((x) => x.dimmed));
});

test("a cart left alone has no train row, and the timer connector lights into Cleared", () => {
  const s = scenario("left-in-cart");
  const before = trackAt(recordings, s, 0);
  assert.equal(before.train, null);
  assert.equal(before.timer?.lit, false);
  assert.equal(before.timer?.text, "after 30 min (timer)");
  const end = trackAt(recordings, s, durationOf(s));
  assert.equal(end.timer?.lit, true);
  assert.deepEqual(end.machine.map((x) => [x.key, x.tone]), [["Cart", "done"], ["Cleared", "done"]]);
});

test("the phone speaks plainly: no revisions or internals on its face, one colour per stage", () => {
  for (const s of recordings.scenarios) {
    const view = viewAt(recordings, s, durationOf(s));
    for (const l of view.phone.lines) assert.doesNotMatch(l.text, /\brev\b|attempt|junction|manifest/i, `${s.key}: ${l.text}`);
    assert.doesNotMatch(view.phone.status + view.phone.detail, /\brev\b|manifest/i);
  }
  const view = end("order");
  assert.deepEqual(
    view.phone.lines.map((l) => [l.text, l.stage]),
    [
      ["Added 1 × Margherita to your cart", "cart"],
      ["Order placed", "sent"],
      ["Sent to the restaurant", "sent"],
      ["Your card was charged $12.00", "charged"],
      ["The kitchen is cooking it", "cooking"],
      ["A courier picked it up", "courier"],
      ["On its way to you", "courier"],
      ["Delivered. Enjoy your meal.", "delivered"],
    ],
  );
  assert.deepEqual(view.phone.machine.map((m) => [m.label, m.at]), [["Cart", "done"], ["Ordered", "done"], ["Delivered", "current"]]);
  assert.deepEqual(end("restaurant-closed").phone.machine.map((m) => m.label), ["Cart", "Ordered", "Cancelled"]);
  assert.equal(end("restaurant-closed").lit?.stage, "cancelled");
});

test("while the train runs, the junction acting is lit in the train's own code, one Chain line per step", () => {
  const code = recordings.train.source?.code ?? "";
  for (const step of recordings.train.steps) assert.ok(code.includes(`Chain<${step}>`), `no Chain<${step}> in the train`);

  const s = scenario("order");
  const lit = new Set<string>();
  for (const e of eventsOf(s)) {
    const view = viewAt(recordings, s, e.t);
    if (view.trainLit) lit.add(view.trainLit.step);
  }
  assert.deepEqual([...lit], recordings.train.steps);
  assert.equal(end("order").trainLit, null, "the run is over, so the machine's code shows again");
});
