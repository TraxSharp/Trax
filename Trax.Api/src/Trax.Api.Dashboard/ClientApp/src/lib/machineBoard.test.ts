import { describe, expect, it } from "vitest";
import { hasProvenance, layoutBoard, neighbours, type Board, type BoardSticky } from "./machineBoard";

const sticky = (id: string, kind: string, machine: string, label: string): BoardSticky => ({ id, kind, machine, label, scenarios: [] });

// An order that is placed, then paid or cancelled, rings the shop when it is placed, and lapses by a timer.
const board: Board = {
  machine: "order",
  lanes: ["order", "shop"],
  stickies: [
    sticky("aggregate:order", "aggregate", "order", "order"),
    sticky("actor:customer", "actor", "order", "customer"),
    sticky("command:order:Pay", "command", "order", "Pay"),
    sticky("event:order:Placed:Pay:Paid", "event", "order", "Placed → Paid"),
    sticky("command:order:Place", "command", "order", "Place"),
    sticky("event:order:Draft:Place:Placed", "event", "order", "Draft → Placed"),
    sticky("guard:order:Placed:Pay", "policy", "order", "only if allowed: Pay from Placed"),
    sticky("timer:order:Placed", "policy", "order", "after 1 day in Placed"),
    sticky("event:order:Placed:after:Lapsed", "event", "order", "Placed → Lapsed"),
    sticky("view:orders", "view", "order", "orders"),
    sticky("aggregate:shop", "aggregate", "shop", "shop"),
    sticky("policy:order:Placed:shop:Ring", "policy", "shop", "when order is Placed, Ring shop"),
    sticky("command:shop:Ring", "command", "shop", "Ring"),
    sticky("event:shop:Quiet:Ring:Ringing", "event", "shop", "Quiet → Ringing"),
  ],
  links: [
    { from: "actor:customer", to: "aggregate:order" },
    { from: "actor:customer", to: "command:order:Place" },
    { from: "actor:customer", to: "command:order:Pay" },
    { from: "command:order:Place", to: "event:order:Draft:Place:Placed" },
    { from: "command:order:Pay", to: "event:order:Placed:Pay:Paid" },
    { from: "guard:order:Placed:Pay", to: "command:order:Pay" },
    { from: "timer:order:Placed", to: "event:order:Placed:after:Lapsed" },
    { from: "event:order:Draft:Place:Placed", to: "aggregate:order" },
    { from: "event:order:Draft:Place:Placed", to: "policy:order:Placed:shop:Ring" },
    { from: "policy:order:Placed:shop:Ring", to: "command:shop:Ring" },
    { from: "command:shop:Ring", to: "event:shop:Quiet:Ring:Ringing" },
    { from: "aggregate:order", to: "view:orders" },
    { from: "view:orders", to: "actor:customer" },
  ],
};

const layout = layoutBoard(board);
const notes = (id: string) => layout.notes.filter((n) => n.sticky.id === id);
const one = (id: string) => {
  const found = notes(id);
  expect(found, id).toHaveLength(1);
  return found[0];
};

