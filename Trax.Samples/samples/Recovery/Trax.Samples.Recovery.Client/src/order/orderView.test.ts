import { describe, expect, it } from "vitest";
import { ORDER_SOURCES, orderLines } from "../sources";
import { durationText, junctionEventsOf, trackOf, viewOf, type RunStep, type TabletCall, type TimedEvent } from "./orderView";

const LINES = orderLines(ORDER_SOURCES.machine.code);
const ORDER = { item: "1 × Margherita", price: "$12.00", restaurant: "Pizzeria Napoli" };
const BASE = Date.parse("2026-10-13T19:42:00Z");
const iso = (t: number) => new Date(BASE + t).toISOString();

/** What the laptop and the phone hear while an order is started, filled and placed twice under one request id. */
function placed(): TimedEvent[] {
  return [
    { t: 0, kind: "request", operation: "start", requestId: "s", label: "Start an order" },
    { t: 40, kind: "answer", operation: "start", requestId: "s", revision: 1, state: "Cart", problem: null },
    { t: 60, kind: "push", screen: "laptop", revision: 1, state: "Cart" },
    { t: 61, kind: "push", screen: "phone", revision: 1, state: "Cart" },
    { t: 600, kind: "request", operation: "addItem", requestId: "a", label: "Add 1 × Margherita" },
    { t: 640, kind: "answer", operation: "addItem", requestId: "a", revision: 2, state: "Cart", problem: null },
    { t: 650, kind: "push", screen: "laptop", revision: 2, state: "Cart" },
    { t: 651, kind: "push", screen: "phone", revision: 2, state: "Cart" },
    { t: 1400, kind: "request", operation: "placeOrder", requestId: "p", label: "Place order" },
    { t: 1460, kind: "push", screen: "laptop", revision: 3, state: "Ordered" },
    { t: 1461, kind: "push", screen: "phone", revision: 3, state: "Ordered" },
    { t: 1470, kind: "answer", operation: "placeOrder", requestId: "p", revision: 3, state: "Ordered", problem: null },
    { t: 1520, kind: "request", operation: "placeOrder", requestId: "p", label: "Place order (pressed again)" },
    { t: 1560, kind: "answer", operation: "placeOrder", requestId: "p", revision: 3, state: "Ordered", problem: null, replayed: true },
  ];
}

const STEPS = ["SendToRestaurant", "ChargeCard", "Cook", "PickUp", "Deliver"];

/** The run's steps, each 600 ms, from 1600 ms; `failAt` fails that step and stops there. */
function steps(failAt?: string, upTo = STEPS.length): RunStep[] {
  const out: RunStep[] = [];
  for (const [i, name] of STEPS.slice(0, upTo).entries()) {
    const failed = name === failAt;
    out.push({ position: i, name, state: failed ? "FAILED" : "COMPLETED", startedAt: iso(1600 + i * 600), endedAt: iso(2200 + i * 600) });
    if (failed) break;
  }
  return out;
}

const delivered = (t: number): TimedEvent[] => [
  { t, kind: "push", screen: "laptop", revision: 4, state: "Delivered" },
  { t: t + 1, kind: "push", screen: "phone", revision: 4, state: "Delivered" },
];

describe("the order is delivered", () => {
  const events = [...placed(), ...junctionEventsOf(BASE, steps(), [], null), ...delivered(4700)];
  const view = viewOf(events, LINES, ORDER, BASE);

  it("follows the order to the door on the phone", () => {
    expect(view.state).toBe("Delivered");
    expect(view.revision).toBe(4);
    expect(view.phone.status).toBe("Delivered");
    expect(view.phone.lines.map((l) => l.text)).toEqual([
      "Added 1 × Margherita to your cart",
      "Order placed",
      "Sent to the restaurant",
      "Your card was charged $12.00",
      "The kitchen is cooking it",
      "A courier picked it up",
      "On its way to you",
      "Delivered. Enjoy your meal.",
    ]);
    expect(view.phone.machine.map((m) => m.at)).toEqual(["done", "done", "current"]);
  });

  it("lights the line that sent the order there, and ignores the second press", () => {
    expect(view.lit?.line).toBe(LINES["state:Delivered"]);
    expect(ORDER_SOURCES.machine.code.split("\n")[view.lit!.line - 1]).toContain(".OnDone(OrderState.Delivered");
    expect(view.note).toBeNull();
    expect(view.progress.map((p) => p.tone)).toEqual(["done", "done", "done"]);
  });

  it("fills every box of the timeline and lights both connectors", () => {
    const track = trackOf(view, 5000, { cart: false, timer: null, over: true });
    expect(track.machine.map((s) => [s.label, s.tone, s.dimmed])).toEqual([
      ["Cart", "done", false],
      ["Ordered", "done", false],
      ["Delivered", "done", false],
      ["Cancelled", "idle", true],
    ]);
    expect(track.train?.map((s) => s.tone)).toEqual(["done", "done", "done", "done", "done"]);
    expect(track.starts).toEqual({ lit: true });
    expect(track.outcome).toEqual({ lit: true, text: "its outcome moves the order", tone: "done" });
  });
});

describe("while the run is on its junctions", () => {
  const running: RunStep[] = [...steps(undefined, 2), { position: 2, name: "Cook", state: "IN_PROGRESS", startedAt: iso(2800), endedAt: null }];
  const view = viewOf([...placed(), ...junctionEventsOf(BASE, running, [], null)], LINES, ORDER, BASE);

  it("lights the train's junction and says so on the phone", () => {
    expect(view.trainLit).toEqual({ step: "Cook", tone: "running", stage: "cooking" });
    expect(view.lit?.line).toBe(LINES.invoke);
    expect(view.phone.status).toBe("Cooking");
  });

  it("fills the running box part way and Ordered by the junctions done", () => {
    const track = trackOf(view, 3200, { cart: false, timer: null, over: false });
    const cook = track.train![2];
    expect(cook.tone).toBe("running");
    expect(cook.fill).toBeGreaterThan(0);
    expect(cook.fill).toBeLessThan(1);
    expect(track.machine[1]).toMatchObject({ label: "Ordered: waiting for DeliverOrderTrain", tone: "waiting", fill: 2 / 5 });
    expect(track.outcome?.lit).toBe(false);
  });
});