describe("layoutBoard", () => {
  it("gives each machine a lane, the asked-for one first, and starts each with the machine itself", () => {
    expect(layout.lanes.map((l) => l.machine)).toEqual(["order", "shop"]);
    const [order, shop] = layout.lanes;
    expect(shop.y).toBe(order.y + order.h);
    for (const lane of layout.lanes) {
      const inLane = layout.notes.filter((n) => n.y >= lane.y && n.y < lane.y + lane.h);
      const first = Math.min(...inLane.map((n) => n.x));
      expect(one(`aggregate:${lane.machine}`).x).toBe(first);
    }
  });

  it("reads left to right in the order the states are reached", () => {
    const place = one("command:order:Place").x;
    const pay = one("command:order:Pay").x;
    const lapse = one("event:order:Placed:after:Lapsed").x;
    expect(place).toBeLessThan(pay);
    expect(place).toBeLessThan(lapse);
    // The views end the lane.
    expect(one("view:orders").x).toBeGreaterThan(Math.max(pay, lapse));
  });

  it("puts the actor above its command, the guard between them, and what happens below", () => {
    const pay = one("command:order:Pay");
    const guard = one("guard:order:Placed:Pay");
    const paid = one("event:order:Placed:Pay:Paid");
    const customer = notes("actor:customer").find((n) => n.x === pay.x)!;
    expect(guard.x).toBe(pay.x);
    expect(paid.x).toBe(pay.x);
    expect(customer.y).toBeLessThan(guard.y);
    expect(guard.y).toBeLessThan(pay.y);
    expect(paid.y).toBeGreaterThan(pay.y);
  });

  it("repeats an actor on every column it acts in, and over the view it reads", () => {
    const columns = notes("actor:customer").map((n) => n.x);
    for (const id of ["aggregate:order", "command:order:Place", "command:order:Pay", "view:orders"])
      expect(columns).toContain(one(id).x);
  });

  it("gives a timer's move a column of its own, the timer above it", () => {
    const lapsed = one("event:order:Placed:after:Lapsed");
    const timer = one("timer:order:Placed");
    expect(timer.x).toBe(lapsed.x);
    expect(timer.y).toBeLessThan(lapsed.y);
    expect(layout.notes.some((n) => n.sticky.kind === "command" && n.x === lapsed.x && n.y < layout.lanes[0].y + layout.lanes[0].h)).toBe(false);
  });

  it("puts a rule that reaches another machine in that machine's lane, above the command it sends", () => {
    const ring = one("command:shop:Ring");
    const rule = one("policy:order:Placed:shop:Ring");
    expect(rule.x).toBe(ring.x);
    expect(rule.y).toBeGreaterThanOrEqual(layout.lanes[1].y);
    expect(rule.y).toBeLessThan(ring.y);
  });

  it("draws the arrows a wall would, and leaves out the ones the lane already says", () => {
    const drawn = layout.arrows.map((a) => `${a.from.sticky.id} -> ${a.to.sticky.id}`);
    expect(drawn).toContain("event:order:Draft:Place:Placed -> policy:order:Placed:shop:Ring");
    expect(drawn).toContain("policy:order:Placed:shop:Ring -> command:shop:Ring");
    expect(drawn).toContain("guard:order:Placed:Pay -> command:order:Pay");
    expect(drawn).not.toContain("event:order:Draft:Place:Placed -> aggregate:order");
    expect(drawn).not.toContain("aggregate:order -> view:orders");
    // A repeated actor is drawn to its command from the copy in the same column.
    for (const a of layout.arrows.filter((x) => x.from.sticky.kind === "actor")) expect(a.local).toBe(true);
    // An arrow to another lane is not local, so it is drawn only for the note it touches.
    const across = layout.arrows.find((a) => a.to.sticky.id === "policy:order:Placed:shop:Ring")!;
    expect(across.local).toBe(false);
  });

  it("keeps every note inside the wall and apart from the others", () => {
    for (const n of layout.notes) {
      expect(n.x + n.w).toBeLessThanOrEqual(layout.width);
      expect(n.y + n.h).toBeLessThanOrEqual(layout.height);
    }
    for (const a of layout.notes)
      for (const b of layout.notes)
        if (a !== b) expect(a.x + a.w <= b.x || b.x + b.w <= a.x || a.y + a.h <= b.y || b.y + b.h <= a.y, `${a.key} / ${b.key}`).toBe(true);
  });
});

describe("a machine that comes round again", () => {
  // Open -> Closed -> Departed -> Open: every state is entered, so only the initial state says where to start.
  const round: Board = {
    machine: "flight",
    lanes: ["flight"],
    stickies: [
      sticky("command:flight:Open", "command", "flight", "Open"),
      sticky("event:flight:Departed:Open:Open", "event", "flight", "Departed → Open"),
      sticky("command:flight:Close", "command", "flight", "Close"),
      sticky("event:flight:Open:Close:Closed", "event", "flight", "Open → Closed"),
      sticky("command:flight:Depart", "command", "flight", "Depart"),
      sticky("event:flight:Closed:Depart:Departed", "event", "flight", "Closed → Departed"),
    ],
    links: [
      { from: "command:flight:Open", to: "event:flight:Departed:Open:Open" },
      { from: "command:flight:Close", to: "event:flight:Open:Close:Closed" },
      { from: "command:flight:Depart", to: "event:flight:Closed:Depart:Departed" },
    ],
  };
  const order = (l: ReturnType<typeof layoutBoard>) =>
    l.notes.filter((n) => n.sticky.kind === "command").sort((a, b) => a.x - b.x).map((n) => n.sticky.label);

  it("starts from the machine's initial state", () => {
    expect(order(layoutBoard(round, { flight: "Departed" }))).toEqual(["Open", "Close", "Depart"]);
    expect(order(layoutBoard(round, { flight: "Open" }))).toEqual(["Close", "Depart", "Open"]);
  });

  it("starts from the first state by name when the initial one is not known", () => {
    expect(order(layoutBoard(round))).toEqual(["Depart", "Open", "Close"]);
  });
});

describe("neighbours", () => {
  it("is the sticky and everything linked to it either way", () => {
    expect([...neighbours(board, "command:order:Pay")].sort()).toEqual(
      ["actor:customer", "command:order:Pay", "event:order:Placed:Pay:Paid", "guard:order:Placed:Pay"].sort(),
    );
  });
});

describe("hasProvenance", () => {
  it("tells a board drawn without provenance", () => {
    expect(hasProvenance(board)).toBe(false);
    const withOne = { ...board, stickies: [{ ...board.stickies[0], scenarios: [{ file: "o.feature", line: 2, scenario: "Ada has an order" }] }] };
    expect(hasProvenance(withOne)).toBe(true);
  });
});