describe("the restaurant's tablet times out", () => {
  const calls: TabletCall[] = [
    { attempt: 1, startedAt: iso(1610), endedAt: iso(2610), problem: "The tablet did not answer in time." },
    { attempt: 2, startedAt: iso(3110), endedAt: iso(3710), problem: null },
  ];

  it("shows the try that failed, the retry, and the run going on", () => {
    const sending: RunStep[] = [{ position: 0, name: "SendToRestaurant", state: "IN_PROGRESS", startedAt: iso(1600), endedAt: null }];
    const events = junctionEventsOf(BASE, sending, calls, null);
    expect(events.map((e) => (e.kind === "junction" ? `${e.step}:${e.status}:${e.attempt}` : e.kind))).toEqual([
      "SendToRestaurant:running:1",
      "SendToRestaurant:failed:1",
      "retry",
      "SendToRestaurant:running:2",
    ]);
    const view = viewOf([...placed(), ...events], LINES, ORDER, BASE);
    expect(view.phone.lines.map((l) => l.text)).toContain("The restaurant has not answered yet; trying again");
  });

  it("marks the junction tried twice", () => {
    const done: RunStep[] = [{ position: 0, name: "SendToRestaurant", state: "COMPLETED", startedAt: iso(1600), endedAt: iso(3720) }];
    const view = viewOf([...placed(), ...junctionEventsOf(BASE, done, calls, null)], LINES, ORDER, BASE);
    const track = trackOf(view, 4000, { cart: false, timer: null, over: false });
    expect(track.train![0]).toMatchObject({ tone: "done", attempt: 2 });
  });

  it("does not call a try failed while the next has not started", () => {
    const sending: RunStep[] = [{ position: 0, name: "SendToRestaurant", state: "IN_PROGRESS", startedAt: iso(1600), endedAt: null }];
    expect(junctionEventsOf(BASE, sending, calls.slice(0, 1), null)).toHaveLength(1);
  });
});

describe("the restaurant is closed", () => {
  const failure = { junction: "SendToRestaurant", reason: "The restaurant is closed." };
  const events = [
    ...placed(),
    ...junctionEventsOf(BASE, steps("SendToRestaurant"), [{ attempt: 1, startedAt: iso(1610), endedAt: iso(2190), problem: "The restaurant is closed." }], failure),
    { t: 2300, kind: "push", screen: "laptop", revision: 4, state: "Cancelled" } as TimedEvent,
    { t: 2301, kind: "push", screen: "phone", revision: 4, state: "Cancelled" } as TimedEvent,
  ];
  const view = viewOf(events, LINES, ORDER, BASE);

  it("cancels the order before the card is charged", () => {
    expect(view.state).toBe("Cancelled");
    expect(view.phone.detail).toBe("The restaurant is closed. You were not charged.");
    expect(view.lit?.line).toBe(LINES["state:Cancelled"]);
    const track = trackOf(view, 2400, { cart: false, timer: null, over: true });
    expect(track.machine.map((s) => s.tone)).toEqual(["done", "failed", "idle", "failed"]);
    expect(track.train?.map((s) => s.tone)).toEqual(["failed", "idle", "idle", "idle", "idle"]);
    expect(track.outcome).toEqual({ lit: true, text: "failed, so Cancelled", tone: "failed" });
  });
});

describe("left in the cart", () => {
  const filled = placed().slice(0, 8);

  it("fills the cart by the server's timer and names it", () => {
    const view = viewOf(filled, LINES, ORDER, BASE);
    const track = trackOf(view, 8000, { cart: true, timer: { due: 15640, after: 15000 }, over: false });
    expect(track.train).toBeNull();
    expect(track.timer).toEqual({ lit: false, text: "after 15 s (timer)" });
    expect(track.machine[0].tone).toBe("running");
    expect(track.machine[0].fill).toBeCloseTo((8000 - 40) / (15640 - 40));
  });

  it("is cleared by the timer, which the code lights", () => {
    const cleared: TimedEvent[] = [
      ...filled,
      { t: 15700, kind: "push", screen: "laptop", revision: 3, state: "Cleared" },
      { t: 15701, kind: "push", screen: "phone", revision: 3, state: "Cleared" },
    ];
    const view = viewOf(cleared, LINES, ORDER, BASE);
    expect(view.lit?.line).toBe(LINES.timer);
    expect(view.phone.lines[view.phone.lines.length - 1].text).toBe("Your cart was cleared");
    const track = trackOf(view, 16000, { cart: true, timer: { due: 15640, after: 15000 }, over: true });
    expect(track.machine.map((s) => s.tone)).toEqual(["done", "done"]);
    expect(track.timer?.lit).toBe(true);
  });
});

describe("durationText", () => {
  it("reads a timer's wait", () => {
    expect(durationText(15_000)).toBe("15 s");
    expect(durationText(30 * 60_000)).toBe("30 min");
    expect(durationText(90 * 60_000)).toBe("1 h 30 min");
  });
});

describe("orderLines", () => {
  it("finds every line it lights in OrderMachine.cs", () => {
    for (const [key, line] of Object.entries(LINES)) expect(line, key).toBeGreaterThan(0);
  });
});
